using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Networking;
using Voidward.Progression;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>Main menu, join, lobby, hangar (meta progression), settings, pause and results screens.</summary>
    public class MenuScreens
    {
        readonly UIRoot ui;
        readonly Dictionary<Screen, RectTransform> screens = new Dictionary<Screen, RectTransform>();
        RectTransform backdrop;

        // main / join
        TextMeshProUGUI transportLabel;
        TMP_InputField nameField, ipField;
        RectTransform lobbyList;
        TextMeshProUGUI joinStatus;
        // lobby
        TextMeshProUGUI lobbyPlayers, lobbyInfo;
        Button startButton, inviteButton;
        // hangar
        TextMeshProUGUI stardustLabel;
        RectTransform perkList;
        Button dartButton, muleButton;
        // results
        TextMeshProUGUI resultsTitle, resultsBody;
        Button resultsLobby;
        // connecting
        TextMeshProUGUI connectingLabel;
        float lobbyRefresh;

        public MenuScreens(UIRoot ui, RectTransform root)
        {
            this.ui = ui;
            backdrop = UIKit.Rect(root, "MenuBackdrop");
            UIKit.Stretch(backdrop);
            var shade = UIKit.Image(backdrop, "Shade", new Color(0.04f, 0.03f, 0.1f, 0.35f), false);
            UIKit.Stretch(shade.rectTransform);

            BuildMain(root);
            BuildJoin(root);
            BuildLobby(root);
            BuildHangar(root);
            BuildSettings(root);
            BuildPause(root);
            BuildResults(root);
            BuildConnecting(root);
        }

        RectTransform Window(Transform root, Screen s, Vector2 size, string title)
        {
            var w = UIKit.Panel(root, s + "Screen");
            UIKit.Place(w, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UIKit.VLayout(w.gameObject, 12, 28);
            if (!string.IsNullOrEmpty(title))
            {
                var t = UIKit.Text(w, title, 44, TextAlignmentOptions.Center, UIKit.Accent, FontStyles.Bold);
                UIKit.LayoutElement(t.gameObject, -1, 60);
            }
            screens[s] = w;
            return w;
        }

        public void Show(Screen s)
        {
            foreach (var kv in screens) kv.Value.gameObject.SetActive(kv.Key == s);
            backdrop.gameObject.SetActive(s == Screen.Main || s == Screen.Join || s == Screen.Lobby || s == Screen.Connecting || (s == Screen.Hangar || s == Screen.Settings) && GameState.Phase != GamePhase.Playing);
            if (s == Screen.Hangar) RefreshHangar();
            if (s == Screen.Results) RefreshResults();
            if (s == Screen.Main) RefreshTransport();
        }

        // ================================================================ main

        void BuildMain(Transform root)
        {
            var w = UIKit.Rect(root, "MainScreen");
            UIKit.Place(w, new Vector2(0.08f, 0.5f), Vector2.zero, new Vector2(560, 860), new Vector2(0f, 0.5f));
            UIKit.VLayout(w.gameObject, 14, 0, TextAnchor.MiddleLeft);
            screens[Screen.Main] = w;

            var title = UIKit.Text(w, "VOIDWARD", 110, TextAlignmentOptions.Left, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(title.gameObject, -1, 120);
            var sub = UIKit.Text(w, "Outrun the Collapse. Together.", 30, TextAlignmentOptions.Left, UIKit.Accent2);
            UIKit.LayoutElement(sub.gameObject, -1, 44);

            var nameRow = UIKit.Row(w, 52);
            UIKit.LayoutElement(UIKit.Text(nameRow, "Callsign", 24, TextAlignmentOptions.Left, UIKit.TextDim).gameObject, 130, 52);
            nameField = UIKit.Input(nameRow, "Your name", SessionManager.Instance != null ? SessionManager.Instance.LocalPlayerName : "Pilot");
            nameField.onEndEdit.AddListener(v =>
            {
                SessionManager.Instance?.SetLocalName(v);
                GameSettings.Current.PlayerName = v;
                GameSettings.Current.Save();
            });

            var tRow = UIKit.Row(w, 52);
            transportLabel = UIKit.Text(tRow, "", 24, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(transportLabel.gameObject, 330, 52);
            UIKit.LayoutElement(UIKit.Button(tRow, "Switch", () =>
            {
                TransportConfig.SetRequested(TransportConfig.RequestedTransport == TransportKind.UTP ? TransportKind.Steam : TransportKind.UTP);
                RefreshTransport();
            }, 52, UIKit.PanelLight * 2f, 22).gameObject, 160, 52);

            UIKit.Button(w, "Host Game", () => { ApplyName(); SessionManager.Instance?.HostGame(); ui.Show(Screen.Connecting); }, 64);
            UIKit.Button(w, "Join Game", () => { ApplyName(); ui.Push(Screen.Join); }, 64);
            UIKit.Button(w, "Hangar", () => ui.Push(Screen.Hangar), 64, UIKit.Accent2);
            UIKit.Button(w, "Settings", () => ui.Push(Screen.Settings), 64, UIKit.PanelLight * 2.2f);
            UIKit.Button(w, "Quit", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 64, UIKit.Bad);

            var hint = UIKit.Text(w, "F1: debug cheats   |   Co-op 1-4 players   |   Steam test AppID 480", 18, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(hint.gameObject, -1, 30);
        }

        void ApplyName()
        {
            if (nameField != null) SessionManager.Instance?.SetLocalName(nameField.text);
        }

        void RefreshTransport()
        {
            if (transportLabel == null) return;
            var sm = SessionManager.Instance;
            bool steamAvail = sm != null && sm.Steam.SteamAvailable;
            transportLabel.text = TransportConfig.RequestedTransport == TransportKind.Steam
                ? "Transport: <b>Steam</b>" + (steamAvail ? "" : " <size=70%>(falls back to IP)</size>")
                : "Transport: <b>Direct IP (UTP)</b>";
        }

        // ================================================================ join

        void BuildJoin(Transform root)
        {
            var w = Window(root, Screen.Join, new Vector2(820, 760), "JOIN GAME");
            UIKit.Text(w, "Direct IP (Unity Transport, port " + TransportConfig.Port + ")", 24, TextAlignmentOptions.Left, UIKit.TextDim);
            var row = UIKit.Row(w, 56);
            ipField = UIKit.Input(row, "127.0.0.1", TransportConfig.IpAddress);
            UIKit.LayoutElement(UIKit.Button(row, "Join IP", () =>
            {
                ApplyName();
                SessionManager.Instance?.JoinGame(ipField.text);
                ui.Show(Screen.Connecting);
            }, 56).gameObject, 200, 56);

            UIKit.Text(w, "Steam lobbies (AppID 480)", 24, TextAlignmentOptions.Left, UIKit.TextDim);
            var r2 = UIKit.Row(w, 50);
            UIKit.LayoutElement(UIKit.Button(r2, "Refresh", RefreshLobbies, 50, UIKit.Accent2, 22).gameObject, 200, 50);
            joinStatus = UIKit.Text(r2, "", 22, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(joinStatus.gameObject, -1, 50, 1);
            lobbyList = UIKit.Scroll(w, 330);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.PanelLight * 2.2f);
        }

        void RefreshLobbies()
        {
            var sm = SessionManager.Instance;
            if (sm == null) return;
            sm.Steam.TryInit();
            if (!sm.Steam.SteamAvailable) { joinStatus.text = "Steam is not running - use Direct IP."; return; }
            joinStatus.text = "Searching...";
            sm.Steam.RequestLobbyList(list =>
            {
                if (lobbyList == null) return;
                UIKit.Clear(lobbyList);
                int n = 0;
                if (list != null)
                    foreach (var l in list)
                    {
                        var entry = l;
                        var row = UIKit.Row(lobbyList, 50);
                        var t = UIKit.Text(row, $"{entry.Name}  <color=#a0a0c0>({entry.Members}/{entry.MaxMembers})</color>", 22);
                        UIKit.LayoutElement(t.gameObject, -1, 50, 1);
                        UIKit.LayoutElement(UIKit.Button(row, "Join", () => { sm.JoinSteamLobby(entry.Id); ui.Show(Screen.Connecting); }, 46, UIKit.Accent, 22).gameObject, 140, 46);
                        n++;
                    }
                joinStatus.text = n == 0 ? "No lobbies found (friends can also invite you via the Steam overlay)." : n + " lobbies";
            });
        }

        // ================================================================ connecting

        void BuildConnecting(Transform root)
        {
            var w = Window(root, Screen.Connecting, new Vector2(700, 300), "");
            connectingLabel = UIKit.Text(w, "Connecting...", 34, TextAlignmentOptions.Center);
            UIKit.LayoutElement(connectingLabel.gameObject, -1, 120);
            UIKit.Button(w, "Cancel", () => SessionManager.Instance?.LeaveGame(), 56, UIKit.Bad);
        }

        // ================================================================ lobby

        void BuildLobby(Transform root)
        {
            var w = Window(root, Screen.Lobby, new Vector2(900, 820), "LOBBY");
            lobbyInfo = UIKit.Text(w, "", 22, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(lobbyInfo.gameObject, -1, 70);
            var listBg = UIKit.Panel(w, "Players", new Color(0, 0, 0, 0.2f));
            UIKit.LayoutElement(listBg.gameObject, -1, 300);
            lobbyPlayers = UIKit.Text(listBg, "", 30);
            UIKit.Stretch(lobbyPlayers.rectTransform, 24, 24, 16, 16);

            var tip = UIKit.Text(w, "Every player flies their own ship. Find the derelict <b>Ark</b> to crew a big ship together.", 22, TextAlignmentOptions.Center, UIKit.Accent2);
            UIKit.LayoutElement(tip.gameObject, -1, 60);

            var row = UIKit.Row(w, 60);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            startButton = UIKit.Button(row, "Start Run", () => SessionManager.Instance?.StartRun(), 60, UIKit.Good);
            inviteButton = UIKit.Button(row, "Invite Friends", () => SessionManager.Instance?.Steam.InviteFriends(), 60, UIKit.Accent);
            var row2 = UIKit.Row(w, 60);
            row2.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UIKit.Button(row2, "Hangar", () => ui.Push(Screen.Hangar), 60, UIKit.Accent2);
            UIKit.Button(row2, "Settings", () => ui.Push(Screen.Settings), 60, UIKit.PanelLight * 2.2f);
            UIKit.Button(row2, "Leave", () => SessionManager.Instance?.LeaveGame(), 60, UIKit.Bad);
        }

        void RefreshLobby()
        {
            var sm = SessionManager.Instance;
            var nm = NetworkManager.Singleton;
            if (sm == null || nm == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var p in NetworkPlayer.All)
                sb.AppendLine($"<color={UIKit.Hex(UIKit.Accent)}>></color>  {p.PlayerName.Value}{(p.OwnerClientId == NetworkManager.ServerClientId ? "  <size=70%><color=#ffd080>HOST</color></size>" : "")}{(p.IsOwner ? "  <size=70%>(you)</size>" : "")}");
            lobbyPlayers.text = sb.Length > 0 ? sb.ToString() : "Waiting for players...";
            string info = sm.ActiveTransport == TransportKind.Steam
                ? "Steam lobby - invite friends via the button or the Steam overlay (Shift+Tab)."
                : $"Direct IP - others join with <b>{LocalIp()}</b> : {TransportConfig.Port}";
            lobbyInfo.text = info + $"\nShip: <b>{Meta.SelectedShip}</b>   Stardust: <b>{Meta.Data.Stardust}</b>";
            startButton.interactable = nm.IsHost;
            UIKit.SetLabel(startButton, nm.IsHost ? "Start Run" : "Waiting for host...");
            inviteButton.gameObject.SetActive(sm.ActiveTransport == TransportKind.Steam);
        }

        static string cachedIp;
        static string LocalIp()
        {
            if (cachedIp != null) return cachedIp;
            cachedIp = "127.0.0.1";
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) { cachedIp = a.ToString(); break; }
            }
            catch { }
            return cachedIp;
        }

        // ================================================================ hangar

        void BuildHangar(Transform root)
        {
            var w = Window(root, Screen.Hangar, new Vector2(1100, 900), "HANGAR");
            stardustLabel = UIKit.Text(w, "", 30, TextAlignmentOptions.Center, UIKit.Warn);
            UIKit.LayoutElement(stardustLabel.gameObject, -1, 44);
            var shipRow = UIKit.Row(w, 64);
            shipRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            dartButton = UIKit.Button(shipRow, "Dart", () => { Meta.SelectedShip = ShipClass.Dart; RefreshHangar(); }, 64);
            muleButton = UIKit.Button(shipRow, "Mule", () => { Meta.SelectedShip = ShipClass.Mule; RefreshHangar(); }, 64);
            UIKit.Text(w, "Perks (unlock with Stardust, toggle to equip for the next run)", 22, TextAlignmentOptions.Center, UIKit.TextDim);
            perkList = UIKit.Scroll(w, 520);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.PanelLight * 2.2f);
        }

        void RefreshHangar()
        {
            var d = Meta.Data;
            stardustLabel.text = $"* {d.Stardust} Stardust *   <size=70%><color=#b0b0d0>Runs {d.Runs}  |  Wins {d.Victories}  |  Best sector {d.BestSector}</color></size>";
            bool mule = Meta.IsUnlocked(Meta.MuleLicense);
            UIKit.SetLabel(dartButton, (Meta.SelectedShip == ShipClass.Dart ? "> " : "") + "Dart - agile fighter");
            UIKit.SetLabel(muleButton, mule ? (Meta.SelectedShip == ShipClass.Mule ? "> " : "") + "Mule - tough hauler" : "Mule (locked)");
            muleButton.interactable = mule;
            UIKit.Clear(perkList);
            foreach (var p in Meta.Perks)
            {
                var perk = p;
                var row = UIKit.Panel(perkList, "Perk", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 14, 10);
                UIKit.LayoutElement(row.gameObject, -1, 78);
                var t = UIKit.Text(row, $"<b>{p.Name}</b>\n<size=80%><color=#c0c0e0>{p.Description}</color></size>", 24);
                UIKit.LayoutElement(t.gameObject, -1, 70, 1);
                bool unlocked = Meta.IsUnlocked(p.Id);
                if (!unlocked)
                {
                    var b = UIKit.Button(row, $"Unlock ({p.Cost})", () =>
                    {
                        if (Meta.TryUnlock(perk.Id)) Sfx.PlayUI("stardust");
                        else Sfx.PlayUI("ui_back");
                        RefreshHangar();
                    }, 56, UIKit.Warn, 22);
                    b.interactable = d.Stardust >= p.Cost;
                    UIKit.LayoutElement(b.gameObject, 220, 56);
                }
                else if (p.Id == Meta.MuleLicense)
                    UIKit.LayoutElement(UIKit.Text(row, "Owned", 24, TextAlignmentOptions.Center, UIKit.Good).gameObject, 220, 56);
                else
                {
                    bool eq = Meta.IsEquipped(p.Id);
                    var b = UIKit.Button(row, eq ? "Equipped" : "Equip", () => { Meta.ToggleEquip(perk.Id); RefreshHangar(); }, 56, eq ? UIKit.Good : UIKit.PanelLight * 2.5f, 22);
                    UIKit.LayoutElement(b.gameObject, 220, 56);
                }
            }
        }

        // ================================================================ settings

        void BuildSettings(Transform root)
        {
            var w = Window(root, Screen.Settings, new Vector2(1000, 940), "SETTINGS");
            var content = UIKit.Scroll(w, 700);
            var s = GameSettings.Current;

            Label(content, "Graphics");
            var qRow = UIKit.Row(content, 50);
            UIKit.LayoutElement(UIKit.Text(qRow, "Quality", 24).gameObject, 260, 50);
            UIKit.Dropdown(qRow, QualitySettings.names, s.Quality < 0 ? QualitySettings.GetQualityLevel() : s.Quality, v => { s.Quality = v; s.Save(); });
            UIKit.Toggle(content, "Fullscreen", s.Fullscreen, v => { s.Fullscreen = v; s.Save(); });
            UIKit.Toggle(content, "VSync", s.VSync, v => { s.VSync = v; s.Save(); });
            SliderRow(content, "Field of view", 60, 100, s.Fov, v => { s.Fov = v; s.Save(); });

            Label(content, "Audio");
            SliderRow(content, "Master", 0, 1, s.Master, v => { s.Master = v; s.Save(); });
            SliderRow(content, "Music", 0, 1, s.Music, v => { s.Music = v; s.Save(); });
            SliderRow(content, "Effects", 0, 1, s.SfxVol, v => { s.SfxVol = v; s.Save(); });

            Label(content, "Controls");
            SliderRow(content, "Mouse sensitivity", 0.2f, 3f, s.Sensitivity, v => { s.Sensitivity = v; s.Save(); });
            UIKit.Toggle(content, "Invert Y", s.InvertY, v => { s.InvertY = v; s.Save(); });
            foreach (var (label, action) in VwInput.Rebindable)
            {
                var a = action;
                var row = UIKit.Row(content, 50);
                UIKit.LayoutElement(UIKit.Text(row, label, 24).gameObject, 260, 50);
                Button b = null;
                b = UIKit.Button(row, VwInput.BindingDisplay(a), () =>
                {
                    UIKit.SetLabel(b, "Press a key... (Esc cancels)");
                    VwInput.StartRebind(a, cancelled => UIKit.SetLabel(b, VwInput.BindingDisplay(a)));
                }, 48, UIKit.PanelLight * 2.5f, 22);
                UIKit.LayoutElement(b.gameObject, 360, 48);
            }
            UIKit.Button(content, "Reset key bindings", () => { VwInput.ResetBindings(); ui.Show(Screen.Settings); }, 48, UIKit.Accent2, 22);
            var fixedKeys = UIKit.Text(content, "Fixed: WASD move/thrust, Mouse look/steer, Q/E roll, Space/Ctrl up/down, 1-4 tools, Esc pause, F1 cheats", 20, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(fixedKeys.gameObject, -1, 56);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.PanelLight * 2.2f);
        }

        static void Label(Transform parent, string text)
        {
            var t = UIKit.Text(parent, text, 28, TextAlignmentOptions.Left, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(t.gameObject, -1, 44);
        }

        static void SliderRow(Transform parent, string label, float min, float max, float value, System.Action<float> onChange)
        {
            var row = UIKit.Row(parent, 46);
            UIKit.LayoutElement(UIKit.Text(row, label, 24).gameObject, 260, 46);
            var val = UIKit.Text(row, value.ToString("0.##"), 22, TextAlignmentOptions.Right, UIKit.TextDim);
            UIKit.Slider(row, min, max, value, v => { val.text = v.ToString("0.##"); onChange(v); });
            UIKit.LayoutElement(val.gameObject, 70, 46);
            val.transform.SetAsLastSibling();
        }

        // ================================================================ pause

        void BuildPause(Transform root)
        {
            var w = Window(root, Screen.Pause, new Vector2(560, 560), "PAUSED");
            UIKit.Button(w, "Resume", ui.Back, 64);
            UIKit.Button(w, "Settings", () => ui.Push(Screen.Settings), 64, UIKit.PanelLight * 2.2f);
            UIKit.Button(w, "Leave to Menu", () => SessionManager.Instance?.LeaveGame(), 64, UIKit.Accent2);
            UIKit.Button(w, "Quit Game", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 64, UIKit.Bad);
            var t = UIKit.Text(w, "The game keeps running while paused (co-op).", 20, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(t.gameObject, -1, 40);
        }

        // ================================================================ results

        void BuildResults(Transform root)
        {
            var w = Window(root, Screen.Results, new Vector2(900, 760), "");
            resultsTitle = UIKit.Text(w, "", 56, TextAlignmentOptions.Center, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(resultsTitle.gameObject, -1, 140);
            resultsBody = UIKit.Text(w, "", 30, TextAlignmentOptions.Center);
            UIKit.LayoutElement(resultsBody.gameObject, -1, 360);
            var row = UIKit.Row(w, 64);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            resultsLobby = UIKit.Button(row, "Return to Lobby", () => RunManager.Instance?.ReturnToLobbyServerRpc(), 64, UIKit.Good);
            UIKit.Button(row, "Hangar", () => ui.Push(Screen.Hangar), 64, UIKit.Accent2);
            UIKit.Button(row, "Leave", () => SessionManager.Instance?.LeaveGame(), 64, UIKit.Bad);
        }

        void RefreshResults()
        {
            var s = ui.LastSummary;
            if (s == null) { resultsTitle.text = "RUN OVER"; resultsBody.text = ""; return; }
            resultsTitle.text = s.Victory
                ? "DEMO COMPLETE\n<size=55%><color=#ffd8a0>Thanks for playing VOIDWARD!</color></size>"
                : "RUN OVER\n<size=55%><color=#ffa0b0>" + s.EndReason + "</color></size>";
            int min = Mathf.FloorToInt(s.RunTimeSeconds / 60f), sec = Mathf.FloorToInt(s.RunTimeSeconds % 60f);
            resultsBody.text =
                $"Sector reached: <b>{s.SectorReached}/3</b>\n" +
                $"Distance traveled: <b>{s.DistanceTraveled / 1000f:0.0} km</b>\n" +
                $"Enemies destroyed: <b>{s.Kills}</b>\n" +
                $"Loot collected: <b>{s.LootCollected}</b>\n" +
                $"Run time: <b>{min:00}:{sec:00}</b>\n\n" +
                $"<color=#ffd070><size=120%>+{s.StardustEarned} Stardust</size></color>\n" +
                $"<size=75%><color=#b0b0d0>Total: {Meta.Data.Stardust} - spend it in the Hangar</color></size>";
            var nm = NetworkManager.Singleton;
            resultsLobby.interactable = nm != null && nm.IsHost;
        }

        // ================================================================ per-frame

        public void Update()
        {
            var s = ui.Current;
            if (s == Screen.Lobby)
            {
                lobbyRefresh -= Time.unscaledDeltaTime;
                if (lobbyRefresh <= 0f) { lobbyRefresh = 0.5f; RefreshLobby(); }
            }
            if (s == Screen.Connecting && connectingLabel != null)
            {
                var sm = SessionManager.Instance;
                connectingLabel.text = sm != null && sm.InSession ? "Connected - preparing lobby" + new string('.', 1 + (int)(Time.unscaledTime * 2) % 3) : "Connecting" + new string('.', 1 + (int)(Time.unscaledTime * 2) % 3);
            }
        }
    }
}
