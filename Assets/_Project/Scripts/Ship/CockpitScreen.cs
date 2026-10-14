using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Planet;

namespace Voidward.Ship
{
    public enum ScreenKind { Flight, Hull, Radar, Nav, Fuel, Drive }

    /// <summary>
    /// One live cockpit display (#94): glass plate, segmented gauges and glowing text, drawn with VW_Screen SDF quads
    /// (crisp at any distance, HDR so bloom picks them up) and the HUD's Rajdhani font. The transform is the screen
    /// centre; the pilot looks along +Z at it, `Size` is its width x height in metres. Content is built on Start
    /// and refreshed only while a camera is close. Every screen has one job with few, big values (sizes in metres,
    /// so they read the same on every screen), nothing is shown twice:
    ///   Flight - flight mode, speed, altitude, throttle / cruise / warp bar
    ///   Radar  - scope (hostiles, crew ships, planets; top = nose), hostile count, range
    ///   Nav    - Home ETA, galaxy, system
    ///   Hull   - hull and shield
    ///   Fuel   - system fuel
    ///   Drive  - Exotic Matter and warp core; in a warp: route progress and drive heat
    /// </summary>
    public class CockpitScreen : MonoBehaviour
    {
        public ScreenKind Kind;
        public Vector2 Size = new Vector2(0.2f, 0.1f);
        public Material ElementMaterial;

        static readonly Color Cyan = new Color(0.35f, 0.9f, 1f);
        static readonly Color Dim = new Color(0.5f, 0.68f, 0.78f);
        static readonly Color Amber = new Color(1f, 0.62f, 0.2f);
        static readonly Color Alert = new Color(1f, 0.28f, 0.3f);
        static readonly Color Teal = new Color(0.3f, 1f, 0.75f);
        static readonly Color Violet = new Color(0.72f, 0.5f, 1f);
        const float Glow = 1.8f;      // HDR boost of lit elements
        const float NearDistance = 9f;
        // text heights in metres: labels, values, hero numbers
        const float LabelH = 0.026f, ValueH = 0.044f;

        static Mesh quad;
        static TMP_FontAsset font, fontBold;
        static readonly Dictionary<TMP_FontAsset, Material> glowMaterials = new Dictionary<TMP_FontAsset, Material>();
        static MaterialPropertyBlock mpb;

        class Element { public MeshRenderer R; public Color C, Bg; public float Fill = 1f; }

        /// <summary>Label + value on one row, gauge underneath.</summary>
        class Block { public TextMeshPro Label, Value; public Element Bar; }

        ShipCore ship;
        Transform content;
        readonly List<Element> elements = new List<Element>();
        readonly List<TextMeshPro> texts = new List<TextMeshPro>();
        int order;
        float timer;
        bool shown = true;

        // Flight
        TextMeshPro speed, mode, alt;
        Element throttle;
        // Radar
        readonly List<Element> blips = new List<Element>();
        TextMeshPro range, contacts, contactsLabel;
        float radarRange = 2500f, scopeSize;
        Vector2 scopeCenter;
        // Nav
        TextMeshPro eta, galaxy, system;
        // Hull / Fuel / Drive
        Block blockA, blockB;

        void Start()
        {
            ship = GetComponentInParent<ShipCore>();
            if (ElementMaterial == null) { enabled = false; return; }
            if (quad == null) quad = BuildQuad();
            if (font == null) font = Resources.Load<TMP_FontAsset>("UI/VW_Font");
            if (fontBold == null) fontBold = Resources.Load<TMP_FontAsset>("UI/VW_Font_Bold");
            if (font == null) font = TMP_Settings.defaultFontAsset;
            if (fontBold == null) fontBold = font;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);
            Build();
        }

        // ------------------------------------------------------------ layout

