using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.AI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>
    /// In-game HUD: vitals, ship status, hotbar, crosshair + prompts, bearing compass, Collapse
    /// distance, objective/gate marker, ping markers, toasts, boss bar and damage feedback.
    /// </summary>
    public class HudView
    {
        readonly RectTransform root;
        Image hpFill, o2Fill, energyFill, hullFill, shieldFill, fuelFill, collapseFill, bossFill;
        TextMeshProUGUI hpText, o2Text, energyText, shipName, shipText, prompt, collapseText, sectorText, objective, creditsText, jumpText, deathText, bossText, o2Warn;
        RectTransform shipPanel, bossPanel, compass, markerLayer, toastLayer, hotbarRoot;
        Image crosshair, vignette;
        readonly Image[] hotbar = new Image[4];
        readonly TextMeshProUGUI[] hotbarLabels = new TextMeshProUGUI[4];
        readonly List<(TextMeshProUGUI t, float until)> toasts = new List<(TextMeshProUGUI, float)>();
        readonly List<Ping> pings = new List<Ping>();
        readonly Dictionary<string, TextMeshProUGUI> compassMarks = new Dictionary<string, TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> pingLabels = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> siteLabels = new List<TextMeshProUGUI>();
        TextMeshProUGUI gateMarker, stationMarker, arkMarker;
        float vignetteAlpha;
        float lowO2Beep;
        bool lowO2;
        bool stormWarned;
        HealthComponentHook hook;

        struct Ping { public Vector3 Pos; public PingKind Kind; public string Label; public float Until; }

        public HudView(RectTransform canvas)
        {
            root = UIKit.Rect(canvas, "HUD");
            UIKit.Stretch(root);

            vignette = UIKit.Image(root, "Vignette", new Color(1f, 0.15f, 0.3f, 0f), false);
            UIKit.Stretch(vignette.rectTransform);
            vignette.sprite = MakeVignette();
            vignette.raycastTarget = false;

            markerLayer = UIKit.Rect(root, "Markers");
            UIKit.Stretch(markerLayer);
            gateMarker = Marker("[ GATE ]", UIKit.Accent);
            stationMarker = Marker("[ STATION ]", UIKit.Warn);
            arkMarker = Marker("[ ARK ]", UIKit.Accent2);

            // Vitals (bottom-left)
            var vit = UIKit.Panel(root, "Vitals");
            UIKit.Place(vit, new Vector2(0, 0), new Vector2(30, 30), new Vector2(420, 170), Vector2.zero);
            UIKit.VLayout(vit.gameObject, 6, 14, TextAnchor.UpperLeft);
            hpFill = BarRow(vit, "HEALTH", UIKit.Good, out hpText);
            o2Fill = BarRow(vit, "OXYGEN", UIKit.Accent, out o2Text);
            energyFill = BarRow(vit, "JETPACK", UIKit.Warn, out energyText);

            o2Warn = UIKit.Text(root, "LOW OXYGEN - RETURN TO A SHIP OR ATMOSPHERE", 30, TextAlignmentOptions.Center, UIKit.Bad, FontStyles.Bold);
            UIKit.Place(o2Warn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(1000, 50));

            // Ship (bottom-right)
            shipPanel = UIKit.Panel(root, "Ship");
            UIKit.Place(shipPanel, new Vector2(1, 0), new Vector2(-30, 30), new Vector2(420, 210), new Vector2(1, 0));
            UIKit.VLayout(shipPanel.gameObject, 6, 14, TextAnchor.UpperLeft);
            shipName = UIKit.Text(shipPanel, "", 24, TextAlignmentOptions.Left, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(shipName.gameObject, -1, 30);
            hullFill = BarRow(shipPanel, "HULL", UIKit.Accent2, out _);
            shieldFill = BarRow(shipPanel, "SHIELD", new Color(0.5f, 0.75f, 1f), out _);
            fuelFill = BarRow(shipPanel, "FUEL", UIKit.Warn, out shipText);

            // Hotbar (bottom-center)
            var hb = UIKit.Rect(root, "Hotbar");
            hotbarRoot = hb;
            UIKit.Place(hb, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(560, 96), new Vector2(0.5f, 0));
            UIKit.HLayout(hb.gameObject, 12, 0, TextAnchor.MiddleCenter);
            string[] names = { "Drill", "Blaster", "Rifle", "Medkit" };
            for (int i = 0; i < 4; i++)
            {
                hotbar[i] = UIKit.Image(hb, "Slot", UIKit.PanelColor);
                UIKit.LayoutElement(hotbar[i].gameObject, 128, 90);
                hotbarLabels[i] = UIKit.Text(hotbar[i].transform, $"<size=70%>{i + 1}</size>\n{names[i]}", 22, TextAlignmentOptions.Center);
                UIKit.Stretch(hotbarLabels[i].rectTransform, 4, 4, 4, 4);
            }

            // Crosshair + prompt
            crosshair = UIKit.Image(root, "Crosshair", new Color(1, 1, 1, 0.85f));
            crosshair.sprite = UIKit.Circle;
            UIKit.Place(crosshair.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            prompt = UIKit.Text(root, "", 28, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            UIKit.Place(prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1000, 44));

            // Compass (top-center)
            compass = UIKit.Panel(root, "Compass", new Color(0.09f, 0.08f, 0.17f, 0.6f));
            UIKit.Place(compass, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(760, 46), new Vector2(0.5f, 1));
            compass.gameObject.AddComponent<RectMask2D>();
            var tick = UIKit.Image(compass, "Center", Color.white, false);
            UIKit.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 34));

            bossPanel = UIKit.Panel(root, "Boss", new Color(0.12f, 0.04f, 0.12f, 0.8f));
            UIKit.Place(bossPanel, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(900, 70), new Vector2(0.5f, 1));
            UIKit.VLayout(bossPanel.gameObject, 4, 8);
            bossText = UIKit.Text(bossPanel, "THE WARDEN", 24, TextAlignmentOptions.Center, UIKit.Bad, FontStyles.Bold);
            UIKit.LayoutElement(bossText.gameObject, -1, 26);
            bossFill = UIKit.Bar(bossPanel, "BossHp", UIKit.Bad, new Vector2(860, 20));

            // Collapse + sector (top-right)
            var cp = UIKit.Panel(root, "Collapse");
            UIKit.Place(cp, new Vector2(1, 1), new Vector2(-30, -20), new Vector2(380, 150), new Vector2(1, 1));
            UIKit.VLayout(cp.gameObject, 4, 12, TextAnchor.UpperRight);
            sectorText = UIKit.Text(cp, "", 22, TextAlignmentOptions.Right, UIKit.TextDim);
            UIKit.LayoutElement(sectorText.gameObject, -1, 28);
            collapseText = UIKit.Text(cp, "", 28, TextAlignmentOptions.Right, UIKit.Bad, FontStyles.Bold);
            UIKit.LayoutElement(collapseText.gameObject, -1, 34);
            collapseFill = UIKit.Bar(cp, "CollapseBar", UIKit.Bad, new Vector2(356, 14));
            creditsText = UIKit.Text(cp, "", 22, TextAlignmentOptions.Right, UIKit.Warn);
            UIKit.LayoutElement(creditsText.gameObject, -1, 28);

            // Objective (top-left)
            objective = UIKit.Text(root, "", 26, TextAlignmentOptions.TopLeft, Color.white);
            UIKit.Place(objective.rectTransform, new Vector2(0, 1), new Vector2(30, -24), new Vector2(620, 120), new Vector2(0, 1));

            jumpText = UIKit.Text(root, "", 54, TextAlignmentOptions.Center, UIKit.Accent, FontStyles.Bold);
            UIKit.Place(jumpText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(1200, 80));
            deathText = UIKit.Text(root, "", 46, TextAlignmentOptions.Center, UIKit.Bad, FontStyles.Bold);
            UIKit.Place(deathText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1200, 160));

            toastLayer = UIKit.Rect(root, "Toasts");
            UIKit.Place(toastLayer, new Vector2(0, 0.5f), new Vector2(30, 40), new Vector2(720, 420), new Vector2(0, 0.5f));
            var v = UIKit.VLayout(toastLayer.gameObject, 6, 0, TextAnchor.LowerLeft);
            v.childForceExpandWidth = false;

            EventBus.Notification += OnNotification;
            EventBus.PingCreated += (p, k, l) => { pings.Add(new Ping { Pos = p, Kind = k, Label = l, Until = Time.time + 14f }); Sfx.PlayUI("ping", 0.6f); };
            EventBus.LocalPlayerHurt += i => vignetteAlpha = Mathf.Max(vignetteAlpha, 0.25f + i * 0.5f);
            hook = new HealthComponentHook();
        }

        TextMeshProUGUI Marker(string text, Color c)
        {
            var t = UIKit.Text(markerLayer, text, 22, TextAlignmentOptions.Center, c, FontStyles.Bold);
            t.rectTransform.sizeDelta = new Vector2(260, 60);
            t.gameObject.SetActive(false);
            return t;
        }

        static Image BarRow(Transform parent, string label, Color c, out TextMeshProUGUI valueText)
        {
            var row = UIKit.Row(parent, 30, 8);
            var l = UIKit.Text(row, label, 18, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold);
            UIKit.LayoutElement(l.gameObject, 90, 26);
            var fill = UIKit.Bar(row, label, c, new Vector2(210, 22));
            valueText = UIKit.Text(row, "", 18, TextAlignmentOptions.Right);
            UIKit.LayoutElement(valueText.gameObject, 70, 26);
            return fill;
        }

        static Sprite MakeVignette()
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float d = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.55f) / 0.6f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, d * d));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        void OnNotification(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            var bg = UIKit.Panel(toastLayer, "Toast", new Color(0.09f, 0.08f, 0.17f, 0.82f));
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var h = UIKit.HLayout(bg.gameObject, 0, 12);
            h.childControlWidth = true;
            var t = UIKit.Text(bg, msg, 24);
            t.textWrappingMode = TextWrappingModes.Normal;
            UIKit.LayoutElement(t.gameObject, Mathf.Min(680f, t.GetPreferredValues(msg, 680f, 0f).x + 8f));
            toasts.Add((t, Time.time + 5f));
            if (toasts.Count > 6) { if (toasts[0].t) Object.Destroy(toasts[0].t.transform.parent.gameObject); toasts.RemoveAt(0); }
        }

        public void SetVisible(bool v)
        {
            if (root.gameObject.activeSelf != v) root.gameObject.SetActive(v);
        }

        public void Update()
        {
            // Toasts fade regardless of HUD visibility.
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var (t, until) = toasts[i];
                if (t == null) { toasts.RemoveAt(i); continue; }
                float left = until - Time.time;
                if (left <= 0f) { Object.Destroy(t.transform.parent.gameObject); toasts.RemoveAt(i); continue; }
                t.alpha = Mathf.Clamp01(left / 0.6f);
            }
            if (!root.gameObject.activeInHierarchy) return;

            // Weather warning when a storm builds up around the player.
            bool storm = PlanetAmbience.Storm > 0.35f;
            if (storm && !stormWarned && PlanetAmbience.Weather != PlanetWeather.None && PlanetAmbience.Weather != PlanetWeather.Shimmer)
                OnNotification(PlanetAmbience.Weather switch
                {
                    PlanetWeather.Sandstorm => "Sandstorm rolling in - visibility dropping",
                    PlanetWeather.Blizzard => "Blizzard incoming - stay close to your crew",
                    PlanetWeather.AshFall => "Ash fall - the sky is going dark",
                    _ => "Spore bloom - the air is thick",
                });
            if (storm) stormWarned = true;
            else if (PlanetAmbience.Storm < 0.1f) stormWarned = false;

            var pawn = PlayerPawn.Local;
            var cam = Camera.main;
            hook.Track(pawn);

            vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, 0f, Time.deltaTime * 0.8f);
            vignette.color = new Color(1f, 0.15f, 0.3f, vignetteAlpha);

            if (pawn != null)
            {
                var h = pawn.Health;
                hpFill.fillAmount = h.Fraction;
                hpText.text = Mathf.CeilToInt(h.Hp.Value).ToString();
                float maxO2 = pawn.Gear != null ? pawn.Gear.MaxOxygen : 90f;
                float o2 = pawn.Oxygen.Value / Mathf.Max(1f, maxO2);
                o2Fill.fillAmount = o2;
                o2Text.text = Mathf.CeilToInt(pawn.Oxygen.Value) + "s";
                float maxE = pawn.Gear != null ? pawn.Gear.JetpackEnergy : 100f;
                energyFill.fillAmount = pawn.SuitEnergy / Mathf.Max(1f, maxE);
                energyText.text = Mathf.CeilToInt(pawn.SuitEnergy).ToString();

                bool low = o2 < 0.25f && !pawn.IsDown.Value && pawn.Active.Value;
                if (low != lowO2) { lowO2 = low; EventBus.RaiseLowOxygenChanged(low); }
                o2Warn.gameObject.SetActive(low && Mathf.Repeat(Time.time, 1f) < 0.6f);
                if (low) { lowO2Beep -= Time.deltaTime; if (lowO2Beep <= 0f) { lowO2Beep = 1.6f; Sfx.PlayUI("low_oxygen", 0.7f); } }

                bool seated = pawn.CurrentSeat != null;
                hotbarRoot.gameObject.SetActive(!seated);
                for (int i = 0; i < 4; i++)
                {
                    bool sel = (int)pawn.CurrentTool == i && !seated;
                    bool avail = i != 2 || (pawn.Gear != null && pawn.Gear.HasRifle.Value);
                    hotbar[i].color = sel ? UIKit.Accent * new Color(1, 1, 1, 0.9f) : UIKit.PanelColor;
                    hotbarLabels[i].color = sel ? new Color(0.08f, 0.07f, 0.16f) : avail ? UIKit.TextColor : new Color(1, 1, 1, 0.3f);
                }
                if (pawn.Gear != null && pawn.Inventory != null)
                    hotbarLabels[3].text = $"<size=70%>4</size>\nMedkit x{pawn.Inventory.Count("medkit")}";

                prompt.text = pawn.Prompt;
                crosshair.enabled = !seated || pawn.CurrentShipSeat == null || pawn.CurrentShipSeat.Role != StationRole.Engineer;
                creditsText.text = $"{pawn.Inventory.Credits.Value} credits";

                // Death overlay
                if (pawn.IsLost.Value) deathText.text = "LOST\n<size=55%>Spectating - your crew fights on</size>";
                else if (pawn.IsDown.Value)
                {
                    float t = pawn.RespawnAt.Value - Time.time;
                    var rm0 = RunManager.Instance;
                    deathText.text = t > 0f ? $"YOU DIED\n<size=55%>Respawning in {Mathf.CeilToInt(t)}s  (team respawns left: {(rm0 != null ? rm0.RespawnsLeft.Value : 0)})</size>" : "YOU DIED\n<size=55%>Waiting for a ship to respawn at...</size>";
                }
                else deathText.text = "";

                UpdateShip(pawn);
            }

            var rm = RunManager.Instance;
            if (rm != null && pawn != null)
            {
                float dist = pawn.transform.position.x - GameState.CollapseX;
                collapseText.text = $"COLLAPSE  {Mathf.Max(0, dist):0} m";
                collapseText.color = dist < 400 ? UIKit.Bad : dist < 1200 ? UIKit.Warn : UIKit.Good;
                collapseFill.fillAmount = Mathf.Clamp01(1f - dist / 3000f);
                sectorText.text = $"SECTOR {rm.SectorIndex.Value}/3   |   respawns {rm.RespawnsLeft.Value}";
                jumpText.text = rm.JumpCountdown.Value >= 0f ? $"JUMPING IN {Mathf.CeilToInt(rm.JumpCountdown.Value)}" : "";
                UpdateObjective(pawn);
            }

            var boss = BossBrain.Instance;
            bossPanel.gameObject.SetActive(boss != null && boss.IsSpawned);
            if (boss != null && boss.IsSpawned)
            {
                var bh = boss.GetComponent<Combat.HealthComponent>();
                bossFill.fillAmount = bh.Fraction;
                bossText.text = boss.Phase.Value == 1 ? "THE WARDEN - destroy the core pods!" : boss.Phase.Value == 2 ? "THE WARDEN - shield down!" : "THE WARDEN - ENRAGED";
            }

            if (cam != null) UpdateMarkers(cam);
        }

        void UpdateShip(PlayerPawn pawn)
        {
            ShipCore ship = pawn.CurrentShipSeat != null ? pawn.CurrentShipSeat.Ship : ShipCore.OwnedBy(pawn.OwnerClientId);
            shipPanel.gameObject.SetActive(ship != null);
            if (ship == null) return;
            var s = ship.Stats;
            shipName.text = ship.ShipName.Value + (pawn.CurrentShipSeat != null ? $"  <size=75%><color=#c0c0e0>[{pawn.CurrentShipSeat.Role}]</color></size>" : $"  <size=75%><color=#c0c0e0>{Vector3.Distance(ship.transform.position, pawn.transform.position):0} m</color></size>");
            hullFill.fillAmount = ship.Hull.Fraction;
            shieldFill.fillAmount = s.MaxShield > 0f ? ship.Shield.Value / s.MaxShield : 0f;
            fuelFill.fillAmount = s.MaxFuel > 0f ? ship.Fuel.Value / s.MaxFuel : 0f;
            shipText.text = ship.Flight != null ? $"{ship.Flight.Speed:0} m/s" : "";
        }

        void UpdateObjective(PlayerPawn pawn)
        {
            var gate = JumpGate.Instance;
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>OBJECTIVE</b>\n");
            if (BossBrain.Instance != null || GameState.Objective == "Defeat the Warden")
                sb.Append(BossBrain.Instance != null ? "Defeat the Warden!" : "Fly to the end of the sector and face the Warden");
            else if (gate != null)
            {
                float d = Vector3.Distance(pawn.transform.position, gate.transform.position);
                if (!gate.Powered) sb.Append($"Bring Energy Cells to the Jump Gate ({gate.CellsInserted.Value}/{gate.CellsRequired.Value})  <color=#a0a0c0>{d / 1000f:0.0} km</color>");
                else sb.Append($"Gather everyone at the Jump Gate  <color=#a0a0c0>{d:0} m</color>");
            }
            if (pawn.CurrentShipSeat != null && pawn.CurrentShipSeat.Role == StationRole.Pilot)
                sb.Append("\n<size=75%><color=#b0b0d0>LMB/RMB fire  |  Shift boost  |  X brake  |  F leave seat</color></size>");
            objective.text = sb.ToString();
        }

        void UpdateMarkers(Camera cam)
        {
            Vector3 camPos = cam.transform.position;
            var gate = JumpGate.Instance;
            Place(gateMarker, cam, gate != null ? gate.transform.position : (Vector3?)null, "[ GATE ]");
            var st = TraderStation.Instance;
            Place(stationMarker, cam, st != null ? st.transform.position : (Vector3?)null, "[ STATION ]");
            ShipCore ark = null;
            foreach (var s in ShipCore.All) if (s != null && s.Class == ShipClass.Ark) ark = s;
            Place(arkMarker, cam, ark != null ? ark.transform.position + ark.transform.up * 8f : (Vector3?)null, ark != null && ark.Derelict.Value ? "[ DERELICT ARK ]" : "[ ARK ]");

            pings.RemoveAll(p => Time.time > p.Until);
            while (pingLabels.Count < pings.Count) pingLabels.Add(Marker("", Color.white));
            for (int i = 0; i < pingLabels.Count; i++)
            {
                if (i >= pings.Count) { pingLabels[i].gameObject.SetActive(false); continue; }
                var p = pings[i];
                Color c = p.Kind == PingKind.Enemy ? UIKit.Bad : p.Kind == PingKind.Loot ? UIKit.Warn : p.Kind == PingKind.Gate ? UIKit.Accent : Color.white;
                pingLabels[i].color = c;
                Place(pingLabels[i], cam, p.Pos, "v " + p.Label);
            }
            UpdateSiteMarkers(cam);

            // Bearing compass
            var marks = new List<(string id, string label, Vector3 pos, Color c)>();
            if (gate != null) marks.Add(("gate", "G", gate.transform.position, UIKit.Accent));
            if (st != null) marks.Add(("station", "S", st.transform.position, UIKit.Warn));
            if (ark != null) marks.Add(("ark", "A", ark.transform.position, UIKit.Accent2));
            marks.Add(("collapse", "!", new Vector3(GameState.CollapseX, camPos.y, camPos.z), UIKit.Bad));
            for (int i = 0; i < pings.Count; i++) marks.Add(("ping" + i, "v", pings[i].Pos, Color.white));
            var used = new HashSet<string>();
            foreach (var m in marks)
            {
                used.Add(m.id);
                if (!compassMarks.TryGetValue(m.id, out var t))
                {
                    t = UIKit.Text(compass, m.label, 26, TextAlignmentOptions.Center, m.c, FontStyles.Bold);
                    t.rectTransform.sizeDelta = new Vector2(40, 40);
                    compassMarks[m.id] = t;
                }
                Vector3 to = m.pos - camPos;
                Vector3 flat = Vector3.ProjectOnPlane(to, cam.transform.up);
                float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward, cam.transform.up), flat, cam.transform.up);
                t.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(ang / 100f, -1f, 1f) * 360f, 0f);
                t.alpha = Mathf.Abs(ang) > 100f ? 0.35f : 1f;
            }
            foreach (var kv in compassMarks) kv.Value.gameObject.SetActive(used.Contains(kv.Key));
        }

        /// <summary>Planet points of interest (from approach/orbit) and nearby landmark names.</summary>
        void UpdateSiteMarkers(Camera cam)
        {
            const int MaxSites = 8, MaxLandmarks = 3;
            int used = 0;
            Vector3 camPos = cam.transform.position;
            var planet = PlanetBody.Nearest(camPos);
            if (planet != null && planet.Terrain != null && Vector3.Distance(camPos, planet.transform.position) < planet.AtmosphereRadius + 3000f)
            {
                var t = planet.Terrain;
                Vector3 center = planet.transform.position;
                for (int i = 0; i < t.Sites.Count && used < MaxSites; i++)
                {
                    Vector3 p = planet.SiteWorldPoint(i);
                    if (!AboveHorizon(center, p, camPos, 45f, 5000f)) continue;
                    var kind = planet.SiteKinds[i];
                    var label = SiteLabel(used++);
                    label.color = kind == SiteKind.Hive ? UIKit.Bad : kind == SiteKind.CrashSite ? UIKit.Accent2 : kind == SiteKind.Ruins || kind == SiteKind.Cache ? UIKit.Warn : UIKit.Good;
                    Place(label, cam, p + (p - center).normalized * 8f, "[ " + PlanetBody.SiteLabel(kind) + " ]");
                }
                var visuals = planet.Visuals;
                if (visuals != null && visuals.Landmarks != null)
                {
                    int shown = 0;
                    foreach (var lm in visuals.Landmarks.Landmarks)
                    {
                        if (shown >= MaxLandmarks) break;
                        if (lm.AtSite) continue;
                        Vector3 p = planet.transform.TransformPoint(lm.LocalPos);
                        if (!AboveHorizon(center, p, camPos, 60f, 1100f)) continue;
                        var label = SiteLabel(used++);
                        label.color = UIKit.TextDim;
                        Place(label, cam, p, "<size=80%>" + lm.Name + "</size>");
                        shown++;
                    }
                }
            }
            for (int i = used; i < siteLabels.Count; i++) siteLabels[i].gameObject.SetActive(false);
        }

        TextMeshProUGUI SiteLabel(int i)
        {
            while (siteLabels.Count <= i) siteLabels.Add(Marker("", Color.white));
            return siteLabels[i];
        }

        static bool AboveHorizon(Vector3 center, Vector3 p, Vector3 camPos, float minDist, float maxDist)
        {
            Vector3 to = camPos - p;
            float d = to.magnitude;
            if (d < minDist || d > maxDist) return false;
            return Vector3.Dot((p - center).normalized, to / d) > -0.05f;
        }

        static void Place(TextMeshProUGUI marker, Camera cam, Vector3? world, string label)
        {
            if (world == null) { marker.gameObject.SetActive(false); return; }
            Vector3 sp = cam.WorldToScreenPoint(world.Value);
            if (sp.z <= 0f) { marker.gameObject.SetActive(false); return; }
            marker.gameObject.SetActive(true);
            float d = Vector3.Distance(cam.transform.position, world.Value);
            marker.text = $"{label}\n<size=80%>{(d > 1000f ? (d / 1000f).ToString("0.0") + " km" : d.ToString("0") + " m")}</size>";
            var canvasRt = (RectTransform)marker.rectTransform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var local);
            marker.rectTransform.anchoredPosition = local;
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Raises LocalPlayerHurt when the local pawn takes damage.</summary>
        class HealthComponentHook
        {
            PlayerPawn tracked;
            void OnDamaged(DamageInfo i) => EventBus.RaiseLocalPlayerHurt(Mathf.Clamp01(i.Amount / 40f));
            public void Track(PlayerPawn p)
            {
                if (p == tracked) return;
                if (tracked != null && tracked.Health != null) tracked.Health.DamagedClient -= OnDamaged;
                tracked = p;
                if (p != null && p.Health != null) p.Health.DamagedClient += OnDamaged;
            }
        }
    }
}
