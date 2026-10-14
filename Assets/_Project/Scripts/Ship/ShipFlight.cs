using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Voidward.Combat;
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
        [Tooltip("Impacts below this relative speed (m/s) are harmless (landing, scraping).")]
        public float ImpactSafeSpeed = 22f;
        [Tooltip("Hull damage per m/s of impact speed above the safe speed.")]
        public float ImpactDamagePerMs = 1.1f;

        Rigidbody rb;
        ShipCore core;

        // pilot input (set every frame by the pilot seat on the owning client)
        Vector3 linearIn;
        Vector3 angularIn;   // pitch, yaw, roll  (-1..1, mouse may exceed)
        bool boost, brake;
        float inputAge = 99f;
        float nextImpact;

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

            if (WarpBubble.Flying) { WarpStep(piloted && !core.Derelict.Value, s, dt); return; }

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

        /// <summary>Authority only (the dynamic body): crashes hurt the hull and whatever was rammed.</summary>
        void OnCollisionEnter(Collision c)
        {
            if (!IsSpawned || rb.isKinematic || Time.time < nextImpact) return;
            var other = c.collider.GetComponentInParent<IDamageable>();
            if (other != null && other.Team == Team.Players) return; // crew, other ships: no friendly ramming
            float v = c.relativeVelocity.magnitude;
            if (v < ImpactSafeSpeed) return;
            nextImpact = Time.time + 0.5f;
            float dmg = (v - ImpactSafeSpeed) * ImpactDamagePerMs;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            Vector3 dir = -c.relativeVelocity.normalized;
            if (core != null && core.Hull != null && core.Hull.IsSpawned)
                core.Hull.TakeDamageServerRpc(new DamageInfo { Amount = dmg, Type = DamageType.Kinetic, Point = point, Direction = dir });
            if (other != null && other.IsAlive)
                HealthComponent.DamageHit(c.collider, point, dmg * 2f, DamageType.Kinetic, dir, NetworkManager.LocalClientId, true);
            Vfx.Explosion(point, Mathf.Clamp(dmg * 0.08f, 1.5f, 6f), new Color(1f, 0.7f, 0.4f));
            Sfx.Play(dmg > 30f ? "explosion_big" : "explosion", point, 1f, Random.Range(0.85f, 1.05f));
            if (inputAge < 0.25f) Vfx.Shake(Mathf.Clamp(dmg / 50f, 0.25f, 1f)); // only the pilot feels it here
        }

        // ------------------------------------------------------------ warp bubble (#79)

        /// <summary>The nose of the lead ship stays within this cone around the direction of travel.</summary>
        const float WarpNoseDeg = 35f;

        /// <summary>This peer simulates the ship (its pilot's client, or the server while unpiloted).</summary>
        public bool IsSimulating => IsSpawned && rb != null && !rb.isKinematic;

        float radius = -1f;
        /// <summary>Rough hull radius for corridor impacts (from the solid colliders).</summary>
        public float Radius
        {
            get
            {
                if (radius > 0f) return radius;
                var b = new Bounds(transform.position, Vector3.zero);
                foreach (var c in GetComponentsInChildren<Collider>())
                    if (!c.isTrigger && c.enabled) b.Encapsulate(c.bounds);
                radius = Mathf.Clamp(b.extents.magnitude * 0.55f, 3f, 40f);
                return radius;
            }
        }

        ShipCore warpLead;

        /// <summary>
        /// Flight inside the warp bubble: the corridor flows past, so the ship holds x near 0 and only moves sideways.
        /// The lead ship's pilot steers with the nose (and strafes); every other ship holds its formation slot behind
        /// the lead, its pilot can still turn it to aim. Throttle is the bubble's, not the engines'.
        /// </summary>
        void WarpStep(bool piloted, ShipStats s, float dt)
        {
            core.ReportThrottle(0f);
            Vector3 axis = WarpBubble.Axis;
            Vector3 pos = rb.position;
            bool lead = core.NetworkObjectId == WarpBubble.LeadShip;

            if (piloted)
            {
                Vector3 targetLocalAng = new Vector3(angularIn.x * s.Turn, angularIn.y * s.Turn, -angularIn.z * s.Roll) * Mathf.Deg2Rad;
                Vector3 curLocal = transform.InverseTransformDirection(rb.angularVelocity);
                rb.angularVelocity = transform.TransformDirection(Vector3.Lerp(curLocal, targetLocalAng, 1f - Mathf.Exp(-8f * dt)));
            }
            else rb.angularVelocity *= 1f - Mathf.Clamp01(3f * dt);

            Vector3 target;
            if (lead)
            {
                float lateralSpeed = Mathf.Max(60f, s.MaxSpeed * 1.35f);
                Vector3 v = Vector3.zero;
                if (piloted)
                {
                    Vector3 nose = Vector3.ProjectOnPlane(transform.forward, axis) / Mathf.Sin(WarpNoseDeg * Mathf.Deg2Rad);
                    Vector3 strafe = Vector3.ProjectOnPlane(transform.right * linearIn.x + transform.up * linearIn.y, axis);
                    v = Vector3.ClampMagnitude(nose + strafe, 1f) * lateralSpeed;
                }
                if (WarpBubble.Pull != null) v += WarpBubble.Pull(pos);
                // Soft corridor walls.
                Vector3 lat = Vector3.ProjectOnPlane(pos, axis);
                float over = lat.magnitude - WarpBubble.HalfWidth;
                if (over > 0f) v -= lat.normalized * over * 3f;
                v -= axis * Vector3.Dot(pos, axis) * 1.5f;
                target = v;
            }
            else
            {
                if (warpLead == null || !warpLead.IsSpawned || warpLead.NetworkObjectId != WarpBubble.LeadShip) warpLead = FindLead();
                Vector3 anchor = warpLead != null ? warpLead.transform.position : Vector3.zero;
                Vector3 goal = anchor + WarpBubble.FormationOffset(FormationSlot(), core.Class == ShipClass.Ark);
                target = Vector3.ClampMagnitude((goal - pos) * 2.5f, 260f);
            }
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, target, 1f - Mathf.Exp(-5f * dt));

            // Nose: the lead stays within its cone; unpiloted ships turn back to the direction of travel.
            float ang = Vector3.Angle(transform.forward, axis);
            if (!piloted) rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(axis, Vector3.ProjectOnPlane(transform.up, axis).normalized + Vector3.up * 0.01f), 1.5f * dt));
            else if (lead && ang > WarpNoseDeg)
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, Quaternion.LookRotation(axis, transform.up), ang - WarpNoseDeg));
        }

        ShipCore FindLead()
        {
            foreach (var o in ShipCore.All) if (o != null && o.IsSpawned && o.NetworkObjectId == WarpBubble.LeadShip) return o;
            return null;
        }

        /// <summary>Formation slot 1.. (ships other than the lead, ordered by network id, as WarpFlight places them).</summary>
        int FormationSlot()
        {
            int slot = 1;
            foreach (var o in ShipCore.All)
                if (o != null && o != core && o.NetworkObjectId != WarpBubble.LeadShip && o.NetworkObjectId < core.NetworkObjectId) slot++;
            return slot;
        }

        void LevelTo(Vector3 up, float strength, float dt)
        {
            Quaternion target = Quaternion.FromToRotation(transform.up, up) * rb.rotation;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, strength * dt));
        }
    }
}
