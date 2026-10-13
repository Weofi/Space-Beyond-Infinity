using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voidward.UI
{
    /// <summary>
    /// HUD style kit (#24, DESIGN.md "HUD Layout Specifications"): translucent navy plates with a hairline
    /// border, micro labels, tabular numerals, procedural ring/diamond/gradient sprites and the semantic
    /// gauge colours. Building blocks for HudView.
    /// </summary>
    public static class HudKit
    {
        public static readonly Color PlateColor = Hex(0x0E1420, 0.8f);
        public static readonly Color BorderColor = Hex(0x334155, 0.55f);
        public static readonly Color Raised = Hex(0x162032, 0.95f);
        public static readonly Color Inset = Hex(0x0A0F1A, 0.9f);
        public static readonly Color TextHi = Hex(0xF8FAFC);
        public static readonly Color TextDim = Hex(0x94A3B8);
        public static readonly Color TextHead = Hex(0xCBD5E1);
        public static readonly Color KeyText = Hex(0x38BDF8);
        public static readonly Color KeyBg = Hex(0x1E293B);
        public static readonly Color Health = Hex(0x4EED8C);
        public static readonly Color Shield = Hex(0x38BDF8);
        public static readonly Color Oxygen = Hex(0x06B6D4);
        public static readonly Color Fuel = Hex(0xFBBF24);
        public static readonly Color Energy = Hex(0xC084FC);
        public static readonly Color Hull = Hex(0xE2E8F0);
        public static readonly Color Danger = Hex(0xEF4444);
        public static readonly Color Gold = Hex(0xF59E0B);
        public static readonly Color Teal = Hex(0x2DD4BF);
        public static readonly Color Violet = Hex(0x7C3AED);
        public static readonly Color Crimson = Hex(0xDC2626);
        public static readonly Color VoidTrack = Hex(0x180D2E, 0.92f);
        public static readonly Color BossTitle = Hex(0xFDA4AF);
        public static readonly Color BossFill = Hex(0xE11D48);
        public static readonly Color BossEmpty = Hex(0x37131D, 0.9f);

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        /// <summary>Translucent rounded plate with a 1px border.</summary>
        public static Image Plate(Transform parent, string name, Color? c = null)
        {
            var img = UIKit.Image(parent, name, c ?? PlateColor, false);
            img.sprite = Soft;
            img.type = Image.Type.Sliced;
            var o = img.gameObject.AddComponent<Outline>();
            o.effectColor = BorderColor;
            o.effectDistance = new Vector2(1f, -1f);
            o.useGraphicAlpha = false;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Thin vertical accent bar on the left edge of a plate.</summary>
        public static Image AccentBar(Transform plate, Color c, float width = 4f)
        {
            var bar = UIKit.Image(plate, "Accent", c, false);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 3f); rt.offsetMax = new Vector2(width, -3f);
            bar.raycastTarget = false;
            return bar;
        }

        /// <summary>Uppercase micro label (bold, tracked).</summary>
        public static TextMeshProUGUI Micro(Transform parent, string text, float size = 14f, Color? c = null, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var t = UIKit.Text(parent, text, size, align, c ?? TextDim, FontStyles.Bold | FontStyles.UpperCase);
            t.characterSpacing = 3f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>Monospaced digits so values don't jitter.</summary>
        public static string Num(string s) => "<mspace=0.52em>" + s + "</mspace>";

        public static string Dist(float m) => m >= 1000f ? Num(F(m / 1000f, "0.0")) + " km" : Num(F(m, "0")) + " m";

        /// <summary>Culture-independent number format (no German decimal commas in the HUD).</summary>
        public static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
        public static string F(int v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>"[F] Open crate" -> keycap badge + action text.</summary>
        public static string Keycap(string prompt)
        {
            if (string.IsNullOrEmpty(prompt) || prompt[0] != '[') return prompt;
            int close = prompt.IndexOf(']');
            if (close < 2) return prompt;
            string key = prompt.Substring(1, close - 1);
            string rest = prompt.Substring(close + 1).Trim();
            return $"<mark=#1E293BFF padding=\"10,10,4,4\"><color=#38BDF8><b>{key.ToUpperInvariant()}</b></color></mark>   {rest.ToUpperInvariant()}";
        }

        static Sprite soft, ring, diamond, chevron;

        /// <summary>Rounded rect with a small radius (plates, slots, tracks).</summary>
        public static Sprite Soft => soft != null ? soft : soft = MakeRounded(32, 6);

        /// <summary>Thin anti-aliased ring (crosshair, gate glyph backing).</summary>
        public static Sprite Ring => ring != null ? ring : ring = MakeRing(64, 2.4f);

        public static Sprite Diamond => diamond != null ? diamond : diamond = MakeDiamond(32);

        /// <summary>Downward chevron (player pip, edge arrows).</summary>
        public static Sprite Chevron => chevron != null ? chevron : chevron = MakeChevron(32);

        /// <summary>Horizontal two-colour gradient sprite.</summary>
        public static Sprite Gradient(Color a, Color b)
        {
            var tex = new Texture2D(128, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 128; x++)
            {
                var c = Color.Lerp(a, b, x / 127f);
                tex.SetPixel(x, 0, c); tex.SetPixel(x, 1, c);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 128, 2), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeRing(int size, float width)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f - 1.5f, c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(width * 0.5f - Mathf.Abs(d - (r - width * 0.5f)) + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeDiamond(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - c) + Mathf.Abs(y + 0.5f - c);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(c - 1f - d + 0.5f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeChevron(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // filled triangle pointing down
                    float u = (x + 0.5f - c) / c, v = (y + 0.5f) / size;
                    float edge = v - Mathf.Abs(u) * 0.95f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(edge * size * 0.5f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>Icon + label + bar with a trailing ghost fill (catches up over ~350 ms) + tabular value.</summary>
    public class HudGauge
    {
        public readonly RectTransform Root;
        readonly Image fill, ghost, track;
        readonly TextMeshProUGUI value, icon;
        readonly Color color;
        float ghostFrac = 1f, lastFrac = 1f, ghostHold;

        public HudGauge(Transform parent, string glyph, string label, Color c, float barWidth, float barHeight, float rowHeight = 30f)
        {
            color = c;
            Root = UIKit.Row(parent, rowHeight, 8);
            icon = UIKit.IconText(Root, glyph, rowHeight * 0.72f, c);
            UIKit.LayoutElement(icon.gameObject, 26, rowHeight);
            var l = HudKit.Micro(Root, label, 14f);
            UIKit.LayoutElement(l.gameObject, 74, rowHeight);
            track = UIKit.Image(Root, "Track", HudKit.Inset, false);
            track.sprite = HudKit.Soft; track.type = Image.Type.Sliced;
            UIKit.LayoutElement(track.gameObject, barWidth, barHeight);
            ghost = Fill(track.transform, new Color(1f, 1f, 1f, 0.55f));
            fill = Fill(track.transform, c);
            value = UIKit.Text(Root, "", 19, TextAlignmentOptions.Right, HudKit.TextHi, FontStyles.Bold);
            UIKit.LayoutElement(value.gameObject, 58, rowHeight);
        }

        static Image Fill(Transform track, Color c)
        {
            var f = UIKit.Image(track, "Fill", c, false);
            UIKit.Stretch(f.rectTransform, 2, 2, 2, 2);
            f.sprite = UIKit.White;
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Horizontal;
            f.raycastTarget = false;
            return f;
        }

        /// <summary>critical: pulse to danger red at 2 Hz (DESIGN.md thresholds).</summary>
        public void Set(float frac, string text, bool critical = false)
        {
            frac = Mathf.Clamp01(frac);
            float dt = Time.unscaledDeltaTime;
            if (frac < lastFrac - 0.0005f) ghostHold = 0.25f;
            if (frac > ghostFrac) ghostFrac = frac;
            ghostHold -= dt;
            if (ghostHold <= 0f) ghostFrac = Mathf.MoveTowards(ghostFrac, frac, dt / 0.35f);
            lastFrac = frac;
            fill.fillAmount = frac;
            ghost.fillAmount = ghostFrac;
            Color c = critical ? Color.Lerp(color, HudKit.Danger, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f)) : color;
            fill.color = c;
            icon.color = c;
            value.text = HudKit.Num(text);
        }
    }

    /// <summary>World-space marker: glyph in a diamond, label and distance; optionally clamped to the screen edge.</summary>
    public class HudMarker
    {
        public readonly RectTransform Root;
        readonly TextMeshProUGUI glyph, label, dist;
        readonly Image backing, arrow;
        readonly CanvasGroup group;

        public HudMarker(Transform layer)
        {
            Root = UIKit.Rect(layer, "Marker");
            Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = new Vector2(34, 34);
            group = Root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            backing = UIKit.Image(Root, "Back", HudKit.PlateColor, false);
            backing.sprite = HudKit.Diamond;
            UIKit.Place(backing.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            glyph = UIKit.IconText(Root, "", 20);
            UIKit.Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            label = HudKit.Micro(Root, "", 14f, HudKit.TextHi, TextAlignmentOptions.Center);
            UIKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(320, 22));
            dist = UIKit.Text(Root, "", 15, TextAlignmentOptions.Center, HudKit.TextDim, FontStyles.Bold);
            UIKit.Place(dist.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -28), new Vector2(200, 20));
            arrow = UIKit.Image(Root, "Arrow", Color.white, false);
            arrow.sprite = HudKit.Chevron;
            UIKit.Place(arrow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
            Root.gameObject.SetActive(false);
        }

        public void Hide() { if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false); }

        public void Place(Camera cam, RectTransform layer, Vector3? world, string icon, string text, Color c, bool clamp, float alpha = 1f)
        {
            if (world == null) { Hide(); return; }
            Vector3 sp = cam.WorldToScreenPoint(world.Value);
            bool behind = sp.z <= 0f;
            if (behind && !clamp) { Hide(); return; }
            Root.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, behind ? new Vector2(UnityEngine.Screen.width - sp.x, UnityEngine.Screen.height - sp.y) : (Vector2)sp, null, out var local);
            // Clamp rectangle; the top margin keeps edge markers below the collapse meter + compass band.
            Vector2 size = layer.rect.size * 0.5f;
            float halfX = size.x - 70f, top = size.y - 175f, bottom = size.y - 90f;
            bool off = behind || Mathf.Abs(local.x) > halfX || local.y > top || local.y < -bottom;
            if (off && clamp)
            {
                // Push onto the screen-edge rectangle along the direction from the centre.
                Vector2 dir = local.sqrMagnitude > 1f ? local.normalized : Vector2.down;
                if (behind && dir.y > 0f) dir.y = -dir.y;
                float halfY = dir.y > 0f ? top : bottom;
                float k = Mathf.Min(halfX / Mathf.Max(0.001f, Mathf.Abs(dir.x)), halfY / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                local = dir * k;
                arrow.gameObject.SetActive(true);
                arrow.color = c;
                arrow.rectTransform.anchoredPosition = dir * 26f;
                arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
            }
            else arrow.gameObject.SetActive(false);
            Root.anchoredPosition = local;
            glyph.text = icon;
            glyph.color = c;
            backing.color = new Color(HudKit.PlateColor.r, HudKit.PlateColor.g, HudKit.PlateColor.b, 0.85f);
            label.text = text;
            label.color = c;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text) && !off);
            float d = Vector3.Distance(cam.transform.position, world.Value);
            dist.text = HudKit.Dist(d);
            group.alpha = alpha;
        }
    }
}
