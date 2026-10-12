using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Marks a physics body that needs planet terrain colliders around it (pawns, ships).
    /// Planets stream collider tiles only near anchors, so large worlds stay cheap on every peer.
    /// </summary>
    public class TerrainAnchor : MonoBehaviour
    {
        [Tooltip("Collider radius around the anchor (meters). Velocity look-ahead is added on top.")]
        public float Radius = 120f;

        public static readonly List<TerrainAnchor> All = new List<TerrainAnchor>();

        Rigidbody rb;

        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;

        void Awake() => rb = GetComponent<Rigidbody>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
