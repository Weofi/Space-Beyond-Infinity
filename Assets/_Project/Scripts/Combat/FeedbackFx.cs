using System;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Combat
{
    /// <summary>One hit by the local player, as seen by the hit-feedback UI (hit marker, damage numbers).</summary>
    public struct LocalHit
    {
        public HealthComponent Target;
        public Vector3 Point;
        public float Amount;
        public bool Crit;     // weak point / critical hit
        public bool Killed;
        public int Combo;     // kills in the current combo window (only set on kills)
    }

    /// <summary>
    /// Central game-feel service (#43): hit markers, hit sounds, damage numbers, kill feedback, camera impulses
    /// and the local hit-stop. Purely client-side and cosmetic: hooks are the client RPCs of HealthComponent
    /// (damage, kills) and CombatNet (projectile impacts); nothing here touches simulation or Time.timeScale.
    /// Strength settings (shake, numbers, hit-stop) are pushed in by GameSettings.Apply.
    /// </summary>
    public static class FeedbackFx
    {
        /// <summary>Player setting: show floating damage numbers.</summary>
        public static bool DamageNumbers = true;
        /// <summary>Player setting: hit-stop duration multiplier (0 = off).</summary>
        public static float HitStopScale = 1f;

        /// <summary>Local player hit (or killed) an enemy. HUD hit marker + damage numbers listen.</summary>
        public static event Action<LocalHit> LocalHitLanded;

        const float ComboWindow = 3.5f;
        static int combo;
        static float comboUntil, nextHitSound, streakUntil;
        static int streak;

        /// <summary>Kills in the running combo (0 when the window has expired).</summary>
        public static int Combo => Time.unscaledTime < comboUntil ? combo : 0;
        /// <summary>Seconds left in the combo window, 0..1 normalised (HUD combo bar).</summary>
        public static float ComboFraction => Mathf.Clamp01((comboUntil - Time.unscaledTime) / ComboWindow);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LocalHitLanded = null;
            combo = 0; comboUntil = 0f; nextHitSound = 0f; streak = 0; streakUntil = 0f;
        }

        static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        // ------------------------------------------------------------------ hooks

        /// <summary>Every client, from HealthComponent.DamagedClientRpc. Only the shooter gets hit feedback.</summary>
        internal static void OnDamagedClient(HealthComponent h, DamageInfo info)
        {
            if (h == null || !info.ByPlayer || h.Team == Team.Players || info.SourceClientId != LocalId) return;
            float now = Time.unscaledTime;
            var hit = new LocalHit { Target = h, Point = info.Point, Amount = info.Amount, Crit = info.IsWeakPoint, Killed = info.Killed };

            if (info.Killed)
            {
                combo = now < comboUntil ? combo + 1 : 1;
                comboUntil = now + ComboWindow;
                hit.Combo = combo;
                // Kill confirm: punchy 2D sound (pitch climbs with the combo), camera impulse, longest hit-stop.
                Sfx.Play2D("kill", 0.9f, 1f + Mathf.Min(combo - 1, 6) * 0.045f + UnityEngine.Random.Range(-0.02f, 0.02f));
                Vfx.Impulse(-3.5f, 0.3f);
                HitStop(0.07f);
            }
            else
            {
                // Rapid hits form a streak: each tick is a little higher, plus random jitter so it never machine-guns.
                streak = now < streakUntil ? Mathf.Min(streak + 1, 8) : 0;
                streakUntil = now + 0.45f;
                if (now >= nextHitSound)
                {
                    nextHitSound = now + 0.045f;
                    float pitch = 1f + streak * 0.025f + UnityEngine.Random.Range(-0.06f, 0.06f);
                    if (hit.Crit) Sfx.Play2D("hit_crit", 0.75f, pitch);
                    else Sfx.Play2D("hitmarker", 0.55f, pitch);
                }
                // Heavy hits (crits, or a big chunk of the target's health) get a tiny hold and a nudge.
                float heavy = h.MaxHp.Value > 0f ? info.Amount / h.MaxHp.Value : 0f;
                if (hit.Crit || heavy >= 0.2f)
                {
                    HitStop(0.035f);
                    Vfx.Impulse(-1.2f, 0.08f);
                }
            }
            LocalHitLanded?.Invoke(hit);
        }

        /// <summary>Every client, from CombatNet.ImpactClientRpc: where a projectile ended.</summary>
        internal static void ProjectileImpact(Vector3 point, float aoe, Color color)
        {
            if (aoe > 0f)
            {
                Vfx.Explosion(point, aoe * 0.6f, color * 0.5f);
                Sfx.Play(aoe > 8f ? "explosion_big" : "explosion", point, 1f, UnityEngine.Random.Range(0.9f, 1.1f));
            }
            else
            {
                Vfx.LaserImpact(point);
                Sfx.Play("hit", point, 0.6f, UnityEngine.Random.Range(0.9f, 1.2f));
            }
        }

        /// <summary>Every client: an enemy died here. Explosion + debris burst + shock ring; shake falls off with distance.</summary>
        public static void EnemyDeath(Vector3 pos, float radius, Color tint, string sound)
        {
            Vfx.Explosion(pos, radius, tint);
            Vfx.KillBurst(pos, radius, tint);
            if (!string.IsNullOrEmpty(sound)) Sfx.Play(sound, pos, 1f, UnityEngine.Random.Range(0.85f, 1.1f));
        }

        /// <summary>Local, visual-only hit-stop (camera + effects hold). Scaled by the player setting.</summary>
        public static void HitStop(float seconds)
        {
            seconds *= Mathf.Clamp01(HitStopScale);
            if (seconds < 0.005f) return;
            GameState.HitStopUntil = Mathf.Max(GameState.HitStopUntil, Time.unscaledTime + seconds);
        }
    }
}