        void Build()
        {
            float w = Size.x, h = Size.y;
            Rect(0, 0, w, h, 0, Dim * 0.35f, new Color(0.012f, 0.03f, 0.05f, 0.95f), 0.006f, 0.0012f);
            Rect(0, 0, w - 0.004f, h - 0.004f, 4, Cyan * 0.8f, default, Mathf.Min(w, h) * 0.12f, 0.0011f);
            float pad = 0.014f;
            float left = -w * 0.5f + pad, right = w * 0.5f - pad, top = h * 0.5f - pad, bottom = -h * 0.5f + pad;
            float inner = right - left;
            switch (Kind)
            {
                case ScreenKind.Flight:
                    mode = Label("NORMAL", left, top, ValueH, Cyan, TextAlignmentOptions.TopLeft, inner * 0.6f, true);
                    alt = Label("", right, top, LabelH * 1.2f, Dim, TextAlignmentOptions.TopRight, inner * 0.45f, true);
                    speed = Label("0", 0f, (top + bottom) * 0.5f + 0.006f, 0.1f, Cyan, TextAlignmentOptions.Center, inner, true);
                    Label("M/S", right, bottom + 0.03f, LabelH, Dim, TextAlignmentOptions.BottomRight, inner * 0.3f, true);
                    throttle = Bar(0, bottom + 0.009f, inner, 0.018f, Cyan, 30);
                    break;
                case ScreenKind.Radar:
                    scopeSize = h - pad * 1.4f;
                    scopeCenter = new Vector2(left + scopeSize * 0.5f - pad * 0.3f, 0f);
                    var scope = Add(scopeCenter.x, scopeCenter.y, scopeSize, scopeSize, 2, Cyan * 0.9f, new Color(0.02f, 0.09f, 0.13f, 0.9f));
                    SetShape(scope, new Vector2(scopeSize, scopeSize), 0f, 0.0014f, 1f, 0f);
                    float tx = scopeCenter.x + scopeSize * 0.5f + pad * 0.6f, tw = right - tx;
                    contacts = Label("0", tx + tw * 0.5f, 0.022f, 0.075f, Dim, TextAlignmentOptions.Center, tw, true);
                    contactsLabel = Label("CLEAR", tx + tw * 0.5f, -0.03f, LabelH, Dim, TextAlignmentOptions.Center, tw, true);
                    range = Label("", tx + tw * 0.5f, bottom, LabelH * 0.9f, Dim, TextAlignmentOptions.Bottom, tw);
                    for (int i = 0; i < 28; i++)
                    {
                        var b = Add(0, 0, 0.012f, 0.012f, 3, Alert, default);
                        b.R.enabled = false;
                        blips.Add(b);
                    }
                    if (ship != null && ship.Stats.Scanner > 100f) radarRange = ship.Stats.Scanner;
                    break;
                case ScreenKind.Nav:
                    Label("HOME ETA", left, top, LabelH, Dim, TextAlignmentOptions.TopLeft, inner, true);
                    eta = Label("--", left, top - 0.034f, 0.06f, Cyan, TextAlignmentOptions.TopLeft, inner, true);
                    galaxy = Label("", left, bottom + 0.04f, ValueH * 0.85f, Violet, TextAlignmentOptions.BottomLeft, inner, true);
                    system = Label("", left, bottom, LabelH * 1.2f, Dim, TextAlignmentOptions.BottomLeft, inner, true);
                    break;
                case ScreenKind.Hull:
                {
                    float half = (top - bottom) * 0.5f;
                    blockA = MakeBlock("HULL", left, right, top, Amber);
                    blockB = MakeBlock("SHIELD", left, right, top - half, Cyan);
                    break;
                }
                case ScreenKind.Fuel:
                    blockA = MakeBlock("FUEL", left, right, top, Teal);
                    break;
                case ScreenKind.Drive:
                {
                    float half = (top - bottom) * 0.5f;
                    blockA = MakeBlock("EXOTIC", left, right, top, Violet);
                    blockB = MakeBlock("CORE", left, right, top - half, Amber);
                    break;
                }
            }
        }

        /// <summary>Label (left) and value (right) on one row, gauge underneath; about 0.075 m tall.</summary>
        Block MakeBlock(string label, float left, float right, float y, Color c)
        {
            float w = right - left;
            return new Block
            {
                // label and value share the row half and half; autosizing shrinks a long one instead of overlapping
                Label = Label(label, left, y - (ValueH - LabelH) * 0.6f, LabelH, Dim, TextAlignmentOptions.TopLeft, w * 0.46f, true),
                Value = Label("--", right, y, ValueH, c, TextAlignmentOptions.TopRight, w * 0.52f, true),
                Bar = Bar((left + right) * 0.5f, y - ValueH - 0.008f, w, 0.016f, c, 10),
            };
        }

