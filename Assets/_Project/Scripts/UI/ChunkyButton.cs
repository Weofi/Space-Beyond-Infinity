using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Voidward.Core;

namespace Voidward.UI
{
    /// <summary>Tactile hover/press/disabled animation for the chunky low-poly buttons built by UIKit.</summary>
    public class ChunkyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Button Button;
        public RectTransform Body;
        public Image Base, Frame, Face, Glow, IconPlate;
        public TextMeshProUGUI Label, Icon;
        public Color FaceColor;
        public float Depth = 6f;
        public float HoverSlide;
        public float HoverScale = 1.035f;
        /// <summary>Seconds to wait before the slide-in intro on enable; negative disables the intro.</summary>
        public float IntroDelay = -1f;

        bool pointerOver, pointerDown, selected;
        float hover, press, intro = 1f, introWait;
        CanvasGroup group;

        void OnEnable()
        {
            pointerOver = pointerDown = selected = false;
            hover = press = 0f;
            if (IntroDelay >= 0f)
            {
                if (group == null) group = gameObject.GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                intro = 0f;
                introWait = IntroDelay;
                group.alpha = 0f;
            }
            Apply();
        }

        bool Interactable => Button == null || Button.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            pointerOver = true;
            if (Interactable) Sfx.PlayUI("ui_hover", 0.5f);
        }

        public void OnPointerExit(PointerEventData e) { pointerOver = false; pointerDown = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pointerDown = true; }
        public void OnPointerUp(PointerEventData e) { pointerDown = false; }
        public void OnSelect(BaseEventData e) { selected = true; }
        public void OnDeselect(BaseEventData e) { selected = false; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool on = Interactable;
            hover = Mathf.MoveTowards(hover, on && (pointerOver || selected) ? 1f : 0f, dt * 8f);
            press = Mathf.MoveTowards(press, on && pointerDown ? 1f : 0f, dt * 18f);
            if (intro < 1f)
            {
                if (introWait > 0f) introWait -= dt;
                else intro = Mathf.MoveTowards(intro, 1f, dt * 4f);
                if (group != null) group.alpha = intro;
            }
            Apply();
        }

        void Apply()
        {
            if (Body == null) return;
            float h = Smooth(hover);
            float i = 1f - Smooth(intro);
            // Body is stretched with a top inset of Depth; resting raises it so the dark base shows as a lip.
            Body.anchoredPosition = new Vector2(HoverSlide * h - 90f * i, Depth * (0.5f - press));
            Body.localScale = Vector3.one * Mathf.Lerp(1f, HoverScale, h) * Mathf.Lerp(1f, 0.98f, press);

            bool on = Interactable;
            Color face = on ? Color.Lerp(FaceColor, Brighten(FaceColor), h * 0.6f) : Desaturate(FaceColor);
            if (Face != null) Face.color = Color.Lerp(face, face * 0.85f, press);
            if (Base != null) { var d = face * 0.42f; d.a = 1f; Base.color = d; }
            if (IconPlate != null) { var p = face * Mathf.Lerp(0.72f, 0.6f, press); p.a = 1f; IconPlate.color = p; }
            if (Frame != null) Frame.color = on ? Color.Lerp(UIKit.Frame, UIKit.FrameHot, h) : UIKit.FrameShade;
            if (Glow != null) Glow.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.45f * h);
            if (Label != null) Label.alpha = on ? 1f : 0.55f;
            if (Icon != null) Icon.alpha = on ? 1f : 0.55f;
        }

        public void SetColor(Color c)
        {
            FaceColor = c;
            Apply();
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
        static Color Brighten(Color c) => new Color(Mathf.Lerp(c.r, 1f, 0.3f), Mathf.Lerp(c.g, 1f, 0.3f), Mathf.Lerp(c.b, 1f, 0.3f), c.a);

        static Color Desaturate(Color c)
        {
            float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return new Color(Mathf.Lerp(g, 0.35f, 0.4f), Mathf.Lerp(g, 0.38f, 0.4f), Mathf.Lerp(g, 0.45f, 0.4f), 1f);
        }
    }
}
