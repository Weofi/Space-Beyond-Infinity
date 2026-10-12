using System;
using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
#if VOIDWARD_STEAM
    using Steamworks;
    using Steamworks.Data;
#endif

    /// <summary>
    /// Lightweight lobby listing entry returned by <see cref="SteamLobbyService.RequestLobbyList"/>.
    /// </summary>
    public struct LobbyEntry
    {
        /// <summary>The lobby's Steam ID as a ulong.</summary>
        public ulong Id;

        /// <summary>Display name (from lobby metadata "name").</summary>
        public string Name;

        /// <summary>Current member count.</summary>
        public int Members;

        /// <summary>Maximum allowed members.</summary>
        public int MaxMembers;
    }

    /// <summary>
    /// Hidden MonoBehaviour that pumps <c>SteamClient.RunCallbacks()</c> every frame.
    /// Required because SteamClient.Init is called with asyncCallbacks=false.
    /// Lazily created by <see cref="SteamLobbyService"/>.
    /// </summary>
    public class SteamCallbackPump : MonoBehaviour
    {
        float _accumulator;

#if VOIDWARD_STEAM
        void Update()
        {
            if (!SteamClient.IsValid) return;
            try
            {
                _accumulator += Time.unscaledDeltaTime;
                if (_accumulator >= 0.1f)
                {
                    _accumulator = 0f;
                    SteamClient.RunCallbacks();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamCallbackPump] RunCallbacks error: " + e.Message);
            }
        }
#else
        void Update() { }
#endif

        /// <summary>Creates or finds an existing pump on a hidden DontDestroyOnLoad GameObject.</summary>
        public static SteamCallbackPump EnsureExists()
        {
            var existing = FindObjectOfType<SteamCallbackPump>();
            if (existing != null) return existing;

            var go = new GameObject("_SteamCallbackPump");
            GameObject.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            return go.AddComponent<SteamCallbackPump>();
        }
    }

    /// <summary>
    /// Manages Steam lobby creation, joining, listing, and invite overlays via Facepunch.Steamworks.
    /// All Facepunch calls are guarded by VOIDWARD_STEAM; the class compiles and no-ops gracefully
    /// when the define is absent or Steam is not running.
    /// </summary>
    public class SteamLobbyService
    {
        /// <summary>True when SteamClient has been initialised and is valid.</summary>
        public bool SteamAvailable { get; private set; }

        /// <summary>Raised after successful Steam initialisation.</summary>
        public event Action SteamInitialized;

        /// <summary>Raised when a friend invite is accepted while the game is running (lobby Steam ID).</summary>
        public event Action<ulong> LobbyJoinRequested;

        /// <summary>The Steam ID of the current lobby (0 if none).</summary>
        public ulong CurrentLobbyId { get; private set; }

        /// <summary>After joining a lobby, the host's Steam ID read from lobby metadata.</summary>
        public ulong HostSteamIdOfJoinedLobby { get; private set; }

        /// <summary>The local user's Steam ID (0 if Steam unavailable).</summary>
        public ulong LocalSteamId
        {
            get
            {
#if VOIDWARD_STEAM
                if (SteamClient.IsValid) return SteamClient.SteamId.Value;
#endif
                return 0;
            }
        }

        /// <summary>The local user's Steam persona name (empty if Steam unavailable).</summary>
        public string LocalName
        {
            get
            {
#if VOIDWARD_STEAM
                if (SteamClient.IsValid) return SteamClient.Name;
#endif
                return string.Empty;
            }
        }

#if VOIDWARD_STEAM
        Lobby? _currentLobby;
        bool _hooksInstalled;
#endif

        /// <summary>
        /// Attempts to initialise the Steam client (App ID 480).
        /// Catches all exceptions; sets <see cref="SteamAvailable"/> to false on any failure.
        /// </summary>
        public void TryInit()
        {
#if VOIDWARD_STEAM
            if (SteamAvailable) return;

            try
            {
                SteamClient.Init(480, false);
                SteamAvailable = SteamClient.IsValid;
                if (SteamAvailable)
                {
                    Debug.Log("[SteamLobbyService] Steam initialised. SteamId=" + SteamClient.SteamId.Value + " Name=" + SteamClient.Name);
                    InstallHooks();
                    SteamCallbackPump.EnsureExists();
                    SteamInitialized?.Invoke();
                }
                else
                {
                    Debug.LogWarning("[SteamLobbyService] SteamClient.Init returned but IsValid is false — Steam not running.");
                }
            }
            catch (Exception e)
            {
                SteamAvailable = false;
                Debug.LogWarning("[SteamLobbyService] Steam init failed: " + e.Message);
            }
#else
            SteamAvailable = false;
            Debug.LogWarning("[SteamLobbyService] VOIDWARD_STEAM define is not set — Steam unavailable.");
#endif
        }

#if VOIDWARD_STEAM
        void InstallHooks()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        void OnGameLobbyJoinRequested(Lobby lobby, SteamId friend)
        {
            Debug.Log("[SteamLobbyService] Friend " + friend.Value + " invited to lobby " + lobby.Id.Value);
            LobbyJoinRequested?.Invoke(lobby.Id.Value);
        }
#endif

        /// <summary>
        /// Creates a new public lobby and stores its ID.
        /// Calls <paramref name="onDone"/> with true on success, false on failure.
        /// </summary>
        public void CreateLobby(int maxMembers = 4, Action<bool> onDone = null)
        {
#if VOIDWARD_STEAM
            if (!SteamAvailable)
            {
                Debug.LogWarning("[SteamLobbyService] Cannot create lobby — Steam unavailable.");
                onDone?.Invoke(false);
                return;
            }
            CreateLobbyAsync(maxMembers, onDone);
#else
            Debug.LogWarning("[SteamLobbyService] Cannot create lobby — VOIDWARD_STEAM not defined.");
            onDone?.Invoke(false);
#endif
        }

#if VOIDWARD_STEAM
        async void CreateLobbyAsync(int maxMembers, Action<bool> onDone)
        {
            try
            {
                var result = await SteamMatchmaking.CreateLobbyAsync(maxMembers);
                if (result == null)
                {
                    Debug.LogWarning("[SteamLobbyService] CreateLobbyAsync returned null.");
                    onDone?.Invoke(false);
                    return;
                }

                _currentLobby = result;
                CurrentLobbyId = result.Value.Id.Value;
                var lobby = result.Value;
                lobby.SetPublic();
                lobby.SetJoinable(true);
                lobby.SetData("name", "VOIDWARD Lobby");
                lobby.SetData("host", SteamClient.SteamId.Value.ToString());
                lobby.SetData("joinable", "1");
                Debug.Log("[SteamLobbyService] Lobby created: id=" + CurrentLobbyId + " max=" + maxMembers);
                onDone?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamLobbyService] CreateLobby failed: " + e.Message);
                onDone?.Invoke(false);
            }
        }
