using UnityEngine;

namespace Voidward.UI
{
    /// <summary>Fade + slide-in whenever a menu screen becomes active.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIScreenFx : MonoBehaviour
    {
        public Vector2 SlideFrom = new Vector2(0f, -30f);
        public float Duration = 0.25f;

        RectTransform rt;
        CanvasGroup group;
        Vector2 home;
        float t;
        bool init;

        void OnEnable()
        {
            if (!init)
            {
                rt = (RectTransform)transform;
                group = GetComponent<CanvasGroup>();
                home = rt.anchoredPosition;
                init = true;
            }
            t = 0f;
            Apply();
        }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.MoveTowards(t, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, Duration));
            Apply();
        }

        void Apply()
        {
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            group.alpha = e;
            rt.anchoredPosition = home + SlideFrom * (1f - e);
        }
    }
}
