using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Planet;
using Voidward.Ship;

namespace Voidward.Player
{
    /// <summary>
    /// Networked first-person astronaut. Owner-authoritative movement (instant local response,
    /// NetworkTransform interpolation for others) with spherical planet gravity, ship-interior
    /// gravity volumes (incl. moving-platform carry), zero-g EVA jetpack, seats, tools,
    /// interaction and pings. Oxygen, damage and death are server-authoritative.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [DefaultExecutionOrder(-50)]
    public class PlayerPawn : NetworkBehaviour, IInteractAgent
    {
        const float WalkAccel = 45f;
        const float GroundMaxSpeed = 6f;
        const float SprintMaxSpeed = 9.5f;
        const float JumpImpulse = 5.5f;
        const float AirControl = 0.45f;
        const float AlignSpeed = 8f;
        const float GroundCheckRadius = 0.3f;
        const float GroundCheckDist = 0.35f;
        const float PitchLimit = 88f;
        const float InteractRange = 4.2f;
        const float EvaThrust = 7f;

        public enum Tool { Drill = 0, Blaster = 1, Rifle = 2, Medkit = 3 }

        [Header("Rig")]
        [SerializeField] Transform cameraPoint;
        [SerializeField] Transform body;
        [SerializeField] Transform[] toolModels = new Transform[4];
        [SerializeField] Transform muzzle;
        [SerializeField] LineRenderer beam;

        // ---- Networked state ----
        public NetworkVariable<bool> Active = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> IsDown = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> IsLost = new NetworkVariable<bool>(false);
        public NetworkVariable<float> Oxygen = new NetworkVariable<float>(90f);
        public NetworkVariable<float> RespawnAt = new NetworkVariable<float>(0f);