#endif

        /// <summary>
        /// Joins the specified lobby by ID. Reads the host's Steam ID from lobby metadata.
        /// Calls <paramref name="onDone"/> with true on success, false on failure.
        /// </summary>
        public void JoinLobbyById(ulong lobbyId, Action<bool> onDone = null)
        {
#if VOIDWARD_STEAM
            if (!SteamAvailable)
            {
                Debug.LogWarning("[SteamLobbyService] Cannot join lobby — Steam unavailable.");
                onDone?.Invoke(false);
                return;
            }
            JoinLobbyAsync(lobbyId, onDone);
#else
            Debug.LogWarning("[SteamLobbyService] Cannot join lobby — VOIDWARD_STEAM not defined.");
            onDone?.Invoke(false);
#endif
        }

#if VOIDWARD_STEAM
        async void JoinLobbyAsync(ulong lobbyId, Action<bool> onDone)
        {
            try
            {
                var targetId = new SteamId { Value = lobbyId };
                var result = await SteamMatchmaking.JoinLobbyAsync(targetId);
                if (result == null)
                {
                    Debug.LogWarning("[SteamLobbyService] JoinLobbyAsync returned null.");
                    onDone?.Invoke(false);
                    return;
                }

                _currentLobby = result;
                CurrentLobbyId = result.Value.Id.Value;
                var lobby = result.Value;
                string hostStr = lobby.GetData("host");
                if (ulong.TryParse(hostStr, out ulong hostId))
                {
                    HostSteamIdOfJoinedLobby = hostId;
                    Debug.Log("[SteamLobbyService] Joined lobby " + CurrentLobbyId + ", host=" + hostId);
                    onDone?.Invoke(true);
                }
                else
                {
                    Debug.LogWarning("[SteamLobbyService] Joined lobby but no 'host' metadata found.");
                    HostSteamIdOfJoinedLobby = 0;
                    onDone?.Invoke(false);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamLobbyService] JoinLobby failed: " + e.Message);
                onDone?.Invoke(false);
            }
        }
