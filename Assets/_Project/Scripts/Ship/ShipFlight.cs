using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>
    /// 6-DOF arcade flight. Simulated only by the transform authority (the pilot's client while
    /// piloted — instant, prediction-free response — otherwise the server). NetworkTransform
    /// (owner authority) interpolates the result on everyone else.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ShipFlight : NetworkBehaviour
    {
        public float Mass = 2000f;
        [Tooltip("Altitude under which landing assist levels the ship to the surface.")]
        public float LandingAssistAltitude = 45f;

        Rigidbody rb;
        ShipCore core;

        // pilot input (set every frame by the pilot seat on the owning client)
        Vector3 linearIn;
        Vector3 angularIn;   // pitch, yaw, roll  (-1..1, mouse may exceed)
        bool boost, brake;
        float inputAge = 99f;

        public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public bool Boosting => boost && inputAge < 0.2f;
        public float Altitude { get; private set; } = 9999f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            core = GetComponent<ShipCore>();
            rb.useGravity = false;
            rb.mass = Mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public override void OnNetworkSpawn() => UpdateAuthority();
        public override void OnGainedOwnership() => UpdateAuthority();
        public override void OnLostOwnership() => UpdateAuthority();

        void UpdateAuthority()
        {
            var nt = GetComponent<NetworkTransform>();
            bool auth = nt != null && nt.AuthorityMode == NetworkTransform.AuthorityModes.Owner ? IsOwner : IsServer;
            rb.isKinematic = !auth;
            if (core != null && core.Def != null)
            {
                rb.linearDamping = core.Def.LinearDamping;
                rb.angularDamping = core.Def.AngularDamping;
            }
            linearIn = angularIn = Vector3.zero;
        }

        /// <summary>Pilot client: per-frame input.</summary>
        public void SetPilotInput(Vector3 linear, Vector3 angular, bool boosting, bool braking)
        {
            linearIn = Vector3.ClampMagnitude(linear, 1f);
            angularIn = new Vector3(Mathf.Clamp(angular.x, -1.5f, 1.5f), Mathf.Clamp(angular.y, -1.5f, 1.5f), Mathf.Clamp(angular.z, -1f, 1f));
            boost = boosting;
            brake = braking;
            inputAge = 0f;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || rb.isKinematic || core == null) return;
            float dt = Time.fixedDeltaTime;
            inputAge += dt;
            bool piloted = inputAge < 0.25f;
            var s = core.Stats;

            Vector3 g = GravitySource.GravityAtPoint(rb.position);
            rb.AddForce(g, ForceMode.Acceleration);

            var src = GravitySource.Dominant(rb.position);
            Altitude = src != null ? src.SurfaceDistance(rb.position) : 9999f;

            if (piloted && !core.Derelict.Value)
            {
                bool hasFuel = core.Fuel.Value > 0.01f || CheatManager.GodMode;
                float fuelFactor = hasFuel ? 1f : 0.2f;
                float thrust = s.Thrust * fuelFactor * (boost && hasFuel ? s.Boost : 1f);
                Vector3 f = new Vector3(linearIn.x * 0.7f, linearIn.y * 0.8f, linearIn.z) * thrust;
                rb.AddRelativeForce(f, ForceMode.Acceleration);
                core.ReportThrottle(linearIn.magnitude * (boost ? 1.8f : 1f));

                Vector3 targetLocalAng = new Vector3(angularIn.x * s.Turn, angularIn.y * s.Turn, -angularIn.z * s.Roll) * Mathf.Deg2Rad;
                Vector3 curLocal = transform.InverseTransformDirection(rb.angularVelocity);
                Vector3 newLocal = Vector3.Lerp(curLocal, targetLocalAng, 1f - Mathf.Exp(-8f * dt));
                rb.angularVelocity = transform.TransformDirection(newLocal);

                if (brake) rb.linearVelocity *= 1f - Mathf.Clamp01(2.2f * dt);

                // Landing assist: near a surface with little rotation input, level to the surface normal.
                if (src != null && Altitude < LandingAssistAltitude && Mathf.Abs(angularIn.x) + Mathf.Abs(angularIn.z) < 0.1f)
                    LevelTo(src.UpAt(rb.position), 2.5f, dt);

                float cap = s.MaxSpeed * (boost ? 1.6f : 1f);
                float spd = rb.linearVelocity.magnitude;
                if (spd > cap) rb.linearVelocity *= Mathf.Lerp(1f, cap / spd, 4f * dt);
            }
            else
            {
                core.ReportThrottle(0f);
                // Unpiloted: drift to a stop in space, settle on planets.
                if (g.sqrMagnitude < 0.01f)
                {
                    rb.linearVelocity *= 1f - Mathf.Clamp01(0.6f * dt);
                    rb.angularVelocity *= 1f - Mathf.Clamp01(1.5f * dt);
                }
                else if (src != null && Altitude < LandingAssistAltitude)
                    LevelTo(src.UpAt(rb.position), 1.5f, dt);
            }
        }

        void LevelTo(Vector3 up, float strength, float dt)
        {
            Quaternion target = Quaternion.FromToRotation(transform.up, up) * rb.rotation;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, strength * dt));
        }
    }
}
