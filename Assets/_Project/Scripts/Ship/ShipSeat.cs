using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Ship
{
    /// <summary>
    /// A ship station (Pilot / Turret / Engineer). One occupant, tracked by a server-written
    /// NetworkVariable. Pawns poll <see cref="SeatOf"/> and follow the seat locally (no NGO
    /// re-parenting). The pilot seat transfers ship ownership to the occupant for instant flight.
    /// </summary>
    public class ShipSeat : NetworkBehaviour, ISeat, IInteractable
    {
        public const ulong NoOne = ulong.MaxValue;

        public StationRole SeatRole = StationRole.Pilot;
        [SerializeField] Transform seatAnchor;
        [SerializeField] Transform viewPoint;
        [SerializeField] Transform exitPoint;
        [Tooltip("Shown on other clients while someone sits here.")]
        [SerializeField] GameObject seatedBody;

        [Header("Turret")]
        public Transform TurretYaw;
        public Transform TurretPitch;
        public Transform Muzzle;
        public WeaponKind TurretWeapon = WeaponKind.Flak;
        public int TurretIndex;

        public NetworkVariable<ulong> Occupant = new NetworkVariable<ulong>(NoOne);
        public NetworkVariable<Vector2> TurretAim = new NetworkVariable<Vector2>(Vector2.zero);

        public ShipCore Ship { get; private set; }
        ShipFlight flight;

        Vector2 localAim;
        float aimSendTimer;
        Vector2 pilotLook;

        /// <summary>Max distance for aim assist / lead indicators.</summary>
        const float AssistRange = 900f;

        static readonly List<ShipSeat> all = new List<ShipSeat>();
        public static IReadOnlyList<ShipSeat> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        // ---- ISeat ----
        public StationRole Role => SeatRole;
        public bool IsOccupied => Occupant.Value != NoOne;
        public GameObject GameObject => gameObject;
        public Transform SeatAnchor => seatAnchor != null ? seatAnchor : transform;
        public Transform ViewPoint => viewPoint != null ? viewPoint : SeatAnchor;
        public Transform ExitPoint => exitPoint != null ? exitPoint : transform;

        void Awake()
        {
            Ship = GetComponentInParent<ShipCore>();
            flight = GetComponentInParent<ShipFlight>();
        }

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            Occupant.OnValueChanged += OnOccupantChanged;
            OnOccupantChanged(NoOne, Occupant.Value);
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            Occupant.OnValueChanged -= OnOccupantChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        void OnClientDisconnect(ulong id)
        {
            if (Occupant.Value == id) Vacate();
        }

        void OnOccupantChanged(ulong oldV, ulong newV)
        {
            if (seatedBody != null)
                seatedBody.SetActive(newV != NoOne && (NetworkManager == null || newV != NetworkManager.LocalClientId));
            if (newV != NoOne && NetworkManager != null && newV == NetworkManager.LocalClientId)
            {
                localAim = TurretAim.Value;
                if (SeatRole == StationRole.Engineer && Ship != null) EventBus.RaiseEngineerPanelRequested(Ship.NetworkObjectId);
                EventBus.RaiseNotification(SeatRole == StationRole.Pilot ? "Piloting - [F] to leave seat" : SeatRole + " station - [F] to leave");
            }
        }

        /// <summary>The seat a client occupies (any peer), or null.</summary>
        public static ShipSeat SeatOf(ulong client)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Occupant.Value == client) return all[i];
            return null;
        }

        /// <summary>Server: kick everyone off a ship (destroyed / jumped).</summary>
        public static void VacateAllOn(ShipCore ship)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Ship == ship && all[i].IsOccupied) all[i].Vacate();
        }

        // ---- IInteractable ----
        public string Prompt
        {
            get
            {
                if (Ship != null && Ship.Derelict.Value) return $"Restore derelict power ({ShipCore.DerelictRepairCost} Scrap)";
                if (IsOccupied) return SeatRole + " station (occupied)";
                switch (SeatRole)
                {
                    case StationRole.Pilot: return Ship != null && Ship.Class != ShipClass.Ark ? $"Board {Ship.ShipName.Value}" : "Take the helm";
                    case StationRole.Turret: return "Man the turret";
                    default: return "Engineer station";
                }
            }
        }

        public bool CanInteract(IInteractAgent agent) => IsSpawned && (!IsOccupied || (Ship != null && Ship.Derelict.Value));

        public void Interact(IInteractAgent agent)
        {
            if (Ship != null && Ship.Derelict.Value) { Ship.ClaimServerRpc(); return; }
            SitServerRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SitServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (IsOccupied || Ship == null || Ship.Derelict.Value) return;
            var prev = SeatOf(c);
            if (prev != null) prev.Vacate();
            Occupant.Value = c;
            if (SeatRole == StationRole.Pilot && Ship.NetworkObject.OwnerClientId != c)
                Ship.NetworkObject.ChangeOwnership(c);
            if (Ship.OwnerPlayer.Value == ShipCore.NoPlayer && Ship.Class != ShipClass.Ark)
                Ship.OwnerPlayer.Value = c;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void LeaveServerRpc(RpcParams rp = default)
        {
            if (Occupant.Value == rp.Receive.SenderClientId) Vacate();
        }

        /// <summary>Server.</summary>
        public void Vacate()
        {
            if (!IsServer) return;
            Occupant.Value = NoOne;
            if (SeatRole == StationRole.Pilot && Ship != null && Ship.IsSpawned && Ship.NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
                Ship.NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        public void RequestLeave() => LeaveServerRpc();

        // ---- Occupant tick (local) ----
        public void TickOccupant(float dt)
        {
            bool input = GameState.GameplayInputAllowed;
            switch (SeatRole)
            {
                case StationRole.Pilot: TickPilot(input); break;
                case StationRole.Turret: TickTurret(input, dt); break;
                case StationRole.Engineer:
                    if (input && VwInput.Pressed(VwInput.Fire) && Ship != null) EventBus.RaiseEngineerPanelRequested(Ship.NetworkObjectId);
                    break;
            }
        }

        void TickPilot(bool input)
        {
            if (flight == null || Ship == null) return;
            if (!input) { pilotLook = Vector2.zero; AimAssist.Clear(); flight.SetPilotInput(Vector3.zero, Vector3.zero, false, false); return; }
            Vector2 mv = VwInput.Move.ReadValue<Vector2>();
            float vert = VwInput.Vertical.ReadValue<float>();
            float roll = VwInput.Roll.ReadValue<float>();
            // Mouse speed -> turn rate, lightly smoothed against per-frame mouse jitter.
            float dt = Time.deltaTime;
            pilotLook = Vector2.Lerp(pilotLook, VwInput.ShipLookRate(dt), 1f - Mathf.Exp(-30f * dt));
            flight.SetPilotInput(new Vector3(mv.x, vert, mv.y), new Vector3(-pilotLook.y, pilotLook.x, roll),
                VwInput.Held(VwInput.Sprint), VwInput.Held(VwInput.Brake));

            var s = Ship.Stats;
            var muzzle = Ship.MuzzlesA != null && Ship.MuzzlesA.Length > 0 && Ship.MuzzlesA[0] != null ? Ship.MuzzlesA[0].position : Ship.transform.position;
            var kindA = s.WeaponA != WeaponKind.None ? s.WeaponA : s.WeaponB;
            Vector3 aim = AimDirection(muzzle, kindA, s.AimAssist);
            if (VwInput.Held(VwInput.Fire)) Ship.TryFireThrottled(0, aim);
            if (VwInput.Held(VwInput.AltFire)) Ship.TryFireThrottled(1, s.WeaponB == kindA ? aim : AimDirection(muzzle, s.WeaponB, s.AimAssist));
        }

        /// <summary>
        /// Converge fire on whatever is under the crosshair (or 400 m ahead); with a hostile inside the
        /// assist cone, fire at its lead point instead.
        /// </summary>
        Vector3 AimDirection(Vector3 from, WeaponKind weapon, float assistDeg)
        {
            // The crosshair is the screen centre: aim along the rendering camera (falls back to the seat view).
            var cam = Camera.main;
            var vp = cam != null ? cam.transform : ViewPoint;
            // Missiles home on their own: no lead needed, but still show the indicator.
            float speed = weapon == WeaponKind.None ? 0f : CombatNet.ProjectileSpeed(ShipCore.ProjectileFor(weapon));
            if (AimAssist.Solve(vp.position, vp.forward, from, speed, weapon == WeaponKind.Missile ? 0f : assistDeg, AssistRange, out var assisted))
                return assisted;
            Vector3 target = vp.position + vp.forward * 400f;
            if (Physics.Raycast(vp.position + vp.forward * 12f, vp.forward, out var hit, 900f, ~0, QueryTriggerInteraction.Ignore))
                target = hit.point;
            return (target - from).normalized;
        }

        void TickTurret(bool input, float dt)
        {
            if (input)
            {
                Vector2 look = VwInput.LookDelta;
                localAim.x += look.x;
                localAim.y = Mathf.Clamp(localAim.y - look.y, -10f, 80f);
            }
            ApplyAim(localAim);
            aimSendTimer -= dt;
            if (aimSendTimer <= 0f)
            {
                aimSendTimer = 0.1f;
                AimServerRpc(localAim);
            }
            if (!input) { AimAssist.Clear(); return; }
            var m = Muzzle != null ? Muzzle : ViewPoint;
            Vector3 dir = AimDirection(m.position, TurretWeapon, Ship != null ? Ship.Stats.AimAssist : ShipCore.BaseAimAssist);
            if (VwInput.Held(VwInput.Fire) && Time.time >= localFireTime)
            {
                localFireTime = Time.time + ShipCore.Cooldown(TurretWeapon, 2) * 0.9f;
                TurretFireServerRpc(dir);
                FeedbackFx.LocalRecoil(ShipCore.Recoil(TurretWeapon));
            }
        }

        float localFireTime;

        void ApplyAim(Vector2 aim)
        {
            if (TurretYaw != null) TurretYaw.localRotation = Quaternion.Euler(0f, aim.x, 0f);
            if (TurretPitch != null) TurretPitch.localRotation = Quaternion.Euler(-aim.y, 0f, 0f);
        }

        void Update()
        {
            if (SeatRole != StationRole.Turret || !IsSpawned) return;
            bool mine = NetworkManager != null && Occupant.Value == NetworkManager.LocalClientId;
            if (!mine) ApplyAim(Vector2.Lerp(CurrentAim(), TurretAim.Value, 10f * Time.deltaTime));
        }

        Vector2 CurrentAim()
        {
            float yaw = TurretYaw != null ? TurretYaw.localEulerAngles.y : 0f;
            float pitch = TurretPitch != null ? -TurretPitch.localEulerAngles.x : 0f;
            if (yaw > 180f) yaw -= 360f;
            if (pitch < -180f) pitch += 360f;
            Vector2 t = TurretAim.Value;
            yaw = t.x + Mathf.DeltaAngle(t.x, yaw);
            return new Vector2(yaw, pitch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AimServerRpc(Vector2 aim, RpcParams rp = default)
        {
            if (Occupant.Value == rp.Receive.SenderClientId) TurretAim.Value = aim;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void TurretFireServerRpc(Vector3 dir, RpcParams rp = default)
        {
            if (Occupant.Value != rp.Receive.SenderClientId || Ship == null || Ship.Derelict.Value) return;
            Ship.FireTurret(this, dir.normalized, rp.Receive.SenderClientId);
        }
    }
}
