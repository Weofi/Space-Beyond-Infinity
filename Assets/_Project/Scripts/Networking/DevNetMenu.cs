using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
    /// <summary>
    /// TEMPORARY IMGUI networking menu used before (and alongside) the real UI for
    /// multiplayer verification. Editor/development builds only. Hides itself once the
    /// real UI registers its UIGate service.
    /// </summary>
    public class DevNetMenu : MonoBehaviour
    {
        string ip = "127.0.0.1";
        string status = "";
        Vector2 scroll;
        readonly List<LobbyEntry> lobbies = new List<LobbyEntry>();
        float lobbyRefreshTimer;

        void OnGUI()
        {
            // Real UI present? Stand down.
            if (G.Get<UIGate>() != null) return;

            var sm = SessionManager.Instance;
            if (sm == null) return;

            const float w = 320;
            var r = new Rect(Screen.width - w - 12, 12, w, 430);
            GUI.Box(r, "VOIDWARD NET (dev)");
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 24, w - 20, r.height - 34));

            GUILayout.Label($"Transport: {sm.ActiveTransport}  (requested: {TransportConfig.RequestedTransport})");
            GUILayout.Label($"Session: {(sm.InSession ? (sm.Net.IsHost ? "HOST" : "CLIENT") : "none")}");
            GUILayout.Label($"Players: {NetworkPlayer.All.Count}");
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);

            if (!sm.InSession)
            {
                if (GUILayout.Button("Host (UTP)")) { sm.HostGame(); status = "hosting utp"; }
                GUILayout.BeginHorizontal();
                ip = GUILayout.TextField(ip, GUILayout.Width(150));
                if (GUILayout.Button("Join (UTP)")) { sm.JoinGame(ip); status = "joining " + ip; }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label("Steam (AppID 480): " + (sm.Steam.SteamAvailable ? "available" : "unavailable"));
                if (GUILayout.Button("Host (Steam)"))
                {
                    // Force-request steam for this attempt by hosting directly on steam path.
                    sm.Steam.TryInit();
                    if (sm.Steam.SteamAvailable) { status = "hosting steam"; HostSteam(sm); }
                    else status = "Steam unavailable";
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh Lobbies"))
                {
                    sm.Steam.TryInit();
                    sm.Steam.RequestLobbyList(list =>
                    {
                        lobbies.Clear();
                        if (list != null) lobbies.AddRange(list);
                        status = "lobbies: " + lobbies.Count;
                    });
                }
                if (GUILayout.Button("Invite Friends") && sm.InSession) sm.Steam.InviteFriends();
                GUILayout.EndHorizontal();

                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(120));
                foreach (var l in lobbies)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{l.Name} ({l.Members}/{l.MaxMembers})", GUILayout.Width(190));
                    if (GUILayout.Button("Join")) { sm.JoinSteamLobby(l.Id); status = "joining lobby " + l.Id; }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            else
            {
                if (GUILayout.Button("Leave")) sm.LeaveGame();
                if (sm.Net != null && sm.Net.IsHost && GUILayout.Button("Start Run"))
                    sm.StartRun();
                foreach (var p in NetworkPlayer.All)
                    GUILayout.Label($"  [{p.OwnerClientId}] {p.PlayerName.Value}");
            }

            GUILayout.EndArea();
        }

        static void HostSteam(SessionManager sm)
        {
            // HostGame() honors TransportConfig.RequestedTransport; when running in the editor the
            // default is UTP, so hosting via Steam here requires the steam path directly:
            // simplest robust approach: temporarily host with UTP is wrong — we need steam transport.
            // SessionManager.HostGame auto-falls back; to force steam we set requested via reflection-free
            // approach: SessionManager exposes ActiveTransport only. We emulate by trying Steam init and
            // letting HostGame's fallback handle it: but HostGame reads TransportConfig.RequestedTransport
            // (UTP in editor). So instead we nudge: create lobby first, then host normally if steam active.
            var nm = sm.EnsureNetworkManager();
            var steam = nm.GetComponent<SteamTransport>();
            if (steam == null) { sm.HostGame(); return; }
            nm.NetworkConfig.NetworkTransport = steam;
            if (nm.StartHost())
                sm.Steam.CreateLobby(4, ok => sm.Steam.InviteFriends());
            else
                sm.HostGame();
        }
    }
}
