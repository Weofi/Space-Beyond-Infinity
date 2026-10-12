using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Voidward.Core;
using Voidward.Combat;

namespace Voidward.Player
{
    /// <summary>
    /// Networked first-person player character. Owner-client authoritative movement with
    /// custom gravity (planetary <see cref="GravitySource"/> or ship-interior
    /// <see cref="GravityVolume"/>), jetpack, interaction, tool use, and ship seating.
    /// <see cref="NetworkTransform"/> syncs position/rotation to other clients.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerPawn : NetworkBehaviour, IInteractAgent
    {
        // ---- Tunable constants ----
        const float WalkAccel = 40f;
        const float GroundMaxSpeed = 6f;
        const float SprintMaxSpeed = 9f;
        const float JumpImpulse = 5.5f;
        const float JetpackThrust = 12f;
        const float SuitEnergyMax = 100f;
        const float JetpackDrainRate = 18f;   // per second
        const float SuitRegenRate = 20f;      // per second when grounded
        const float AirControlMult = 0.4f;
        const float AlignSpeed = 8f;          // slerp per second
        const float GroundCheckRadius = 0.35f;
        const float GroundCheckDist = 0.5f;
        const float PitchLimit = 89f;
        const float JumpBufferTime = 0.15f;

        // ---- Inspector ----
        [Header("Camera")]
        [Tooltip("Child transform at eye height holding the first-person Camera + AudioListener.")]
        [SerializeField] Transform cameraPoint;

        [Header("Body")]
        [Tooltip("Child GameObject with the visible body mesh(es). Hidden for the local player.")]
        [SerializeField] Transform body;

        // ---- Public API ----
        /// <summary>The first-person camera anchor (eye position, pitch applied here).</summary>
        public Transform CameraPoint => cameraPoint;

        /// <summary>Seat the pawn currently occupies, or null when on foot.</summary>
        public ISeat CurrentSeat { get; private set; }

        /// <summary>True when gameplay input is enabled (false while dead).</summary>
        public bool InputEnabled => inputEnabled;

        /// <summary>The local player's pawn, set on spawn / cleared on despawn. Null on non-owners.</summary>
        public static PlayerPawn Local { get; private set; }

        /// <summary>A ray from the camera position along the camera forward (for interaction, tools, pings).</summary>
        public Ray ViewRay => cameraPoint != null
            ? new Ray(cameraPoint.position, cameraPoint.forward)
            : new Ray(transform.position, transform.forward);

        // IInteractAgent
        /// <inheritdoc/>
        public GameObject GameObject => gameObject;
        /// <inheritdoc/>
        public Vector3 Position => transform.position;
        // OwnerClientId is inherited from NetworkBehaviour.

        // ---- Private state ----
        Rigidbody rb;
        CapsuleCollider capsuleCollider;
        HealthComponent health;

        bool inputEnabled = true;
        bool grounded;
        float suitEnergy = SuitEnergyMax;
        float pitchAngle;
        float jumpBufferTimer;

        // Saved state for seat attach/detach
        Transform originalParent;
        Transform originalCamParent;
        Vector3 originalCamLocalPos;
        Quaternion originalCamLocalRot;

        // ---- Lifecycle ----

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsuleCollider = GetComponent<CapsuleCollider>();
            health = GetComponent<HealthComponent>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                Local = this;
                inputEnabled = true;
                pitchAngle = 0f;

                // Enable camera + listener for owner only.
                if (cameraPoint != null)
                {
                    var cam = cameraPoint.GetComponent<Camera>();
                    if (cam != null) cam.enabled = true;
                    var listener = cameraPoint.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = true;
                    cameraPoint.gameObject.tag = "MainCamera";
                }

                // Hide own body so it doesn't clip into the first-person camera.
                if (body != null)
                {
                    foreach (var r in body.GetComponentsInChildren<Renderer>())
                        r.enabled = false;
                }
            }
            else
            {
                // Non-owner: camera stays disabled, body stays visible.
                if (cameraPoint != null)
                {
                    var cam = cameraPoint.GetComponent<Camera>();
                    if (cam != null) cam.enabled = false;
                    var listener = cameraPoint.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = false;
                }
                // Non-owner Rigidbody should not fight the NetworkTransform.
                if (rb != null) rb.isKinematic = true;
            }

