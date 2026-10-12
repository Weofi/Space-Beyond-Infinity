using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Voidward.UI
{
    /// <summary>
    /// Tiny code-first uGUI/TMP toolkit with the Voidward pastel style (rounded, soft, chunky).
    /// Every screen is assembled from these helpers at runtime — no prefabs to keep in sync.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color PanelColor = new Color(0.09f, 0.08f, 0.17f, 0.88f);
        public static readonly Color PanelLight = new Color(0.16f, 0.14f, 0.28f, 0.92f);
        public static readonly Color Accent = new Color(0.36f, 0.86f, 0.92f);
        public static readonly Color Accent2 = new Color(1f, 0.62f, 0.55f);
        public static readonly Color Good = new Color(0.55f, 0.95f, 0.6f);
        public static readonly Color Warn = new Color(1f, 0.78f, 0.35f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.5f);
        public static readonly Color TextColor = new Color(0.95f, 0.94f, 1f);
        public static readonly Color TextDim = new Color(0.7f, 0.68f, 0.85f);

        static Sprite rounded;
        static Sprite circle;
        static TMP_FontAsset font;

        public static Sprite Rounded
        {
            get
            {
                if (rounded == null) rounded = MakeRounded(64, 18);
                return rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = MakeRounded(64, 32);
                return circle;
            }
        }

        public static TMP_FontAsset Font
        {
            get
            {
                if (font == null) font = TMP_Settings.defaultFontAsset;
                return font;
            }
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = radius;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        // ------------------------------------------------------------ primitives

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Color c, bool roundedSprite = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            if (roundedSprite) { img.sprite = Rounded; img.type = UnityEngine.UI.Image.Type.Sliced; }
            return img;
        }

        public static RectTransform Panel(Transform parent, string name, Color? c = null)
        {
            var img = Image(parent, name, c ?? PanelColor);
            return img.rectTransform;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size = 26, TextAlignmentOptions align = TextAlignmentOptions.Left, Color? color = null, FontStyles style = FontStyles.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, float height = 56, Color? color = null, float fontSize = 26)
        {
            var img = Image(parent, "Button", color ?? Accent);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.6f);
            colors.colorMultiplier = 1f;
            b.colors = colors;
            var t = Text(img.transform, label, fontSize, TextAlignmentOptions.Center, new Color(0.08f, 0.07f, 0.16f), FontStyles.Bold);
            Stretch(t.rectTransform, 10, 10, 2, 2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            if (onClick != null) b.onClick.AddListener(() => { Core.Sfx.PlayUI("ui_click"); onClick(); });
            LayoutElement(img.gameObject, -1, height);
            return b;
        }

        public static void SetLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = label;
        }

        public static LayoutElement LayoutElement(GameObject go, float width = -1, float height = -1, float flexW = -1, float flexH = -1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (width >= 0) le.preferredWidth = width;
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing = 10, int padding = 16, TextAnchor align = TextAnchor.UpperCenter, bool expandW = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = expandW;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(GameObject go, float spacing = 10, int padding = 0, TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childAlignment = align;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = expandW;
            return h;
        }

        public static RectTransform Row(Transform parent, float height = 56, float spacing = 10)
        {
            var rt = Rect(parent, "Row");
            HLayout(rt.gameObject, spacing);
            LayoutElement(rt.gameObject, -1, height);
            return rt;
        }

        public static GridLayoutGroup Grid(GameObject go, Vector2 cell, Vector2 spacing, int columns)
        {
            var g = go.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = spacing;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            return g;
        }

        /// <summary>Vertical scroll view; returns the content transform (has a VerticalLayoutGroup).</summary>
        public static RectTransform Scroll(Transform parent, float height)
        {
            var view = Image(parent, "Scroll", new Color(0, 0, 0, 0.18f));
            LayoutElement(view.gameObject, -1, height, 1, -1);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect(view.transform, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VLayout(content.gameObject, 6, 8);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content;
            sr.horizontal = false;
            sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, Action<float> onChange)
        {
            var root = Rect(parent, "Slider");
            LayoutElement(root.gameObject, -1, 32, 1);
            var bg = Image(root, "Bg", new Color(1, 1, 1, 0.15f));
            Stretch(bg.rectTransform, 0, 0, 10, 10);
            var fillArea = Rect(root, "FillArea");
            Stretch(fillArea, 6, 6, 10, 10);
            var fill = Image(fillArea, "Fill", Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect(root, "HandleArea");
            Stretch(handleArea, 12, 12, 0, 0);
            var handle = Image(handleArea, "Handle", Color.white);
            handle.sprite = Circle;
            handle.rectTransform.sizeDelta = new Vector2(28, 0);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var row = Row(parent, 40);
            var box = Image(row, "Box", new Color(1, 1, 1, 0.18f));
            LayoutElement(box.gameObject, 36, 36);
            var check = Image(box.transform, "Check", Accent);
            Stretch(check.rectTransform, 6, 6, 6, 6);
            var t = Text(row, label, 24);
            LayoutElement(t.gameObject, -1, 36, 1);
            var tg = row.gameObject.AddComponent<Toggle>();
            tg.targetGraphic = box;
            tg.graphic = check;
            tg.isOn = value;
            if (onChange != null) tg.onValueChanged.AddListener(v => { Core.Sfx.PlayUI("ui_click"); onChange(v); });
            return tg;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, string value, float height = 52)
        {
            var bg = Image(parent, "Input", new Color(1, 1, 1, 0.12f));
            LayoutElement(bg.gameObject, -1, height, 1);
            var area = Rect(bg.transform, "TextArea");
            Stretch(area, 14, 14, 6, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, placeholder, 24, TextAlignmentOptions.Left, TextDim, FontStyles.Italic);
            Stretch(ph.rectTransform);
            var txt = Text(area, "", 24, TextAlignmentOptions.Left);
            Stretch(txt.rectTransform);
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            var f = bg.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = txt;
            f.placeholder = ph;
            f.fontAsset = Font;
            f.pointSize = 24;
            f.text = value ?? "";
            return f;
        }

        public static TMP_Dropdown Dropdown(Transform parent, string[] options, int value, Action<int> onChange)
        {
            var res = new TMP_DefaultControls.Resources { standard = Rounded, background = Rounded, inputField = Rounded, knob = Circle, checkmark = Rounded, dropdown = Rounded, mask = Rounded };
            var go = TMP_DefaultControls.CreateDropdown(res);
            go.transform.SetParent(parent, false);
            LayoutElement(go, -1, 48, 1);
            var dd = go.GetComponent<TMP_Dropdown>();
            go.GetComponent<Image>().color = new Color(1, 1, 1, 0.9f);
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) { t.font = Font; t.fontSize = 22; t.color = new Color(0.1f, 0.08f, 0.2f); }
            dd.ClearOptions();
            dd.AddOptions(new System.Collections.Generic.List<string>(options));
            dd.value = Mathf.Clamp(value, 0, options.Length - 1);
            dd.RefreshShownValue();
            if (onChange != null) dd.onValueChanged.AddListener(v => onChange(v));
            return dd;
        }

        /// <summary>Simple horizontal bar (fill image). Returns the fill to scale via fillAmount.</summary>
        public static Image Bar(Transform parent, string name, Color fillColor, Vector2 size)
        {
            var bg = Image(parent, name, new Color(0, 0, 0, 0.45f));
            bg.rectTransform.sizeDelta = size;
            LayoutElement(bg.gameObject, size.x, size.y);
            var fill = Image(bg.transform, "Fill", fillColor, false);
            Stretch(fill.rectTransform, 3, 3, 3, 3);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.sprite = MakeWhite();
            return fill;
        }

        static Sprite white;
        static Sprite MakeWhite()
        {
            if (white == null)
            {
                var tex = Texture2D.whiteTexture;
                white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return white;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
