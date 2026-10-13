using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Voidward.Combat;

namespace Voidward.UI
{
    /// <summary>
    /// Floating damage numbers for the local player's hits (#43, toggle in Settings > Feedback). Pooled TMP labels
    /// on the HUD marker layer, anchored to the world hit point: pop in, drift up and sideways, fade. Rapid hits on
    /// the same target merge into one counting-up number; crits are bigger and yellow, killing blows red.
    /// </summary>
    public class DamageNumbers
    {
        class Num
        {
            public TextMeshProUGUI Text;
            public HealthComponent Target;
            public Vector3 World;
            public Vector2 Drift;
            public float Born, Value;
            public bool Crit, Kill;
        }

        const float MergeWindow = 0.3f;
        readonly RectTransform layer;
        readonly List<Num> live = new List<Num>();
        readonly Stack<Num> pool = new Stack<Num>();
        Material outlined;

        public DamageNumbers(RectTransform parent)
        {
            layer = UIKit.Rect(parent, "DamageNumbers");
            UIKit.Stretch(layer);
            FeedbackFx.LocalHitLanded += OnHit;
        }

        void OnHit(LocalHit h)
        {
            if (!FeedbackFx.DamageNumbers || h.Amount < 0.5f) return;
            float now = Time.unscaledTime;
            // AoE hits report the blast centre: show those numbers on the target instead.
            Vector3 world = h.Point;
            if (h.Target != null && (h.Target.transform.position - world).sqrMagnitude > 36f) world = h.Target.transform.position;

            for (int i = 0; i < live.Count; i++)
            {
                var n = live[i];
                if (h.Target == null || n.Target != h.Target || n.Crit != h.Crit || n.Kill || now - n.Born > MergeWindow) continue;
                n.Value += h.Amount;
                n.Kill = h.Killed;
                n.Born = now;   // restart the pop so the merge reads as a tick-up
                n.World = world;
                Style(n);
                return;
            }

            var num = pool.Count > 0 ? pool.Pop() : Create();
            num.Target = h.Target;
            num.World = world;
            num.Value = h.Amount;
            num.Crit = h.Crit;
            num.Kill = h.Killed;
            num.Born = now;
            num.Drift = new Vector2(Random.Range(-46f, 46f), Random.Range(-6f, 10f));
            num.Text.gameObject.SetActive(true);
            Style(num);
            live.Add(num);
        }

        Num Create()
        {
            var t = UIKit.Text(layer, "", 26, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(160, 50);
            if (outlined == null)
            {
                outlined = new Material(t.fontSharedMaterial) { name = "VW_DamageNumber" };
                outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
                outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.24f);
                outlined.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.04f, 0.05f, 0.1f, 0.9f));
            }
            t.fontSharedMaterial = outlined;
            return new Num { Text = t };
        }

        static void Style(Num n)
        {
            n.Text.text = Mathf.Max(1, Mathf.RoundToInt(n.Value)).ToString();
            n.Text.fontSize = n.Crit ? 36f : n.Kill ? 32f : 25f;
            n.Text.color = n.Kill ? HudKit.Hex(0xF87171) : n.Crit ? HudKit.Fuel : HudKit.TextHi;
        }

        public void Update(Camera cam)
        {
            float now = Time.unscaledTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var n = live[i];
                float t = now - n.Born;
                float life = n.Crit || n.Kill ? 1.05f : 0.8f;
                Vector3 sp = cam != null ? cam.WorldToScreenPoint(n.World) : Vector3.zero;
                if (t > life || !FeedbackFx.DamageNumbers)
                {
                    n.Text.gameObject.SetActive(false);
                    n.Target = null;
                    live.RemoveAt(i);
                    pool.Push(n);
                    continue;
                }
                if (sp.z <= 0f) { n.Text.alpha = 0f; continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out var local);
                float rise = 1f - (1f - Mathf.Clamp01(t / life)) * (1f - Mathf.Clamp01(t / life)); // ease-out
                n.Text.rectTransform.anchoredPosition = local + n.Drift * rise + new Vector2(0f, 26f + 64f * rise);
                // pop: overshoot to 1.5x in 60 ms, settle to 1x by 160 ms
                float pop = t < 0.06f ? Mathf.Lerp(0.8f, 1.5f, t / 0.06f) : Mathf.Lerp(1.5f, 1f, Mathf.Clamp01((t - 0.06f) / 0.1f));
                n.Text.rectTransform.localScale = Vector3.one * pop;
                n.Text.alpha = 1f - Mathf.Clamp01((t - (life - 0.25f)) / 0.25f);
            }
        }
    }
}
