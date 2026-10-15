using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Cosmetic, client-side rotation of a model part (Warden sentinel halo, boss rings, #98). Local space, unsynced:
    /// every peer spins it on its own, the start angle is seeded from the position so a swarm does not spin in step.
    /// Optional wobble tilts the spin axis a little over time.
    /// </summary>
    public class SpinPart : MonoBehaviour
    {
        [Tooltip("Local spin axis.")]
        public Vector3 Axis = Vector3.forward;
        [Tooltip("Degrees per second.")]
        public float Speed = 90f;
        [Tooltip("Axis wobble amplitude in degrees (0 = none).")]
        public float Wobble;

        Quaternion rest;
        float angle;

        void Awake()
        {
            rest = transform.localRotation;
            Vector3 p = transform.position;
            angle = Mathf.Repeat(p.x * 13.1f + p.y * 7.7f + p.z * 3.3f, 360f);
        }

        void Update()
        {
            angle = Mathf.Repeat(angle + Speed * Time.deltaTime, 360f);
            var tilt = Wobble > 0f ? Quaternion.Euler(Mathf.Sin(Time.time * 0.7f + angle * 0.01f) * Wobble, 0f, Mathf.Cos(Time.time * 0.5f) * Wobble) : Quaternion.identity;
            transform.localRotation = rest * tilt * Quaternion.AngleAxis(angle, Axis);
        }
    }
}
