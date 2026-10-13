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
        void MuzzleFlash(Vector3 pos, Vector3 dir);
        void LaserImpact(Vector3 pos);
        void ShieldHit(Vector3 pos);
        void JumpFlash(Vector3 pos);
        void AtmosphereEntryFlash();
        void PickupSparkle(Vector3 pos, Color tint);
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
        public static void MuzzleFlash(Vector3 pos, Vector3 dir) { if (G.TryGet<IVfxService>(out var v)) v.MuzzleFlash(pos, dir); }
        public static void LaserImpact(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.LaserImpact(pos); }
        public static void ShieldHit(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.ShieldHit(pos); }
        public static void JumpFlash(Vector3 pos) { if (G.TryGet<IVfxService>(out var v)) v.JumpFlash(pos); }
        public static void AtmosphereEntry() { if (G.TryGet<IVfxService>(out var v)) v.AtmosphereEntryFlash(); }
        public static void PickupSparkle(Vector3 pos, Color tint) { if (G.TryGet<IVfxService>(out var v)) v.PickupSparkle(pos, tint); }
        public static void KillBurst(Vector3 pos, float radius, Color tint) { if (G.TryGet<IVfxService>(out var v)) v.KillBurst(pos, radius, tint); }
        /// <summary>Local camera impulse: short FOV punch (degrees, negative = in) plus a sharp shake.</summary>
        public static void Impulse(float fovDegrees, float shake)
        {
            float k = Mathf.Clamp01(ShakeScale);
            if (k <= 0f) return;
            if (Mathf.Abs(fovDegrees * k) > Mathf.Abs(GameState.FovPunch)) GameState.FovPunch = fovDegrees * k;
            Shake(shake);
        }
        public static void Shake(float intensity) { if (G.TryGet<IVfxService>(out var v)) v.Shake(intensity); }
    }
}