        void SetBlock(Block b, string value, Color c, float fill, bool showBar = true)
        {
            b.Value.text = value;
            b.Value.color = c;
            b.Bar.R.enabled = showBar;
            if (showBar) Paint(b.Bar, c, fill);
        }

        Element Bar(float x, float y, float w, float h, Color c, int segments)
        {
            var e = Add(x, y, w, h, 1, c, new Color(c.r * 0.12f, c.g * 0.12f, c.b * 0.12f, 0.85f));
            SetShape(e, new Vector2(w, h), h * 0.5f, 0f, 1f, segments);
            return e;
        }

        void Rect(float x, float y, float w, float h, int modeId, Color c, Color bg, float radius, float line)
        {
            var e = Add(x, y, w, h, modeId, c, bg);
            SetShape(e, new Vector2(w, h), radius, line, 1f, 0f);
        }

        Element Add(float x, float y, float w, float h, int modeId, Color c, Color bg)
        {
            var go = new GameObject("E" + elements.Count);
            go.transform.SetParent(content, false);
            go.transform.localPosition = new Vector3(x, y, -0.0004f * order);
            go.transform.localScale = new Vector3(w, h, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ElementMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = order++;
            var e = new Element { R = r, C = c, Bg = bg };
            elements.Add(e);
            mpb.Clear();
            mpb.SetFloat("_Mode", modeId);
            r.SetPropertyBlock(mpb);
            return e;
        }

        void SetShape(Element e, Vector2 size, float radius, float line, float fill, float segments)
        {
            e.Fill = fill;
            e.R.GetPropertyBlock(mpb);
            mpb.SetVector("_Size", size);
            mpb.SetFloat("_Radius", radius);
            mpb.SetFloat("_Line", line);
            mpb.SetFloat("_Segments", segments);
            Paint(e, e.C, fill);
        }

        void Paint(Element e, Color c, float fill)
        {
            e.C = c;
            e.Fill = fill;
            e.R.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", new Color(c.r * Glow, c.g * Glow, c.b * Glow, c.a));
            mpb.SetColor("_BgColor", e.Bg);
            mpb.SetFloat("_Fill", fill);
            e.R.SetPropertyBlock(mpb);
        }

        TextMeshPro Label(string text, float x, float y, float height, Color c, TextAlignmentOptions align, float width, bool bold = false)
        {
            var go = new GameObject("T" + texts.Count);
            go.transform.SetParent(content, false);
            go.transform.localPosition = new Vector3(x, y, -0.0004f * (order + 40));
            var t = go.AddComponent<TextMeshPro>();
            t.font = bold ? fontBold : font;
            t.fontSharedMaterial = GlowMaterial(t.font);
            t.fontSize = height * 10f / 0.75f;
            t.color = c;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = t.fontSize * 0.5f;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(width, height * 1.3f);
            // pivot on the aligned edge so (x, y) is the text's anchor point
            float px = align == TextAlignmentOptions.TopRight || align == TextAlignmentOptions.Right || align == TextAlignmentOptions.BottomRight ? 1f
                : align == TextAlignmentOptions.Top || align == TextAlignmentOptions.Center || align == TextAlignmentOptions.Bottom ? 0.5f : 0f;
            float py = align == TextAlignmentOptions.TopLeft || align == TextAlignmentOptions.TopRight || align == TextAlignmentOptions.Top ? 1f
                : align == TextAlignmentOptions.BottomLeft || align == TextAlignmentOptions.BottomRight || align == TextAlignmentOptions.Bottom ? 0f : 0.5f;
            rt.pivot = new Vector2(px, py);
            t.GetComponent<MeshRenderer>().sortingOrder = 100;
            t.text = text;
            texts.Add(t);
            return t;
        }

        static Material GlowMaterial(TMP_FontAsset f)
        {
            if (glowMaterials.TryGetValue(f, out var m) && m != null) return m;
            m = new Material(f.material) { name = f.name + " Screen" };
            if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", Color.white * Glow);
            glowMaterials[f] = m;
            return m;
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "ScreenQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };   // faces -Z (towards the pilot)
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------ live data

        void Update()
        {
            if (content == null || ship == null || !ship.IsSpawned) return;
            if (Kind == ScreenKind.Radar && shown) UpdateRadar();
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.1f;
            var cam = Camera.main;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < NearDistance * NearDistance;
            if (near != shown)
            {
                shown = near;
                content.gameObject.SetActive(near);
            }
            if (!near) return;
            switch (Kind)
            {
                case ScreenKind.Flight: UpdateFlight(); break;
                case ScreenKind.Nav: UpdateNav(); break;
                case ScreenKind.Hull: UpdateHull(); break;
                case ScreenKind.Fuel: UpdateFuel(); break;
                case ScreenKind.Drive: UpdateDrive(); break;
            }
        }

        void UpdateFlight()
        {
            var s = ship.Stats;
            var f = ship.Flight;
            float spd = f != null ? f.Speed : 0f;
            float a = f != null ? f.Altitude : 9999f;
            alt.text = a < 2000f ? "ALT " + a.ToString("0") + " M" : "DEEP SPACE";
            if (WarpBubble.Active)
            {
                speed.text = WarpBubble.Speed.ToString("0");
                speed.color = Violet;
                mode.text = WarpBubble.Flying ? (WarpBubble.Overheated ? "DRIVE HOT" : "WARP") : "DROPPED OUT";
                mode.color = WarpBubble.Overheated ? Alert : Violet;
                alt.text = "THROTTLE " + Mathf.RoundToInt(WarpBubble.Throttle * 100f) + "%";
                Paint(throttle, WarpBubble.Overheated ? Alert : Violet, WarpBubble.Throttle);
                return;
            }
            speed.text = spd.ToString("0");
            var cruise = f != null ? f.Cruise : CruiseMode.Off;
            if (cruise != CruiseMode.Off)
            {
                float topSpeed = ShipFlight.CruiseTopSpeed;
                mode.text = cruise == CruiseMode.Spooling ? "SPOOLING" : "CRUISE";
                mode.color = speed.color = Teal;
                Paint(throttle, Teal, cruise == CruiseMode.Spooling ? f.CruiseSpool01 : Mathf.Clamp01(spd / Mathf.Max(1f, topSpeed)));
            }
            else
            {
                bool boost = s.MaxSpeed > 0f && spd > s.MaxSpeed * 1.02f;
                mode.text = boost ? "BOOST" : "NORMAL";
                mode.color = speed.color = boost ? Amber : Cyan;
                Paint(throttle, boost ? Amber : Cyan, s.MaxSpeed > 0f ? Mathf.Clamp01(spd / (s.MaxSpeed * 1.6f)) : 0f);
            }
        }

        void UpdateNav()
        {
            eta.text = GameState.HomeEta.Length > 0 ? GameState.HomeEta.ToUpperInvariant() : "--";
            var g = ContentCatalog.GetGalaxy(GameState.GalaxyIndex);
            galaxy.text = g != null ? g.Designation + "  " + g.DisplayName.ToUpperInvariant() : "GALAXY " + GameState.GalaxyIndex;
            system.text = "GALAXY " + GameState.GalaxyIndex + "/5   SYSTEM " + (GameState.SystemIndex + 1);
        }

        void UpdateHull()
        {
            var s = ship.Stats;
            float hull = ship.Hull != null ? ship.Hull.Fraction : 0f;
            SetBlock(blockA, ship.Hull != null ? Mathf.CeilToInt(ship.Hull.Hp.Value).ToString() : "--", hull < 0.25f ? Alert : Amber, hull);
            if (ship.ShieldsOffline.Value) SetBlock(blockB, "OFF", Alert, 0f);
            else SetBlock(blockB, Mathf.CeilToInt(ship.Shield.Value).ToString(), Cyan, s.MaxShield > 0f ? ship.Shield.Value / s.MaxShield : 0f);
        }

        void UpdateFuel()
        {
            var s = ship.Stats;
            float fuel = s.MaxFuel > 0f ? ship.Fuel.Value / s.MaxFuel : 0f;
            SetBlock(blockA, Mathf.CeilToInt(ship.Fuel.Value).ToString(), fuel < 0.15f ? Alert : Teal, fuel);
        }

        void UpdateDrive()
        {
            if (WarpBubble.Active)
            {
                blockA.Label.text = "ROUTE";
                SetBlock(blockA, Mathf.RoundToInt(WarpBubble.Progress01 * 100f) + "%", Violet, WarpBubble.Progress01);
                blockB.Label.text = "HEAT";
                bool hot = WarpBubble.Overheated || WarpBubble.Heat > 0.85f;
                SetBlock(blockB, WarpBubble.Overheated ? "HOT" : Mathf.RoundToInt(WarpBubble.Heat * 100f) + "%", hot ? Alert : Amber, Mathf.Clamp01(WarpBubble.Heat));
                return;
            }
            blockA.Label.text = "EXOTIC";
            int cap = Mathf.Max(1, WarpBubble.ExoticCapacity);
            SetBlock(blockA, Mathf.FloorToInt(WarpBubble.Exotic) + "/" + WarpBubble.ExoticCapacity, Violet, Mathf.Clamp01(WarpBubble.Exotic / cap));
            blockB.Label.text = "CORE";
            SetBlock(blockB, TierName(WarpBubble.CoreTier), Amber, 0f, false);
        }

        static string TierName(int t) => t >= 6 ? "HOME" : "MK " + (t <= 1 ? "I" : t == 2 ? "II" : t == 3 ? "III" : t == 4 ? "IV" : "V");

        void UpdateRadar()
        {
            float rad = scopeSize * 0.5f * 0.9f;
            var inv = Quaternion.Inverse(ship.transform.rotation);
            Vector3 origin = ship.transform.position;
            int n = 0, hostiles = 0;
            void Blip(Vector3 world, Color c, float size, bool clampEdge)
            {
                if (n >= blips.Count) return;
                var local = inv * (world - origin);
                var p = new Vector2(local.x, local.z) / radarRange;
                float m = p.magnitude;
                if (m > 1f) { if (!clampEdge) return; p /= m; c.a *= 0.45f; }
                var b = blips[n++];
                b.R.enabled = true;
                b.R.transform.localPosition = new Vector3(scopeCenter.x + p.x * rad, scopeCenter.y + p.y * rad, -0.0004f * (order + 2));
                b.R.transform.localScale = new Vector3(size, size, 1f);
                // above / below: fade a little so the plane reads
                c.a *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(Mathf.Abs(local.y) / radarRange));
                Paint(b, c, 1f);
            }
            float s = 0.012f;
            foreach (var p in PlanetBody.All)
                if (p != null) Blip(p.transform.position, new Color(0.35f, 0.55f, 1f, 0.8f), s * 2f, true);
            foreach (var other in ShipCore.All)
                if (other != null && other != ship) Blip(other.transform.position, Teal, s, true);
            foreach (var hc in HealthComponent.All)
            {
                if (hc == null || !hc.IsAlive || hc.IsWeakPointPart) continue;
                if (hc.Team != Team.Aliens && hc.Team != Team.Pirates) continue;
                if ((hc.transform.position - origin).sqrMagnitude > radarRange * radarRange * 4f) continue;
                hostiles++;
                Blip(hc.transform.position, Alert, s, true);
            }
            for (int i = n; i < blips.Count; i++) blips[i].R.enabled = false;
            if (range != null) range.text = (radarRange / 1000f).ToString("0.#") + " KM";
            if (contacts != null)
            {
                contacts.text = hostiles.ToString();
                contacts.color = contactsLabel.color = hostiles > 0 ? Alert : Dim;
                contactsLabel.text = hostiles == 1 ? "HOSTILE" : hostiles > 1 ? "HOSTILES" : "CLEAR";
            }
        }
    }
}
