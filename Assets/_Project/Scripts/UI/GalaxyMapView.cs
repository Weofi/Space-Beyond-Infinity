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
    /// Galaxy map (#46, key M): the route home through the Local Group, the systems of the current galaxy with kind,
    /// threat and visit state, the hop lanes between them and the jump zones of the current system. Navigation only
    /// (#108): nobody warps from here - the crew flies out through a jump zone at the system edge (hops to the
    /// neighbouring systems; the Galactic Rim and the skip zone beyond the outermost system). Galaxy warps show the
    /// Home ETA, flight time and Exotic Matter need (red = the zone will hold).
    /// </summary>
    public class GalaxyMapView
    {
        static readonly Vector2 MapSize = new Vector2(980f, 600f);

        readonly RectTransform panel;
        readonly TextMeshProUGUI header, route;
        RectTransform body;
        int selected = -1;
        int shownSystem = -1;
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

        void Select(int i) { selected = i; timer = 0f; Sfx.PlayUI("ui_click"); }

        void Rebuild(RunManager rm)
        {
            var plan = rm.Plan;
            var def = plan.Def;
            // After a warp the map follows the crew.
            int here = rm.GalaxyIndex.Value * 16 + rm.SystemIndex.Value;
            if (here != shownSystem) { shownSystem = here; selected = rm.SystemIndex.Value; }
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
            // Hop lanes (#108): every system has jump zones to its neighbours in the plan's order.
            for (int i = 1; i < plan.Systems.Length; i++)
                Line(map, P(plan.Systems[i - 1]), P(plan.Systems[i]), new Color(0.3f, 0.6f, 0.85f, 0.45f));
            // The Galactic Rim leaves from the outermost system towards the next galaxy.
            var rimSys = plan.Systems[plan.Systems.Length - 1];
            Line(map, P(rimSys), P(rimSys) + new Vector2(70f, 0f), new Color(1f, 0.68f, 0.3f, 0.7f));
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

            var flight = WarpFlight.Instance;
            if (flight != null && flight.Active.Value)
                Line(info, UIKit.Text(info, $"In warp flight - {Mathf.RoundToInt(flight.Progress01 * 100f)}% of the way", 24, TextAlignmentOptions.Left, UIKit.Warn), 34);
            else if (current)
                Line(info, UIKit.Text(info, JumpList(rm), 21, TextAlignmentOptions.TopLeft, UIKit.TextColor), 30f * Mathf.Max(2, JumpZones.Current.Length + 1));
            else
            {
                int hops = Mathf.Abs(sys.Index - rm.SystemIndex.Value);
                var hopDrive = WarpDrive.Instance;
                string eta = hopDrive != null ? DriveMath.Eta(hopDrive.HopMinutes * hops) : "--";
                Line(info, UIKit.Text(info, $"{hops} hop{(hops == 1 ? "" : "s")} away  <color=#F59E0B>{eta}</color>\n<color=#64748B>Fly out through the jump zone towards {plan.Systems[rm.SystemIndex.Value + (sys.Index > rm.SystemIndex.Value ? 1 : -1)].Name} (no Exotic Matter)</color>", 21, TextAlignmentOptions.TopLeft, UIKit.TextDim), 60);
            }

            UIKit.LayoutElement(UIKit.Image(info, "Rule", new Color(1f, 1f, 1f, 0.12f), false).gameObject, -1, 2);
            bool last = g >= GalaxyDef.Count;
            var rim = plan.Systems[plan.Systems.Length - 1];
            Line(info, UIKit.Text(info, (last ? "The Milky Way fills the sky. Home is one warp away - " : "One-way: this galaxy's systems stay behind. ")
                + $"The Galactic Rim lies beyond <b>{rim.Name}</b>, the outermost system.", 20, TextAlignmentOptions.TopLeft, UIKit.TextDim), 56);
            GalaxyWarp(info, rm, RunManager.WarpNextGalaxy, last ? "Flight home" : "Galactic Rim");
            if (g + 2 <= GalaxyDef.Count) GalaxyWarp(info, rm, RunManager.WarpSkipGalaxy, "Skip zone");
        }

        /// <summary>The jump zones of the system the crew is in (#108): target, distance from the local player, state.</summary>
        static string JumpList(RunManager rm)
        {
            var zones = JumpZones.Current;
            if (zones.Length == 0) return "No jump zones here";
            var pawn = PlayerPawn.Local;
            Vector3 from = pawn != null ? pawn.transform.position : Vector3.zero;
            var sb = new System.Text.StringBuilder("<b>JUMP ZONES</b>  <color=#64748B>fly out through one to warp</color>");
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                var block = JumpZones.BlockOf(i);
                string col = "#" + ColorUtility.ToHtmlStringRGB(z.Color);
                string state = block == JumpBlock.Open ? "<color=#4ADE80>open</color>" : $"<color=#EF4444>{JumpZones.BlockText(block, JumpZones.CrewMissing).ToLowerInvariant()}</color>";
                sb.Append($"\n<color={col}>{z.Label}</color>  {HudKit.Dist(Vector3.Distance(from, z.Center))}  {state}");
            }
            return sb.ToString();
        }

        /// <summary>One galaxy warp: destination, flight time and Exotic Matter need (red = the zone will hold).</summary>
        static void GalaxyWarp(RectTransform info, RunManager rm, int target, string label)
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
            Line(info, UIKit.Text(info, $"<color=#94A3B8>{label}</color>  <b>{rm.WarpLabel(target)}</b>   <color=#F59E0B>{eta}</color>\n<size=85%>{fuel}</size>", 21, TextAlignmentOptions.TopLeft, UIKit.TextColor), 56);
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
