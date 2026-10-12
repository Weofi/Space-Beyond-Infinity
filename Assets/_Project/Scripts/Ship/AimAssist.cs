using System.Collections.Generic;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>
    /// Local (client-side) ship aim helper: estimates enemy velocities, computes intercept ("lead")
    /// points for a projectile speed and snaps fire onto the lead point when a target sits inside the
    /// assist cone around the crosshair. The latest result is exposed for the HUD lead indicator.
    /// Only the aim direction sent to the server changes, so this is safe in multiplayer.
    /// </summary>
    public static class AimAssist
    {
        /// <summary>Targets inside this cone get a lead indicator (assist or not).</summary>
        const float IndicatorConeDeg = 22f;

        struct Track { public Vector3 Pos, Vel; public int Frame; }
        static readonly Dictionary<HealthComponent, Track> tracks = new Dictionary<HealthComponent, Track>();
        static readonly List<HealthComponent> stale = new List<HealthComponent>();
        static int solvedFrame = -1;

        /// <summary>HUD: a target has a lead indicator this frame.</summary>
        public static bool HasLead { get; private set; }
        /// <summary>HUD: fire is currently snapped onto the lead point.</summary>
        public static bool Locked { get; private set; }
        public static Vector3 LeadPoint { get; private set; }
        public static Vector3 TargetPoint { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { tracks.Clear(); Clear(); }

        public static void Clear()
        {
            HasLead = Locked = false;
            solvedFrame = -1;
        }

        /// <summary>HUD: drop the indicator when nobody solved this frame (seat left, menu open).</summary>
        public static bool FreshThisFrame => solvedFrame >= Time.frameCount - 1;

        /// <summary>
        /// Aim from <paramref name="muzzle"/> for a projectile of <paramref name="speed"/> m/s, looking along
        /// <paramref name="viewFwd"/> from <paramref name="eye"/>. Returns true and the assisted direction when a
        /// target is inside <paramref name="assistDeg"/>.
        /// </summary>
        public static bool Solve(Vector3 eye, Vector3 viewFwd, Vector3 muzzle, float speed, float assistDeg, float range, out Vector3 dir)
        {
            dir = viewFwd;
            UpdateTracks();
            solvedFrame = Time.frameCount;
            HasLead = Locked = false;
            if (speed <= 0f) return false;

            float bestAng = Mathf.Max(IndicatorConeDeg, assistDeg);
            HealthComponent best = null;
            Vector3 bestLead = Vector3.zero;
            foreach (var h in HealthComponent.All)
            {
                if (!IsHostile(h)) continue;
                Vector3 p = h.transform.position;
                if ((p - eye).sqrMagnitude > range * range) continue;
                tracks.TryGetValue(h, out var tr);
                Vector3 lead = Intercept(muzzle, p, tr.Vel, speed);
                // Players aim at the ship itself or at the lead marker: either counts for the cone.
                float ang = Mathf.Min(Vector3.Angle(viewFwd, lead - eye), Vector3.Angle(viewFwd, p - eye));
                if (ang < bestAng) { bestAng = ang; best = h; bestLead = lead; }
            }
            if (best == null) return false;

            HasLead = true;
            LeadPoint = bestLead;
            TargetPoint = best.transform.position;
            if (!VwInput.ShipAimAssist || bestAng > assistDeg) return false;
            Locked = true;
            dir = (bestLead - muzzle).normalized;
            return true;
        }

        static bool IsHostile(HealthComponent h) =>
            h != null && h.IsAlive && h.Team != Team.Players && h.Team != Team.Neutral && h.gameObject.activeInHierarchy;

        /// <summary>Point where a projectile fired now from <paramref name="from"/> meets a target moving at constant velocity.</summary>
        public static Vector3 Intercept(Vector3 from, Vector3 targetPos, Vector3 targetVel, float speed)
        {
            Vector3 d = targetPos - from;
            float a = Vector3.Dot(targetVel, targetVel) - speed * speed;
            float b = 2f * Vector3.Dot(d, targetVel);
            float c = Vector3.Dot(d, d);
            float t;
            if (Mathf.Abs(a) < 0.001f) t = b < 0f ? -c / b : d.magnitude / speed;
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return targetPos;
                float sq = Mathf.Sqrt(disc);
                float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
                t = t1 > 0f && t2 > 0f ? Mathf.Min(t1, t2) : Mathf.Max(t1, t2);
            }
            if (t <= 0f || float.IsNaN(t)) return targetPos;
            return targetPos + targetVel * Mathf.Min(t, 4f);
        }

        /// <summary>Velocity estimate per hostile from its (interpolated) transform, once per frame.</summary>
        static void UpdateTracks()
        {
            int frame = Time.frameCount;
            if (solvedFrame == frame) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            foreach (var h in HealthComponent.All)
            {
                if (!IsHostile(h)) continue;
                Vector3 p = h.transform.position;
                if (tracks.TryGetValue(h, out var tr) && frame - tr.Frame <= 2)
                {
                    Vector3 v = (p - tr.Pos) / dt;
                    // Ignore teleports (floating-origin rebase), smooth out interpolation jitter.
                    tr.Vel = v.sqrMagnitude > 600f * 600f ? Vector3.zero : Vector3.Lerp(tr.Vel, v, 1f - Mathf.Exp(-10f * dt));
                }
                else tr.Vel = Vector3.zero;
                tr.Pos = p;
                tr.Frame = frame;
                tracks[h] = tr;
            }
            if (tracks.Count > 64)
            {
                stale.Clear();
                foreach (var kv in tracks) if (kv.Key == null || frame - kv.Value.Frame > 30) stale.Add(kv.Key);
                foreach (var k in stale) tracks.Remove(k);
            }
        }
    }
}
