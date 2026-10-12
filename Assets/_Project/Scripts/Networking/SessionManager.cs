using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
    /// <summary>
    /// Owns the NetworkManager and the host/join/leave flow for both transports.
    /// Transport resolution: command-line requested transport (Steam by default in builds, UTP in
    /// the editor); if Steam is requested but unavailable or fails, automatically falls back to
    /// UTP with a notification — never crashes.
    /// Registered in G as the session entry point.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        /// <summary>Transport currently in use (after fallback resolution).</summary>
        public TransportKind ActiveTransport { get; private set; } = TransportKind.UTP;

        /// <summary>Steam lobby/identity service (safe to touch when Steam is absent).</summary>
        public SteamLobbyService Steam { get; private set; }

        /// <summary>Display name for the local player.</summary>
        public string LocalPlayerName { get; private set; } = "Pilot";

        /// <summary>Server + clients: a client finished connecting.</summary>
        public event Action<ulong> ClientConnected;
        /// <summary>Server: a client disconnected. Clients: id == LocalClientId means we were dropped.</summary>
        public event Action<ulong> ClientDisconnected;
        /// <summary>Host pressed Start in the lobby — the world run should begin (RunManager listens).</summary>
        public event Action RunStartRequested;
        /// <summary>Host: the server side is up (spawn the game director here).</summary>
        public event Action ServerStarted;

        UnityTransport utp;
        SteamTransport steamTransport;
        bool intentionalShutdown;
        bool callbacksHooked;

        public NetworkManager Net => NetworkManager.Singleton;

        /// <summary>True once StartHost/StartClient has been called (includes connecting clients).</summary>
        public bool InSession => Net != null && (Net.IsHost || Net.IsClient);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            G.Register<SessionManager>(this);

            Steam = new SteamLobbyService();
            Steam.SteamInitialized += () => { if (!string.IsNullOrEmpty(Steam.LocalName)) LocalPlayerName = Steam.LocalName; };
            Steam.LobbyJoinRequested += lobbyId =>
            {
                if (!InSession)
                {
                    EventBus.RaiseNotification("Joining invited lobby...");
                    JoinSteamLobby(lobbyId);
                }
            };

            LocalPlayerName = "Pilot-" + UnityEngine.Random.Range(1000, 9999);

            // Ping forwarding: any local system raises PingRequested -> network broadcast.
            EventBus.PingRequested += OnPingRequested;
            EventBus.PhaseChanged += OnPhaseChanged;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            G.Unregister<SessionManager>();
            EventBus.PingRequested -= OnPingRequested;
            EventBus.PhaseChanged -= OnPhaseChanged;
            try { Steam?.Shutdown(); } catch { }
        }

        void OnPingRequested(Vector3 pos, PingKind kind, string label) => NetworkPlayer.SendPing(pos, kind, label);

        // Host: mirror the session phase into the Steam lobby so the join list can show Lobby / In run.
        void OnPhaseChanged(GamePhase p)
        {
            if (ActiveTransport == TransportKind.Steam && Net != null && Net.IsHost) Steam.SetLobbyPhase(p.ToString());
        }

        /// <summary>Override the display name (main menu callsign). Steam persona name wins when Steam is active.</summary>
        public void SetLocalName(string n)
        {
            if (!string.IsNullOrWhiteSpace(n)) LocalPlayerName = n.Trim().Length > 24 ? n.Trim().Substring(0, 24) : n.Trim();
        }

        /// <summary>Finds or creates the NetworkManager with both transports attached.</summary>
        public NetworkManager EnsureNetworkManager()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");
                nm = go.AddComponent<NetworkManager>();
                DontDestroyOnLoad(go);
            }
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            if (utp == null) utp = nm.GetComponent<UnityTransport>();
            if (utp == null) utp = nm.gameObject.AddComponent<UnityTransport>();
            if (steamTransport == null) steamTransport = nm.GetComponent<SteamTransport>();
            if (steamTransport == null) steamTransport = nm.gameObject.AddComponent<SteamTransport>();
            if (nm.NetworkConfig.NetworkTransport == null)
                nm.NetworkConfig.NetworkTransport = utp;
            return nm;
        }

        void HookCallbacks(NetworkManager nm)
        {
            if (callbacksHooked) return;
            callbacksHooked = true;
            nm.OnClientConnectedCallback += HandleClientConnected;
            nm.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        void UnhookCallbacks(NetworkManager nm)
        {
            if (!callbacksHooked || nm == null) return;
            callbacksHooked = false;
            nm.OnClientConnectedCallback -= HandleClientConnected;
            nm.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        void HandleClientConnected(ulong clientId)
        {
            Debug.Log("[Session] Client connected: " + clientId);
            ClientConnected?.Invoke(clientId);
        }

        void HandleClientDisconnected(ulong clientId)
        {
            var nm = Net;
            Debug.Log("[Session] Client disconnected: " + clientId);
            ClientDisconnected?.Invoke(clientId);

            if (nm != null && !nm.IsServer && !intentionalShutdown)
            {
                // We are a client and our connection ended (host quit / transport failure).
                ulong localId = nm.LocalClientId;
                if (clientId == localId)
                {
                    UnhookCallbacks(nm);
                    string reason = nm.DisconnectReason;
                    Debug.LogWarning("[Session] Lost connection to host" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason));
                    try { Steam.LeaveLobby(); } catch { }
                    EventBus.RaiseNotification(string.IsNullOrEmpty(reason) ? "Connection to host lost" : "Disconnected: " + reason);
                    nm.Shutdown();
                    EventBus.RaisePhaseChanged(GamePhase.MainMenu);
                }
            }
            else if (nm != null && nm.IsServer)
            {
                EventBus.RaiseNotification("A player departed");
            }
        }

        /// <summary>Start a host session on the requested transport (UTP fallback automatic).</summary>
        public void HostGame()
        {
            var nm = EnsureNetworkManager();
            if (InSession) { EventBus.RaiseNotification("Already in a session"); return; }

            intentionalShutdown = false;
            bool wantSteam = TransportConfig.RequestedTransport == TransportKind.Steam;
            bool steamOk = false;

            if (wantSteam)
            {
                Steam.TryInit();
                steamOk = Steam.SteamAvailable;
                if (!steamOk)
                {
                    Debug.LogWarning("[Session] Steam requested but unavailable - falling back to UTP.");
                    EventBus.RaiseNotification("Steam unavailable - using direct IP");
                }
            }

            if (steamOk)
            {
                nm.NetworkConfig.NetworkTransport = steamTransport;
                ActiveTransport = TransportKind.Steam;
                HookCallbacks(nm);
                if (nm.StartHost())
                {
                    ServerStarted?.Invoke();
                    Steam.CreateLobby(4, ok =>
                    {
                        if (ok) EventBus.RaiseNotification("Steam lobby created - friends can join!");
                        else EventBus.RaiseNotification("Lobby creation failed - use Steam profile join");
                    });
                    EventBus.RaiseNotification("Hosting via Steam (AppID 480)");
                    return;
                }
                Debug.LogWarning("[Session] Steam host failed - falling back to UTP.");
                EventBus.RaiseNotification("Steam host failed - using direct IP");
                UnhookCallbacks(nm);
            }

            // UTP host
            nm.NetworkConfig.NetworkTransport = utp;
            utp.SetConnectionData("0.0.0.0", (ushort)TransportConfig.Port);
            ActiveTransport = TransportKind.UTP;
            HookCallbacks(nm);
            if (!nm.StartHost())
            {
                EventBus.RaiseNotification("Failed to start host");
                return;
            }
            ServerStarted?.Invoke();
            EventBus.RaiseNotification("Hosting on port " + TransportConfig.Port);
        }

        void Start()
        {
            // Automation for local multiplayer tests: -autohost / -autojoin=127.0.0.1
            if (TransportConfig.AutoHost) Invoke(nameof(HostGame), 1f);
            else if (!string.IsNullOrEmpty(TransportConfig.AutoJoinIp)) Invoke(nameof(AutoJoin), 1.5f);
            else if (TransportConfig.RequestedTransport == TransportKind.Steam)
            {
                // Init Steam up front so friend invites ("Join Game") reach us while we sit in the menu.
                Steam.TryInit();
                if (Steam.LaunchLobbyId != 0) Invoke(nameof(JoinLaunchLobby), 1.5f);
            }
        }

        void JoinLaunchLobby()
        {
            EventBus.RaiseNotification("Joining invited lobby...");
            JoinSteamLobby(Steam.LaunchLobbyId);
        }

        void AutoJoin() => JoinGame(TransportConfig.AutoJoinIp);

        /// <summary>Join a host directly by IP over UTP.</summary>
        public void JoinGame(string ip)
        {
            var nm = EnsureNetworkManager();
            if (InSession) { EventBus.RaiseNotification("Already in a session"); return; }

            intentionalShutdown = false;
            nm.NetworkConfig.NetworkTransport = utp;
            utp.SetConnectionData(string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim(), (ushort)TransportConfig.Port);
            ActiveTransport = TransportKind.UTP;
            HookCallbacks(nm);
            if (!nm.StartClient())
                EventBus.RaiseNotification("Failed to start client");
            else
                EventBus.RaiseNotification("Connecting to " + ip + ":" + TransportConfig.Port + "...");
        }

        /// <summary>Join a Steam lobby (from the lobby list or an invite).</summary>
        public void JoinSteamLobby(ulong lobbyId)
        {
            var nm = EnsureNetworkManager();
            if (InSession) { EventBus.RaiseNotification("Already in a session"); return; }

            Steam.TryInit();
            if (!Steam.SteamAvailable)
            {
                EventBus.RaiseNotification("Steam unavailable");
                return;
            }

            Steam.JoinLobbyById(lobbyId, ok =>
            {
                if (!ok) { EventBus.RaiseNotification("Failed to join lobby"); return; }
                ulong host = Steam.HostSteamIdOfJoinedLobby;
                if (host == 0) { EventBus.RaiseNotification("Lobby is missing host info"); return; }

                intentionalShutdown = false;
                steamTransport.HostSteamId = host;
                nm.NetworkConfig.NetworkTransport = steamTransport;
                ActiveTransport = TransportKind.Steam;
                HookCallbacks(nm);
                if (!nm.StartClient())
                {
                    Steam.LeaveLobby();
                    EventBus.RaiseNotification("Failed to connect via Steam");
                }
                else
                    EventBus.RaiseNotification("Connecting via Steam relay...");
            });
        }

        /// <summary>Leave the current session and lobby, back to the main menu state.</summary>
        public void LeaveGame()
        {
            intentionalShutdown = true;
            try { Steam.LeaveLobby(); } catch { }
            var nm = Net;
            if (nm != null && (nm.IsHost || nm.IsClient))
            {
                UnhookCallbacks(nm);
                nm.Shutdown();
            }
            EventBus.RaiseNotification("Left session");
            EventBus.RaisePhaseChanged(GamePhase.MainMenu);
        }

        /// <summary>Host only: begin the run (spawns the world via RunManager).</summary>
        public void StartRun()
        {
            var nm = Net;
            if (nm == null || !nm.IsHost)
            {
                EventBus.RaiseNotification("Only the host can start the run");
                return;
            }
            RunStartRequested?.Invoke();
        }
    }
}
