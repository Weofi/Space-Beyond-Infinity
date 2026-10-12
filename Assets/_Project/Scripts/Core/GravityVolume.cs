using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Trigger volume that overrides gravity while inside (ship interiors, station pads): constant
    /// local-down gravity relative to its transform. Higher priority than planetary sources.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GravityVolume : MonoBehaviour
    {
        [Tooltip("Local-space gravity direction (usually down) and magnitude.")]
        public Vector3 LocalGravity = new Vector3(0f, -9.81f, 0f);

        static readonly List<GravityVolume> Active = new List<GravityVolume>();
        Collider col;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        void Awake() => col = GetComponent<Collider>();
        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public Vector3 WorldGravity => transform.TransformDirection(LocalGravity.normalized) * LocalGravity.magnitude;

        /// <summary>Returns the volume containing pos, or null.</summary>
        public static GravityVolume At(Vector3 pos)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var v = Active[i];
                var c = v.col;
                if (c != null && c.enabled && c.bounds.Contains(pos) && (c.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f)
                    return v;
            }
            return null;
        }
    }
}
