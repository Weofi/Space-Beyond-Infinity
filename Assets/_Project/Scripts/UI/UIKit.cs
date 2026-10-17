using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Voidward.UI
{
    /// <summary>
    /// Tiny code-first uGUI/TMP toolkit with the Voidward "stylized low-poly" style
    /// (chamfered frames, bold blue faces, white accents, chunky 3D buttons).
    /// Every screen is assembled from these helpers at runtime — no prefabs to keep in sync.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color PanelColor = new Color(0.04f, 0.08f, 0.18f, 0.9f);
        public static readonly Color PanelLight = new Color(0.09f, 0.16f, 0.32f, 0.94f);
        public static readonly Color Accent = new Color(0.25f, 0.6f, 1f);
        public static readonly Color Accent2 = new Color(1f, 0.74f, 0.25f);
        public static readonly Color Primary = new Color(0.12f, 0.4f, 0.9f);
        public static readonly Color Secondary = new Color(0.2f, 0.3f, 0.5f);
        public static readonly Color Good = new Color(0.22f, 0.72f, 0.45f);
        public static readonly Color Warn = new Color(1f, 0.74f, 0.25f);
        public static readonly Color Bad = new Color(0.86f, 0.26f, 0.32f);
        public static readonly Color TextColor = new Color(0.96f, 0.98f, 1f);
        public static readonly Color TextDim = new Color(0.64f, 0.74f, 0.9f);
        public static readonly Color Frame = new Color(0.88f, 0.92f, 0.97f);
        public static readonly Color FrameHot = new Color(1f, 1f, 1f);
        public static readonly Color FrameShade = new Color(0.55f, 0.62f, 0.74f);

        static Sprite rounded, circle, chamfer, chamferFace, gradient;
        static TMP_FontAsset font, iconFont;
        static Material shadowMat;

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

        /// <summary>Box with 45-degree cut corners - the base shape of the low-poly UI.</summary>
        public static Sprite Chamfer
        {
            get
            {
                if (chamfer == null) chamfer = MakeChamfer(64, 14, false);
                return chamfer;
            }
        }

        /// <summary>Chamfer with a baked top-to-bottom shading ramp for button faces.</summary>
        public static Sprite ChamferFace
        {
            get
            {
                if (chamferFace == null) chamferFace = MakeChamfer(64, 14, true);
                return chamferFace;
            }
        }

        /// <summary>Horizontal alpha ramp (opaque left, transparent right).</summary>
        public static Sprite Gradient
        {
            get
            {
                if (gradient == null)
                {
                    var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[256 * 4];
                    for (int x = 0; x < 256; x++)
                    {
                        float u = x / 255f;
                        byte a = (byte)(255 * (1f - u) * (1f - u));
                        for (int y = 0; y < 4; y++) px[y * 256 + x] = new Color32(255, 255, 255, a);
                    }
                    tex.SetPixels32(px);
                    tex.Apply();
                    gradient = Sprite.Create(tex, new Rect(0, 0, 256, 4), new Vector2(0.5f, 0.5f));
                }
                return gradient;
            }
        }

        public static TMP_FontAsset Font
        {
            get
            {
                if (font == null) font = Resources.Load<TMP_FontAsset>("UI/VW_Font");
                if (font == null) font = TMP_Settings.defaultFontAsset;
                return font;
            }
        }

        public static TMP_FontAsset IconFont
        {
            get
            {
                if (iconFont == null) iconFont = Resources.Load<TMP_FontAsset>("UI/VW_Icons");
                return iconFont;
            }
        }

        /// <summary>Shared font material with a soft drop shadow (underlay) for text on bright faces.</summary>
        public static Material ShadowMaterial
        {
            get
            {
                if (shadowMat == null)
                {
                    shadowMat = new Material(Font.material) { name = "VW_Font_Shadow" };
                    shadowMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    shadowMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.02f, 0.05f, 0.15f, 0.65f));
                    shadowMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.7f);
                    shadowMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
                }
                return shadowMat;
            }
        }

        static Sprite MakeChamfer(int size, int cut, bool shaded)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float ax = Mathf.Min(x + 0.5f, size - x - 0.5f);
                    float ay = Mathf.Min(y + 0.5f, size - y - 0.5f);
                    float a = Mathf.Min(Mathf.Clamp01(ax), Mathf.Min(Mathf.Clamp01(ay), Mathf.Clamp01((ax + ay - cut) * 0.7071f + 0.5f)));
                    float v = 1f;
                    if (shaded)
                    {
                        v = Mathf.Lerp(0.74f, 1f, y / (float)(size - 1));
                        if (y < 4) v *= 0.82f;
                    }
                    byte g = (byte)(v * 255);
                    px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = cut + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
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
            if (roundedSprite) { img.sprite = Chamfer; img.type = UnityEngine.UI.Image.Type.Sliced; }
            return img;
        }

        public static RectTransform Panel(Transform parent, string name, Color? c = null)
        {
            var img = Image(parent, name, c ?? PanelColor);
            return img.rectTransform;
        }

        /// <summary>Window with a light chamfered frame, navy interior and an optional blue title plate. Has a VerticalLayoutGroup.</summary>
        public static RectTransform FramedPanel(Transform parent, string name, string title = null, int padding = 28, float spacing = 12)
        {
            var frame = Image(parent, name, Frame);
            var inner = Image(frame.transform, "Inner", new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.96f));
            Stretch(inner.rectTransform, 6, 6, 6, 6);
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            VLayout(frame.gameObject, spacing, padding);
            if (!string.IsNullOrEmpty(title))
            {
                var plate = Image(frame.transform, "Title", Primary);
                plate.sprite = ChamferFace;
                LayoutElement(plate.gameObject, -1, 66);
                var t = Text(plate.transform, title, 40, TextAlignmentOptions.Center, TextColor, FontStyles.Bold | FontStyles.UpperCase);
                Stretch(t.rectTransform, 10, 10, 0, 4);
                t.characterSpacing = 6;
                t.fontSharedMaterial = ShadowMaterial;
            }
            return frame.rectTransform;
        }

        public static TextMeshProUGUI IconText(Transform parent, string icon, float size, Color? color = null)
        {
            var rt = Rect(parent, "Icon");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = IconFont != null ? IconFont : Font;
            t.text = IconFont != null ? icon : "";
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color ?? TextColor;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
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

        /// <summary>Chunky 3D button: dark base lip, light chamfered frame, shaded colored face, optional icon plate.</summary>
        public static Button Button(Transform parent, string label, UnityAction onClick, float height = 56, Color? color = null, float fontSize = 26, string icon = null)
        {
            var c = color ?? Primary;
            c.a = 1f;
            float depth = Mathf.Clamp(height * 0.1f, 4f, 8f);

            var root = Image(parent, "Button", Color.white);
            var b = root.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = root;
            var fx = root.gameObject.AddComponent<ChunkyButton>();
            fx.Button = b;
            fx.Base = root;
            fx.Depth = depth;
            fx.FaceColor = c;

            var body = Rect(root.transform, "Body");
            Stretch(body, 0, 0, depth, 0);
            fx.Body = body;

            fx.Glow = Image(body, "Glow", new Color(1, 1, 1, 0));
            Stretch(fx.Glow.rectTransform, -6, -6, -6, -6);
            fx.Glow.raycastTarget = false;
            fx.Frame = Image(body, "Frame", Frame);
            Stretch(fx.Frame.rectTransform);
            fx.Frame.raycastTarget = false;
            float side = height >= 52 ? 10 : 6;
            fx.Face = Image(body, "Face", c);
            fx.Face.sprite = ChamferFace;
            Stretch(fx.Face.rectTransform, side, side, 4, 4);
            fx.Face.raycastTarget = false;
            var shine = Image(fx.Face.transform, "Shine", new Color(1, 1, 1, 0.13f));
            shine.rectTransform.anchorMin = new Vector2(0, 0.55f);
            shine.rectTransform.anchorMax = Vector2.one;
            shine.rectTransform.offsetMin = new Vector2(3, 0);
            shine.rectTransform.offsetMax = new Vector2(-3, -3);
            shine.raycastTarget = false;

            float labelLeft = 10;
            if (!string.IsNullOrEmpty(icon) && IconFont != null)
            {
                float plate = height - depth - 8 - 6;
                fx.IconPlate = Image(fx.Face.transform, "IconPlate", c * 0.7f);
                Place(fx.IconPlate.rectTransform, new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(plate + 10, plate), new Vector2(0, 0.5f));
                fx.IconPlate.raycastTarget = false;
                fx.Icon = IconText(fx.IconPlate.transform, icon, plate * 0.72f);
                Stretch(fx.Icon.rectTransform);
                labelLeft = plate + 26;
            }

            var t = Text(fx.Face.transform, label, fontSize, string.IsNullOrEmpty(icon) ? TextAlignmentOptions.Center : TextAlignmentOptions.Left, TextColor, FontStyles.Bold | FontStyles.UpperCase);
            Stretch(t.rectTransform, labelLeft, 10, 0, 2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.characterSpacing = 3;
            t.fontSharedMaterial = ShadowMaterial;
            fx.Label = t;

            if (onClick != null) b.onClick.AddListener(() => { Core.Sfx.PlayUI("ui_click"); onClick(); });
            LayoutElement(root.gameObject, -1, height);
            return b;
        }

        public static void SetLabel(Button b, string label)
        {
            var fx = b.GetComponent<ChunkyButton>();
            var t = fx != null ? fx.Label : b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = label;
        }

        public static void SetButtonColor(Button b, Color c)
        {
            var fx = b.GetComponent<ChunkyButton>();
            if (fx != null) fx.SetColor(c);
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

        // ---------------------------------------------------------------- items

        static readonly Color TierGrey = new Color(0.39f, 0.45f, 0.55f);
        static readonly Color TierCyan = new Color(0.22f, 0.74f, 0.97f);
        static readonly Color TierPurple = new Color(0.66f, 0.33f, 0.97f);

        /// <summary>Quality colour (DESIGN.md): tier 1 grey, 2 cyan, 3 purple.</summary>
        public static Color TierColor(int tier) => tier >= 3 ? TierPurple : tier == 2 ? TierCyan : TierGrey;

        /// <summary>Tier of modules and gear, 0 for resources and consumables.</summary>
        public static int TierOf(Voidward.Data.ItemDef d) =>
            d == null ? 0 : d.Module != null ? d.Module.Tier : d.Kind == Voidward.Core.ItemKind.Gear ? d.GearTier : 0;

        /// <summary>The item's rendered icon (falls back to a tinted swatch), aspect preserved, not raycastable.</summary>
        public static Image ItemIcon(Transform parent, Voidward.Data.ItemDef d, float size)
        {
            var img = Image(parent, "Icon", Color.white, false);
            img.raycastTarget = false;
            if (d != null && d.Icon != null) { img.sprite = d.Icon; img.preserveAspect = true; }
            else { img.sprite = Rounded; img.type = UnityEngine.UI.Image.Type.Sliced; img.color = d != null ? d.Tint : Color.gray; size *= 0.6f; }
            img.rectTransform.sizeDelta = new Vector2(size, size);
            LayoutElement(img.gameObject, size, size);
            return img;
        }

        /// <summary>Tier badge text ("II") for item cells; empty for untiered items.</summary>
        public static string TierLabel(int tier) => tier >= 3 ? "III" : tier == 2 ? "II" : tier == 1 ? "I" : "";

        static Sprite white;
        public static Sprite White => MakeWhite();
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

    /// <summary>Material Icons code points; ContentBuilder bakes <see cref="All"/> into Resources/UI/VW_Icons.</summary>
    public static class Icons
    {
        public const string Rocket = "\ueb9b";
        public const string Join = "\uea77";
        public const string Hangar = "\uebb8";
        public const string Settings = "\ue8b8";
        public const string Quit = "\ue9ba";
        public const string Group = "\uf233";
        public const string Invite = "\ue7fe";
        public const string Back = "\ue5c4";
        public const string Play = "\ue037";
        public const string Refresh = "\ue5d5";
        public const string Swap = "\ue8d4";
        public const string Close = "\ue5c9";
        public const string Pause = "\ue034";
        public const string Replay = "\ue042";
        public const string Flight = "\ue539";
        public const string Star = "\ue838";
        public const string Lan = "\ueb2f";
        public const string Person = "\ue7fd";
        // HUD (#24)
        public const string Heart = "\ue87d";
        public const string Air = "\uefd8";
        public const string Bolt = "\uea0b";
        public const string Fuel = "\ue546";
        public const string Shield = "\ue9e0";
        public const string Wrench = "\ue869";
        public const string Ship = "\ueba5";
        public const string Gate = "\ue57b";
        public const string Store = "\uea12";
        public const string Warning = "\ue002";
        public const string Pin = "\ue55f";
        public const string Salvage = "\uf041";
        public const string Respawn = "\ue863";
        public const string Speed = "\ue9e4";
        public const string Target = "\ue1b3";
        public const string Danger = "\ue99a";
        public const string Explore = "\ue87a";
        public const string Diamond = "\uead5";

        public const string All = Rocket + Join + Hangar + Settings + Quit + Group + Invite + Back + Play + Refresh + Swap + Close + Pause + Replay + Flight + Star + Lan + Person
            + Heart + Air + Bolt + Fuel + Shield + Wrench + Ship + Gate + Store + Warning + Pin + Salvage + Respawn + Speed + Target + Danger + Explore + Diamond;
    }
}
