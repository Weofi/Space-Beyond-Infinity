using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// A spherical gravity source (planet). Objects near it are pulled toward the center and
    /// align their up-axis to the surface normal. Register/unregister happens automatically.
    /// </summary>
    public class GravitySource : MonoBehaviour
    {
        [Tooltip("Radius of the planet surface in meters.")]
        public float SurfaceRadius = 200f;
        [Tooltip("Gravity acceleration at the surface (m/s^2).")]
        public float SurfaceGravity = 9.81f;
        [Tooltip("Distance from center where gravity fades to zero.")]
        public float InfluenceRadius = 600f;

        public static readonly List<GravitySource> All = new List<GravitySource>();

        public Vector3 Center => transform.position;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Gravity acceleration at a world position (points toward center, fades with distance).</summary>
        public Vector3 GravityAt(Vector3 pos)
        {
            Vector3 toCenter = Center - pos;
            float dist = toCenter.magnitude;
            if (dist >= InfluenceRadius || dist < 0.001f) return Vector3.zero;
            float falloff = Mathf.Clamp01((InfluenceRadius - dist) / Mathf.Max(1f, InfluenceRadius - SurfaceRadius));
            falloff = falloff * falloff * (3f - 2f * falloff);
            return toCenter / dist * (SurfaceGravity * falloff);
        }

        public Vector3 UpAt(Vector3 pos) => (pos - Center).normalized;
        public float SurfaceDistance(Vector3 pos) => Vector3.Distance(pos, Center) - SurfaceRadius;

        /// <summary>Strongest source affecting a position, or null in deep space.</summary>
        public static GravitySource Dominant(Vector3 pos)
        {
            GravitySource best = null;
            float bestMag = 0f;
            for (int i = 0; i < All.Count; i++)
            {
                float m = All[i].GravityAt(pos).magnitude;
                if (m > bestMag) { bestMag = m; best = All[i]; }
            }
            return best;
        }

        public static Vector3 GravityAtPoint(Vector3 pos)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < All.Count; i++) sum += All[i].GravityAt(pos);
            return sum;
        }
    }
}
