using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Voidward.Core;

namespace Voidward.Networking
{
#if VOIDWARD_STEAM
    using Steamworks;
    using Steamworks.Data;
#endif

    /// <summary>
    /// Netcode for GameObjects transport that tunnels traffic through Steam P2P relay sockets
    /// via Facepunch.Steamworks. All Facepunch API usage is guarded by VOIDWARD_STEAM.
    /// When the define is absent or Steam is not running, StartServer/StartClient return false.
    /// </summary>
    public class SteamTransport : NetworkTransport
    {
        /// <summary>Steam ID of the host to connect to (client side). Set before StartClient.</summary>
        public ulong HostSteamId;

        /// <summary>Virtual port for the relay socket (default 0).</summary>
        public int VirtualPort = 0;

        // ---- Abstract member: server client id is always 0 ----
        /// <inheritdoc/>
        public override ulong ServerClientId => 0;

#if VOIDWARD_STEAM
        // ---- Event queue ----
        struct QueuedEvent
        {
            public NetworkEvent Type;
            public ulong ClientId;
            public byte[] Data;
        }

        readonly Queue<QueuedEvent> _eventQueue = new Queue<QueuedEvent>();
        ushort _nextClientId = 1;

        // Host side
        VwSocketManager _socketManager;

        // Client side
        VwConnectionManager _connectionManager;

        // Mappings (server side)
        readonly Dictionary<ulong, Connection> _clientToConnection = new Dictionary<ulong, Connection>();
        readonly Dictionary<Connection, ulong> _connectionToClient = new Dictionary<Connection, ulong>();

        // Cached receive buffer for PollEvent
        byte[] _receiveBuffer;

        /// <inheritdoc/>
        public override void Initialize(NetworkManager networkManager = null)
        {
            _eventQueue.Clear();
            _clientToConnection.Clear();
            _connectionToClient.Clear();
            _nextClientId = 1;
            _socketManager = null;
            _connectionManager = null;
        }

        /// <inheritdoc/>
        public override bool StartServer()
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogWarning("[SteamTransport] Steam is not running — cannot start Steam server.");
                return false;
            }