#endif

        /// <summary>
        /// Requests the current lobby list and returns entries via <paramref name="onDone"/>.
        /// No-ops gracefully when Steam is unavailable.
        /// </summary>
        public void RequestLobbyList(Action<List<LobbyEntry>> onDone = null)
        {
#if VOIDWARD_STEAM
            if (!SteamAvailable)
            {
                Debug.LogWarning("[SteamLobbyService] Cannot request lobby list — Steam unavailable.");
                onDone?.Invoke(new List<LobbyEntry>());
                return;
            }
            RequestLobbyListAsync(onDone);
#else
            Debug.LogWarning("[SteamLobbyService] Cannot request lobby list — VOIDWARD_STEAM not defined.");
            onDone?.Invoke(new List<LobbyEntry>());
#endif
        }

#if VOIDWARD_STEAM
        async void RequestLobbyListAsync(Action<List<LobbyEntry>> onDone)
        {
            try
            {
                var lobbies = await SteamMatchmaking.LobbyList
                    .WithMaxResults(20)
                    .RequestAsync();

                var entries = new List<LobbyEntry>();
                if (lobbies != null)
                {
                    foreach (var lobby in lobbies)
                    {
                        entries.Add(new LobbyEntry
                        {
                            Id = lobby.Id.Value,
                            Name = lobby.GetData("name"),
                            Members = lobby.MemberCount,
                            MaxMembers = lobby.MaxMembers
                        });
                    }
                }
                Debug.Log("[SteamLobbyService] Found " + entries.Count + " lobbies.");
                onDone?.Invoke(entries);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamLobbyService] RequestLobbyList failed: " + e.Message);
                onDone?.Invoke(new List<LobbyEntry>());
            }
        }
#endif

        /// <summary>Leaves the current lobby if any.</summary>
        public void LeaveLobby()
        {
#if VOIDWARD_STEAM
            if (_currentLobby != null)
            {
                try { _currentLobby.Value.Leave(); } catch { }
                _currentLobby = null;
                CurrentLobbyId = 0;
                HostSteamIdOfJoinedLobby = 0;
                Debug.Log("[SteamLobbyService] Left lobby.");
            }
#endif
        }

        /// <summary>Opens the Steam game invite overlay for the current lobby.</summary>
        public void InviteFriends()
        {
#if VOIDWARD_STEAM
            if (!SteamAvailable || CurrentLobbyId == 0)
            {
                Debug.LogWarning("[SteamLobbyService] Cannot invite — no active lobby.");
                return;
            }
            try
            {
                var lobbyId = new SteamId { Value = CurrentLobbyId };
                SteamFriends.OpenGameInviteOverlay(lobbyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamLobbyService] InviteFriends failed: " + e.Message);
            }
#endif
        }

        /// <summary>Shuts down the Steam client if it was initialised by this service.</summary>
        public void Shutdown()
        {
#if VOIDWARD_STEAM
            LeaveLobby();
            if (_hooksInstalled)
            {
                try { SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested; } catch { }
                _hooksInstalled = false;
            }
            if (SteamClient.IsValid)
            {
                try { SteamClient.Shutdown(); } catch { }
            }
            SteamAvailable = false;
#endif
        }
    }
}
