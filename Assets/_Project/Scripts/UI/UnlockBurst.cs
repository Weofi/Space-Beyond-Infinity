using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Voidward.UI
{
    /// <summary>
    /// UI celebration (#43) for perk unlocks and the Stardust reward: the target punches in scale, a glow flashes
    /// over it and sparkles burst out along its length. Runs on unscaled time and removes itself when done.
    /// strength 0..1 = Reward effects setting (0 = only a short glow).
    /// </summary>
    public class UnlockBurst : MonoBehaviour
    {
        struct Spark { public RectTransform Rt; public Image Img; public Vector2 Vel; public float Spin, Life; }

        readonly List<Spark> sparks = new List<Spark>();
        RectTransform target, layer;
        Image flash;
        bool spawned;
        Color color;
        float start, strength;
        Vector3 baseScale;
        const float Duration = 1.1f;

        public static void Play(RectTransform target, Color color, float strength)
        {
            if (target == null) return;
            var old = target.GetComponent<UnlockBurst>();
            if (old != null) { old.Cleanup(); Destroy(old); }
            var b = target.gameObject.AddComponent<UnlockBurst>();
            b.Begin(target, color, Mathf.Clamp01(strength));
        }

        void Begin(RectTransform t, Color c, float k)
        {
            target = t; color = c; strength = k;
            start = Time.unscaledTime;
            baseScale = t.localScale;
            layer = UIKit.Rect(t, "Burst");
            layer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Stretch(layer);
            flash = UIKit.Image(layer, "Flash", new Color(c.r, c.g, c.b, 0f));
            flash.raycastTarget = false;
            UIKit.Stretch(flash.rectTransform, -6, -6, -6, -6);
        }

        /// <summary>Sparks spawn on the first Update, once the layout has given the target its real size.</summary>
        void SpawnSparks()
        {
            spawned = true;
            if (strength <= 0.05f) return;
            int count = Mathf.RoundToInt(30 * strength);
            Vector2 size = target.rect.size;
            if (size.x < 10f) size = new Vector2(600f, 70f);
            for (int i = 0; i < count; i++)
            {
                var img = UIKit.Image(layer, "Spark", Color.Lerp(color, Color.white, Random.value * 0.5f), false);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float sz = Random.Range(6f, 13f);
                rt.sizeDelta = new Vector2(sz, sz);
                // Start along the card, fly outwards and up.
                Vector2 p = new Vector2(Random.Range(-0.48f, 0.48f) * size.x, Random.Range(-0.4f, 0.4f) * size.y);
                rt.anchoredPosition = p;
                Vector2 dir = new Vector2(p.x / Mathf.Max(1f, size.x * 0.5f) * 0.6f + Random.Range(-0.4f, 0.4f), Random.Range(0.3f, 1f)).normalized;
                sparks.Add(new Spark { Rt = rt, Img = img, Vel = dir * Random.Range(180f, 420f), Spin = Random.Range(-400f, 400f), Life = Random.Range(0.55f, 1f) });
            }
        }

        void Update()
        {
            float t = Time.unscaledTime - start;
            float dt = Time.unscaledDeltaTime;
            if (t > Duration) { Cleanup(); Destroy(this); return; }
            if (!spawned) SpawnSparks();
            // Scale punch: quick overshoot, damped settle.
            float punch = Mathf.Sin(t * 22f) * Mathf.Exp(-t * 7f) * 0.06f * Mathf.Max(0.3f, strength);
            target.localScale = baseScale * (1f + punch);
            float f = Mathf.Clamp01(1f - t / 0.6f);
            flash.color = new Color(color.r, color.g, color.b, 0.55f * f * f);
            for (int i = 0; i < sparks.Count; i++)
            {
                var s = sparks[i];
                s.Vel *= Mathf.Exp(-3.5f * dt);
                s.Vel += Vector2.down * 260f * dt;
                s.Rt.anchoredPosition += s.Vel * dt;
                s.Rt.localRotation = Quaternion.Euler(0f, 0f, 45f + s.Spin * t);
                float a = Mathf.Clamp01(1f - t / s.Life);
                var c = s.Img.color; c.a = a; s.Img.color = c;
                s.Rt.localScale = Vector3.one * (0.4f + a * 0.6f);
                sparks[i] = s;
            }
        }

        void Cleanup()
        {
            if (target != null) target.localScale = baseScale;
            if (layer != null) Destroy(layer.gameObject);
            sparks.Clear();
        }

        void OnDestroy() => Cleanup();
    }
}
