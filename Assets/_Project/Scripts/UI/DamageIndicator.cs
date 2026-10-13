using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Voidward.UI
{
    /// <summary>
    /// Damage direction indicator (#43): pooled crescents on a ring around the crosshair that point towards
    /// whatever hit the local player (or the ship they fly). The source is kept in world space and re-projected
    /// every frame, so the arc keeps pointing at the attacker while the player turns. On foot the angle uses the
    /// horizontal plane (classic shooter compass); in a seat it uses the screen plane, so "above" means above.
    /// Red = health / hull, blue = shield absorbed it.
    /// </summary>
    public class DamageIndicator
    {
        class Arc
        {
            public Image Img;
            public Vector3 Source;
            public Color Color;
            public float Born, Strength;
        }

        const float Life = 1.6f;
        const float Radius = 150f;
        const float MergeDegrees = 25f;
        readonly RectTransform layer;
        readonly List<Arc> live = new List<Arc>();
        readonly Stack<Arc> pool = new Stack<Arc>();
        static Sprite sprite;

        public DamageIndicator(RectTransform parent)
        {
            layer = UIKit.Rect(parent, "DamageIndicator");
            UIKit.Stretch(layer);
        }

        /// <summary>
        /// Something hit us from <paramref name="source"/> (world point). strength 0..1 scales size and opacity.
        /// A new hit from roughly the same direction refreshes the existing arc instead of stacking.
        /// </summary>
        public void Add(Camera cam, Vector3 source, Color color, float strength, bool seated)
        {
            if (cam == null) return;
            float now = Time.unscaledTime;
            float angle = AngleOf(cam, source, seated);
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                if (a.Color != color || Mathf.Abs(Mathf.DeltaAngle(AngleOf(cam, a.Source, seated), angle)) > MergeDegrees) continue;
                a.Source = source;
                a.Born = now;
                a.Strength = Mathf.Min(1f, Mathf.Max(a.Strength, strength) + 0.15f);
                return;
            }
            var arc = pool.Count > 0 ? pool.Pop() : Create();
            arc.Source = source;
            arc.Color = color;
            arc.Born = now;
            arc.Strength = Mathf.Clamp01(strength);
            arc.Img.gameObject.SetActive(true);
            live.Add(arc);
        }

        public void Update(Camera cam, bool seated)
        {
            float now = Time.unscaledTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var a = live[i];
                float age = now - a.Born;
                if (age >= Life || cam == null)
                {
                    a.Img.gameObject.SetActive(false);
                    pool.Push(a);
                    live.RemoveAt(i);
                    continue;
                }
                float angle = AngleOf(cam, a.Source, seated);
                var rt = a.Img.rectTransform;
                float pop = 1f + 0.25f * Mathf.Clamp01(1f - age / 0.12f);
                float r = Radius + 18f * a.Strength;
                rt.anchoredPosition = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad)) * r;
                rt.localEulerAngles = new Vector3(0f, 0f, -angle);
                rt.sizeDelta = new Vector2(150f + 70f * a.Strength, 44f + 14f * a.Strength) * pop;
                float fade = 1f - Mathf.Clamp01((age - Life * 0.45f) / (Life * 0.55f));
                var c = a.Color;
                c.a = (0.55f + 0.4f * a.Strength) * fade;
                a.Img.color = c;
            }
        }

        /// <summary>Clockwise degrees from screen-up towards the source.</summary>
        static float AngleOf(Camera cam, Vector3 source, bool seated)
        {
            var v = cam.transform.InverseTransformPoint(source);
            Vector2 d = seated ? new Vector2(v.x, v.y) : new Vector2(v.x, v.z);
            // Seated and dead ahead or dead behind: the screen-plane offset says nothing, point down for "behind".
            if (d.sqrMagnitude < 1e-4f) d = seated && v.z < 0f ? Vector2.down : Vector2.up;
            if (seated && v.z < 0f && d.sqrMagnitude < v.z * v.z * 0.01f) d = Vector2.down;
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        Arc Create()
        {
            var img = UIKit.Image(layer, "Arc", Color.white, false);
            img.sprite = sprite != null ? sprite : (sprite = MakeArc());
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return new Arc { Img = img };
        }

        /// <summary>Crescent bowed outwards (towards +Y): bright outer edge, soft inner glow, tapered ends.</summary>
        static Sprite MakeArc()
        {
            const int W = 192, H = 56;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            const float R = 260f;                   // arc radius in texels, centre below the texture
            var centre = new Vector2(W * 0.5f, H - 12f - R);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f) - centre;
                    float dist = p.magnitude - R;                       // 0 on the outer edge, negative inside
                    float ang = Mathf.Abs(Mathf.Atan2(p.x, p.y)) / (W * 0.5f / R);
                    float taper = Mathf.Clamp01((1f - ang) / 0.45f);
                    taper *= taper;
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(dist + 3f) / 4f);          // crisp rim
                    float glow = Mathf.Clamp01(1f + dist / 30f) * (dist < 0f ? 1f : 0f); // fades inwards
                    float a = Mathf.Max(edge, glow * glow * 0.55f) * taper;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        }
    }
}