            // Server tracks death.
            if (IsServer && health != null)
                health.Died += OnServerDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && health != null)
                health.Died -= OnServerDied;
            if (IsOwner && Local == this)
                Local = null;
        }

        // ---- Per-frame (owner only) ----

        void Update()
        {
            if (!IsOwner || !inputEnabled) return;

            HandleSeats();
            if (CurrentSeat != null) return;

            HandleLook();

            // Buffer jump input so FixedUpdate never misses a press.
            if (VwInput.Pressed(VwInput.Jump))
                jumpBufferTimer = JumpBufferTime;
            if (jumpBufferTimer > 0f)
                jumpBufferTimer -= Time.deltaTime;
        }

        void FixedUpdate()
        {
            if (!IsOwner || !inputEnabled || CurrentSeat != null) return;
            HandleMovement();
        }

        // ---- Movement (FixedUpdate, owner only) ----

        void HandleMovement()
        {
            Vector3 gravity = ComputeGravity(transform.position);
            rb.AddForce(gravity, ForceMode.Acceleration);

            grounded = CheckGround();

            Vector2 moveInput = VwInput.Move != null ? VwInput.Move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 inputDir = transform.forward * moveInput.y + transform.right * moveInput.x;
            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

            float maxSpeed = VwInput.Held(VwInput.Sprint) ? SprintMaxSpeed : GroundMaxSpeed;

            if (grounded)
            {
                // Directly approach target horizontal velocity, preserving vertical component.
                Vector3 targetVel = inputDir * maxSpeed;
                Vector3 currentVel = rb.linearVelocity;
                Vector3 vertical = Vector3.Project(currentVel, transform.up);
                Vector3 horizontal = currentVel - vertical;
                horizontal = Vector3.MoveTowards(horizontal, targetVel, WalkAccel * Time.fixedDeltaTime);
                rb.linearVelocity = horizontal + vertical;
            }
            else
            {
                // Air control: reduced acceleration.
                if (inputDir.sqrMagnitude > 0.001f)
                    rb.AddForce(inputDir * (WalkAccel * AirControlMult), ForceMode.Acceleration);
            }

            // Jump (consume buffer).
            if (jumpBufferTimer > 0f && grounded)
            {
                rb.AddForce(transform.up * JumpImpulse, ForceMode.Impulse);
                jumpBufferTimer = 0f;
            }

            // Jetpack: airborne + Vertical > 0 (Space held).
            float verticalInput = VwInput.Vertical != null ? VwInput.Vertical.ReadValue<float>() : 0f;
            if (!grounded && verticalInput > 0f && suitEnergy > 0f)
            {
                rb.AddForce(transform.up * JetpackThrust, ForceMode.Acceleration);
                suitEnergy = Mathf.Max(0f, suitEnergy - JetpackDrainRate * Time.fixedDeltaTime);
            }
            else if (grounded)
            {
                suitEnergy = Mathf.Min(SuitEnergyMax, suitEnergy + SuitRegenRate * Time.fixedDeltaTime);
            }

            // Align up-axis toward -gravity (perpendicular to gravity direction).
            if (gravity.sqrMagnitude > 0.01f)
            {
                Vector3 desiredUp = -gravity.normalized;
                Quaternion targetRot = Quaternion.FromToRotation(transform.up, desiredUp) * transform.rotation;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, AlignSpeed * Time.fixedDeltaTime);
            }
        }

        /// <summary>Gravity at a world position: GravityVolume first (ship interiors), else GravitySource sum.</summary>
        Vector3 ComputeGravity(Vector3 pos)
        {
            var vol = GravityVolume.At(pos);
            if (vol != null) return vol.WorldGravity;
            return GravitySource.GravityAtPoint(pos);
        }

        /// <summary>SphereCast downward from just inside the capsule to detect ground.</summary>
        bool CheckGround()
        {
            Vector3 origin = transform.position + transform.up * GroundCheckRadius;
            if (Physics.SphereCast(origin, GroundCheckRadius, -transform.up, out var hit,
                GroundCheckDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.collider != capsuleCollider;
            }
            return false;
        }

        // ---- Look (Update, owner only) ----

        void HandleLook()
        {
            Vector2 look = VwInput.LookDelta;

            // Yaw around current up.
            if (Mathf.Abs(look.x) > 0.0001f)
                transform.rotation = Quaternion.AngleAxis(look.x, transform.up) * transform.rotation;

            // Pitch on camera child (local X), clamped.
            pitchAngle = Mathf.Clamp(pitchAngle - look.y, -PitchLimit, PitchLimit);
            if (cameraPoint != null)
                cameraPoint.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        }

        // ---- Seats ----

        void HandleSeats()
        {
            ISeat pending = SeatLink.PendingSeat;
            if (pending != null && pending != CurrentSeat)
                AttachToSeat(pending);
            else if (pending == null && CurrentSeat != null)
                DetachFromSeat();
        }

        /// <summary>
        /// Occupy a ship seat: disable movement/collider, parent to the seat, move camera to the
        /// seat's camera anchor. Uses <see cref="ISeat.GameObject"/> as the anchor and looks for a
        /// child named "CameraPoint" for the camera position (ISeat lacks explicit Anchor/CameraPoint
        /// properties — see report).
        /// </summary>
        public void AttachToSeat(ISeat seat)
        {
            if (seat == null || seat.GameObject == null) return;
            CurrentSeat = seat;

            if (capsuleCollider != null) capsuleCollider.enabled = false;
            if (rb != null) rb.isKinematic = true;

            // Save state for restoration.
            originalParent = transform.parent;
            if (cameraPoint != null)
            {
                originalCamParent = cameraPoint.parent;
                originalCamLocalPos = cameraPoint.localPosition;
                originalCamLocalRot = cameraPoint.localRotation;
            }

            Transform anchor = seat.GameObject.transform;
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Move camera child to the seat's camera point (convention: child named "CameraPoint").
            if (cameraPoint != null)
            {
                Transform seatCam = anchor.Find("CameraPoint");
                Transform camTarget = seatCam != null ? seatCam : anchor;
                cameraPoint.SetParent(camTarget, false);
                cameraPoint.localPosition = Vector3.zero;
                cameraPoint.localRotation = Quaternion.identity;
            }

            EventBus.RaiseLocalPlayerBoardedShip();
        }

        /// <summary>Vacate the current seat: restore movement, collider, camera, and parenting.</summary>
        public void DetachFromSeat()
        {
            if (CurrentSeat == null) return;
            CurrentSeat = null;

            if (capsuleCollider != null) capsuleCollider.enabled = true;
            if (rb != null) rb.isKinematic = false;

            // Restore camera to its original parent / position / pitch.
            if (cameraPoint != null)
            {
                Transform parent = originalCamParent != null ? originalCamParent : transform;
                cameraPoint.SetParent(parent, false);
                cameraPoint.localPosition = originalCamLocalPos;
                cameraPoint.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
            }

            transform.SetParent(originalParent);

            EventBus.RaiseLocalPlayerLeftShip();
        }

        // ---- Death / Respawn ----

        void OnServerDied(DamageInfo info)
        {
            InputEnabledClientRpc(false);
        }

        /// <summary>Server-only: instantly move the pawn and snap all clients (no interpolation).</summary>
        public void ServerTeleport(Vector3 pos, Quaternion rot)
        {
            if (!IsServer) return;
            TeleportImmediate(pos, rot);
            TeleportClientRpc(pos, rot);
        }

        /// <summary>Server-only: restore health, teleport, and re-enable input for the owner.</summary>
        public void RespawnServer(Vector3 pos)
        {
            if (!IsServer) return;
            if (health != null) health.Hp.Value = health.MaxHealth;

            // Align up to gravity at the respawn position so the player doesn't start sideways.
            Vector3 gravity = ComputeGravity(pos);
            Vector3 up = gravity.sqrMagnitude > 0.01f ? -gravity.normalized : Vector3.up;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, up);

            ServerTeleport(pos, rot);
            InputEnabledClientRpc(true);
        }

        [ClientRpc]
        void TeleportClientRpc(Vector3 pos, Quaternion rot)
        {
            TeleportImmediate(pos, rot);
        }

        [ClientRpc]
        void InputEnabledClientRpc(bool enabled)
        {
            inputEnabled = enabled;
        }

        void TeleportImmediate(Vector3 pos, Quaternion rot)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            transform.position = pos;
            transform.rotation = rot;
        }
    }
}