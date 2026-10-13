using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
    /// <summary>
    /// Persistent per-player networked state. Lives on the player pawn prefab (the
    /// NetworkManager PlayerPrefab) so it exists for the whole session. World systems
    /// (respawn, ship assignment, stats) read/write these NetworkVariables on the server.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
            "Pilot", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>NetworkObjectId of the ship this player currently owns (0 = none).</summary>
        public NetworkVariable<ulong> CurrentShipNetId = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsAlive = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> Kills = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> Stardust = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Crew colour slot (#60, <see cref="CrewPalette"/>), assigned by the server on spawn.</summary>
        public NetworkVariable<byte> CrewSlot = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public Color CrewColor => CrewPalette.Get(CrewSlot.Value);

        /// <summary>All spawned players (every client).</summary>
        public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();

        /// <summary>The local player's NetworkPlayer (null before spawn).</summary>
        public static NetworkPlayer Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Local = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) CrewSlot.Value = FreeSlot();
            All.Add(this);
            if (IsOwner)
            {
                Local = this;
                string name = SessionManager.Instance != null ? SessionManager.Instance.LocalPlayerName : "Pilot";
                SetNameServerRpc(name);
                EventBus.RaiseLocalPawnSpawned(gameObject);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsOwner)
            {
                if (Local == this) Local = null;
                EventBus.RaiseLocalPawnDespawned();
            }
        }

        /// <summary>Lowest crew slot no other player uses, so colours stay unique and rejoiners refill gaps.</summary>
        static byte FreeSlot()
        {
            for (int s = 0; s < CrewPalette.Slots; s++)
            {
                bool used = false;
                foreach (var p in All) if (p != null && p.CrewSlot.Value == s) { used = true; break; }
                if (!used) return (byte)s;
            }
            return (byte)(All.Count % CrewPalette.Slots);
        }

        /// <summary>Finds the NetworkPlayer owned by a given client, or null.</summary>
        public static NetworkPlayer GetByClient(ulong clientId)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].OwnerClientId == clientId) return All[i];
            return null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetNameServerRpc(string playerName) => PlayerName.Value = playerName;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddKillServerRpc() => Kills.Value++;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddStardustServerRpc(int amount) => Stardust.Value += amount;

        /// <summary>Server-side helper: assign the ship this player owns.</summary>
        public void SetShipServer(ulong shipNetId)
        {
            if (IsServer) CurrentShipNetId.Value = shipNetId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PingServerRpc(Vector3 pos, PingKind kind, FixedString64Bytes label) => PingClientRpc(pos, kind, label);

        [ClientRpc]
        void PingClientRpc(Vector3 pos, PingKind kind, FixedString64Bytes label)
        {
            EventBus.RaisePingCreated(pos, kind, label.ToString());
        }

        /// <summary>Local-side ping entry point (null-safe).</summary>
        public static void SendPing(Vector3 pos, PingKind kind, string label)
        {
            if (Local != null) Local.PingServerRpc(pos, kind, label);
        }
    }
}