            try
            {
                _socketManager = SteamNetworkingSockets.CreateRelaySocket<VwSocketManager>(VirtualPort);
                _socketManager.Transport = this;
                Debug.Log("[SteamTransport] Server started (Steam relay, vport=" + VirtualPort + ").");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamTransport] Failed to create relay socket: " + e.Message);
                _socketManager = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public override bool StartClient()
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogWarning("[SteamTransport] Steam is not running — cannot start Steam client.");
                return false;
            }

            if (HostSteamId == 0)
            {
                Debug.LogWarning("[SteamTransport] HostSteamId is 0 — cannot connect.");
                return false;
            }

            try
            {
                var targetId = new SteamId { Value = HostSteamId };
                _connectionManager = SteamNetworkingSockets.ConnectRelay<VwConnectionManager>(targetId, VirtualPort);
                _connectionManager.Transport = this;
                Debug.Log("[SteamTransport] Client connecting to Steam ID " + HostSteamId + " (vport=" + VirtualPort + ").");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamTransport] Failed to connect relay: " + e.Message);
                _connectionManager = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public override void Shutdown()
        {
            if (_socketManager != null)
            {
                try { _socketManager.Close(); } catch { }
                _socketManager = null;
            }
            if (_connectionManager != null)
            {
                try { _connectionManager.Connection.Close(false, 0, "shutdown"); } catch { }
                _connectionManager = null;
            }
            _clientToConnection.Clear();
            _connectionToClient.Clear();
            _eventQueue.Clear();
            _nextClientId = 1;
        }

        /// <inheritdoc/>
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (_clientToConnection.TryGetValue(clientId, out var conn))
            {
                try { conn.Close(false, 0, "kicked"); } catch { }
                _clientToConnection.Remove(clientId);
                _connectionToClient.Remove(conn);
            }
        }

        /// <inheritdoc/>
        public override void DisconnectLocalClient()
        {
            if (_connectionManager != null)
            {
                try { _connectionManager.Connection.Close(false, 0, "local disconnect"); } catch { }
            }
        }

        /// <inheritdoc/>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // Pump callbacks only when the queue is empty.
            if (_eventQueue.Count == 0)
            {
                try
                {
                    _socketManager?.Receive();
                    _connectionManager?.Receive();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SteamTransport] Receive pump error: " + e.Message);
                }
            }

            if (_eventQueue.Count > 0)
            {
                var evt = _eventQueue.Dequeue();
                clientId = evt.ClientId;
                receiveTime = Time.realtimeSinceStartup;
                if (evt.Data != null)
                    payload = new ArraySegment<byte>(evt.Data);
                else
                    payload = default;
                return evt.Type;
            }

            clientId = 0;
            payload = default;
            receiveTime = 0f;
            return NetworkEvent.Nothing;
        }

        /// <inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            var sendType = DeliveryToSendType(networkDelivery);

            if (_socketManager != null)
            {
                // Server sending to a specific client
                if (_clientToConnection.TryGetValue(clientId, out var conn))
                {
                    SendToConnection(conn, payload, sendType);
                }
            }
            else if (_connectionManager != null)
            {
                // Client sending to server
                SendToConnection(_connectionManager.Connection, payload, sendType);
            }
        }

        /// <inheritdoc/>
        public override ulong GetCurrentRtt(ulong clientId)
        {
            // Estimate: Steam relay RTT is not directly exposed per-connection in this API.
            return 30;
        }

        // ---- Helpers ----

        static SendType DeliveryToSendType(NetworkDelivery delivery)
        {
            switch (delivery)
            {
                case NetworkDelivery.Reliable:
                case NetworkDelivery.ReliableSequenced:
                case NetworkDelivery.ReliableFragmentedSequenced:
                    return SendType.Reliable;
                default:
                    return SendType.NoNagle;
            }
        }

        static void SendToConnection(Connection conn, ArraySegment<byte> payload, SendType sendType)
        {
            if (payload.Count == 0 || payload.Array == null) return;

            if (payload.Offset == 0)
            {
                // SendMessage(byte[], SendType) — full array
                conn.SendMessage(payload.Array, sendType);
            }
            else
            {
                // SendMessage(byte[], int offset, int size, SendType) — sub-range
                conn.SendMessage(payload.Array, payload.Offset, payload.Count, sendType);
            }
        }

        // ---- Event enqueue helpers (called by socket/connection manager) ----

        internal void EnqueueConnect(ulong clientId)
        {
            _eventQueue.Enqueue(new QueuedEvent { Type = NetworkEvent.Connect, ClientId = clientId, Data = null });
        }

        internal void EnqueueData(ulong clientId, byte[] data)
        {
            _eventQueue.Enqueue(new QueuedEvent { Type = NetworkEvent.Data, ClientId = clientId, Data = data });
        }

        internal void EnqueueDisconnect(ulong clientId)
        {
            _eventQueue.Enqueue(new QueuedEvent { Type = NetworkEvent.Disconnect, ClientId = clientId, Data = null });
        }

        internal ulong RegisterConnection(Connection conn)
        {
            ulong id = _nextClientId++;
            _clientToConnection[id] = conn;
            _connectionToClient[conn] = id;
            return id;
        }

        internal void UnregisterConnection(Connection conn)
        {
            if (_connectionToClient.TryGetValue(conn, out var id))
            {
                _connectionToClient.Remove(conn);
                _clientToConnection.Remove(id);
            }
        }

        internal ulong GetClientIdFor(Connection conn)
        {
            return _connectionToClient.TryGetValue(conn, out var id) ? id : 0;
        }

        // ---- SocketManager subclass (host) ----
        class VwSocketManager : SocketManager
        {
            public SteamTransport Transport;

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                ulong id = Transport.RegisterConnection(connection);
                Debug.Log("[SteamTransport] Client connected: id=" + id);
                Transport.EnqueueConnect(id);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                ulong id = Transport.GetClientIdFor(connection);
                Transport.UnregisterConnection(connection);
                Debug.Log("[SteamTransport] Client disconnected: id=" + id);
                Transport.EnqueueDisconnect(id);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long recvTime, long msgNum, int channel)
            {
                ulong id = Transport.GetClientIdFor(connection);
                byte[] copy = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data, copy, 0, size);
                Transport.EnqueueData(id, copy);
            }
        }

        // ---- ConnectionManager subclass (client) ----
        class VwConnectionManager : ConnectionManager
        {
            public SteamTransport Transport;

            public override void OnConnected(ConnectionInfo info)
            {
                Debug.Log("[SteamTransport] Connected to host.");
                Transport.EnqueueConnect(Transport.ServerClientId);
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                Debug.Log("[SteamTransport] Disconnected from host.");
                Transport.EnqueueDisconnect(Transport.ServerClientId);
            }

            public override void OnMessage(IntPtr data, int size, long recvTime, long msgNum, int channel)
            {
                byte[] copy = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data, copy, 0, size);
                Transport.EnqueueData(Transport.ServerClientId, copy);
            }
        }
#else
        // ---- Stub implementations when VOIDWARD_STEAM is not defined ----

        /// <inheritdoc/>
        public override void Initialize(NetworkManager networkManager = null) { }

        /// <inheritdoc/>
        public override bool StartServer()
        {
            Debug.LogWarning("[SteamTransport] VOIDWARD_STEAM define is not set — Steam transport unavailable.");
            return false;
        }

        /// <inheritdoc/>
        public override bool StartClient()
        {
            Debug.LogWarning("[SteamTransport] VOIDWARD_STEAM define is not set — Steam transport unavailable.");
            return false;
        }

        /// <inheritdoc/>
        public override void Shutdown() { }

        /// <inheritdoc/>
        public override void DisconnectRemoteClient(ulong clientId) { }

        /// <inheritdoc/>
        public override void DisconnectLocalClient() { }

        /// <inheritdoc/>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = 0f;
            return NetworkEvent.Nothing;
        }

        /// <inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery) { }

        /// <inheritdoc/>
        public override ulong GetCurrentRtt(ulong clientId) => 0;
#endif
    }
}