        // ---- Public API ----
        public static PlayerPawn Local { get; private set; }
        public static readonly List<PlayerPawn> All = new List<PlayerPawn>();
        public Transform CameraPoint => cameraPoint;
        public ISeat CurrentSeat { get; private set; }
        public ShipSeat CurrentShipSeat => CurrentSeat as ShipSeat;
        public HealthComponent Health { get; private set; }
        public NetInventory Inventory { get; private set; }
        public PlayerGear Gear { get; private set; }
        public Tool CurrentTool { get; private set; } = Tool.Drill;
        public float SuitEnergy { get; private set; } = 100f;
        public bool ZeroG { get; private set; }
        public bool Grounded => grounded;
        public string Prompt { get; private set; } = "";
        public float ToolCooldown01 { get; private set; }
        public GameObject GameObject => gameObject;
        public Vector3 Position => transform.position;
        public Ray ViewRay => new Ray(cameraPoint.position, cameraPoint.forward);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); Local = null; }

        Rigidbody rb;
        CapsuleCollider capsule;
        NetworkTransform nt;
        CinemachineCamera vcam;
        bool grounded;
        float pitch;
        float jumpBuffer;
        float toolTimer;
        float mineTick;
        float shakeSeed;
        bool hitStopHeld;
        Vector3 hitStopPos;
        Quaternion hitStopRot;
        Vector3[] toolBasePos;
        Quaternion[] toolBaseRot;
        IInteractable focused;
        Transform platform;
        Vector3 platformLocalPos;
        Quaternion platformLastRot;
        bool lastBodyVisible = true;

        // server
        float o2DamageTimer;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            nt = GetComponent<NetworkTransform>();
            Health = GetComponent<HealthComponent>();
            Inventory = GetComponent<NetInventory>();
            Gear = GetComponent<PlayerGear>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (cameraPoint != null) vcam = cameraPoint.GetComponent<CinemachineCamera>();
            toolBasePos = new Vector3[toolModels.Length];
            toolBaseRot = new Quaternion[toolModels.Length];
            for (int i = 0; i < toolModels.Length; i++)
                if (toolModels[i]) { toolBasePos[i] = toolModels[i].localPosition; toolBaseRot[i] = toolModels[i].localRotation; }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner)
            {
                Local = this;
                if (body != null)
                    foreach (var r in body.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            else
            {
                rb.isKinematic = true;
                capsule.isTrigger = true; // remote pawns never shove local physics (ships, own pawn)
            }
            if (vcam != null) vcam.enabled = false;
            foreach (var t in toolModels) if (t) t.gameObject.SetActive(false);
            if (beam) beam.enabled = false;

            if (IsServer)
            {
                Health.Died += OnServerDied;
                Inventory.UseHandler = UseItemServer;
                NetInventory.NotifyClient = NotifyServer;
            }
            Active.OnValueChanged += (a, b) => RefreshActive();
            IsDown.OnValueChanged += (a, b) => RefreshActive();
            RefreshActive();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer && Health != null) Health.Died -= OnServerDied;
            if (Local == this)
            {
                Local = null;
                Sfx.Engine(false, 0f);
            }
        }

        void RefreshActive()
        {
            bool alive = Active.Value && !IsDown.Value;
            if (IsOwner)
            {
                rb.isKinematic = !alive || CurrentSeat != null;
                capsule.enabled = alive && CurrentSeat == null;
                if (vcam != null)
                {
                    vcam.enabled = Active.Value;
                    vcam.Priority = 30;
                }
                if (alive) SelectTool(CurrentTool);
                else { foreach (var t in toolModels) if (t) t.gameObject.SetActive(false); if (beam) beam.enabled = false; }
            }
            else capsule.enabled = alive && CurrentSeat == null;
            SetBodyVisible(alive && CurrentSeat == null);
        }

        void SetBodyVisible(bool v)
        {
            if (body == null || v == lastBodyVisible) return;
            lastBodyVisible = v;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        }

        // ================================================================ Update (all peers)

        void Update()
        {
            if (!IsSpawned) return;
            UpdateSeat();
            if (IsServer) ServerTick(Time.deltaTime);
            if (!IsOwner) return;

            bool alive = Active.Value && !IsDown.Value;
            bool input = alive && GameState.GameplayInputAllowed;

            if (CurrentSeat != null)
            {
                CurrentSeat.TickOccupant(Time.deltaTime);
                Prompt = "";
                if (input && VwInput.Pressed(VwInput.Interact)) CurrentSeat.RequestLeave();
                if (input && VwInput.Pressed(VwInput.Ping)) DoPing();
                return;
            }
            if (!alive) { Prompt = ""; if (beam) beam.enabled = false; return; }

            if (input) HandleLook();
            if (input && VwInput.Pressed(VwInput.Jump)) jumpBuffer = 0.15f;
            jumpBuffer -= Time.deltaTime;

            UpdateInteraction(input);
            UpdateTools(input);
            if (input && VwInput.Pressed(VwInput.Ping)) DoPing();

            // Loot magnet (#43): drops in range fly into the pawn (WorldPickup animates and requests the pickup).
            foreach (var p in WorldPickup.All)
            {
                if (p == null) continue;
                float r = p.MagnetRadius;
                if ((p.transform.position - transform.position).sqrMagnitude < r * r) p.Attract(transform);
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned) return;
            if (CurrentSeat != null)
            {
                var a = CurrentSeat.SeatAnchor;
                transform.SetPositionAndRotation(a.position, a.rotation);
                if (IsOwner && cameraPoint != null)
                    cameraPoint.SetPositionAndRotation(CurrentSeat.ViewPoint.position, CurrentSeat.ViewPoint.rotation);
            }
            if (IsOwner && cameraPoint != null) { ApplyShake(); ApplyRecoil(); ApplyHitStop(); }
            float fov = GameState.FieldOfView + GameState.FovPunch + WarpBubble.FovBoost;
            if (IsOwner && vcam != null && Mathf.Abs(vcam.Lens.FieldOfView - fov) > 0.05f)
            {
                var lens = vcam.Lens;
                lens.FieldOfView = fov;
                vcam.Lens = lens;
            }
        }

        // Local hit-stop (#43): the view holds its pose for a few frames. On foot the whole camera holds; in a
        // seat only the rotation, so a fast ship never leaves the camera behind. Purely visual, no timeScale.
        void ApplyHitStop()
        {
            if (!GameState.HitStopActive)
            {
                if (hitStopHeld && CurrentSeat == null)
                {
                    cameraPoint.localPosition = new Vector3(0f, 1.62f, 0f);
                    cameraPoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                }
                hitStopHeld = false;
                return;
            }
            if (!hitStopHeld) { hitStopHeld = true; hitStopPos = cameraPoint.position; hitStopRot = cameraPoint.rotation; }
            if (CurrentSeat == null) cameraPoint.SetPositionAndRotation(hitStopPos, hitStopRot);
            else cameraPoint.rotation = hitStopRot;
        }

        // Weapon kick (#43): the held tool snaps back and its muzzle climbs, the view nudges up; in a seat the camera
        // jolts back along the view. GameState.Recoil (already scaled by Screen shake) springs back to 0 on its own.
        void ApplyRecoil()
        {
            float r = GameState.Recoil;
            int cur = (int)CurrentTool;
            for (int i = 0; i < toolModels.Length; i++)
            {
                var t = toolModels[i];
                if (t == null || !t.gameObject.activeSelf) continue;
                float k = i == cur ? r : 0f;
                t.localPosition = toolBasePos[i] + new Vector3(0f, 0.012f, -0.09f) * k;
                t.localRotation = toolBaseRot[i] * Quaternion.Euler(-12f * k, 0f, 0f);
            }
            if (r <= 0.002f) return;
            if (CurrentSeat == null)
            {
                // on top of the look pitch (ApplyShake may already have set the rotation this frame)
                if (GameState.ShakeIntensity <= 0.001f) cameraPoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                cameraPoint.localRotation *= Quaternion.Euler(-1.2f * r, 0f, 0f);
            }
            else
            {
                cameraPoint.position -= cameraPoint.forward * (0.5f * r);
                cameraPoint.rotation *= Quaternion.Euler(-1.2f * r, 0f, 0f);
            }
        }

        void ApplyShake()
        {
            float s = GameState.ShakeIntensity;
            if (s <= 0.001f) return;
            shakeSeed += Time.deltaTime * 30f;
            float x = (Mathf.PerlinNoise(shakeSeed, 0f) - 0.5f) * 2f * s;
            float y = (Mathf.PerlinNoise(0f, shakeSeed) - 0.5f) * 2f * s;
            var shake = Quaternion.Euler(x * 2.5f, y * 2.5f, 0f);
            if (CurrentSeat == null) cameraPoint.localRotation = Quaternion.Euler(pitch, 0f, 0f) * shake;
            else cameraPoint.rotation *= shake;
        }

        // ================================================================ Seats

        void UpdateSeat()
        {
            var seat = ShipSeat.SeatOf(OwnerClientId);
            if (!Active.Value || IsDown.Value) seat = null;
            if ((ISeat)seat == CurrentSeat) return;
            if (CurrentSeat != null) Detach();
            if (seat != null) Attach(seat);
        }

        void Attach(ShipSeat seat)
        {
            CurrentSeat = seat;
            SetBodyVisible(false);
            capsule.enabled = false; // the ship's hull protects seated players on every peer
            if (IsOwner)
            {
                rb.isKinematic = true;
                foreach (var t in toolModels) if (t) t.gameObject.SetActive(false);
                if (beam) beam.enabled = false;
                EventBus.RaiseLocalPlayerBoardedShip();
                Sfx.PlayUI("seat");
                Debug.Log($"[Player] Seated at {seat.Role} of {(seat.Ship != null ? seat.Ship.ShipName.Value.ToString() : "?")}");
            }
        }

        void Detach()
        {
            var seat = CurrentShipSeat;
            CurrentSeat = null;
            SetBodyVisible(Active.Value && !IsDown.Value);
            capsule.enabled = Active.Value && !IsDown.Value;
            if (!IsOwner) return;
            Vector3 exit = seat != null ? seat.ExitPoint.position : transform.position;
            Quaternion rot = seat != null ? seat.ExitPoint.rotation : transform.rotation;
            rb.isKinematic = false;
            capsule.enabled = true;
            TeleportLocal(exit, rot);
            if (seat != null && seat.Ship != null && seat.Ship.Flight != null)
                rb.linearVelocity = seat.Ship.Flight.Velocity;
            pitch = 0f;
            cameraPoint.localPosition = new Vector3(0f, 1.62f, 0f);
            cameraPoint.localRotation = Quaternion.identity;
            SelectTool(CurrentTool);
            Sfx.Engine(false, 0f);
            EventBus.RaiseLocalPlayerLeftShip();
        }

        void TeleportLocal(Vector3 pos, Quaternion rot)
        {
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, rot, transform.localScale);
            platform = null;
        }

        // ================================================================ Movement (owner)

        void FixedUpdate()
        {
            if (!IsOwner || !IsSpawned || CurrentSeat != null || rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            bool input = GameState.GameplayInputAllowed;

            ApplyPlatformCarry();
            var vol = GravityVolume.At(rb.position);
            platform = vol != null ? vol.transform : null;
            if (platform != null) { platformLocalPos = platform.position; platformLastRot = platform.rotation; }
            Vector3 g = vol != null ? vol.WorldGravity : GravitySource.GravityAtPoint(rb.position);
            ZeroG = g.sqrMagnitude < 0.2f;

            Vector2 mv = input ? VwInput.Move.ReadValue<Vector2>() : Vector2.zero;
            float vert = input ? VwInput.Vertical.ReadValue<float>() : 0f;
            float maxEnergy = Gear != null ? Gear.JetpackEnergy : 100f;

            if (ZeroG)
            {
                // EVA: 6-DOF jetpack along view axes.
                Vector3 dir = cameraPoint.forward * mv.y + cameraPoint.right * mv.x + transform.up * vert;
                if (dir.sqrMagnitude > 0.01f && SuitEnergy > 0f)
                {
                    float boost = input && VwInput.Held(VwInput.Sprint) ? 1.8f : 1f;
                    rb.AddForce(Vector3.ClampMagnitude(dir, 1f) * EvaThrust * boost, ForceMode.Acceleration);
                    SuitEnergy = Mathf.Max(0f, SuitEnergy - 6f * boost * dt);
                }
                else SuitEnergy = Mathf.Min(maxEnergy, SuitEnergy + 4f * dt);
                if (input && VwInput.Held(VwInput.Brake)) rb.linearVelocity *= 1f - Mathf.Clamp01(3f * dt);
                rb.linearVelocity *= 1f - Mathf.Clamp01(0.15f * dt);
                float roll = input ? VwInput.Roll.ReadValue<float>() : 0f;
                if (Mathf.Abs(roll) > 0.01f) rb.MoveRotation(Quaternion.AngleAxis(-roll * 90f * dt, transform.forward) * rb.rotation);
                grounded = false;
                return;
            }

            rb.AddForce(g, ForceMode.Acceleration);
            grounded = CheckGround();
            Vector3 up = -g.normalized;

            Vector3 inputDir = transform.forward * mv.y + transform.right * mv.x;
            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();
            float maxSpeed = input && VwInput.Held(VwInput.Sprint) ? SprintMaxSpeed : GroundMaxSpeed;

            if (grounded)
            {
                Vector3 vel = rb.linearVelocity;
                Vector3 vertical = Vector3.Project(vel, up);
                Vector3 horizontal = Vector3.MoveTowards(vel - vertical, inputDir * maxSpeed, WalkAccel * dt);
                rb.linearVelocity = horizontal + vertical;
            }
            else if (inputDir.sqrMagnitude > 0.001f)
                rb.AddForce(inputDir * (WalkAccel * AirControl), ForceMode.Acceleration);

            if (jumpBuffer > 0f && grounded)
            {
                rb.AddForce(up * JumpImpulse, ForceMode.VelocityChange);
                jumpBuffer = 0f;
            }

            float thrust = Gear != null ? Gear.JetpackThrust : 11f;
            if (!grounded && vert > 0f && SuitEnergy > 0f)
            {
                rb.AddForce(up * thrust, ForceMode.Acceleration);
                SuitEnergy = Mathf.Max(0f, SuitEnergy - 20f * dt);
            }
            else if (grounded) SuitEnergy = Mathf.Min(maxEnergy, SuitEnergy + 30f * dt);

            Quaternion target = Quaternion.FromToRotation(transform.up, up) * rb.rotation;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, AlignSpeed * dt));
        }

        /// <summary>Move with the ship interior we stand in (moving platform).</summary>
        void ApplyPlatformCarry()
        {
            Transform p = platform;
            if (p != null)
            {
                // Re-express our position in the platform's previous frame, then map it to the current one.
                Vector3 local = Quaternion.Inverse(platformLastRot) * (rb.position - platformLocalPos);
                Quaternion dRot = p.rotation * Quaternion.Inverse(platformLastRot);
                rb.position = p.position + p.rotation * local;
                rb.rotation = dRot * rb.rotation;
                rb.linearVelocity = dRot * rb.linearVelocity;
            }
        }

        bool CheckGround()
        {
            Vector3 origin = rb.position + transform.up * (GroundCheckRadius + 0.05f);
            return Physics.SphereCast(origin, GroundCheckRadius, -transform.up, out _, GroundCheckDist,
                ~0, QueryTriggerInteraction.Ignore);
        }

        void HandleLook()
        {
            Vector2 look = VwInput.LookDelta;
            if (ZeroG)
            {
                // full-body rotation in zero-g; camera pitch eases back to body forward
                SetRotationNow(rb.rotation * Quaternion.Euler(-look.y, look.x, 0f));
                pitch = Mathf.Lerp(pitch, 0f, 4f * Time.deltaTime);
            }
            else
            {
                if (Mathf.Abs(look.x) > 0.0001f)
                    SetRotationNow(Quaternion.AngleAxis(look.x, transform.up) * rb.rotation);
                pitch = Mathf.Clamp(pitch - look.y, -PitchLimit, PitchLimit);
            }
            cameraPoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            cameraPoint.localPosition = new Vector3(0f, 1.62f, 0f);
        }

        void SetRotationNow(Quaternion r)
        {
            rb.rotation = r;
            transform.rotation = r;
        }

        // ================================================================ Interaction

        void UpdateInteraction(bool input)
        {
            focused = null;
            Prompt = "";
            if (Physics.Raycast(ViewRay, out var hit, InteractRange, ~0, QueryTriggerInteraction.Collide))
            {
                var it = hit.collider.GetComponentInParent<IInteractable>();
                if (it != null && !ReferenceEquals(it, this) && it.CanInteract(this))
                {
                    focused = it;
                    Prompt = $"[{VwInput.BindingDisplay(VwInput.Interact)}] {it.Prompt}";
                }
                else if (it != null) Prompt = it.Prompt;
                else
                {
                    var node = hit.collider.GetComponentInParent<ResourceNode>();
                    if (node != null) Prompt = $"{node.DisplayName} - mine with the Drill [1]";
                }
            }
            if (input && focused != null && VwInput.Pressed(VwInput.Interact))
            {
                focused.Interact(this);
                Sfx.PlayUI("ui_click", 0.5f);
            }
        }

        void DoPing()
        {
            Ray r = ViewRay;
            PingKind kind = PingKind.Look;
            string label = "Look here";
            Vector3 pos = r.origin + r.direction * 60f;
            if (Physics.Raycast(r.origin + r.direction * 1f, r.direction, out var hit, 1500f, ~0, QueryTriggerInteraction.Collide))
            {
                pos = hit.point;
                var h = hit.collider.GetComponentInParent<HealthComponent>();
                if (h != null && h.Team != Team.Players) { kind = PingKind.Enemy; label = "Enemy"; }
                else if (hit.collider.GetComponentInParent<WorldPickup>() || hit.collider.GetComponentInParent<LootCrate>()) { kind = PingKind.Loot; label = "Loot"; }
                else if (hit.collider.GetComponentInParent<ResourceNode>() is ResourceNode n) { kind = PingKind.Loot; label = n.DisplayName; }
            }
            else if (PlanetBody.RaycastAll(r, 6000f, out var ground, out _))
            {
                // Distant terrain has no streamed colliders; the height field still answers.
                pos = ground;
            }
            EventBus.RaisePingRequested(pos, kind, label);
            Sfx.PlayUI("ping");
        }

        // ================================================================ Tools

        public void SelectTool(Tool t)
        {
            if (t == Tool.Rifle && (Gear == null || !Gear.HasRifle.Value)) { EventBus.RaiseNotification("No rifle yet - craft or buy one"); return; }
            CurrentTool = t;
            for (int i = 0; i < toolModels.Length; i++)
                if (toolModels[i]) toolModels[i].gameObject.SetActive(IsOwner && i == (int)t && CurrentSeat == null && Active.Value && !IsDown.Value);
        }

        void UpdateTools(bool input)
        {
            if (input)
            {
                for (int i = 0; i < 4; i++)
                    if (VwInput.Pressed(VwInput.Hotbar[i])) { SelectTool((Tool)i); Sfx.PlayUI("ui_click", 0.4f); }
            }
            toolTimer -= Time.deltaTime;
            bool firing = input && VwInput.Held(VwInput.Fire);
            bool firePressed = input && VwInput.Pressed(VwInput.Fire);
            if (beam) beam.enabled = false;

            switch (CurrentTool)
            {
                case Tool.Drill:
                    if (firing) TickDrill();
                    break;
                case Tool.Blaster:
                    if (firePressed && toolTimer <= 0f)
                    {
                        toolTimer = 0.28f;
                        Vector3 dir = AimDir(150f);
                        if (CombatNet.Instance != null) CombatNet.Instance.RequestFireServerRpc(ProjectileKind.Blaster, MuzzlePos, dir, 15f);
                        Kick(0.08f);
                        FeedbackFx.LocalRecoil(0.55f);
                    }
                    break;
                case Tool.Rifle:
                    if (firing && toolTimer <= 0f)
                    {
                        toolTimer = 0.11f;
                        FireRifle();
                    }
                    break;
                case Tool.Medkit:
                    if (firePressed && toolTimer <= 0f)
                    {
                        toolTimer = 1f;
                        int slot = FindSlot("medkit");
                        if (slot >= 0) Inventory.UseSlotServerRpc(slot);
                        else EventBus.RaiseNotification("No medkits - craft them (Crystal + Ore)");
                    }
                    break;
            }
            ToolCooldown01 = Mathf.Clamp01(toolTimer / 0.3f);
        }

        Vector3 MuzzlePos => muzzle != null && muzzle.gameObject.activeInHierarchy ? muzzle.position : cameraPoint.position + cameraPoint.forward * 0.6f - cameraPoint.up * 0.2f;

        Vector3 AimDir(float range)
        {
            Ray r = ViewRay;
            Vector3 target = r.origin + r.direction * range;
            if (Physics.Raycast(r, out var hit, range, ~0, QueryTriggerInteraction.Ignore)) target = hit.point;
            return (target - MuzzlePos).normalized;
        }

        void Kick(float amount)
        {
            pitch = Mathf.Clamp(pitch - amount * 10f, -PitchLimit, PitchLimit);
        }

        int FindSlot(string id)
        {
            for (int i = 0; i < Inventory.Slots.Count; i++)
                if (!Inventory.Slots[i].IsEmpty && Inventory.Slots[i].Id.ToString() == id) return i;
            return -1;
        }

        void TickDrill()
        {
            Ray r = ViewRay;
            const float range = 7f;
            Vector3 end = r.origin + r.direction * range;
            bool hitSomething = Physics.Raycast(r, out var hit, range, ~0, QueryTriggerInteraction.Collide);
            if (hitSomething && hit.collider.isTrigger && hit.collider.GetComponentInParent<IDamageable>() == null)
                hitSomething = Physics.Raycast(r, out hit, range, ~0, QueryTriggerInteraction.Ignore);
            if (hitSomething) end = hit.point;
            if (beam)
            {
                beam.enabled = true;
                beam.SetPosition(0, MuzzlePos);
                beam.SetPosition(1, end);
            }
            mineTick -= Time.deltaTime;
            if (mineTick > 0f || !hitSomething) return;
            mineTick = 0.2f;
            Sfx.Play("mine", end, 0.35f, Random.Range(0.9f, 1.1f));
            Vfx.LaserImpact(end);
            var node = hit.collider.GetComponentInParent<ResourceNode>();
            if (node != null) { node.MineServerRpc(Gear.MiningPower * 0.2f * 2.5f); return; }
            var h = hit.collider.GetComponentInParent<HealthComponent>();
            if (h != null && h.Team != Team.Players)
                HealthComponent.DamageHit(hit.collider, hit.point, Gear.MiningDamage * 0.2f * 5f, DamageType.Mining, r.direction, OwnerClientId, true);
        }

        void FireRifle()
        {
            Ray r = ViewRay;
            Vector3 spread = Random.insideUnitSphere * 0.012f;
            Vector3 dir = (r.direction + spread).normalized;
            Vector3 end = r.origin + dir * 160f;
            Vector3 normal = Vector3.zero;
            SurfaceKind surface = SurfaceKind.None;
            if (Physics.Raycast(r.origin, dir, out var hit, 160f, ~0, QueryTriggerInteraction.Collide))
            {
                var d = hit.collider.GetComponentInParent<IDamageable>();
                if (!hit.collider.isTrigger || d != null)
                {
                    end = hit.point;
                    normal = hit.normal;
                    surface = FeedbackFx.SurfaceOf(hit.collider);
                    if (d != null && d.Team != Team.Players)
                        HealthComponent.DamageHit(hit.collider, hit.point, 8f, DamageType.Kinetic, dir, OwnerClientId, true);
                }
            }
            // tracer, muzzle flash and impact (sparks + burn mark) here and on every other peer
            var rifleColor = new Color(3f, 2.2f, 0.8f);
            if (CombatNet.Instance != null) CombatNet.Instance.Tracer(MuzzlePos, end, rifleColor, normal, surface);
            else { FeedbackFx.WeaponFired(MuzzlePos, end - MuzzlePos, rifleColor, 0.3f); FeedbackFx.HitscanImpact(end, normal, surface, rifleColor); }
            Sfx.Play("rifle", MuzzlePos, 0.6f, Random.Range(0.95f, 1.05f));
            Kick(0.05f);
            FeedbackFx.LocalRecoil(0.3f);
        }

        // ================================================================ Server

        bool IsSheltered()
        {
            if (CurrentSeat != null) return true;
            if (GravityVolume.At(transform.position) != null) return true;
            var planet = PlanetBody.Nearest(transform.position);
            return planet != null && planet.Breathable && planet.IsInsideAtmosphere(transform.position);
        }

        /// <summary>Server: true when protected from vacuum/radiation.</summary>
        public bool ServerSheltered => IsSheltered();

        void ServerTick(float dt)
        {
            if (!Active.Value || IsDown.Value) return;
            float max = Gear != null ? Gear.MaxOxygen : 90f;
            if (IsSheltered()) Oxygen.Value = Mathf.Min(max, Oxygen.Value + 30f * dt);
            else if (!CheatManager.GodMode) Oxygen.Value = Mathf.Max(0f, Oxygen.Value - dt);
            if (Oxygen.Value <= 0f)
            {
                o2DamageTimer -= dt;
                if (o2DamageTimer <= 0f)
                {
                    o2DamageTimer = 1f;
                    Health.ApplyDamage(new DamageInfo { Amount = 9f, Type = DamageType.Environmental, Point = transform.position });
                }
            }
        }

        void OnServerDied(DamageInfo info)
        {
            IsDown.Value = true;
            if (info.Type == DamageType.Collapse) IsLost.Value = true;
            var seat = ShipSeat.SeatOf(OwnerClientId);
            if (seat != null) seat.Vacate();
            DiedClientRpc(transform.position, info.Type == DamageType.Collapse);
        }

        [ClientRpc]
        void DiedClientRpc(Vector3 pos, bool lost)
        {
            Vfx.Explosion(pos + transform.up, 1.5f, new Color(0.6f, 0.9f, 1f));
            if (IsOwner)
            {
                Sfx.PlayUI("alarm");
                EventBus.RaiseNotification(lost ? "You were lost to the Collapse" : "You died - respawning at a ship...");
            }
        }

        /// <summary>Server: enable/disable the pawn in the world (lobby parking vs. playing).</summary>
        public void SetActiveServer(bool active)
        {
            if (!IsServer) return;
            Active.Value = active;
            if (active) { IsDown.Value = false; IsLost.Value = false; }
        }

        /// <summary>Server: revive at a position.</summary>
        public void RespawnServer(Vector3 pos, Quaternion rot)
        {
            if (!IsServer) return;
            Health.SetMaxHealthServer(Gear != null ? Gear.MaxHp : 100f, true);
            Health.DamageTakenMult = Gear != null ? Gear.Armor : 1f;
            Oxygen.Value = Gear != null ? Gear.MaxOxygen : 90f;
            IsDown.Value = false;
            GetComponent<NetTeleport>().ServerTeleport(pos, rot);
        }

        bool UseItemServer(ItemDef def)
        {
            switch (def.Kind)
            {
                case ItemKind.Consumable:
                    if (def.Id == "repair_kit")
                    {
                        var ship = ShipCore.Nearest(transform.position, 40f);
                        if (ship == null) { NotifyServer(OwnerClientId, "Stand next to a ship to use a repair kit"); return false; }
                        ship.Hull.Heal(def.HealAmount);
                        NotifyServer(OwnerClientId, "Ship hull repaired");
                        return true;
                    }
                    if (def.HealAmount > 0f)
                    {
                        if (Health.Hp.Value >= Health.MaxHp.Value - 0.5f) { NotifyServer(OwnerClientId, "Health is full"); return false; }
                        Health.Heal(def.HealAmount);
                        PlaySfxClientRpc("heal");
                        return true;
                    }
                    return false;
                case ItemKind.Gear:
                    if (Gear == null || !Gear.ApplyGear(def)) { NotifyServer(OwnerClientId, "Already equipped or better"); return false; }
                    Health.SetMaxHealthServer(Gear.MaxHp, false);
                    Health.DamageTakenMult = Gear.Armor;
                    NotifyServer(OwnerClientId, "Equipped " + def.DisplayName);
                    PlaySfxClientRpc("craft");
                    return true;
                case ItemKind.Module:
                    var own = ShipCore.OwnedBy(OwnerClientId);
                    if (own == null) own = ShipCore.Nearest(transform.position, 40f);
                    if (own == null) { NotifyServer(OwnerClientId, "No ship nearby to install into"); return false; }
                    string old = own.InstallServer(def.Id, false);
                    if (!string.IsNullOrEmpty(old)) NetInventory.GiveOrDrop(OwnerClientId, old, 1, transform.position);
                    NotifyServer(OwnerClientId, $"Installed {def.DisplayName} on {own.ShipName.Value}");
                    return true;
            }
            return false;
        }

        [ClientRpc]
        void PlaySfxClientRpc(string id)
        {
            if (IsOwner) Sfx.PlayUI(id);
        }

        static void NotifyServer(ulong client, string msg)
        {
            foreach (var p in All)
                if (p != null && p.OwnerClientId == client && p.IsSpawned) { p.NotifyClientRpc(msg, p.RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void NotifyClientRpc(string msg, RpcParams p) => EventBus.RaiseNotification(msg);

        /// <summary>Server: pawn of a client.</summary>
        public static PlayerPawn ForClient(ulong client)
        {
            foreach (var p in All) if (p != null && p.OwnerClientId == client) return p;
            return null;
        }
    }
}
