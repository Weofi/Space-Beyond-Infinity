using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
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
        TMP_InputField nameField, ipField, lobbyIdField;
        RectTransform lobbyList;
        TextMeshProUGUI joinStatus;
        // lobby
        TextMeshProUGUI lobbyPlayers, lobbyInfo;
        Button startButton, continueButton, inviteButton, copyIdButton;
        // hangar
        TextMeshProUGUI memoriesLabel;
        RectTransform perkList;
        Button dartButton, muleButton;
        // results
        TextMeshProUGUI resultsTitle, resultsBody, resultsMemories, resultsLog;
        // Results Memories count-up (#43): starts after a short beat, ticks up, bursts when done.
        int memoriesTarget, memoriesShown, memoriesTotal;
        float memoriesStart = -1f, memoriesNextTick;
        string celebratePerk;
        Button resultsLobby, resultsNewLoop;
        // connecting
        TextMeshProUGUI connectingLabel;
        float lobbyRefresh;

        public MenuScreens(UIRoot ui, RectTransform root)
        {
            this.ui = ui;
            backdrop = UIKit.Rect(root, "MenuBackdrop");
            UIKit.Stretch(backdrop);
            var shade = UIKit.Image(backdrop, "Shade", new Color(0.02f, 0.05f, 0.14f, 0.18f), false);
            UIKit.Stretch(shade.rectTransform);
            var side = UIKit.Image(backdrop, "SideShade", new Color(0.02f, 0.05f, 0.14f, 0.75f), false);
            side.sprite = UIKit.Gradient;
            side.rectTransform.anchorMin = Vector2.zero;
            side.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            side.rectTransform.offsetMin = side.rectTransform.offsetMax = Vector2.zero;
            side.raycastTarget = false;

            BuildMain(root);
            BuildJoin(root);
            BuildLobby(root);
            BuildHangar(root);
            BuildSettings(root);
            BuildPause(root);
            BuildResults(root);
            BuildConnecting(root);
            // Start hidden so the first Show() runs the intro animations.
            foreach (var kv in screens) kv.Value.gameObject.SetActive(false);
        }

        RectTransform Window(Transform root, Screen s, Vector2 size, string title)
        {
            var w = UIKit.FramedPanel(root, s + "Screen", title);
            UIKit.Place(w, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            w.gameObject.AddComponent<UIScreenFx>();
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
            UIKit.Stretch(w);
            screens[Screen.Main] = w;

            // Title block (top right)
            var titleBlock = UIKit.Rect(w, "Title");
            UIKit.Place(titleBlock, new Vector2(1, 1), new Vector2(-90, -56), new Vector2(1000, 290), new Vector2(1, 1));
            titleBlock.gameObject.AddComponent<UIScreenFx>().SlideFrom = new Vector2(0, 40);
            var title = UIKit.Text(titleBlock, "VOIDWARD", 150, TextAlignmentOptions.TopRight, Color.white, FontStyles.Bold);
            UIKit.Place(title.rectTransform, new Vector2(1, 1), Vector2.zero, new Vector2(1000, 150), new Vector2(1, 1));
            title.characterSpacing = 10;
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Color.white, Color.white, new Color(0.62f, 0.8f, 1f), new Color(0.62f, 0.8f, 1f));
            title.fontSharedMaterial = UIKit.ShadowMaterial;
            var sub = UIKit.Text(titleBlock, "Find the way home. Together.", 40, TextAlignmentOptions.TopRight, UIKit.Accent, FontStyles.Bold | FontStyles.UpperCase);
            UIKit.Place(sub.rectTransform, new Vector2(1, 1), new Vector2(-6, -150), new Vector2(1000, 50), new Vector2(1, 1));
            sub.characterSpacing = 6;
            sub.fontSharedMaterial = UIKit.ShadowMaterial;
            var badge = UIKit.FramedPanel(titleBlock, "Badge", null, 0, 0);
            UIKit.Place(badge, new Vector2(1, 1), new Vector2(-6, -214), new Vector2(330, 44), new Vector2(1, 1));
            var badgeText = UIKit.Text(badge, "Co-op  |  1-4 Pilots", 22, TextAlignmentOptions.Center, UIKit.TextColor, FontStyles.Bold | FontStyles.UpperCase);
            badgeText.characterSpacing = 4;
            UIKit.LayoutElement(badgeText.gameObject, -1, 44);

            // Console: chunky buttons mounted on a pillar with a pedestal (left)
            var console = UIKit.Rect(w, "Console");
            UIKit.Place(console, new Vector2(0, 0.5f), new Vector2(110, -10), new Vector2(520, 720), new Vector2(0, 0.5f));
            console.gameObject.AddComponent<UIScreenFx>().SlideFrom = new Vector2(-60, 0);

            var pillar = UIKit.Image(console, "Pillar", UIKit.FrameShade);
            UIKit.Place(pillar.rectTransform, new Vector2(0, 1), new Vector2(26, -6), new Vector2(64, 640), new Vector2(0, 1));
            var pillarCore = UIKit.Image(pillar.transform, "Core", new Color(0.06f, 0.11f, 0.24f));
            UIKit.Stretch(pillarCore.rectTransform, 10, 10, 10, 10);
            var powerLine = UIKit.Image(pillarCore.transform, "Power", UIKit.Accent, false);
            UIKit.Place(powerLine.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 600));
            powerLine.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            powerLine.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            powerLine.rectTransform.offsetMin = new Vector2(-3, 8);
            powerLine.rectTransform.offsetMax = new Vector2(3, -8);

            var pedestal = UIKit.Image(console, "Pedestal", UIKit.Frame);
            UIKit.Place(pedestal.rectTransform, new Vector2(0, 0), new Vector2(-6, 22), new Vector2(250, 74), new Vector2(0, 0));
            var pedFace = UIKit.Image(pedestal.transform, "Face", new Color(0.1f, 0.19f, 0.38f));
            pedFace.sprite = UIKit.ChamferFace;
            UIKit.Stretch(pedFace.rectTransform, 14, 14, 10, 12);
            var pedLight = UIKit.Image(pedFace.transform, "Light", UIKit.Accent, false);
            UIKit.Place(pedLight.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 6));
            for (int i = -1; i <= 1; i += 2)
            {
                var bolt = UIKit.Image(pedFace.transform, "Bolt", UIKit.FrameShade);
                bolt.sprite = UIKit.Circle;
                UIKit.Place(bolt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i * 94f, 0f), new Vector2(14, 14));
            }
            var foot = UIKit.Image(console, "Foot", UIKit.FrameShade);
            UIKit.Place(foot.rectTransform, new Vector2(0, 0), new Vector2(-26, 0), new Vector2(290, 30), new Vector2(0, 0));
            foot.transform.SetSiblingIndex(0);

            var column = UIKit.Rect(console, "Buttons");
            UIKit.Place(column, new Vector2(0, 1), new Vector2(0, -24), new Vector2(480, 520), new Vector2(0, 1));
            UIKit.VLayout(column.gameObject, 22, 0, TextAnchor.UpperLeft);
            var entries = new (string label, string icon, Color color, UnityEngine.Events.UnityAction action)[]
            {
                ("Host Game", Icons.Rocket, UIKit.Primary, () => { ApplyName(); SessionManager.Instance?.HostGame(); ui.Show(Screen.Connecting); }),
                ("Join Game", Icons.Join, UIKit.Primary, () => { ApplyName(); ui.Push(Screen.Join); }),
                ("Hangar", Icons.Hangar, UIKit.Primary, () => ui.Push(Screen.Hangar)),
                ("Settings", Icons.Settings, UIKit.Primary, () => ui.Push(Screen.Settings)),
                ("Exit to Desktop", Icons.Quit, UIKit.Secondary, Quit),
            };
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                var b = UIKit.Button(column, e.label, e.action, 86, e.color, 34, e.icon);
                var fx = b.GetComponent<ChunkyButton>();
                fx.HoverSlide = 16f;
                fx.HoverScale = 1.04f;
                fx.IntroDelay = 0.06f * i;
            }

            // Pilot card (bottom right): callsign + transport
            var card = UIKit.FramedPanel(w, "PilotCard", null, 22, 10);
            UIKit.Place(card, new Vector2(1, 0), new Vector2(-90, 64), new Vector2(600, 214), new Vector2(1, 0));
            card.gameObject.AddComponent<UIScreenFx>().SlideFrom = new Vector2(0, -40);

            var nameRow = UIKit.Row(card, 52);
            var nameIcon = UIKit.IconText(nameRow, Icons.Person, 34, UIKit.Accent);
            UIKit.LayoutElement(nameIcon.gameObject, 40, 52);
            UIKit.LayoutElement(UIKit.Text(nameRow, "Callsign", 24, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold | FontStyles.UpperCase).gameObject, 140, 52);
            nameField = UIKit.Input(nameRow, "Your name", SessionManager.Instance != null ? SessionManager.Instance.LocalPlayerName : "Pilot");
            nameField.onEndEdit.AddListener(v =>
            {
                SessionManager.Instance?.SetLocalName(v);
                GameSettings.Current.PlayerName = v;
                GameSettings.Current.Save();
            });

            var tRow = UIKit.Row(card, 56);
            var netIcon = UIKit.IconText(tRow, Icons.Lan, 34, UIKit.Accent);
            UIKit.LayoutElement(netIcon.gameObject, 40, 56);
            transportLabel = UIKit.Text(tRow, "", 24, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(transportLabel.gameObject, -1, 56, 1);
            UIKit.LayoutElement(UIKit.Button(tRow, "Switch", () =>
            {
                TransportConfig.SetRequested(TransportConfig.RequestedTransport == TransportKind.UTP ? TransportKind.Steam : TransportKind.UTP);
                RefreshTransport();
            }, 56, UIKit.Secondary, 22, Icons.Swap).gameObject, 170, 56);

            var hint = UIKit.Text(card, "F1 debug cheats   |   Steam test AppID 480", 18, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(hint.gameObject, -1, 26);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            }, 56, UIKit.Primary, 24, Icons.Join).gameObject, 200, 56);

            UIKit.Text(w, "Steam lobbies (worldwide)", 24, TextAlignmentOptions.Left, UIKit.TextDim);
            var r2 = UIKit.Row(w, 50);
            UIKit.LayoutElement(UIKit.Button(r2, "Refresh", RefreshLobbies, 50, UIKit.Secondary, 22, Icons.Refresh).gameObject, 200, 50);
            joinStatus = UIKit.Text(r2, "", 22, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(joinStatus.gameObject, -1, 50, 1);
            lobbyList = UIKit.Scroll(w, 250);
            var r3 = UIKit.Row(w, 52);
            lobbyIdField = UIKit.Input(r3, "Steam lobby ID (from the host's lobby screen)", "");
            UIKit.LayoutElement(UIKit.Button(r3, "Join ID", JoinLobbyId, 52, UIKit.Primary, 22, Icons.Join).gameObject, 200, 52);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.Secondary, 26, Icons.Back);
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
                        string phase = entry.Phase == "Playing" ? "<color=#ffd080>in run</color>" : "lobby";
                        string ver = entry.VersionMatches ? "" : $"  <color=#ff8080>v{Ascii(entry.Version)}</color>";
                        var t = UIKit.Text(row, $"{Ascii(entry.Name)}  <color=#a0a0c0>({entry.Members}/{entry.MaxMembers}, {phase})</color>{ver}", 22);
                        UIKit.LayoutElement(t.gameObject, -1, 50, 1);
                        var join = UIKit.Button(row, "Join", () => { ApplyName(); sm.JoinSteamLobby(entry.Id); ui.Show(Screen.Connecting); }, 46, UIKit.Primary, 22, Icons.Join);
                        join.interactable = entry.VersionMatches && entry.Members < entry.MaxMembers;
                        UIKit.LayoutElement(join.gameObject, 140, 46);
                        n++;
                    }
                joinStatus.text = n == 0 ? "No Voidward lobbies found (or join via Steam invite / lobby ID)." : n + (n == 1 ? " lobby" : " lobbies");
            });
        }

        void JoinLobbyId()
        {
            var sm = SessionManager.Instance;
            if (sm == null || lobbyIdField == null) return;
            if (!ulong.TryParse(lobbyIdField.text.Trim(), out ulong id) || id == 0) { joinStatus.text = "Invalid lobby ID."; return; }
            sm.Steam.TryInit();
            if (!sm.Steam.SteamAvailable) { joinStatus.text = "Steam is not running - use Direct IP."; return; }
            ApplyName();
            sm.JoinSteamLobby(id);
            ui.Show(Screen.Connecting);
        }

        /// <summary>Steam names can contain any Unicode; TMP only has ASCII glyphs.</summary>
        static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= 32 && c < 127 && c != '<' && c != '>' ? c : '?');
            return sb.ToString();
        }

        // ================================================================ connecting

        void BuildConnecting(Transform root)
        {
            var w = Window(root, Screen.Connecting, new Vector2(700, 300), "");
            connectingLabel = UIKit.Text(w, "Connecting...", 34, TextAlignmentOptions.Center);
            UIKit.LayoutElement(connectingLabel.gameObject, -1, 120);
            UIKit.Button(w, "Cancel", () => SessionManager.Instance?.LeaveGame(), 56, UIKit.Bad, 26, Icons.Close);
        }

        // ================================================================ lobby

        void BuildLobby(Transform root)
        {
            var w = Window(root, Screen.Lobby, new Vector2(900, 750), "LOBBY");
            lobbyInfo = UIKit.Text(w, "", 22, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(lobbyInfo.gameObject, -1, 96);
            var listBg = UIKit.Panel(w, "Players", new Color(0, 0, 0, 0.2f));
            UIKit.LayoutElement(listBg.gameObject, -1, 274);
            lobbyPlayers = UIKit.Text(listBg, "", 30);
            UIKit.Stretch(lobbyPlayers.rectTransform, 24, 24, 16, 16);

            var tip = UIKit.Text(w, "Route home: <b>M33 > M31 > NGC 6822 > Leo I > LMC > Milky Way</b>\nThe crew flies the <b>Nostos</b> home together; every player has a Dart docked at it.", 22, TextAlignmentOptions.Center, UIKit.Accent2);
            UIKit.LayoutElement(tip.gameObject, -1, 60);

            var row = UIKit.Row(w, 64);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            startButton = UIKit.Button(row, "Start Mission", () => SessionManager.Instance?.StartRun(), 64, UIKit.Good, 28, Icons.Play);
            continueButton = UIKit.Button(row, "Continue Loop", () => SessionManager.Instance?.ContinueRun(), 64, UIKit.Primary, 26, Icons.Replay);
            inviteButton = UIKit.Button(row, "Invite Friends", () => SessionManager.Instance?.Steam.InviteFriends(), 64, UIKit.Primary, 28, Icons.Invite);
            copyIdButton = UIKit.Button(row, "Copy Lobby ID", CopyLobbyId, 64, UIKit.Secondary, 24);
            var row2 = UIKit.Row(w, 60);
            row2.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UIKit.Button(row2, "Hangar", () => ui.Push(Screen.Hangar), 60, UIKit.Primary, 24, Icons.Hangar);
            UIKit.Button(row2, "Settings", () => ui.Push(Screen.Settings), 60, UIKit.Secondary, 24, Icons.Settings);
            UIKit.Button(row2, "Leave", () => SessionManager.Instance?.LeaveGame(), 60, UIKit.Bad, 24, Icons.Quit);
        }

        void RefreshLobby()
        {
            var sm = SessionManager.Instance;
            var nm = NetworkManager.Singleton;
            if (sm == null || nm == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var p in NetworkPlayer.All)
                sb.AppendLine($"<color={UIKit.Hex(p.CrewColor)}>></color>  <color={UIKit.Hex(p.CrewColor)}>{p.PlayerName.Value}</color>{(p.OwnerClientId == NetworkManager.ServerClientId ? "  <size=70%><color=#ffd080>HOST</color></size>" : "")}{(p.IsOwner ? "  <size=70%>(you)</size>" : "")}");
            lobbyPlayers.text = sb.Length > 0 ? sb.ToString() : "Waiting for players...";
            bool steam = sm.ActiveTransport == TransportKind.Steam;
            ulong lobbyId = sm.Steam.CurrentLobbyId;
            string info = steam
                ? "Steam lobby - invite friends via the button or the Steam overlay (Shift+Tab)." + (lobbyId != 0 ? $"\nLobby ID: <b>{lobbyId}</b>" : "")
                : $"Direct IP - others join with <b>{LocalIp()}</b> : {TransportConfig.Port}";
            lobbyInfo.text = info + $"\nLoop <b>{Meta.Loops + 1}</b>   Ship: <b>{Meta.SelectedShip}</b>   Memory fragments: <b>{Meta.Data.Memories}</b>";
            startButton.interactable = nm.IsHost;
            UIKit.SetLabel(startButton, nm.IsHost ? (Meta.Loops == 0 && !RunSave.Exists ? "Start Mission" : "New Loop") : "Waiting for host...");
            continueButton.gameObject.SetActive(nm.IsHost && RunSave.Exists);
            inviteButton.gameObject.SetActive(steam);
            copyIdButton.gameObject.SetActive(steam && lobbyId != 0);
        }

        void CopyLobbyId()
        {
            var sm = SessionManager.Instance;
            if (sm == null || sm.Steam.CurrentLobbyId == 0) return;
            GUIUtility.systemCopyBuffer = sm.Steam.CurrentLobbyId.ToString();
            EventBus.RaiseNotification("Lobby ID copied");
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
            memoriesLabel = UIKit.Text(w, "", 30, TextAlignmentOptions.Center, UIKit.Warn);
            UIKit.LayoutElement(memoriesLabel.gameObject, -1, 44);
            var shipRow = UIKit.Row(w, 64);
            shipRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            dartButton = UIKit.Button(shipRow, "Dart", () => { Meta.SelectedShip = ShipClass.Dart; RefreshHangar(); }, 64, UIKit.Primary, 26, Icons.Flight);
            muleButton = UIKit.Button(shipRow, "Mule", () => { Meta.SelectedShip = ShipClass.Mule; RefreshHangar(); }, 64, UIKit.Primary, 26, Icons.Rocket);
            UIKit.Text(w, "Perks (unlock with Memory fragments, toggle to equip for the next run)", 22, TextAlignmentOptions.Center, UIKit.TextDim);
            perkList = UIKit.Scroll(w, 520);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.Secondary, 26, Icons.Back);
        }

        void RefreshHangar()
        {
            var d = Meta.Data;
            memoriesLabel.text = $"* {d.Memories} Memory fragments *   <size=70%><color=#b0b0d0>Runs {d.Runs}  |  Homecomings {d.Victories}  |  Best galaxy {d.BestSector}/5</color></size>";
            bool mule = Meta.IsUnlocked(Meta.MuleLicense);
            UIKit.SetLabel(dartButton, (Meta.SelectedShip == ShipClass.Dart ? "> " : "") + "Dart - agile fighter");
            UIKit.SetLabel(muleButton, mule ? (Meta.SelectedShip == ShipClass.Mule ? "> " : "") + "Mule - tough hauler" : "Mule (locked)");
            muleButton.interactable = mule;
            UIKit.SetButtonColor(dartButton, Meta.SelectedShip == ShipClass.Dart ? UIKit.Primary : UIKit.Secondary);
            UIKit.SetButtonColor(muleButton, Meta.SelectedShip == ShipClass.Mule ? UIKit.Primary : UIKit.Secondary);
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
                        if (Meta.TryUnlock(perk.Id)) { FeedbackFx.PerkUnlocked(); celebratePerk = perk.Id; }
                        else Sfx.PlayUI("ui_back");
                        RefreshHangar();
                    }, 56, UIKit.Warn, 22);
                    b.interactable = d.Memories >= p.Cost;
                    UIKit.LayoutElement(b.gameObject, 220, 56);
                }
                else if (p.Id == Meta.MuleLicense)
                    UIKit.LayoutElement(UIKit.Text(row, "Owned", 24, TextAlignmentOptions.Center, UIKit.Good).gameObject, 220, 56);
                else
                {
                    bool eq = Meta.IsEquipped(p.Id);
                    var b = UIKit.Button(row, eq ? "Equipped" : "Equip", () => { Meta.ToggleEquip(perk.Id); RefreshHangar(); }, 56, eq ? UIKit.Good : UIKit.Secondary, 22);
                    UIKit.LayoutElement(b.gameObject, 220, 56);
                }
                if (celebratePerk == p.Id)
                {
                    celebratePerk = null;
                    UnlockBurst.Play(row, UIKit.Warn, FeedbackFx.RewardFx);
                    UnlockBurst.Play(memoriesLabel.rectTransform, UIKit.Warn, FeedbackFx.RewardFx * 0.5f);
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

            Label(content, "Feedback");
            SliderRow(content, "Screen shake", 0, 1, s.ScreenShake, v => { s.ScreenShake = v; s.Save(); });
            SliderRow(content, "Hit-stop", 0, 1, s.HitStop, v => { s.HitStop = v; s.Save(); });
            UIKit.Toggle(content, "Damage numbers", s.DamageNumbers, v => { s.DamageNumbers = v; s.Save(); });
            UIKit.Toggle(content, "Low health warning", s.LowHealthWarning, v => { s.LowHealthWarning = v; s.Save(); });
            SliderRow(content, "Reward effects", 0, 1, s.RewardFx, v => { s.RewardFx = v; s.Save(); });

            Label(content, "Audio");
            SliderRow(content, "Master", 0, 1, s.Master, v => { s.Master = v; s.Save(); });
            SliderRow(content, "Music", 0, 1, s.Music, v => { s.Music = v; s.Save(); });
            SliderRow(content, "Effects", 0, 1, s.SfxVol, v => { s.SfxVol = v; s.Save(); });
            UIKit.Toggle(content, "Dynamic combat music", s.DynamicMusic, v => { s.DynamicMusic = v; s.Save(); });

            Label(content, "Controls");
            SliderRow(content, "Mouse sensitivity", 0.2f, 3f, s.Sensitivity, v => { s.Sensitivity = v; s.Save(); });
            SliderRow(content, "Ship sensitivity", 0.2f, 3f, s.ShipSensitivity, v => { s.ShipSensitivity = v; s.Save(); });
            UIKit.Toggle(content, "Ship aim assist", s.ShipAimAssist, v => { s.ShipAimAssist = v; s.Save(); });
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
                }, 48, UIKit.Secondary, 22);
                UIKit.LayoutElement(b.gameObject, 360, 48);
            }
            UIKit.Button(content, "Reset key bindings", () => { VwInput.ResetBindings(); ui.Show(Screen.Settings); }, 48, UIKit.Secondary, 22, Icons.Replay);
            var fixedKeys = UIKit.Text(content, "Fixed: WASD move/thrust, Mouse look/steer, Q/E roll, Space/Ctrl up/down, 1-4 tools, Esc pause, F1 cheats", 20, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(fixedKeys.gameObject, -1, 56);
            UIKit.Button(w, "Back", ui.Back, 56, UIKit.Secondary, 26, Icons.Back);
        }

        static void Label(Transform parent, string text)
        {
            var t = UIKit.Text(parent, text, 28, TextAlignmentOptions.Left, UIKit.Accent, FontStyles.Bold | FontStyles.UpperCase);
            t.characterSpacing = 4;
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
            UIKit.Button(w, "Resume", ui.Back, 64, UIKit.Primary, 28, Icons.Play);
            UIKit.Button(w, "Settings", () => ui.Push(Screen.Settings), 64, UIKit.Secondary, 28, Icons.Settings);
            UIKit.Button(w, "Leave to Menu", () => SessionManager.Instance?.LeaveGame(), 64, UIKit.Secondary, 28, Icons.Quit);
            UIKit.Button(w, "Quit Game", Quit, 64, UIKit.Bad, 28, Icons.Close);
            var t = UIKit.Text(w, "The game keeps running while paused (co-op).", 20, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(t.gameObject, -1, 40);
        }

        // ================================================================ results

        void BuildResults(Transform root)
        {
            // The logbook of the loop (#86): cause, stats, the crew's log entries, Memories; the host starts the next loop.
            var w = Window(root, Screen.Results, new Vector2(1180, 900), "");
            resultsTitle = UIKit.Text(w, "", 52, TextAlignmentOptions.Center, UIKit.TextColor, FontStyles.Bold);
            resultsTitle.fontSharedMaterial = UIKit.ShadowMaterial;
            UIKit.LayoutElement(resultsTitle.gameObject, -1, 124);
            resultsBody = UIKit.Text(w, "", 25, TextAlignmentOptions.Center);
            UIKit.LayoutElement(resultsBody.gameObject, -1, 108);
            var head = UIKit.Text(w, "CREW LOGBOOK", 20, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold);
            head.characterSpacing = 8;
            UIKit.LayoutElement(head.gameObject, -1, 26);
            var log = UIKit.Scroll(w, 300);
            resultsLog = UIKit.Text(log, "", 22, TextAlignmentOptions.TopLeft, UIKit.TextColor);
            resultsLog.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            resultsMemories = UIKit.Text(w, "", 28, TextAlignmentOptions.Center);
            UIKit.LayoutElement(resultsMemories.gameObject, -1, 96);
            var row = UIKit.Row(w, 64);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            resultsNewLoop = UIKit.Button(row, "New Loop", () => RunManager.Instance?.NewLoopServerRpc(), 64, UIKit.Good, 26, Icons.Play);
            resultsLobby = UIKit.Button(row, "Lobby", () => RunManager.Instance?.ReturnToLobbyServerRpc(), 64, UIKit.Primary, 24, Icons.Replay);
            UIKit.Button(row, "Hangar", () => ui.Push(Screen.Hangar), 64, UIKit.Primary, 24, Icons.Hangar);
            UIKit.Button(row, "Leave", () => SessionManager.Instance?.LeaveGame(), 64, UIKit.Bad, 24, Icons.Quit);
        }

        void RefreshResults()
        {
            var s = ui.LastSummary;
            if (s == null) { resultsTitle.text = "LOGBOOK"; resultsBody.text = ""; resultsLog.text = ""; resultsMemories.text = ""; memoriesStart = -1f; return; }
            resultsTitle.text = s.Victory
                ? $"HOMECOMING - LOOP {s.Loop}\n<size=55%><color=#ffd8a0>" + s.EndReason + "</color></size>"
                : $"LOOP {s.Loop} - THE LOOP RESETS\n<size=55%><color=#C9A8FF>" + s.EndReason + "</color></size>";
            var galaxy = ContentCatalog.GetGalaxy(s.GalaxyReached);
            string eta = s.Victory ? "home" : DriveMath.Eta(s.HomeEtaMinutes);
            string best = Meta.Data.BestEtaMinutes > 0f ? DriveMath.Eta(Meta.Data.BestEtaMinutes) : "--";
            resultsBody.text =
                $"Galaxy <b>{s.GalaxyReached}/{GalaxyDef.Count}{(galaxy != null ? " - " + galaxy.DisplayName : "")}</b>     Warp core <b>{DriveComponentDef.TierName(DriveSlot.Core, s.DriveTier)}</b>     Home ETA <b>{eta}</b> <size=80%><color=#b0b0d0>(best {best})</color></size>\n" +
                $"Run time <b>{HudView.Clock(s.RunTimeSeconds)}</b>     Distance <b>{s.DistanceTraveled / 1000f:0.0} km</b>     Enemies <b>{s.Kills}</b>     Loot <b>{s.LootCollected}</b>\n" +
                $"<size=80%><color=#b0b0d0>Loops so far: {Meta.Loops}   Homecomings: {Meta.Data.Victories}</color></size>";
            resultsLog.text = string.IsNullOrEmpty(s.Log) ? "<color=#b0b0d0>No entries.</color>" : LogText(s.Log);
            memoriesTarget = Mathf.Max(0, s.MemoriesEarned);
            memoriesTotal = Meta.Data.Memories;
            memoriesShown = 0;
            memoriesStart = Time.unscaledTime + 0.7f;
            SetMemoriesText();
            var nm = NetworkManager.Singleton;
            bool host = nm != null && nm.IsHost;
            resultsLobby.interactable = host;
            resultsNewLoop.interactable = host;
            UIKit.SetLabel(resultsNewLoop, host ? "New Loop" : "Waiting for host...");
        }

        /// <summary>Logbook lines "[h:mm:ss] text" with a dimmed time stamp.</summary>
        static string LogText(string log)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in log.Split('\n'))
            {
                int close = line.IndexOf(']');
                if (line.StartsWith("[") && close > 0) sb.Append("<color=#8FA3C8>").Append(line, 0, close + 1).Append("</color>").Append(line, close + 1, line.Length - close - 1);
                else sb.Append(line);
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        void SetMemoriesText()
        {
            resultsMemories.text = $"<color=#ffd070><size=120%>+{memoriesShown} Memory fragments</size></color>\n" +
                $"<size=75%><color=#b0b0d0>Total: {memoriesTotal - memoriesTarget + memoriesShown} - spend it in the Hangar</color></size>";
        }

        /// <summary>Results: count the earned Memories up with rising ticks, then chime + burst.</summary>
        void TickMemories()
        {
            if (memoriesStart < 0f || Time.unscaledTime < memoriesStart) return;
            float dur = Mathf.Clamp(0.4f + memoriesTarget * 0.012f, 0.4f, 1.8f);
            float k = Mathf.Clamp01((Time.unscaledTime - memoriesStart) / dur);
            int shown = Mathf.RoundToInt(memoriesTarget * (1f - (1f - k) * (1f - k)));
            if (shown != memoriesShown)
            {
                memoriesShown = shown;
                SetMemoriesText();
                if (Time.unscaledTime >= memoriesNextTick) { memoriesNextTick = Time.unscaledTime + 0.045f; FeedbackFx.MemoriesTick(k); }
            }
            if (k >= 1f)
            {
                memoriesStart = -1f;
                memoriesShown = memoriesTarget;
                SetMemoriesText();
                FeedbackFx.MemoriesDone(memoriesTarget);
                if (memoriesTarget > 0) UnlockBurst.Play(resultsMemories.rectTransform, UIKit.Warn, FeedbackFx.RewardFx);
            }
        }

        // ================================================================ per-frame

        public void Update()
        {
            var s = ui.Current;
            if (s == Screen.Results) TickMemories();
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
