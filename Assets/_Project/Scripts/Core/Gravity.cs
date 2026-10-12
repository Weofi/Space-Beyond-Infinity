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

    /// <summary>
    /// Trigger volume that overrides gravity direction while inside (ship interiors: constant local-down
    /// gravity relative to the ship). Higher priority than planetary sources.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GravityVolume : MonoBehaviour
    {
        [Tooltip("Local-space gravity direction (usually down) and magnitude.")]
        public Vector3 LocalGravity = new Vector3(0f, -9.81f, 0f);

        static readonly List<GravityVolume> Active = new List<GravityVolume>();

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public Vector3 WorldGravity => transform.TransformDirection(LocalGravity.normalized) * LocalGravity.magnitude;

        /// <summary>Returns the volume containing pos, or null.</summary>
        public static GravityVolume At(Vector3 pos)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var v = Active[i];
                var col = v.GetComponent<Collider>();
                if (col != null && col.bounds.Contains(pos))
                {
                    // bounds check is cheap; verify with closest point for accuracy
                    if ((col.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f)
                        return v;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies custom gravity (planetary sources or ship GravityVolumes) to a Rigidbody and
    /// optionally aligns the transform up-axis. Used by props, pickups and surface AI.
    /// The player pawn and ships have their own controllers that query the same statics.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GravityBody : MonoBehaviour
    {
        public bool AlignToGravity = true;
        public float AlignSpeed = 6f;
        public float GravityScale = 1f;
        [Tooltip("Extra downward stick force when close to a surface.")]
        public float StickForce = 0f;

        Rigidbody rb;

        public Vector3 CurrentGravity { get; private set; }
        public GravitySource CurrentSource { get; private set; }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
        }

        void FixedUpdate()
        {
            Vector3 g;
            var vol = GravityVolume.At(transform.position);
            if (vol != null)
            {
                g = vol.WorldGravity;
                CurrentSource = null;
            }
            else
            {
                CurrentSource = GravitySource.Dominant(transform.position);
                g = CurrentSource != null ? CurrentSource.GravityAt(transform.position) : Vector3.zero;
            }

            CurrentGravity = g;
            rb.AddForce(g * GravityScale, ForceMode.Acceleration);

            if (AlignToGravity && g.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.FromToRotation(transform.up, -g.normalized) * transform.rotation;
                transform.rotation = Quaternion.Slerp(transform.rotation, target, AlignSpeed * Time.fixedDeltaTime);
            }
        }
    }
}
