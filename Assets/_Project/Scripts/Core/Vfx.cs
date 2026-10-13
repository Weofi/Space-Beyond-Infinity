using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Visual effects contract (implemented by the VfxManager, registered in G).
    /// Implementations must be safe to call on any client and pooled.
    /// </summary>
    public interface IVfxService
    {
        void Explosion(Vector3 pos, float radius, Color tint);
        /// <summary>Directional muzzle flash + short muzzle light. scale 1 = ship gun, ~0.35 = handheld.</summary>
        void MuzzleFlash(Vector3 pos, Vector3 dir, Color tint, float scale);
        /// <summary>Projectile / bullet impact: sparks, chips, dust or goo depending on the surface, along the normal.</summary>
        void Impact(Vector3 pos, Vector3 normal, SurfaceKind surface, Color tint, float scale);
        /// <summary>Pooled burn mark on a surface (parent = object it sticks to, null = world).</summary>
        void Scorch(Vector3 pos, Vector3 normal, Transform parent, float size, Color heat, float cool);
        void LaserImpact(Vector3 pos);
        void ShieldHit(Vector3 pos);
        /// <summary>Ship shield collapsed: big blue shell burst, shard sparks, shock ring and a light pulse.</summary>
        void ShieldBreak(Vector3 pos, float radius);
        /// <summary>Full-screen colour flash for the local player (alpha = peak strength).</summary>
        void ScreenFlash(Color c, float duration);
        void JumpFlash(Vector3 pos);
        void AtmosphereEntryFlash();
        void PickupSparkle(Vector3 pos, Color tint);
        /// <summary>Loot pickup / landing burst; tier = rarity 0..3 (rare+ adds a shock ring and a light pop).</summary>
        void LootSparkle(Vector3 pos, Color tint, float scale, int tier);
        /// <summary>Extra kill burst on top of an explosion: lit debris chunks, hot sparks and a second shock ring.</summary>
        void KillBurst(Vector3 pos, float radius, Color tint);
        /// <summary>Camera shake for the local player, intensity 0..1 (scaled by Vfx.ShakeScale).</summary>
        void Shake(float intensity);
    }

    /// <summary>Null-safe static access to VFX.</summary>
    public static class Vfx
    {
        /// <summary>Player setting: multiplies every camera shake and camera impulse (0 = off).</summary>
        public static float ShakeScale = 1f;

        public static void Explosion(Vector3 pos, float radius = 3f) { if (G.TryGet<IVfxService>(out var v)) v.Explosion(pos, radius, new Color(1f, 0.7f, 0.3f)); }
        public static void Explosion(Vector3 pos, float radius, Color tint) { if (G.TryGet<IVfxService>(out var v)) v.Explosion(pos, radius, tint); }
        public static void MuzzleFlash(Vector3 pos, Vector3 dir) => MuzzleFlash(pos, dir, new Color(2f, 1.8f, 1.2f), 1f);
        public static void MuzzleFlash(Vector3 pos, Vector3 dir, Color tint, float scale) { if (G.TryGet<IVfxService>(out var v)) v.MuzzleFlash(pos, dir, tint, scale); }
        public static void Impact(Vector3 pos, Vector3 normal, SurfaceKind surface, Color tint, float scale = 1f) { if (G.TryGet<IVfxService>(out var v)) v.Impact(pos, normal, surface, tint, scale); }
        public static void Scorch(Vector3 pos, Vector3 normal, Transform parent, float size, Color heat, float cool = 1.5f) { if (G.TryGet<IVfxService>(out var v)) v.Scorch(pos, normal, parent, size, heat, cool); }
        public static void LaserImpact(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.LaserImpact(pos); }
        public static void ShieldHit(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.ShieldHit(pos); }
        public static void ShieldBreak(Vector3 pos, float radius) { if (G.TryGet<IVfxService>(out var v)) v.ShieldBreak(pos, radius); }
        public static void ScreenFlash(Color c, float duration) { if (G.TryGet<IVfxService>(out var v)) v.ScreenFlash(c, duration); }
        public static void JumpFlash(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.JumpFlash(pos); }
        public static void AtmosphereEntry() { if (G.TryGet<IVfxService>(out var v)) v.AtmosphereEntryFlash(); }
        public static void PickupSparkle(Vector3 pos, Color tint) { if (G.TryGet<IVfxService>(out var v)) v.PickupSparkle(pos, tint); }
        public static void LootSparkle(Vector3 pos, Color tint, float scale, int tier) { if (G.TryGet<IVfxService>(out var v)) v.LootSparkle(pos, tint, scale, tier); }
        public static void KillBurst(Vector3 pos, float radius, Color tint) { if (G.TryGet<IVfxService>(out var v)) v.KillBurst(pos, radius, tint); }
        /// <summary>Local camera impulse: short FOV punch (degrees, negative = in) plus a sharp shake.</summary>
        public static void Impulse(float fovDegrees, float shake)
        {
            float k = Mathf.Clamp01(ShakeScale);
            if (k <= 0f) return;
            if (Mathf.Abs(fovDegrees * k) > Mathf.Abs(GameState.FovPunch)) GameState.FovPunch = fovDegrees * k;
            Shake(shake);
        }
        /// <summary>Local weapon kick (viewmodel / seat camera), added up and capped; scaled by the Screen shake setting.</summary>
        public static void Recoil(float amount)
        {
            float k = Mathf.Clamp01(ShakeScale);
            if (k <= 0f) return;
            GameState.Recoil = Mathf.Min(1.5f, GameState.Recoil + amount * k);
        }
        public static void Shake(float intensity) { if (G.TryGet<IVfxService>(out var v)) v.Shake(intensity); }
    }
}
