using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Networking;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>
    /// In-game HUD (#24, DESIGN.md "HUD Layout Specifications", 1080p reference): vitals plate with ghost-fill
    /// gauges (bottom-left), ship card (bottom-right), icon hotbar (bottom-centre), system meter (progress
    /// through the system, galaxy, run clock) and a bearing compass with station/Ark/own-ship markers (top-centre),
    /// expedition card (top-left), galaxy/system card + toasts with item icons (top-right), ring crosshair with hit
    /// markers and keycap prompt, edge-clamped world markers, boss bar, damage and low-oxygen vignettes.
    /// Damage taken (#43): direction arcs around the crosshair, gauge flash + plate kick, low-health pulse with
    /// heartbeat (on foot) or hull klaxon (seated), shield-collapse alert.
    /// </summary>
    public class HudView
    {
        const float Edge = 48f;
        const float CompassWidth = 600f;
        const float CompassRange = 95f;   // degrees visible either side of the centre

        readonly RectTransform root, markerLayer, toastLayer;

        // vitals + ship
        readonly HudGauge hpGauge, o2Gauge, jetGauge, hullGauge, shieldGauge, fuelGauge;
        readonly RectTransform shipPlate, vitalsPlate;
        Vector2 shipPlateHome, vitalsPlateHome;
        float shipKick, vitalsKick;
        readonly TextMeshProUGUI shipName, shipRole, speedText;
        readonly Image speedFill;

        // hotbar
        class Slot { public Image Bg, Icon; public Outline Border; public TextMeshProUGUI Num, Qty; }
        readonly Slot[] slots = new Slot[4];
        readonly RectTransform hotbarRoot;
        readonly TextMeshProUGUI toolName;

        // centre
        readonly Image crossRing, crossPip;
        readonly Image[] hitTicks = new Image[4];
        readonly TextMeshProUGUI hitLabel;
        float hitTime = -9f;
        Color hitColor;
        int hitKind;   // 0 = hit, 1 = crit, 2 = kill, 3 = shielded
        readonly DamageNumbers numbers;
        readonly RectTransform promptPlate;
        readonly TextMeshProUGUI prompt;
        string promptShown = null;
        RectTransform leadMarker;
        readonly Image[] leadTicks = new Image[4];
        Image leadLink;

        // top-centre: collapse meter + compass
        readonly Image meterFill;
        readonly RectTransform meterTrack, pipPlayer, pipEnd;
        readonly TextMeshProUGUI meterText;
        readonly TextMeshProUGUI etaText;
        float etaTimer;
        readonly RectTransform compassStrip;
        readonly List<(RectTransform rt, float deg)> compassTicks = new List<(RectTransform, float)>();
        class CompassMark { public RectTransform Rt; public TextMeshProUGUI Icon, Dist; }
        readonly Dictionary<string, CompassMark> compassMarks = new Dictionary<string, CompassMark>();
        int layoutKey = int.MinValue;
        float systemStartX, systemEndX = 1f;

        // boss
        readonly RectTransform bossPanel;
        readonly TextMeshProUGUI bossTitle;
        readonly Image[] bossSegs = new Image[3];
        readonly Image bossHull, bossGhost;
        readonly RectTransform bossSegRow, bossHullTrack;
        float bossGhostFrac = 1f;

        // cards
        readonly TextMeshProUGUI objHeader, objText, objHint;
        readonly TextMeshProUGUI sectorTitle, sectorName, respawnText, creditsText;

        // overlays
        readonly TextMeshProUGUI jumpText, deathText, o2Warn;
        readonly RectTransform jumpPlate, deathPlate, o2Plate;
        readonly Image vignette, o2Vignette;
        float vignetteAlpha, lowHealthPulse, lowHealthBeat;
        readonly DamageIndicator damageArcs;
        ShipCore shownShip;
        float lowO2Beep;
        bool lowO2;
        bool stormWarned;
        readonly HealthComponentHook pawnHook, shipHook;
        readonly CrewHud crew;

        // world markers
        readonly HudMarker stationMarker, arkMarker, shipMarker;
        readonly HudMarker[] bossMarkers = new HudMarker[4];
        readonly List<HudMarker> pingMarkers = new List<HudMarker>();
        readonly List<HudMarker> siteMarkers = new List<HudMarker>();
        readonly List<Ping> pings = new List<Ping>();

        // toasts
        class Toast { public RectTransform Rt, Body; public CanvasGroup Group; public float Born, Until; }
        readonly List<Toast> toasts = new List<Toast>();
        List<ItemDef> itemsByName;

        struct Ping { public Vector3 Pos; public PingKind Kind; public string Label; public float Until; }

        public HudView(RectTransform canvas)
        {
            root = UIKit.Rect(canvas, "HUD");
            UIKit.Stretch(root);

            vignette = UIKit.Image(root, "Vignette", new Color(0.86f, 0.15f, 0.15f, 0f), false);
            UIKit.Stretch(vignette.rectTransform);
            vignette.sprite = MakeVignette();
            vignette.raycastTarget = false;
            o2Vignette = UIKit.Image(root, "O2Vignette", new Color(0.03f, 0.57f, 0.7f, 0f), false);
            UIKit.Stretch(o2Vignette.rectTransform);
            o2Vignette.sprite = vignette.sprite;
            o2Vignette.raycastTarget = false;

            markerLayer = UIKit.Rect(root, "Markers");
            UIKit.Stretch(markerLayer);
            damageArcs = new DamageIndicator(root);
            stationMarker = new HudMarker(markerLayer);
            arkMarker = new HudMarker(markerLayer);
            shipMarker = new HudMarker(markerLayer);
            for (int i = 0; i < bossMarkers.Length; i++) bossMarkers[i] = new HudMarker(markerLayer);
            numbers = new DamageNumbers(markerLayer);

            // ---------------------------------------------------------- vitals (bottom-left)
            var vit = vitalsPlate = HudKit.Plate(root, "Vitals").rectTransform;
            UIKit.Place(vit, Vector2.zero, new Vector2(Edge, Edge), new Vector2(456, 112), Vector2.zero);
            vitalsPlateHome = vit.anchoredPosition;
            UIKit.VLayout(vit.gameObject, 2, 12, TextAnchor.MiddleLeft);
            hpGauge = new HudGauge(vit, Icons.Heart, "Health", HudKit.Health, 250, 18, 32);
            o2Gauge = new HudGauge(vit, Icons.Air, "Oxygen", HudKit.Oxygen, 250, 12, 28);
            jetGauge = new HudGauge(vit, Icons.Bolt, "Jetpack", HudKit.Energy, 250, 12, 28);

            o2Plate = HudKit.Plate(root, "O2Warn", HudKit.Hex(0x7F1D1D, 0.85f)).rectTransform;
            UIKit.Place(o2Plate, new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(620, 46));
            o2Warn = HudKit.Micro(o2Plate, "LOW OXYGEN - RETURN TO A SHIP OR ATMOSPHERE", 18f, HudKit.TextHi, TextAlignmentOptions.Center);
            UIKit.Stretch(o2Warn.rectTransform);

            // ---------------------------------------------------------- ship card (bottom-right)
            shipPlate = HudKit.Plate(root, "Ship").rectTransform;
            UIKit.Place(shipPlate, new Vector2(1, 0), new Vector2(-Edge, Edge), new Vector2(456, 176), new Vector2(1, 0));
            shipPlateHome = shipPlate.anchoredPosition;
            UIKit.VLayout(shipPlate.gameObject, 2, 12, TextAnchor.UpperLeft);
            var head = UIKit.Row(shipPlate, 30, 8);
            var shipIcon = UIKit.IconText(head, Icons.Ship, 24, HudKit.Shield);
            UIKit.LayoutElement(shipIcon.gameObject, 26, 30);
            shipName = UIKit.Text(head, "", 22, TextAlignmentOptions.Left, HudKit.TextHi, FontStyles.Bold);
            shipName.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.LayoutElement(shipName.gameObject, 250, 30);
            shipRole = HudKit.Micro(head, "", 14f, HudKit.KeyText, TextAlignmentOptions.Right);
            UIKit.LayoutElement(shipRole.gameObject, 140, 30);
            hullGauge = new HudGauge(shipPlate, Icons.Wrench, "Hull", HudKit.Hull, 250, 14, 28);
            shieldGauge = new HudGauge(shipPlate, Icons.Shield, "Shield", HudKit.Shield, 250, 14, 28);
            fuelGauge = new HudGauge(shipPlate, Icons.Fuel, "Fuel", HudKit.Fuel, 250, 12, 28);
            var spRow = UIKit.Row(shipPlate, 28, 8);
            var spIcon = UIKit.IconText(spRow, Icons.Speed, 20, HudKit.TextDim);
            UIKit.LayoutElement(spIcon.gameObject, 26, 28);
            UIKit.LayoutElement(HudKit.Micro(spRow, "Speed", 13f).gameObject, 74, 28);
            var spTrack = UIKit.Image(spRow, "Track", HudKit.Inset, false);
            spTrack.sprite = HudKit.Soft; spTrack.type = Image.Type.Sliced;
            UIKit.LayoutElement(spTrack.gameObject, 250, 6);
            speedFill = UIKit.Image(spTrack.transform, "Fill", HudKit.TextHi, false);
            UIKit.Stretch(speedFill.rectTransform, 1, 1, 1, 1);
            speedFill.sprite = UIKit.White; speedFill.type = Image.Type.Filled; speedFill.fillMethod = Image.FillMethod.Horizontal;
            speedText = UIKit.Text(spRow, "", 19, TextAlignmentOptions.Right, HudKit.TextHi, FontStyles.Bold);
            UIKit.LayoutElement(speedText.gameObject, 58, 28);

            // ---------------------------------------------------------- hotbar (bottom-centre)
            hotbarRoot = UIKit.Rect(root, "Hotbar");
            UIKit.Place(hotbarRoot, new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(4 * 76 + 3 * 10, 76), new Vector2(0.5f, 0));
            UIKit.HLayout(hotbarRoot.gameObject, 10, 0, TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                var s = new Slot();
                s.Bg = UIKit.Image(hotbarRoot, "Slot", HudKit.Hex(0x0E1420, 0.9f), false);
                s.Bg.sprite = HudKit.Soft; s.Bg.type = Image.Type.Sliced;
                UIKit.LayoutElement(s.Bg.gameObject, 76, 76);
                s.Border = s.Bg.gameObject.AddComponent<Outline>();
                s.Border.useGraphicAlpha = false;
                s.Icon = UIKit.Image(s.Bg.transform, "Icon", Color.white, false);
                s.Icon.preserveAspect = true;
                s.Icon.raycastTarget = false;
                UIKit.Place(s.Icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(56, 56));
                s.Num = UIKit.Text(s.Bg.transform, (i + 1).ToString(), 14, TextAlignmentOptions.TopLeft, HudKit.TextDim, FontStyles.Bold);
                UIKit.Stretch(s.Num.rectTransform, 6, 4, 3, 4);
                s.Qty = UIKit.Text(s.Bg.transform, "", 16, TextAlignmentOptions.BottomRight, HudKit.TextHi, FontStyles.Bold);
                UIKit.Stretch(s.Qty.rectTransform, 4, 7, 4, 4);
                slots[i] = s;
            }
            toolName = HudKit.Micro(root, "", 15f, HudKit.TextHead, TextAlignmentOptions.Center);
            UIKit.Place(toolName.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 36 + 76 + 6), new Vector2(400, 22), new Vector2(0.5f, 0));

            // ---------------------------------------------------------- crosshair, hit markers, prompt
            crossRing = UIKit.Image(root, "CrossRing", HudKit.Hex(0xE2E8F0, 0.42f), false);
            crossRing.sprite = HudKit.Ring;
            crossRing.raycastTarget = false;
            UIKit.Place(crossRing.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
            crossPip = UIKit.Image(root, "CrossPip", new Color(1f, 1f, 1f, 0.9f), false);
            crossPip.sprite = UIKit.Circle;
            crossPip.raycastTarget = false;
            UIKit.Place(crossPip.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 4));
            for (int i = 0; i < 4; i++)
            {
                hitTicks[i] = UIKit.Image(root, "Hit", Color.white, false);
                hitTicks[i].raycastTarget = false;
                var rt = hitTicks[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2.5f, 11f);
                rt.localRotation = Quaternion.Euler(0, 0, 45f + 90f * i);
                hitTicks[i].enabled = false;
            }
            hitLabel = HudKit.Micro(root, "", 13f, HudKit.TextDim, TextAlignmentOptions.Center);
            UIKit.Place(hitLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 34), new Vector2(240, 20));
            BuildLeadMarker();
            promptPlate = HudKit.Plate(root, "Prompt").rectTransform;
            UIKit.Place(promptPlate, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(240, 38));
            prompt = UIKit.Text(promptPlate, "", 18, TextAlignmentOptions.Center, HudKit.TextHi, FontStyles.Bold);
            prompt.textWrappingMode = TextWrappingModes.NoWrap;
            prompt.characterSpacing = 2f;
            UIKit.Stretch(prompt.rectTransform, 12, 12, 0, 0);

            // ---------------------------------------------------------- system progress + run clock (top-centre)
            var meter = HudKit.Plate(root, "SystemMeter").rectTransform;
            UIKit.Place(meter, new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(480, 62), new Vector2(0.5f, 1));
            var trackImg = UIKit.Image(meter, "Track", HudKit.VoidTrack, false);
            trackImg.sprite = HudKit.Soft; trackImg.type = Image.Type.Sliced;
            meterTrack = trackImg.rectTransform;
            UIKit.Place(meterTrack, new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(440, 14), new Vector2(0.5f, 1));
            meterFill = UIKit.Image(meterTrack, "Fill", Color.white, false);
            UIKit.Stretch(meterFill.rectTransform, 1, 1, 1, 1);
            meterFill.sprite = HudKit.Gradient(HudKit.Teal, HudKit.Shield);
            meterFill.type = Image.Type.Filled; meterFill.fillMethod = Image.FillMethod.Horizontal;
            pipEnd = Pip(meterTrack, HudKit.Gold, HudKit.Ring, 18);
            pipPlayer = Pip(meterTrack, HudKit.Shield, HudKit.Chevron, 14);
            meterText = UIKit.Text(meter, "", 19, TextAlignmentOptions.Center, HudKit.Teal, FontStyles.Bold);
            UIKit.Place(meterText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(440, 26), new Vector2(0.5f, 0));
            meterText.characterSpacing = 3f;
            // Home ETA of the crew's warp drive (#78), always visible under the system meter and compass.
            etaText = HudKit.Micro(root, "", 15f, HudKit.TextDim, TextAlignmentOptions.Center);
            UIKit.Place(etaText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(640, 22), new Vector2(0.5f, 1));
            etaText.richText = true;
            crew = new CrewHud(root, markerLayer, meterTrack, new Vector2(Edge, Edge + 112 + 14));

            // ---------------------------------------------------------- compass (below the meter)
            var compassWrap = UIKit.Rect(root, "Compass");
            UIKit.Place(compassWrap, new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(CompassWidth, 50), new Vector2(0.5f, 1));
            compassWrap.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(60, 0);
            var strip = HudKit.Plate(compassWrap, "Strip").rectTransform;
            UIKit.Place(strip, new Vector2(0.5f, 1), Vector2.zero, new Vector2(CompassWidth, 26), new Vector2(0.5f, 1));
            compassStrip = compassWrap;
            string[] cardinal = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int d = 0; d < 360; d += 15)
            {
                bool major = d % 45 == 0;
                RectTransform rt;
                if (major)
                {
                    var t = UIKit.Text(compassWrap, cardinal[d / 45], d % 90 == 0 ? 15 : 12, TextAlignmentOptions.Center, d == 0 ? HudKit.Gold : HudKit.TextDim, FontStyles.Bold);
                    rt = t.rectTransform;
                    rt.sizeDelta = new Vector2(30, 26);
                }
                else
                {
                    var t = UIKit.Image(compassWrap, "Tick", HudKit.Hex(0x94A3B8, 0.5f), false);
                    t.raycastTarget = false;
                    rt = t.rectTransform;
                    rt.sizeDelta = new Vector2(1.5f, 8);
                }
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                compassTicks.Add((rt, d));
            }
            var centre = UIKit.Image(compassWrap, "Centre", HudKit.TextHi, false);
            centre.sprite = HudKit.Chevron;
            UIKit.Place(centre.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(10, 7), new Vector2(0.5f, 1));

            // ---------------------------------------------------------- boss bar
            bossPanel = UIKit.Rect(root, "Boss");
            UIKit.Place(bossPanel, new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(660, 60), new Vector2(0.5f, 1));
            bossTitle = HudKit.Micro(bossPanel, "THE WARDEN", 18f, HudKit.BossTitle, TextAlignmentOptions.Center);
            UIKit.Place(bossTitle.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(660, 24), new Vector2(0.5f, 1));
            bossSegRow = UIKit.Rect(bossPanel, "Segments");
            UIKit.Place(bossSegRow, new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(640, 24), new Vector2(0.5f, 1));
            UIKit.HLayout(bossSegRow.gameObject, 6, 0, TextAnchor.MiddleCenter);
            for (int i = 0; i < 3; i++)
            {
                var bg = HudKit.Plate(bossSegRow, "Seg", HudKit.BossEmpty);
                UIKit.LayoutElement(bg.gameObject, 209, 24);
                bossSegs[i] = UIKit.Image(bg.transform, "Fill", HudKit.BossFill, false);
                UIKit.Stretch(bossSegs[i].rectTransform, 2, 2, 2, 2);
                bossSegs[i].sprite = UIKit.White; bossSegs[i].type = Image.Type.Filled; bossSegs[i].fillMethod = Image.FillMethod.Horizontal;
            }
            var hullBg = HudKit.Plate(bossPanel, "Hull", HudKit.BossEmpty);
            bossHullTrack = hullBg.rectTransform;
            UIKit.Place(bossHullTrack, new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(640, 24), new Vector2(0.5f, 1));
            bossGhost = UIKit.Image(bossHullTrack, "Ghost", new Color(1f, 0.85f, 0.88f, 0.6f), false);
            UIKit.Stretch(bossGhost.rectTransform, 2, 2, 2, 2);
            bossGhost.sprite = UIKit.White; bossGhost.type = Image.Type.Filled; bossGhost.fillMethod = Image.FillMethod.Horizontal;
            bossHull = UIKit.Image(bossHullTrack, "Fill", HudKit.BossFill, false);
            UIKit.Stretch(bossHull.rectTransform, 2, 2, 2, 2);
            bossHull.sprite = UIKit.White; bossHull.type = Image.Type.Filled; bossHull.fillMethod = Image.FillMethod.Horizontal;

            // ---------------------------------------------------------- objective card (top-left)
            var obj = HudKit.Plate(root, "Objective").rectTransform;
            UIKit.Place(obj, new Vector2(0, 1), new Vector2(Edge, -18), new Vector2(560, 0), new Vector2(0, 1));
            HudKit.AccentBar(obj, HudKit.Gold);
            var ov = UIKit.VLayout(obj.gameObject, 4, 0, TextAnchor.UpperLeft);
            ov.padding = new RectOffset(20, 16, 12, 12);
            ov.childControlHeight = true;
            ov.childForceExpandHeight = false;
            var fit = obj.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            objHeader = HudKit.Micro(obj, "OBJECTIVE", 15f, HudKit.Gold);
            objText = UIKit.Text(obj, "", 21, TextAlignmentOptions.TopLeft, HudKit.TextHi, FontStyles.Bold);
            objHint = UIKit.Text(obj, "", 15, TextAlignmentOptions.TopLeft, HudKit.TextDim);
            objHint.lineSpacing = 18f;

            // ---------------------------------------------------------- sector card (top-right)
            var sec = HudKit.Plate(root, "Sector").rectTransform;
            UIKit.Place(sec, new Vector2(1, 1), new Vector2(-Edge, -18), new Vector2(340, 98), new Vector2(1, 1));
            sectorTitle = HudKit.Micro(sec, "", 15f, HudKit.KeyText, TextAlignmentOptions.Right);
            UIKit.Place(sectorTitle.rectTransform, new Vector2(1, 1), new Vector2(-16, -10), new Vector2(310, 18), new Vector2(1, 1));
            sectorName = UIKit.Text(sec, "", 24, TextAlignmentOptions.Right, HudKit.TextHi, FontStyles.Bold | FontStyles.UpperCase);
            sectorName.textWrappingMode = TextWrappingModes.NoWrap;
            sectorName.characterSpacing = 3f;
            UIKit.Place(sectorName.rectTransform, new Vector2(1, 1), new Vector2(-16, -28), new Vector2(310, 30), new Vector2(1, 1));
            var respIcon = UIKit.IconText(sec, Icons.Respawn, 20, HudKit.TextHead);
            UIKit.Place(respIcon.rectTransform, new Vector2(0, 0), new Vector2(14, 10), new Vector2(24, 26), new Vector2(0, 0));
            respawnText = UIKit.Text(sec, "", 18, TextAlignmentOptions.Left, HudKit.TextHead, FontStyles.Bold);
            UIKit.Place(respawnText.rectTransform, new Vector2(0, 0), new Vector2(42, 10), new Vector2(150, 26), new Vector2(0, 0));
            var credIcon = UIKit.IconText(sec, Icons.Credits, 20, HudKit.Hex(0xFCD34D));
            UIKit.Place(credIcon.rectTransform, new Vector2(1, 0), new Vector2(-16, 10), new Vector2(24, 26), new Vector2(1, 0));
            creditsText = UIKit.Text(sec, "", 18, TextAlignmentOptions.Right, HudKit.Hex(0xFCD34D), FontStyles.Bold);
            UIKit.Place(creditsText.rectTransform, new Vector2(1, 0), new Vector2(-44, 10), new Vector2(150, 26), new Vector2(1, 0));

            // ---------------------------------------------------------- toasts (top-right, below the sector card)
            toastLayer = UIKit.Rect(root, "Toasts");
            UIKit.Place(toastLayer, new Vector2(1, 1), new Vector2(-Edge, -130), new Vector2(380, 420), new Vector2(1, 1));
            var tv = UIKit.VLayout(toastLayer.gameObject, 8, 0, TextAnchor.UpperRight, false);
            tv.childControlHeight = true;
            tv.childForceExpandHeight = false;

            // ---------------------------------------------------------- big centre messages
            jumpPlate = HudKit.Plate(root, "Jump").rectTransform;
            UIKit.Place(jumpPlate, new Vector2(0.5f, 0.5f), new Vector2(0, 210), new Vector2(560, 84));
            HudKit.AccentBar(jumpPlate, HudKit.Shield, 6);
            jumpText = UIKit.Text(jumpPlate, "", 46, TextAlignmentOptions.Center, HudKit.Shield, FontStyles.Bold);
            jumpText.characterSpacing = 8f;
            UIKit.Stretch(jumpText.rectTransform);
            deathPlate = HudKit.Plate(root, "Death", HudKit.Hex(0x0A0E17, 0.86f)).rectTransform;
            UIKit.Place(deathPlate, new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(820, 150));
            HudKit.AccentBar(deathPlate, HudKit.Danger, 6);
            deathText = UIKit.Text(deathPlate, "", 48, TextAlignmentOptions.Center, HudKit.Danger, FontStyles.Bold);
            deathText.characterSpacing = 6f;
            UIKit.Stretch(deathText.rectTransform, 16, 16, 10, 10);

            EventBus.Notification += OnNotification;
            EventBus.PingCreated += (p, k, l) => { pings.Add(new Ping { Pos = p, Kind = k, Label = l, Until = Time.time + 14f }); Sfx.PlayUI("ping", 0.6f); };
            EventBus.LocalPlayerHurt += i => vignetteAlpha = Mathf.Max(vignetteAlpha, 0.25f + i * 0.5f);
            FeedbackFx.LocalHitLanded += OnLocalHit;
            HealthComponent.AnyImmuneClient += OnAnyImmune;
            ShipCore.ShieldBrokenClient += OnShieldBroken;
            pawnHook = new HealthComponentHook(OnPawnDamaged);
            shipHook = new HealthComponentHook(OnShipDamaged);
        }

        static RectTransform Pip(RectTransform track, Color c, Sprite sprite, float size)
        {
            var img = UIKit.Image(track, "Pip", c, false);
            img.sprite = sprite;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        // ================================================================ events

        static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        // Hit marker (#43): white ticks for hits, yellow for crits (weak points), a bigger red X for kills + combo.
        void OnLocalHit(LocalHit h)
        {
            if (hitKind == 2 && !h.Killed && Time.unscaledTime - hitTime < 0.25f) return; // let the kill X finish
            hitTime = Time.unscaledTime;
            hitKind = h.Killed ? 2 : h.Crit ? 1 : 0;
            hitColor = h.Killed ? HudKit.Hex(0xF87171) : h.Crit ? HudKit.Fuel : HudKit.TextHi;
            hitLabel.text = h.Killed ? (h.Combo > 1 ? "KILL  x" + h.Combo : "KILL") : h.Crit ? "WEAK POINT" : "";
            hitLabel.color = hitColor;
        }

        void OnAnyImmune(HealthComponent h, Vector3 point, ulong shooter)
        {
            if (shooter != LocalId) return;
            hitTime = Time.unscaledTime;
            hitKind = 3;
            hitColor = HudKit.TextDim;
            hitLabel.text = "SHIELDED";
            hitLabel.color = hitColor;
        }

        void OnNotification(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            Color accent = ToastColor(msg);
            var rt = UIKit.Rect(toastLayer, "Toast");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 380;
            var body = HudKit.Plate(rt, "Body").rectTransform;
            UIKit.Stretch(body);
            HudKit.AccentBar(body, accent);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var item = ItemIn(msg);
            float textLeft = 16f;
            if (item != null && item.Icon != null)
            {
                var ic = UIKit.ItemIcon(body, item, 38);
                UIKit.Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(38, 38), new Vector2(0, 0.5f));
                textLeft = 58f;
            }
            var t = UIKit.Text(body, msg, 18, TextAlignmentOptions.MidlineLeft, HudKit.TextHi);
            t.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Stretch(t.rectTransform, textLeft, 12, 8, 8);
            float h = Mathf.Max(46f, t.GetPreferredValues(msg, 380f - textLeft - 12f, 0f).y + 18f);
            le.preferredHeight = h;
            rt.sizeDelta = new Vector2(380, h);
            toasts.Add(new Toast { Rt = rt, Body = body, Group = group, Born = Time.unscaledTime, Until = Time.unscaledTime + 4.5f });
            while (toasts.Count > 4) { if (toasts[0].Rt) Object.Destroy(toasts[0].Rt.gameObject); toasts.RemoveAt(0); }
        }

        static Color ToastColor(string m)
        {
            string l = m.ToLowerInvariant();
            if (l.Contains("warning") || l.Contains("collapse") || l.Contains("enraged") || l.Contains("destroyed!") || l.Contains("lost") || l.Contains("storm") || l.Contains("blizzard") || l.Contains("is down")) return HudKit.Danger;
            if (l.Contains("+") || l.Contains("received") || l.Contains("installed") || l.Contains("crafted") || l.Contains("bought")) return HudKit.Gold;
            if (l.Contains("joined") || l.Contains("jump")) return HudKit.Health;
            return HudKit.Shield;
        }

        /// <summary>First item whose display name appears in the message (longest names first).</summary>
        ItemDef ItemIn(string msg)
        {
            if (itemsByName == null)
            {
                itemsByName = new List<ItemDef>();
                var cat = ContentCatalog.Instance;
                if (cat != null) foreach (var it in cat.Items) if (it != null && !string.IsNullOrEmpty(it.DisplayName)) itemsByName.Add(it);
                itemsByName.Sort((a, b) => b.DisplayName.Length.CompareTo(a.DisplayName.Length));
            }
            foreach (var it in itemsByName)
                if (msg.IndexOf(it.DisplayName, System.StringComparison.OrdinalIgnoreCase) >= 0) return it;
            return null;
        }

        public void SetVisible(bool v)
        {
            if (root.gameObject.activeSelf != v) root.gameObject.SetActive(v);
        }

        // ================================================================ per frame

        public void Update()
        {
            UpdateToasts();
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
            var seatShip = pawn != null && pawn.CurrentShipSeat != null ? pawn.CurrentShipSeat.Ship : null;
            pawnHook.Track(pawn != null ? pawn.Health : null);
            shipHook.Track(seatShip != null ? seatShip.Hull : null);

            UpdateLowHealth(pawn, seatShip);
            vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, 0f, Time.deltaTime * 1.4f);
            // Hurt flash = bright red. The low-health pulse is a near-black red at high alpha: UI blends in linear
            // space, so it acts like darkening the edges and reads about the same on sunlit sand and in deep space
            // (a brighter red glows over space and vanishes over bright planets).
            vignette.color = vignetteAlpha >= lowHealthPulse
                ? new Color(0.86f, 0.15f, 0.15f, vignetteAlpha)
                : new Color(0.3f, 0.01f, 0.02f, lowHealthPulse);
            damageArcs.Update(cam, seatShip != null);
            vitalsKick = Kick(vitalsPlate, vitalsPlateHome, vitalsKick);
            shipKick = Kick(shipPlate, shipPlateHome, shipKick);

            if (pawn != null)
            {
                UpdateVitals(pawn);
                UpdateHotbar(pawn);
                UpdateCentre(pawn);
                UpdateShip(pawn);
                creditsText.text = HudKit.Num(HudKit.F(pawn.Inventory.Credits.Value, "N0"));
                UpdateDeath(pawn);
            }

            var rm = RunManager.Instance;
            if (rm != null && pawn != null)
            {
                UpdateMeter(rm, pawn);
                var plan = rm.Plan;
                var sys = rm.CurrentSystem;
                var wf = WarpFlight.Instance;
                if (wf != null && WarpBubble.Active)
                {
                    sectorTitle.text = $"WARP FLIGHT  -  {Mathf.RoundToInt(wf.Progress01 * 100f)}%";
                    sectorName.text = rm.WarpLabel(wf.To.Value == rm.GalaxyIndex.Value + 1 ? RunManager.WarpNextGalaxy : RunManager.WarpSkipGalaxy);
                }
                else
                {
                    sectorTitle.text = $"GALAXY {rm.GalaxyIndex.Value}/{GalaxyDef.Count}  -  SYSTEM {rm.SystemIndex.Value + 1}/{(plan != null ? plan.Systems.Length : 0)}";
                    sectorName.text = sys != null ? sys.Name : "";
                }
                respawnText.text = HudKit.Num(rm.RespawnsLeft.Value.ToString()) + " <size=70%><color=#94A3B8>RESPAWNS</color></size>";
                bool warping = rm.WarpCountdown.Value >= 0f;
                string flightMsg = WarpMessage();
                jumpPlate.gameObject.SetActive(warping || flightMsg != null);
                if (warping) jumpText.text = "WARP IN " + HudKit.Num(Mathf.CeilToInt(rm.WarpCountdown.Value).ToString()) +
                    $"\n<size=40%><color=#CBD5E1>{rm.WarpLabel(rm.WarpTarget.Value).ToUpperInvariant()}</color></size>";
                else if (flightMsg != null) jumpText.text = flightMsg;
                UpdateObjective(pawn);
            }
            else jumpPlate.gameObject.SetActive(false);

            crew.Update(cam, pawn, systemStartX, systemEndX - systemStartX);

            UpdateBoss();
            numbers.Update(cam);
            if (cam != null)
            {
                UpdateMarkers(cam, pawn);
                UpdateLeadMarker(cam, pawn);
                UpdateCompass(cam, pawn);
            }
        }

        void UpdateToasts()
        {
            float now = Time.unscaledTime;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                if (t.Rt == null) { toasts.RemoveAt(i); continue; }
                float left = t.Until - now;
                if (left <= 0f) { Object.Destroy(t.Rt.gameObject); toasts.RemoveAt(i); continue; }
                float inK = Mathf.Clamp01((now - t.Born) / 0.22f);
                float outK = Mathf.Clamp01(left / 0.35f);
                float slide = (1f - Ease(inK)) * 60f + (1f - outK) * 80f;
                t.Body.offsetMin = new Vector2(slide, 0f);
                t.Body.offsetMax = new Vector2(slide, 0f);
                t.Group.alpha = Mathf.Min(inK, outK);
            }
        }

        static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        void UpdateVitals(PlayerPawn pawn)
        {
            var h = pawn.Health;
            hpGauge.Set(h.Fraction, Mathf.CeilToInt(h.Hp.Value).ToString(), h.Fraction < 0.25f && h.IsAlive);
            float maxO2 = pawn.Gear != null ? pawn.Gear.MaxOxygen : 90f;
            float o2 = pawn.Oxygen.Value / Mathf.Max(1f, maxO2);
            o2Gauge.Set(o2, Mathf.CeilToInt(pawn.Oxygen.Value) + "s", o2 < 0.2f);
            float maxE = pawn.Gear != null ? pawn.Gear.JetpackEnergy : 100f;
            jetGauge.Set(pawn.SuitEnergy / Mathf.Max(1f, maxE), Mathf.CeilToInt(pawn.SuitEnergy).ToString());

            bool low = o2 < 0.25f && !pawn.IsDown.Value && pawn.Active.Value;
            if (low != lowO2) { lowO2 = low; EventBus.RaiseLowOxygenChanged(low); }
            o2Plate.gameObject.SetActive(low && Mathf.Repeat(Time.time, 1f) < 0.65f);
            float pulse = low ? 0.22f + 0.18f * Mathf.Sin(Time.time * Mathf.PI * 2f) : 0f;
            o2Vignette.color = new Color(0.03f, 0.57f, 0.7f, Mathf.MoveTowards(o2Vignette.color.a, pulse, Time.deltaTime * 2f));
            if (low) { lowO2Beep -= Time.deltaTime; if (lowO2Beep <= 0f) { lowO2Beep = 1.6f; Sfx.PlayUI("low_oxygen", 0.7f); } }
        }

        void UpdateHotbar(PlayerPawn pawn)
        {
            bool seated = pawn.CurrentSeat != null;
            hotbarRoot.gameObject.SetActive(!seated);
            toolName.gameObject.SetActive(!seated);
            if (seated) return;
            var cat = ContentCatalog.Instance;
            var medkit = ContentCatalog.GetItem("medkit");
            Sprite[] icons = { cat != null ? cat.IconDrill : null, cat != null ? cat.IconBlaster : null, cat != null ? cat.IconRifle : null, medkit != null ? medkit.Icon : null };
            string[] names = { "Mining Drill", "Blaster", "Pulse Rifle", "Medkit" };
            int medkits = pawn.Inventory != null ? pawn.Inventory.Count("medkit") : 0;
            for (int i = 0; i < 4; i++)
            {
                var s = slots[i];
                bool sel = (int)pawn.CurrentTool == i;
                bool avail = i == 2 ? pawn.Gear != null && pawn.Gear.HasRifle.Value : i != 3 || medkits > 0;
                s.Bg.color = sel ? HudKit.Hex(0x1E293B, 0.95f) : HudKit.Hex(0x0E1420, 0.88f);
                s.Border.effectColor = sel ? HudKit.Shield : HudKit.Hex(0x334155);
                s.Border.effectDistance = sel ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                s.Icon.sprite = icons[i];
                s.Icon.enabled = icons[i] != null;
                s.Icon.color = avail ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                s.Num.color = sel ? HudKit.Shield : HudKit.TextDim;
                s.Qty.text = i == 3 ? HudKit.Num(medkits.ToString()) : "";
                s.Bg.rectTransform.localScale = Vector3.one * (sel ? 1.06f : 1f);
            }
            int cur = Mathf.Clamp((int)pawn.CurrentTool, 0, 3);
            toolName.text = names[cur];
        }

        void UpdateCentre(PlayerPawn pawn)
        {
            var seat = pawn.CurrentShipSeat;
            bool cross = seat == null ? pawn.CurrentSeat == null : seat.Role != StationRole.Engineer;
            crossRing.enabled = crossPip.enabled = cross;

            // Hit marker: 4 diagonal ticks bloom out over 80 ms, then fade. Crits are longer, kills a big held X.
            float t = Time.unscaledTime - hitTime;
            bool kill = hitKind == 2, crit = hitKind == 1;
            float dur = kill ? 0.6f : crit ? 0.42f : 0.35f;
            bool show = t < dur;
            Vector2 tick = kill ? new Vector2(4f, 18f) : crit ? new Vector2(3f, 14f) : new Vector2(2.5f, 11f);
            float r0 = kill ? 30f : 12f, r1 = kill ? 22f : crit ? 27f : 24f;
            for (int i = 0; i < 4; i++)
            {
                hitTicks[i].enabled = show;
                if (!show) continue;
                float r = Mathf.Lerp(r0, r1, Mathf.Clamp01(t / 0.08f));
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                hitTicks[i].rectTransform.sizeDelta = tick;
                hitTicks[i].rectTransform.anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * r;
                hitTicks[i].color = new Color(hitColor.r, hitColor.g, hitColor.b, 1f - Mathf.Clamp01((t - dur * 0.35f) / (dur * 0.65f)));
            }
            hitLabel.alpha = show ? 1f - Mathf.Clamp01((t - dur * 0.6f) / (dur * 0.4f)) : 0f;
            hitLabel.fontSize = kill ? 17f : 13f;

            string p = pawn.Prompt;
            if (p != promptShown)
            {
                promptShown = p;
                promptPlate.gameObject.SetActive(!string.IsNullOrEmpty(p));
                if (!string.IsNullOrEmpty(p))
                {
                    prompt.text = HudKit.Keycap(p);
                    promptPlate.sizeDelta = new Vector2(Mathf.Clamp(prompt.GetPreferredValues(prompt.text).x + 34f, 160f, 900f), 38f);
                }
            }
        }

        void UpdateShip(PlayerPawn pawn)
        {
            ShipCore ship = pawn.CurrentShipSeat != null ? pawn.CurrentShipSeat.Ship : ShipCore.OwnedBy(pawn.OwnerClientId);
            shownShip = ship;
            shipPlate.gameObject.SetActive(ship != null);
            if (ship == null) return;
            var s = ship.Stats;
            shipName.text = ship.ShipName.Value.ToString();
            shipRole.text = pawn.CurrentShipSeat != null ? pawn.CurrentShipSeat.Role.ToString() : HudKit.Dist(Vector3.Distance(ship.transform.position, pawn.transform.position)) + " AWAY";
            hullGauge.Set(ship.Hull.Fraction, Mathf.CeilToInt(ship.Hull.Hp.Value).ToString(), ship.Hull.Fraction < 0.25f);
            float sh = s.MaxShield > 0f ? ship.Shield.Value / s.MaxShield : 0f;
            shieldGauge.Set(sh, Mathf.CeilToInt(ship.Shield.Value).ToString());
            float fuel = s.MaxFuel > 0f ? ship.Fuel.Value / s.MaxFuel : 0f;
            fuelGauge.Set(fuel, Mathf.CeilToInt(ship.Fuel.Value).ToString(), fuel < 0.15f);
            if (WarpBubble.Flying)
            {
                // In the warp bubble the bar is the warp throttle and the number the corridor speed.
                speedFill.fillAmount = WarpBubble.Throttle;
                speedFill.color = HudKit.Energy;
                speedText.text = HudKit.Num(HudKit.F(WarpBubble.Speed, "0"));
                return;
            }
            float spd = ship.Flight != null ? ship.Flight.Speed : 0f;
            speedFill.fillAmount = s.MaxSpeed > 0f ? Mathf.Clamp01(spd / (s.MaxSpeed * 1.6f)) : 0f;
            speedFill.color = spd > s.MaxSpeed * 1.02f ? HudKit.Fuel : HudKit.TextHi;
            speedText.text = HudKit.Num(HudKit.F(spd, "0"));
        }

        void UpdateDeath(PlayerPawn pawn)
        {
            string text = "";
            if (pawn.IsLost.Value) text = "LOST\n<size=50%><color=#CBD5E1>Spectating - your crew fights on</color></size>";
            else if (pawn.IsDown.Value)
            {
                float t = pawn.RespawnAt.Value - Time.time;
                var rm0 = RunManager.Instance;
                text = t > 0f
                    ? $"YOU DIED\n<size=50%><color=#CBD5E1>Respawning in {HudKit.Num(Mathf.CeilToInt(t).ToString())}s   |   team respawns left: {(rm0 != null ? rm0.RespawnsLeft.Value : 0)}</color></size>"
                    : "YOU DIED\n<size=50%><color=#CBD5E1>Waiting for a ship to respawn at...</color></size>";
            }
            deathPlate.gameObject.SetActive(text.Length > 0);
            deathText.text = text;
        }

        // ---------------------------------------------------------------- system meter

        /// <summary>Progress through the system corridor (arrival -> far end) plus galaxy and run clock.</summary>
        void UpdateMeter(RunManager rm, PlayerPawn pawn)
        {
            var flight = WarpFlight.Instance;
            if (flight != null && WarpBubble.Active && flight.Route != null) { UpdateWarpMeter(flight); return; }
            int key = GalaxyGenerator.GalaxySeed(rm.RunSeed.Value, rm.GalaxyIndex.Value) ^ (rm.SystemIndex.Value * 7919 + 1);
            if (key != layoutKey && rm.RunSeed.Value != 0)
            {
                layoutKey = key;
                var layout = GalaxyGenerator.Layout(rm.RunSeed.Value, rm.GalaxyIndex.Value, rm.SystemIndex.Value);
                systemStartX = layout.StartX;
                systemEndX = layout.EndX;
            }
            float len = Mathf.Max(1f, systemEndX - systemStartX);
            float pf = Mathf.Clamp01((pawn.transform.position.x - systemStartX) / len);
            meterFill.fillAmount = pf;
            float w = meterTrack.rect.width;
            pipPlayer.anchoredPosition = new Vector2(pf * w, 13f);
            var np = NetworkPlayer.Local;
            pipPlayer.GetComponent<Image>().color = np != null && NetworkPlayer.All.Count > 1 ? np.CrewColor : HudKit.Shield;
            pipEnd.anchoredPosition = new Vector2(w, 0f);

            var def = rm.Plan != null ? rm.Plan.Def : null;
            meterText.color = HudKit.Teal;
            meterText.text = (def != null ? def.Designation.ToUpperInvariant() : "") + "   " + HudKit.Num(Clock(rm.RunTime));

            etaTimer -= Time.deltaTime;
            if (etaTimer > 0f) return;
            etaTimer = 0.5f;
            var drive = WarpDrive.Instance;
            if (drive == null) { etaText.text = ""; return; }
            int g = rm.GalaxyIndex.Value;
            string Col(int stop) => drive.HasFuelFor(stop) ? "#E2E8F0" : "#EF4444";
            var sb = new System.Text.StringBuilder();
            if (g < GalaxyDef.Count) sb.Append($"NEXT <color={Col(g + 1)}>{DriveMath.Eta(drive.EtaTo(g + 1))}</color>   ");
            if (g + 2 <= GalaxyDef.Count) sb.Append($"SKIP <color={Col(g + 2)}>{DriveMath.Eta(drive.EtaTo(g + 2))}</color>   ");
            sb.Append($"HOME <color=#F59E0B>{DriveMath.Eta(drive.EtaTo(DriveMath.Home))}</color>   ");
            sb.Append($"EXOTIC <color=#C4A5FF>{Mathf.FloorToInt(drive.Exotic.Value)}/{drive.Capacity}</color>");
            etaText.text = sb.ToString();
        }

        // ---------------------------------------------------------------- warp flight (#79)

        static string Short(int stop)
        {
            if (stop > GalaxyDef.Count) return "SOL";
            var def = ContentCatalog.GetGalaxy(stop);
            return def != null ? def.Designation.ToUpperInvariant() : "?";
        }

        /// <summary>System meter = route progress; the line below: throttle, heat, ETA, Exotic Matter, cruise, signal.</summary>
        void UpdateWarpMeter(WarpFlight f)
        {
            var r = f.Route;
            float pf = f.Progress01;
            meterFill.fillAmount = pf;
            float w = meterTrack.rect.width;
            pipPlayer.anchoredPosition = new Vector2(pf * w, 13f);
            pipEnd.anchoredPosition = new Vector2(w, 0f);
            var zone = r.ZoneAt(f.LocalProgress, out _);
            meterText.color = zone == WarpZone.Void ? HudKit.Energy : HudKit.Fuel;
            meterText.text = $"{Short(f.From.Value)} > {Short(f.To.Value)}   {WarpRoute.ZoneLabel(zone)}";

            etaTimer -= Time.deltaTime;
            if (etaTimer > 0f) return;
            etaTimer = 0.2f;
            float throttle = f.Throttle.Value, heat = f.Heat.Value;
            float secs = throttle > 0.05f ? f.SecondsLeft(throttle) : f.PlannedSecondsLeft;
            var drive = WarpDrive.Instance;
            var sb = new System.Text.StringBuilder();
            sb.Append($"THROTTLE <color=#F8FAFC>{Mathf.RoundToInt(throttle * 100f)}%</color>   ");
            sb.Append($"HEAT <color={(heat > 0.75f ? "#EF4444" : heat > 0.5f ? "#FBBF24" : "#F8FAFC")}>{Mathf.RoundToInt(heat * 100f)}%</color>   ");
            sb.Append($"ETA <color=#F59E0B>{DriveMath.Eta(secs / 60f)}</color>   ");
            if (drive != null) sb.Append($"EXOTIC <color=#C4A5FF>{Mathf.FloorToInt(drive.Exotic.Value)}/{drive.Capacity}</color>   ");
            if (f.Cruise.Value) sb.Append("<color=#2DD4BF>CRUISE</color>");
            if (f.NextSignal(out var sig) && !f.DroppedOut.Value)
            {
                float ahead = sig.S - f.LocalProgress;
                bool inRange = Mathf.Abs(ahead) < WarpFlight.DropRange;
                sb.Append($"\n<color=#FCD34D>SIGNAL  {sig.Label.ToUpperInvariant()}  {(ahead > 0f ? HudKit.Dist(ahead) : "ALONGSIDE")}</color>");
                if (inRange && f.LocalIsLeadPilot) sb.Append("  <color=#38BDF8>[G] DROP OUT</color>");
            }
            etaText.text = sb.ToString();
        }

        /// <summary>Big centre message during a warp flight, null when there is nothing to say.</summary>
        string WarpMessage()
        {
            var f = WarpFlight.Instance;
            if (f == null || !WarpBubble.Active) return null;
            string Sub(string s) => $"\n<size=40%><color=#CBD5E1>{s}</color></size>";
            if (f.ResumeCountdown.Value >= 0f) return "WARP IN " + HudKit.Num(Mathf.CeilToInt(f.ResumeCountdown.Value).ToString()) + Sub("EVERYONE BACK TO THE SHIPS");
            if (f.DroppedOut.Value) return "<size=70%>OUT OF WARP</size>" + Sub(f.LocalIsLeadPilot ? "[G] RESUME THE WARP" : "THE LEAD PILOT RESUMES THE WARP");
            if (f.Overheated.Value) return "<size=70%><color=#EF4444>OVERHEAT</color></size>" + Sub($"COOLING DOWN  {Mathf.RoundToInt(f.Heat.Value * 100f)}%");
            if (f.Stalled.Value) return "<size=70%><color=#EF4444>EMERGENCY STOP</color></size>" + Sub("DRIVE RESTARTING");
            if (f.Hazard.Value >= 0 && f.Throttle.Value > 0.05f && Mathf.Repeat(Time.time, 0.5f) < 0.32f)
                return "<size=55%><color=#FBBF24>OBSTACLE AHEAD</color></size>";
            return null;
        }

        /// <summary>Run clock text h:mm:ss (or m:ss in the first hour).</summary>
        public static string Clock(float seconds)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }

        // ---------------------------------------------------------------- objective

        void UpdateObjective(PlayerPawn pawn)
        {
            var rm = RunManager.Instance;
            var sys = rm != null ? rm.CurrentSystem : null;
            string text, header = "EXPEDITION";
            string map = $"<size=80%><color=#94A3B8>{Key("M")} galaxy map - warp on when the crew is ready</color></size>";
            var flight = WarpFlight.Instance;
            bool lead = flight != null && WarpBubble.Active && flight.LocalIsLeadPilot;
            if (flight != null && WarpBubble.Active)
            {
                header = "WARP FLIGHT";
                text = flight.DroppedOut.Value ? "Out of warp at a signal - loot it, then back to the ships"
                    : lead ? "You lead the bubble: throttle down in fields, steer through the gaps"
                    : "Shoot rocks and interceptors - the lead pilot flies the bubble";
                text += $"\n<size=80%><color=#94A3B8>Fields: slow down. Void: full throttle, watch the heat</color></size>";
            }
            else if (BossBrain.Instance != null)
            {
                header = "WARDEN";
                text = BossBrain.Instance.Phase.Value == 1 ? "Destroy the Warden's 3 core pods" : "Destroy the Warden";
            }
            else if (sys != null && sys.Kind == SystemKind.Enclave && GameState.Objective == "Warden enclave")
            {
                header = "WARDEN ENCLAVE";
                text = "A Warden guards a warp core at the far end of this system\n" + map;
            }
            else text = "Explore, salvage and upgrade the ship\n" + map;
            objHeader.text = header;
            objText.text = text;
            bool pilot = pawn.CurrentShipSeat != null && pawn.CurrentShipSeat.Role == StationRole.Pilot;
            string hint = lead ? Key("W") + Key("S") + " THROTTLE    " + Key("X") + " CUT    " + Key("C") + " CRUISE    " + Key("G") + " DROP OUT    " + Key("LMB") + " FIRE"
                : pilot ? Key("LMB") + " " + Key("RMB") + " FIRE    " + Key("SHIFT") + " BOOST    " + Key("X") + " BRAKE    " + Key("F") + " LEAVE" : "";
            objHint.text = hint;
            objHint.gameObject.SetActive(hint.Length > 0);
        }

        static string Key(string k) => $"<mark=#1E293BFF padding=\"6,6,2,2\"><color=#38BDF8><b>{k}</b></color></mark>";

        // ---------------------------------------------------------------- boss

        void UpdateBoss()
        {
            var boss = BossBrain.Instance;
            bool on = boss != null && boss.IsSpawned;
            bossPanel.gameObject.SetActive(on);
            if (!on) return;
            var bh = boss.GetComponent<HealthComponent>();
            int phase = boss.Phase.Value;
            bossTitle.text = phase == 1 ? "THE WARDEN  -  DESTROY THE CORE PODS" : phase == 2 ? "THE WARDEN  -  SHIELD DOWN" : "THE WARDEN  -  ENRAGED";
            bool pods = phase == 1;
            bossSegRow.gameObject.SetActive(pods);
            bossHullTrack.gameObject.SetActive(!pods);
            if (pods)
            {
                for (int i = 0; i < bossSegs.Length; i++)
                    bossSegs[i].fillAmount = i < boss.Pods.Length && boss.Pods[i] != null ? boss.Pods[i].Fraction : 0f;
            }
            else
            {
                float f = bh.Fraction;
                bossHull.fillAmount = f;
                bossGhostFrac = Mathf.Max(f, Mathf.MoveTowards(bossGhostFrac, f, Time.unscaledDeltaTime / 0.5f * 0.25f));
                bossGhost.fillAmount = bossGhostFrac;
                bossHull.color = phase == 3 ? Color.Lerp(HudKit.BossFill, HudKit.Danger, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : HudKit.BossFill;
            }
        }

        // ---------------------------------------------------------------- world markers

        void UpdateMarkers(Camera cam, PlayerPawn pawn)
        {
            var st = TraderStation.Instance;
            stationMarker.Place(cam, markerLayer, st != null ? st.transform.position : (Vector3?)null, Icons.Store, "STATION", HudKit.Fuel, false);
            var ark = FindArk();
            arkMarker.Place(cam, markerLayer, ark != null ? ark.transform.position + ark.transform.up * 8f : (Vector3?)null, Icons.Rocket,
                ark != null && ark.Derelict.Value ? "DERELICT ARK" : "ARK", UIKit.Accent2, false);

            var own = OwnShip(pawn);
            bool aboard = own != null && pawn != null && (pawn.CurrentShipSeat != null && pawn.CurrentShipSeat.Ship == own || Vector3.Distance(own.transform.position, pawn.transform.position) < 14f);
            shipMarker.Place(cam, markerLayer, own != null && !aboard ? own.transform.position + own.transform.up * 3f : (Vector3?)null, Icons.Ship, "YOUR SHIP", HudKit.Shield, true);

            UpdateBossMarkers(cam);

            pings.RemoveAll(p => Time.time > p.Until);
            while (pingMarkers.Count < pings.Count) pingMarkers.Add(new HudMarker(markerLayer));
            for (int i = 0; i < pingMarkers.Count; i++)
            {
                if (i >= pings.Count) { pingMarkers[i].Hide(); continue; }
                var p = pings[i];
                Color c = p.Kind == PingKind.Enemy ? HudKit.Danger : p.Kind == PingKind.Loot ? HudKit.Fuel : p.Kind == PingKind.Gate ? HudKit.Gold : HudKit.TextHi;
                pingMarkers[i].Place(cam, markerLayer, p.Pos, p.Kind == PingKind.Enemy ? Icons.Danger : Icons.Pin, p.Label, c, true);
            }
            UpdateSiteMarkers(cam);
        }

        static ShipCore FindArk()
        {
            foreach (var s in ShipCore.All) if (s != null && s.Class == ShipClass.Ark) return s;
            return null;
        }

        static ShipCore OwnShip(PlayerPawn pawn) => pawn != null ? ShipCore.OwnedBy(pawn.OwnerClientId) : null;

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
                    Color c = kind == SiteKind.Hive ? HudKit.Danger : kind == SiteKind.CrashSite ? UIKit.Accent2 : kind == SiteKind.Ruins || kind == SiteKind.Cache ? HudKit.Fuel : HudKit.Health;
                    string glyph = kind == SiteKind.Hive ? Icons.Danger : kind == SiteKind.CrashSite ? Icons.Flight : kind == SiteKind.CrystalGrove || kind == SiteKind.OreVein ? Icons.Diamond : kind == SiteKind.FuelVent ? Icons.Fuel : Icons.Explore;
                    SiteMarker(used++).Place(cam, markerLayer, p + (p - center).normalized * 8f, glyph, PlanetBody.SiteLabel(kind), c, false);
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
                        SiteMarker(used++).Place(cam, markerLayer, p, Icons.Pin, lm.Name, HudKit.TextDim, false, 0.8f);
                        shown++;
                    }
                }
            }
            for (int i = used; i < siteMarkers.Count; i++) siteMarkers[i].Hide();
        }

        HudMarker SiteMarker(int i)
        {
            while (siteMarkers.Count <= i) siteMarkers.Add(new HudMarker(markerLayer));
            return siteMarkers[i];
        }

        static bool AboveHorizon(Vector3 center, Vector3 p, Vector3 camPos, float minDist, float maxDist)
        {
            Vector3 to = camPos - p;
            float d = to.magnitude;
            if (d < minDist || d > maxDist) return false;
            return Vector3.Dot((p - center).normalized, to / d) > -0.05f;
        }

        /// <summary>Boss fight: mark the live core pods while the hull is shielded, then the hull itself.</summary>
        void UpdateBossMarkers(Camera cam)
        {
            var boss = BossBrain.Instance;
            bool fight = boss != null && boss.IsSpawned;
            for (int i = 0; i < bossMarkers.Length; i++)
            {
                if (fight && boss.Phase.Value == 1 && i < boss.Pods.Length && boss.Pods[i] != null && boss.Pods[i].IsAlive)
                    bossMarkers[i].Place(cam, markerLayer, boss.Pods[i].transform.position, Icons.Target, "CORE", HudKit.BossTitle, true);
                else if (fight && boss.Phase.Value >= 2 && i == bossMarkers.Length - 1)
                    bossMarkers[i].Place(cam, markerLayer, boss.transform.position, Icons.Target, "HULL - FIRE!", HudKit.BossTitle, true);
                else bossMarkers[i].Hide();
            }
        }

        // ---------------------------------------------------------------- compass

        void UpdateCompass(Camera cam, PlayerPawn pawn)
        {
            Vector3 up = cam.transform.up;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(cam.transform.up * -1f, up);
            Vector3 north = Vector3.ProjectOnPlane(Vector3.right, up);
            if (north.sqrMagnitude < 0.04f) north = Vector3.ProjectOnPlane(Vector3.forward, up);
            float heading = Vector3.SignedAngle(north, fwd, up);
            float pxPerDeg = CompassWidth * 0.5f / CompassRange;

            foreach (var (rt, deg) in compassTicks)
            {
                float off = Mathf.DeltaAngle(heading, deg);
                bool vis = Mathf.Abs(off) <= CompassRange + 5f;
                rt.gameObject.SetActive(vis);
                if (vis) rt.anchoredPosition = new Vector2(off * pxPerDeg, rt.sizeDelta.y > 20f ? -13f : -18f);
            }

            Vector3 camPos = cam.transform.position;
            var marks = new List<(string id, string glyph, Vector3 pos, Color c)>();
            var st = TraderStation.Instance;
            if (st != null) marks.Add(("station", Icons.Store, st.transform.position, HudKit.Fuel));
            var ark = FindArk();
            if (ark != null) marks.Add(("ark", Icons.Rocket, ark.transform.position, UIKit.Accent2));
            var own = OwnShip(pawn);
            if (own != null && (pawn == null || pawn.CurrentShipSeat == null || pawn.CurrentShipSeat.Ship != own))
                marks.Add(("ship", Icons.Ship, own.transform.position, HudKit.Shield));
            var boss = BossBrain.Instance;
            if (boss != null && boss.IsSpawned) marks.Add(("boss", Icons.Danger, boss.transform.position, HudKit.BossTitle));
            for (int i = 0; i < pings.Count; i++) marks.Add(("ping" + i, Icons.Pin, pings[i].Pos, HudKit.TextHi));
            foreach (var m in crew.Teammates) marks.Add(("crew" + m.Id, m.Down ? Icons.Danger : Icons.Person, m.Pos, m.Down ? HudKit.Danger : m.Color));

            var used = new HashSet<string>();
            foreach (var m in marks)
            {
                used.Add(m.id);
                if (!compassMarks.TryGetValue(m.id, out var cm))
                {
                    cm = new CompassMark { Rt = UIKit.Rect(compassStrip, "Mark") };
                    cm.Rt.anchorMin = cm.Rt.anchorMax = new Vector2(0.5f, 1f);
                    cm.Rt.sizeDelta = new Vector2(30, 50);
                    cm.Rt.pivot = new Vector2(0.5f, 1f);
                    cm.Icon = UIKit.IconText(cm.Rt, m.glyph, 19, m.c);
                    UIKit.Place(cm.Icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(30, 22), new Vector2(0.5f, 1f));
                    cm.Dist = UIKit.Text(cm.Rt, "", 12, TextAlignmentOptions.Center, m.c, FontStyles.Bold);
                    cm.Dist.textWrappingMode = TextWrappingModes.NoWrap;
                    UIKit.Place(cm.Dist.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -27), new Vector2(70, 18), new Vector2(0.5f, 1f));
                    compassMarks[m.id] = cm;
                }
                Vector3 to = m.pos - camPos;
                float ang = Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(to, up), up);
                bool edge = Mathf.Abs(ang) > CompassRange - 4f;
                cm.Rt.anchoredPosition = new Vector2(Mathf.Clamp(ang, -(CompassRange - 4f), CompassRange - 4f) * pxPerDeg, 0f);
                cm.Icon.text = m.glyph;
                cm.Icon.color = m.c;
                cm.Dist.color = m.c;
                cm.Icon.alpha = edge ? 0.45f : 1f;
                // Distances only for the key bearings, so clustered markers don't overprint each other.
                bool key = m.id == "gate" || m.id == "ship" || m.id == "boss" || m.id.StartsWith("ping") || m.id.StartsWith("crew");
                cm.Dist.text = key ? HudKit.Dist(to.magnitude) : "";
                cm.Dist.alpha = edge ? 0f : 0.9f;
            }
            foreach (var kv in compassMarks) kv.Value.Rt.gameObject.SetActive(used.Contains(kv.Key));
        }

        // ---------------------------------------------------------------- lead indicator

        /// <summary>Ship lead indicator: four ticks around the predicted intercept point, plus a link to the target.</summary>
        void BuildLeadMarker()
        {
            leadLink = UIKit.Image(markerLayer, "LeadLink", Color.white, false);
            leadLink.raycastTarget = false;
            leadLink.rectTransform.anchorMin = leadLink.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            leadLink.rectTransform.pivot = new Vector2(0f, 0.5f);
            leadMarker = UIKit.Rect(markerLayer, "Lead");
            leadMarker.anchorMin = leadMarker.anchorMax = new Vector2(0.5f, 0.5f);
            leadMarker.sizeDelta = new Vector2(30, 30);
            for (int i = 0; i < 4; i++)
            {
                leadTicks[i] = UIKit.Image(leadMarker, "Tick", Color.white, false);
                leadTicks[i].raycastTarget = false;
                var rt = leadTicks[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = i % 2 == 0 ? new Vector2(3, 9) : new Vector2(9, 3);
            }
            leadMarker.gameObject.SetActive(false);
            leadLink.gameObject.SetActive(false);
        }

        void UpdateLeadMarker(Camera cam, PlayerPawn pawn)
        {
            var seat = pawn != null ? pawn.CurrentShipSeat : null;
            bool show = seat != null && seat.Role != StationRole.Engineer && AimAssist.FreshThisFrame && AimAssist.HasLead;
            Vector3 sp = show ? cam.WorldToScreenPoint(AimAssist.LeadPoint) : Vector3.zero;
            Vector3 tp = show ? cam.WorldToScreenPoint(AimAssist.TargetPoint) : Vector3.zero;
            show &= sp.z > 0f && tp.z > 0f;
            leadMarker.gameObject.SetActive(show);
            leadLink.gameObject.SetActive(show);
            if (!show) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(markerLayer, sp, null, out var lead);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(markerLayer, tp, null, out var tgt);
            bool locked = AimAssist.Locked;
            Color c = locked ? new Color(0.35f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.55f);
            float r = locked ? 9f + Mathf.Sin(Time.time * 12f) * 1.5f : 14f;
            leadMarker.anchoredPosition = lead;
            for (int i = 0; i < 4; i++)
            {
                leadTicks[i].color = c;
                float a = i * Mathf.PI * 0.5f;
                leadTicks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
            }
            Vector2 d = lead - tgt;
            leadLink.color = new Color(c.r, c.g, c.b, 0.3f);
            leadLink.rectTransform.anchoredPosition = tgt;
            leadLink.rectTransform.sizeDelta = new Vector2(d.magnitude, 1.5f);
            leadLink.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
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

        // ---------------------------------------------------------------- damage taken (#43)

        void OnPawnDamaged(DamageInfo i)
        {
            if (i.Amount < 0.01f) return;
            float k = Mathf.Clamp01(i.Amount / 40f);
            EventBus.RaiseLocalPlayerHurt(k);
            FeedbackFx.LocalHurt(k, false);
            hpGauge.Flash(HudKit.Danger, 0.6f + k);
            vitalsKick = Mathf.Max(vitalsKick, 0.4f + k * 0.6f);
            var pawn = PlayerPawn.Local;
            if (pawn != null) damageArcs.Add(Camera.main, SourceOf(i, pawn.transform.position), HudKit.Danger, k, pawn.CurrentShipSeat != null);
        }

        /// <summary>The ship the local player is seated in got hit. Amount 0 = the shield soaked all of it.</summary>
        void OnShipDamaged(DamageInfo i)
        {
            var pawn = PlayerPawn.Local;
            var ship = pawn != null && pawn.CurrentShipSeat != null ? pawn.CurrentShipSeat.Ship : null;
            if (ship == null) return;
            bool shield = i.Amount < 0.01f;
            float k = shield ? 0.3f : Mathf.Clamp01(i.Amount / Mathf.Max(1f, ship.Hull.MaxHp.Value * 0.12f));
            FeedbackFx.LocalHurt(k, shield);
            if (shield) shieldGauge.Flash(Color.white, 0.7f);
            else hullGauge.Flash(HudKit.Danger, 0.6f + k);
            shipKick = Mathf.Max(shipKick, shield ? 0.35f : 0.4f + k * 0.6f);
            if (!shield) EventBus.RaiseLocalPlayerHurt(k * 0.6f);
            damageArcs.Add(Camera.main, SourceOf(i, ship.transform.position), shield ? ShieldArc : HudKit.Danger, k, true);
        }

        /// <summary>Paler than the shield gauge so the arc still reads against blue planets and nebulae.</summary>
        static readonly Color ShieldArc = new Color(0.7f, 0.92f, 1f);

        void OnShieldBroken(ShipCore ship)
        {
            if (ship == null || ship != shownShip) return;
            shieldGauge.Flash(Color.white, 1f);
            shipKick = 1f;
            var pawn = PlayerPawn.Local;
            if (pawn == null || pawn.CurrentShipSeat == null || pawn.CurrentShipSeat.Ship != ship) return;
            FeedbackFx.LocalShieldBroken();
            OnNotification("Shields down - hull exposed");
        }

        /// <summary>Where a hit came from: back along the projectile's travel, else outwards through the hit point.</summary>
        static Vector3 SourceOf(DamageInfo i, Vector3 target)
        {
            if (i.Direction.sqrMagnitude > 0.01f) return i.Point - i.Direction.normalized * 30f;
            var d = i.Point - target;
            return d.sqrMagnitude > 0.01f ? target + d.normalized * 30f : i.Point;
        }

        /// <summary>
        /// Below 30 % health (on foot) or 25 % hull (seated): the red vignette pulses with a heartbeat / klaxon whose
        /// rate and strength climb as the bar drops. Off with Settings > Feedback > Low health warning.
        /// </summary>
        void UpdateLowHealth(PlayerPawn pawn, ShipCore seatShip)
        {
            float severity = 0f;
            bool ship = seatShip != null;
            if (pawn != null && pawn.Health != null && pawn.Health.IsAlive && !pawn.IsDown.Value && pawn.Active.Value)
            {
                if (ship) { float f = seatShip.Hull.Fraction; if (seatShip.Hull.IsAlive && f < 0.25f) severity = 1f - f / 0.25f; }
                else { float f = pawn.Health.Fraction; if (f < 0.3f) severity = 1f - f / 0.3f; }
            }
            if (severity <= 0f || !FeedbackFx.LowHealthWarning)
            {
                lowHealthPulse = Mathf.MoveTowards(lowHealthPulse, 0f, Time.deltaTime);
                lowHealthBeat = 0f;
                return;
            }
            float period = ship ? 1.6f : Mathf.Lerp(1.1f, 0.55f, severity);
            lowHealthBeat -= Time.deltaTime;
            if (lowHealthBeat <= 0f)
            {
                lowHealthBeat = period;
                FeedbackFx.LowHealthBeat(ship, severity);
            }
            // Pulse peaks on the beat and decays until the next one.
            float phase = 1f - Mathf.Clamp01(lowHealthBeat / period);
            float beat = Mathf.Exp(-phase * 5f);
            lowHealthPulse = ((0.25f + 0.25f * severity) + beat * (0.15f + 0.15f * severity)) * (ship ? 0.5f : 1f);
        }

        /// <summary>Decaying jitter on a HUD plate (scaled by Screen shake). Returns the decayed kick.</summary>
        static float Kick(RectTransform plate, Vector2 home, float kick)
        {
            kick = Mathf.MoveTowards(kick, 0f, Time.unscaledDeltaTime / 0.3f);
            float amp = kick * kick * 9f * Mathf.Clamp01(Vfx.ShakeScale);
            plate.anchoredPosition = home + (amp > 0.01f ? new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp) * 0.6f) : Vector2.zero);
            return kick;
        }

        /// <summary>Forwards DamagedClient of one tracked HealthComponent (local pawn or seated ship hull).</summary>
        class HealthComponentHook
        {
            HealthComponent tracked;
            readonly System.Action<DamageInfo> handler;
            public HealthComponentHook(System.Action<DamageInfo> h) => handler = h;
            void OnDamaged(DamageInfo i) => handler(i);
            public void Track(HealthComponent h)
            {
                if (h == tracked) return;
                if (!ReferenceEquals(tracked, null)) tracked.DamagedClient -= OnDamaged;
                tracked = h;
                if (h != null) h.DamagedClient += OnDamaged;
            }
        }
    }
}
