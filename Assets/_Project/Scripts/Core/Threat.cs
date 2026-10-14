using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>Something that can make the music go to combat: an engaged enemy or the boss (#43).</summary>
    public interface IThreatSource
    {
        Vector3 Position { get; }
        /// <summary>0 = idle / patrolling, 1 = actively fighting (chase / attack).</summary>
        float Engagement { get; }
        /// <summary>Within this distance of the listener the source counts.</summary>
        float ThreatRadius { get; }
        bool IsBoss { get; }
    }

    /// <summary>
    /// Client-side combat intensity for the dynamic music (#43). Engaged enemies near the listener plus a short
    /// "heat" from recent local hits (dealt or taken) give a 0..1 level; the boss adds its own layer. Purely
    /// cosmetic: sources register on every peer (EnemyBrain / BossBrain read their networked state).
    /// </summary>
    public static class Threat
    {
        static readonly List<IThreatSource> sources = new List<IThreatSource>();
        static float heat, overrideLevel = -1f, overrideUntil;
        static bool overrideBoss;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sources.Clear(); heat = 0f; overrideLevel = -1f; overrideUntil = 0f; overrideBoss = false; }

        public static void Register(IThreatSource s) { if (s != null && !sources.Contains(s)) sources.Add(s); }
        public static void Unregister(IThreatSource s) => sources.Remove(s);

        /// <summary>Recent combat involving the local player (hit landed ~0.2, hit taken ~0.35). Decays over a few seconds.</summary>
        public static void Bump(float amount) => heat = Mathf.Min(1f, heat + amount);

        /// <summary>Dev / tests: force a level (and boss layer) for some seconds; level &lt; 0 clears.</summary>
        public static void Override(float level, bool boss, float seconds)
        {
            overrideLevel = level;
            overrideBoss = boss;
            overrideUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Last evaluated values (HUD / dev logs).</summary>
        public static float Level { get; private set; }
        public static bool BossNear { get; private set; }

        /// <summary>An engaged enemy (chasing / attacking) or a boss within `radius` of a position (combat lock of cruise and warp, #80).</summary>
        public static bool EngagedNear(Vector3 pos, float radius)
        {
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var s = sources[i];
                if (s == null || (s is Object o && o == null)) continue;
                if ((s.IsBoss || s.Engagement > 0.5f) && (s.Position - pos).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        /// <summary>Recompute around the listener (AudioManager, a few times per second).</summary>
        public static void Evaluate(Vector3 listener, float dt)
        {
            heat = Mathf.MoveTowards(heat, 0f, dt * 0.12f);
            if (overrideLevel >= 0f && Time.unscaledTime < overrideUntil)
            {
                Level = Mathf.Clamp01(overrideLevel);
                BossNear = overrideBoss;
                return;
            }
            float sum = 0f;
            bool boss = false;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var s = sources[i];
                if (s == null || (s is Object o && o == null)) { sources.RemoveAt(i); continue; }
                float r = s.ThreatRadius;
                float d2 = (s.Position - listener).sqrMagnitude;
                if (d2 > r * r) continue;
                if (s.IsBoss) { boss = true; sum += 1.5f; continue; }
                // Close enemies weigh more than ones at the edge of the radius.
                sum += s.Engagement * Mathf.Lerp(1f, 0.4f, Mathf.Sqrt(d2) / r);
            }
            // 1 engaged enemy ~ 0.45, 3 ~ 0.83.
            float fromEnemies = 1f - Mathf.Exp(-sum * 0.6f);
            Level = Mathf.Clamp01(Mathf.Max(fromEnemies, heat));
            BossNear = boss;
        }
    }
}
