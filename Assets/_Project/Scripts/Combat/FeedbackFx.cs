using System;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

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
    /// Central game-feel service (#43): hit markers, hit sounds, damage numbers, kill feedback, camera impulses,
    /// the local hit-stop, and weapon feedback (muzzle flash + light, recoil, surface impacts, scorch decals). Purely client-side and cosmetic: hooks are the client RPCs of HealthComponent
    /// (damage, kills) and CombatNet (projectile impacts); nothing here touches simulation or Time.timeScale.
    /// Part 4: loot (pop-out, landing, rarity pickup bursts), unlock celebration sounds and the combat heat that
    /// drives the dynamic music (Threat). Strength settings (shake, numbers, hit-stop, reward effects) are pushed
    /// in by GameSettings.Apply.
    /// </summary>
    public static class FeedbackFx
    {
        /// <summary>Player setting: show floating damage numbers.</summary>
        public static bool DamageNumbers = true;
        /// <summary>Player setting: hit-stop duration multiplier (0 = off).</summary>
        public static float HitStopScale = 1f;
        /// <summary>Player setting: low-health pulse vignette + heartbeat / hull klaxon.</summary>
        public static bool LowHealthWarning = true;
        /// <summary>Player setting: loot beams, pickup bursts and unlock celebrations, 0..1 (0 = minimal).</summary>
        public static float RewardFx = 1f;

        /// <summary>Local player hit (or killed) an enemy. HUD hit marker + damage numbers listen.</summary>
        public static event Action<LocalHit> LocalHitLanded;

        const float ComboWindow = 3.5f;
        static int combo;
        static float comboUntil, nextHitSound, streakUntil, nextHurtSound;
        static int streak, pickupStreak;
        static float pickupStreakUntil, nextLandSound;

        /// <summary>Kills in the running combo (0 when the window has expired).</summary>
        public static int Combo => Time.unscaledTime < comboUntil ? combo : 0;
        /// <summary>Seconds left in the combo window, 0..1 normalised (HUD combo bar).</summary>
        public static float ComboFraction => Mathf.Clamp01((comboUntil - Time.unscaledTime) / ComboWindow);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LocalHitLanded = null;
            combo = 0; comboUntil = 0f; nextHitSound = 0f; streak = 0; streakUntil = 0f; nextHurtSound = 0f;
            pickupStreak = 0; pickupStreakUntil = 0f; nextLandSound = 0f;
        }

        static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        // ------------------------------------------------------------------ hooks

        /// <summary>Every client, from HealthComponent.DamagedClientRpc. Only the shooter gets hit feedback.</summary>
        internal static void OnDamagedClient(HealthComponent h, DamageInfo info)
        {
            if (h == null || !info.ByPlayer || h.Team == Team.Players || info.SourceClientId != LocalId) return;
            float now = Time.unscaledTime;
            var hit = new LocalHit { Target = h, Point = info.Point, Amount = info.Amount, Crit = info.IsWeakPoint, Killed = info.Killed };
            if (h.Team == Team.Pirates || h.Team == Team.Aliens) Threat.Bump(info.Killed ? 0.3f : 0.15f);

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

        // ------------------------------------------------------------ weapons (#43 part 2)

        /// <summary>Hot glow of a fresh burn mark (kinetic / explosive); energy weapons tint it with their bolt colour.</summary>
        static readonly Color BurnHeat = new Color(3f, 1.15f, 0.3f);

        /// <summary>
        /// Every client: a weapon fired here (projectile spawn or hitscan tracer). Directional muzzle flash with a
        /// short muzzle light; scale 1 = ship gun, ~0.35 = handheld.
        /// </summary>
        public static void WeaponFired(Vector3 muzzle, Vector3 dir, Color tint, float scale)
        {
            Vfx.MuzzleFlash(muzzle, dir, tint, scale);
        }

        /// <summary>
        /// Local player only, at the moment they pull the trigger: weapon / seat camera kick. Scaled by the Screen
        /// shake setting (0 = no recoil animation); heavy guns add a tiny shake on top.
        /// </summary>
        public static void LocalRecoil(float amount)
        {
            Vfx.Recoil(amount);
            if (amount >= 0.4f) Vfx.Shake(amount * 0.12f);
        }

        /// <summary>What a collider is made of, for impact effects. Server (projectiles) and shooter (hitscan).</summary>
        public static SurfaceKind SurfaceOf(Collider c)
        {
            if (c == null) return SurfaceKind.None;
            var s = c.GetComponentInParent<ISurface>();
            if (s != null) return s.Surface;
            var d = c.GetComponentInParent<IDamageable>();
            if (d != null)
            {
                switch (d.Team)
                {
                    case Team.Aliens: return SurfaceKind.Organic;
                    case Team.Neutral: return SurfaceKind.Rock;
                    default: return SurfaceKind.Metal; // pirate hulls, drones, suits
                }
            }
            return SurfaceKind.Rock;
        }

        /// <summary>Every client, from CombatNet.ImpactClientRpc: where a projectile ended (normal = zero for air bursts).</summary>
        internal static void ProjectileImpact(Vector3 point, Vector3 normal, SurfaceKind surface, float aoe, Color color, float scale, bool acid)
        {
            if (aoe > 0f)
            {
                Vfx.Explosion(point, aoe * 0.6f, color * 0.5f);
                Sfx.Play(aoe > 8f ? "explosion_big" : "explosion", point, 1f, UnityEngine.Random.Range(0.9f, 1.1f));
                if (acid) Vfx.Impact(point, normal, SurfaceKind.Organic, color, 1.2f);
                // Blast mark on whatever it hit (not for air bursts and not on shields).
                PlaceScorch(point, normal, surface, aoe * (acid ? 0.6f : 0.45f), acid ? new Color(0.9f, 2.6f, 0.4f) : BurnHeat, acid ? 2.5f : 3f);
            }
            else
            {
                Vfx.Impact(point, normal, surface, color, scale);
                Sfx.Play("hit", point, 0.6f, UnityEngine.Random.Range(0.9f, 1.2f));
                PlaceScorch(point, normal, surface, 1.1f * scale, Color.Lerp(BurnHeat, color, 0.25f), 1.4f);
            }
        }

        /// <summary>Hitscan hit (rifle): shooter calls it directly, the others via the tracer RPC.</summary>
        public static void HitscanImpact(Vector3 point, Vector3 normal, SurfaceKind surface, Color color)
        {
            if (surface == SurfaceKind.None) return;
            Vfx.Impact(point, normal, surface, color, 0.45f);
            PlaceScorch(point, normal, surface, 0.32f, BurnHeat, 0.6f);
        }

        /// <summary>
        /// Lay a burn mark where a shot ended. The decal sticks to the object found under the point on this peer
        /// (ships, asteroids, the planet); far terrain without streamed colliders gets a world-anchored mark.
        /// Organic targets, shields, player suits and air bursts get none.
        /// </summary>
        static void PlaceScorch(Vector3 point, Vector3 normal, SurfaceKind surface, float size, Color heat, float cool)
        {
            if (surface == SurfaceKind.None || surface == SurfaceKind.Organic || surface == SurfaceKind.Shield) return;
            if (normal.sqrMagnitude < 0.01f) return;
            normal.Normalize();
            Transform parent = null;
            if (Physics.Raycast(point + normal * 0.6f, -normal, out var hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                var s = hit.collider.GetComponentInParent<ISurface>();
                if (s == null)
                {
                    var d = hit.collider.GetComponentInParent<IDamageable>();
                    if (d != null && (d.Team == Team.Players || d.Team == Team.Aliens)) return; // suits, creatures
                }
                if (s != null && s.Surface == SurfaceKind.Shield) return;
                parent = s != null ? s.transform : hit.collider.transform;
                point = hit.point;
                normal = hit.normal;
            }
            else if (surface != SurfaceKind.Dirt) return; // a moving target that is somewhere else on this peer
            Vfx.Scorch(point, normal, parent, size, heat, cool);
        }

        /// <summary>Every client: an enemy died here. Explosion + debris burst + shock ring; shake falls off with distance.</summary>
        public static void EnemyDeath(Vector3 pos, float radius, Color tint, string sound)
        {
            Vfx.Explosion(pos, radius, tint);
            Vfx.KillBurst(pos, radius, tint);
            if (!string.IsNullOrEmpty(sound)) Sfx.Play(sound, pos, 1f, UnityEngine.Random.Range(0.85f, 1.1f));
        }

        // ------------------------------------------------------- damage taken (#43 part 3)

        /// <summary>
        /// Local player (or the ship they fly) took damage; intensity 0..1. Called by the HUD, which also draws the
        /// direction arc and gauge flash. Shield-absorbed hits only nudge; health/hull hits thud and shake.
        /// </summary>
        public static void LocalHurt(float intensity, bool shield)
        {
            intensity = Mathf.Clamp01(intensity);
            Threat.Bump(0.25f + intensity * 0.3f);
            if (shield) { Vfx.Shake(0.05f + intensity * 0.15f); return; }
            Vfx.Shake(0.12f + intensity * 0.4f);
            float now = Time.unscaledTime;
            if (now < nextHurtSound) return;
            nextHurtSound = now + 0.12f;
            Sfx.Play2D("hurt", 0.45f + intensity * 0.4f, UnityEngine.Random.Range(0.92f, 1.06f));
        }

        /// <summary>Every client: a ship shield collapsed here (radius ~ ship size). World burst + spatial crack.</summary>
        public static void ShieldBroken(Vector3 pos, float radius)
        {
            Vfx.ShieldBreak(pos, radius);
            Sfx.Play("shield_break", pos, 1f, UnityEngine.Random.Range(0.95f, 1.05f));
        }

        /// <summary>The local player's ship lost its shield: blue screen flash, hard shake, 2D crack on top.</summary>
        public static void LocalShieldBroken()
        {
            Vfx.ScreenFlash(new Color(0.35f, 0.75f, 1f, 0.16f), 0.45f);
            Vfx.Shake(0.45f);
            Sfx.Play2D("shield_break", 0.8f, 1f);
        }

        /// <summary>One low-health warning beat: heartbeat on foot, klaxon in a ship. severity 0..1.</summary>
        public static void LowHealthBeat(bool ship, float severity)
        {
            if (!LowHealthWarning) return;
            if (ship) Sfx.Play2D("hull_alarm", 0.35f + severity * 0.25f, 1f);
            else Sfx.Play2D("heartbeat", 0.55f + severity * 0.4f, 1f + severity * 0.08f);
        }

        // ------------------------------------------------------------ loot & progress (#43 part 4)

        /// <summary>Every client: a drop pops out of its source (enemy, crate). Little spatial pop, brighter for rare loot.</summary>
        public static void LootPopped(Vector3 from, Rarity r)
        {
            int t = (int)r;
            Sfx.Play("loot_pop", from, 0.45f + t * 0.1f, UnityEngine.Random.Range(0.94f, 1.08f) + t * 0.05f);
        }

        /// <summary>Every client: a drop finished its arc. Dust puff + thunk; rare loot chimes, epic loot sparkles.</summary>
        public static void LootLanded(Vector3 pos, Vector3 up, Rarity r, Color rarityColor)
        {
            int t = (int)r;
            Vfx.Impact(pos, up, SurfaceKind.Dirt, Color.white, 0.22f);
            float now = Time.unscaledTime;
            if (now >= nextLandSound || r >= Rarity.Rare)
            {
                nextLandSound = now + 0.06f;
                Sfx.Play(r >= Rarity.Rare ? "loot_land_rare" : "loot_land", pos, 0.55f + t * 0.1f, UnityEngine.Random.Range(0.92f, 1.08f) + (r == Rarity.Epic ? -0.1f : 0f));
            }
            float fx = Mathf.Clamp01(RewardFx);
            if (r == Rarity.Epic && fx > 0f) Vfx.LootSparkle(pos + up * 0.6f, rarityColor.linear, 0.8f * fx, 2);
        }

        /// <summary>
        /// Every client, when a drop is collected. Burst in rarity colours (bigger for rarer loot); the collector
        /// hears a 2D chime per rarity whose pitch climbs while picking up a lot in a row, epic loot adds a gold flash.
        /// </summary>
        public static void LootCollected(Vector3 pos, Rarity r, Color itemTint, bool local)
        {
            int t = (int)r;
            float fx = Mathf.Clamp01(RewardFx);
            Color c = Color.Lerp(itemTint, LootRarity.ColorOf(r), r == Rarity.Common ? 0.2f : 0.7f).linear;
            if (!local)
            {
                Vfx.LootSparkle(pos, c, (0.7f + t * 0.3f) * Mathf.Lerp(0.45f, 1f, fx), fx > 0.3f ? t : Mathf.Min(t, 1));
                Sfx.Play("pickup", pos, 0.35f, 1f + t * 0.06f);
                return;
            }
            // The collector's burst happens right below the camera: keep it small (no ring / light), the 2D chime
            // and the HUD notification carry the reward.
            Vfx.LootSparkle(pos, c, (0.25f + t * 0.08f) * Mathf.Lerp(0.5f, 1f, fx), 0);
            float now = Time.unscaledTime;
            pickupStreak = now < pickupStreakUntil ? Mathf.Min(pickupStreak + 1, 7) : 0;
            pickupStreakUntil = now + 0.9f;
            string id = r == Rarity.Common ? "pickup" : r == Rarity.Uncommon ? "pickup_uncommon" : r == Rarity.Rare ? "pickup_rare" : "pickup_epic";
            Sfx.Play2D(id, 0.6f + t * 0.1f, 1f + pickupStreak * 0.06f);
            if (r == Rarity.Epic && fx > 0f)
            {
                Vfx.ScreenFlash(new Color(1f, 0.78f, 0.3f, 0.07f * fx), 0.4f);
                Vfx.Impulse(-1.5f * fx, 0f);
            }
        }

        /// <summary>A perk was unlocked (hangar): rising fanfare. The UI plays the matching card burst.</summary>
        public static void PerkUnlocked() => Sfx.PlayUI("unlock", 1f);

        /// <summary>Results screen Stardust count-up: one tick per step (pitch climbs with progress 0..1).</summary>
        public static void StardustTick(float progress01) => Sfx.Play2D("blip", 0.5f, 1.4f + progress01 * 0.8f);

        /// <summary>Count-up finished: chime, plus a sparkle burst in the UI.</summary>
        public static void StardustDone(int amount) => Sfx.PlayUI(amount > 0 ? "stardust" : "ui_back", 1f);

        /// <summary>Local, visual-only hit-stop (camera + effects hold). Scaled by the player setting.</summary>
        public static void HitStop(float seconds)
        {
            seconds *= Mathf.Clamp01(HitStopScale);
            if (seconds < 0.005f) return;
            GameState.HitStopUntil = Mathf.Max(GameState.HitStopUntil, Time.unscaledTime + seconds);
        }
    }
}
