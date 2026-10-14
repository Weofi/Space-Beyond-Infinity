using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>
    /// Galaxy map (#46, key M): the route home through the Local Group, the systems of the current galaxy with
    /// kind, threat and visit state, and the warp course. A pilot picks a system (short in-galaxy warp, also back
    /// to visited ones) or the one-way warp to the next galaxy (or skips one, #78); from the last galaxy that is the
    /// flight home. Galaxy warps show the Home ETA, flight time and Exotic Matter need (red = not reachable).
    /// Until the flown warp of #79/#80 a warp is a short countdown, then the whole crew jumps.
    /// </summary>
    public class GalaxyMapView
    {
        static readonly Vector2 MapSize = new Vector2(980f, 600f);

        readonly RectTransform panel;
        readonly TextMeshProUGUI header, route;
        RectTransform body;
        int selected = -1;
        int shownSystem = -1;
        int confirmTarget;
        float timer;

        public bool IsOpen => panel.gameObject.activeSelf;

        public GalaxyMapView(RectTransform root)
        {
            panel = UIKit.Panel(root, "GalaxyMapPanel", new Color(0.03f, 0.06f, 0.13f, 0.97f));
            UIKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 860));
            UIKit.VLayout(panel.gameObject, 10, 22);
            var top = UIKit.Row(panel, 56);
            header = UIKit.Text(top, "", 30, TextAlignmentOptions.Left, UIKit.Warn, FontStyles.Bold);
            UIKit.LayoutElement(header.gameObject, -1, 56, 1);
            UIKit.LayoutElement(UIKit.Button(top, "Warp Drive", EventBus.RaiseDrivePanelRequested, 52, UIKit.Accent, 22).gameObject, 200, 52);
            UIKit.LayoutElement(UIKit.Button(top, "Close", Close, 52, UIKit.Bad, 22).gameObject, 160, 52);
            route = UIKit.Text(panel, "", 22, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(route.gameObject, -1, 34);
            body = UIKit.Rect(panel, "Body");
            panel.gameObject.SetActive(false);
        }

        public void Open()
        {
            var rm = RunManager.Instance;
            if (rm == null || rm.RunSeed.Value == 0) return;
            selected = rm.SystemIndex.Value;
            shownSystem = rm.GalaxyIndex.Value * 16 + rm.SystemIndex.Value;
            confirmTarget = 0;
            panel.gameObject.SetActive(true);
            timer = 0f;
            Sfx.PlayUI("ui_click");
        }

        public void Close() { if (IsOpen) panel.gameObject.SetActive(false); }

        public void Update()
        {
            if (!IsOpen) return;
            var rm = RunManager.Instance;
            if (rm == null || GameState.Phase != GamePhase.Playing) { Close(); return; }
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            Rebuild(rm);
        }

        static bool LocalIsPilot()
        {
            var pawn = PlayerPawn.Local;
            if (pawn == null) return false;
            var seat = pawn.CurrentShipSeat;
            return seat != null && seat.Role == StationRole.Pilot;
        }

        void Select(int i) { selected = i; confirmTarget = 0; timer = 0f; Sfx.PlayUI("ui_click"); }

        void Rebuild(RunManager rm)
        {
            var plan = rm.Plan;
            var def = plan.Def;
            // After a warp the map follows the crew.
            int here = rm.GalaxyIndex.Value * 16 + rm.SystemIndex.Value;
            if (here != shownSystem) { shownSystem = here; selected = rm.SystemIndex.Value; confirmTarget = 0; }
            int g = rm.GalaxyIndex.Value;
            header.text = $"{RunManager.GalaxyTitle(def)}   <size=70%><color=#94A3B8>{(def != null ? def.DistanceMly.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "?")} M ly from home  |  run {HudView.Clock(rm.RunTime)}</color></size>";
            var sb = new System.Text.StringBuilder("ROUTE   ");
            for (int i = 1; i <= GalaxyDef.Count + 1; i++)
            {
                var gd = i <= GalaxyDef.Count ? ContentCatalog.GetGalaxy(i) : null;
                string name = i > GalaxyDef.Count ? "MILKY WAY" : gd != null ? gd.Designation : "?";
                string col = i < g ? "#64748B" : i == g ? "#F59E0B" : "#CBD5E1";
                sb.Append($"<color={col}>{(i == g ? "<b>" + name + "</b>" : name)}</color>");
                if (i <= GalaxyDef.Count) sb.Append("  <color=#475569>></color>  ");
            }
            var drive = WarpDrive.Instance;
            if (drive != null)
                sb.Append($"     <color=#94A3B8>HOME ETA</color> <color=#F59E0B><b>{DriveMath.Eta(drive.EtaTo(DriveMath.Home))}</b></color>" +
                          $"   <color=#94A3B8>EXOTIC</color> <b>{Mathf.FloorToInt(drive.Exotic.Value)}/{drive.Capacity}</b>");
            route.text = sb.ToString();

            Object.Destroy(body.gameObject);
            body = UIKit.Rect(panel, "Body");
            UIKit.LayoutElement(body.gameObject, -1, 700, 1, 1);
            UIKit.HLayout(body.gameObject, 22, 0, TextAnchor.UpperLeft);

            // ------------------------------------------------ map
            var mapWrap = UIKit.Panel(body, "Map", new Color(0.008f, 0.014f, 0.035f, 0.98f));
            UIKit.LayoutElement(mapWrap.gameObject, MapSize.x + 40f, MapSize.y + 40f);
            var map = UIKit.Rect(mapWrap, "Area");
            UIKit.Place(map, new Vector2(0.5f, 0.5f), Vector2.zero, MapSize);
            Vector2 P(StarSystem s) => new Vector2((s.MapPos.x - 0.5f) * MapSize.x, (s.MapPos.y - 0.5f) * MapSize.y);
            // Lanes from the arrival system outwards (nearest earlier system), purely for readability.
            for (int i = 1; i < plan.Systems.Length; i++)
            {
                int best = 0;
                for (int j = 0; j < i; j++)
                    if (Vector2.Distance(plan.Systems[j].MapPos, plan.Systems[i].MapPos) < Vector2.Distance(plan.Systems[best].MapPos, plan.Systems[i].MapPos)) best = j;
                Line(map, P(plan.Systems[best]), P(plan.Systems[i]), new Color(0.3f, 0.45f, 0.7f, 0.35f));
            }
            for (int i = 0; i < plan.Systems.Length; i++) Node(map, plan.Systems[i], P(plan.Systems[i]), rm, i);

            // ------------------------------------------------ details
            var info = UIKit.Rect(body, "Info");
            UIKit.VLayout(info.gameObject, 10, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(info.gameObject, 420, 640);
            var sys = plan.Systems[Mathf.Clamp(selected, 0, plan.Systems.Length - 1)];
            bool current = sys.Index == rm.SystemIndex.Value;
            Line(info, UIKit.Text(info, sys.Name.ToUpperInvariant(), 32, TextAlignmentOptions.Left, UIKit.TextColor, FontStyles.Bold), 44);
            Line(info, UIKit.Text(info, GalaxyGenerator.KindLabel(sys.Kind), 24, TextAlignmentOptions.Left, KindColor(sys.Kind), FontStyles.Bold), 32);
            string threat = new string('*', sys.Threat) + "<color=#334155>" + new string('*', 5 - sys.Threat) + "</color>";
            Line(info, UIKit.Text(info, $"Threat  <color=#EF4444>{threat}</color>", 24, TextAlignmentOptions.Left, UIKit.TextDim), 32);
            string state = current ? "<color=#38BDF8>You are here</color>" : rm.IsVisited(sys.Index) ? "Visited - its crates are already opened" : "Unexplored";
            Line(info, UIKit.Text(info, state, 22, TextAlignmentOptions.Left, UIKit.TextDim), 30);
            Line(info, UIKit.Text(info, GalaxyGenerator.KindHint(sys.Kind), 22, TextAlignmentOptions.TopLeft, UIKit.TextDim), 70);

            bool pilot = LocalIsPilot();
            bool warping = rm.WarpCountdown.Value >= 0f;
            if (warping)
            {
                Line(info, UIKit.Text(info, $"Warp to <b>{rm.WarpLabel(rm.WarpTarget.Value)}</b> in {Mathf.CeilToInt(rm.WarpCountdown.Value)} s", 24, TextAlignmentOptions.Left, UIKit.Warn), 34);
                UIKit.LayoutElement(UIKit.Button(info, "Cancel warp", () => rm.CancelWarpServerRpc(), 56, UIKit.Bad, 24).gameObject, -1, 56);
            }
            else
            {
                var warp = UIKit.Button(info, current ? "You are here" : pilot ? "Warp here" : "Pilot seat needed", () => { rm.RequestWarpServerRpc(sys.Index); }, 52, UIKit.Primary, 26);
                warp.interactable = !current && pilot;
                UIKit.LayoutElement(warp.gameObject, -1, 52);
            }

            UIKit.LayoutElement(UIKit.Image(info, "Rule", new Color(1f, 1f, 1f, 0.12f), false).gameObject, -1, 2);
            bool last = g >= GalaxyDef.Count;
            Line(info, UIKit.Text(info, last ? "The Milky Way fills the sky. Home is one warp away." : "One-way: this galaxy's systems stay behind.", 20, TextAlignmentOptions.TopLeft, UIKit.TextDim), 28);
            GalaxyWarp(info, rm, RunManager.WarpNextGalaxy, last ? "Fly home" : "Warp to next galaxy", pilot, warping);
            if (g + 2 <= GalaxyDef.Count) GalaxyWarp(info, rm, RunManager.WarpSkipGalaxy, "Skip a galaxy", pilot, warping);
        }

        /// <summary>One galaxy warp option: destination, flight time and Exotic Matter need, then a two-click warp button.</summary>
        void GalaxyWarp(RectTransform info, RunManager rm, int target, string label, bool pilot, bool warping)
        {
            var drive = WarpDrive.Instance;
            int stop = rm.StopOf(target);
            int need = WarpDrive.FuelTo(stop);
            bool tankOk = drive == null || drive.TankCanHold(stop);
            bool fuelOk = drive == null || drive.HasFuelFor(stop);
            string eta = drive != null ? DriveMath.Eta(drive.EtaTo(stop)) : "--";
            string fuel = !tankOk ? $"<color=#EF4444>needs {need} Exotic - tank too small ({drive.Capacity})</color>"
                : !fuelOk ? $"<color=#EF4444>needs {need} Exotic - {Mathf.FloorToInt(drive.Exotic.Value)} loaded</color>"
                : $"<color=#94A3B8>needs {need} Exotic</color>";
            Line(info, UIKit.Text(info, $"<b>{rm.WarpLabel(target)}</b>   <color=#F59E0B>{eta}</color>\n<size=85%>{fuel}</size>", 21, TextAlignmentOptions.TopLeft, UIKit.TextColor), 52);
            bool confirm = confirmTarget == target;
            string text = !pilot ? "Pilot seat needed" : !tankOk || !fuelOk ? "Not enough Exotic Matter" : confirm ? "Confirm - warp now" : label;
            var btn = UIKit.Button(info, text, () =>
            {
                if (confirmTarget != target) { confirmTarget = target; timer = 0f; return; }
                confirmTarget = 0;
                rm.RequestWarpServerRpc(target);
            }, 52, confirm ? UIKit.Warn : target == RunManager.WarpSkipGalaxy ? UIKit.Accent : UIKit.Good, 24);
            btn.interactable = pilot && !warping && (tankOk && fuelOk || CheatManager.GodMode);
            UIKit.LayoutElement(btn.gameObject, -1, 52);
        }

        static void Line(Transform parent, TextMeshProUGUI t, float h) => UIKit.LayoutElement(t.gameObject, -1, h);

        static Color KindColor(SystemKind k)
        {
            switch (k)
            {
                case SystemKind.Enclave: return HudKit.Danger;
                case SystemKind.Anomaly: return HudKit.Energy;
                case SystemKind.Nomads: return HudKit.Fuel;
                case SystemKind.AsteroidField: return HudKit.Hull;
                case SystemKind.Void: return HudKit.TextDim;
                default: return HudKit.Teal;
            }
        }

        void Node(RectTransform map, StarSystem s, Vector2 pos, RunManager rm, int index)
        {
            bool current = s.Index == rm.SystemIndex.Value;
            bool visited = rm.IsVisited(s.Index);
            float size = s.Kind == SystemKind.Enclave ? 46f : 36f;
            var img = UIKit.Image(map, "System_" + s.Index, KindColor(s.Kind), false);
            img.sprite = UIKit.Circle;
            if (!visited && !current) img.color = Color.Lerp(KindColor(s.Kind), Color.black, 0.35f);
            UIKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => Select(index));
            if (current || index == selected)
            {
                var ring = UIKit.Image(map, "Ring", current ? HudKit.Shield : HudKit.TextHi, false);
                ring.sprite = HudKit.Ring;
                ring.raycastTarget = false;
                UIKit.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(size + 22f, size + 22f));
            }
            var label = UIKit.Text(map, s.Name + (current ? "\n<size=75%><color=#38BDF8>YOU ARE HERE</color></size>" : visited ? "\n<size=75%><color=#64748B>visited</color></size>" : ""), 20, TextAlignmentOptions.Top, UIKit.TextColor, FontStyles.Bold);
            label.raycastTarget = false;
            UIKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(0f, -size * 0.5f - 6f), new Vector2(220f, 52f), new Vector2(0.5f, 1f));
        }

        static void Line(RectTransform parent, Vector2 a, Vector2 b, Color c)
        {
            var img = UIKit.Image(parent, "Lane", c, false);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            Vector2 d = b - a;
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), (a + b) * 0.5f, new Vector2(d.magnitude, 3f));
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            rt.SetAsFirstSibling();
        }
    }
}
