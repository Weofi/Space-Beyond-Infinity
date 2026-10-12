using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Inventory;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// End-of-sector Jump Gate. Needs Energy Cells inserted (from the interacting player's inventory
    /// or their ship's cargo). Once powered, it charges when every living player is within range;
    /// the RunManager performs the jump.
    /// </summary>
    public class JumpGate : NetworkBehaviour, IInteractable
    {
        public const float GatherRange = 380f;

        public NetworkVariable<int> CellsInserted = new NetworkVariable<int>(0);
        public NetworkVariable<int> CellsRequired = new NetworkVariable<int>(2);
        public NetworkVariable<float> Charge = new NetworkVariable<float>(0f); // 0..1

        [SerializeField] Transform ring;
        [SerializeField] Renderer core;

        public static JumpGate Instance { get; private set; }
        public bool Powered => CellsInserted.Value >= CellsRequired.Value;

        public string Prompt => Powered
            ? "Gate powered - gather everyone nearby to jump"
            : $"Insert Energy Cell ({CellsInserted.Value}/{CellsRequired.Value})";

        public bool CanInteract(IInteractAgent agent) => !Powered;
        public void Interact(IInteractAgent agent) => InsertServerRpc();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            GameState.HasGate = true;
            GameState.GatePosition = transform.position;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) { Instance = null; GameState.HasGate = false; }
        }

        void Update()
        {
            GameState.GatePosition = transform.position;
            AutoInsert();
            float speed = Powered ? 40f + Charge.Value * 260f : 8f;
            if (ring) ring.Rotate(0f, 0f, speed * Time.deltaTime, Space.Self);
            if (core)
            {
                Color c = Powered ? Color.Lerp(new Color(0.3f, 1.4f, 2.2f), new Color(2.5f, 2.5f, 3f), Charge.Value) : new Color(0.25f, 0.2f, 0.35f);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_EmissionColor", c);
                core.SetPropertyBlock(mpb);
            }
        }
float autoTimer;

        /// <summary>Flying (or walking) close with Energy Cells inserts them automatically.</summary>
        void AutoInsert()
        {
            if (!IsSpawned || Powered) return;
            autoTimer -= Time.deltaTime;
            if (autoTimer > 0f) return;
            autoTimer = 2f;
            var pawn = Voidward.Player.PlayerPawn.Local;
            if (pawn == null || !pawn.Active.Value || pawn.IsDown.Value) return;
            if (Vector3.Distance(pawn.transform.position, transform.position) > 160f) return;
            var ship = ShipCore.OwnedBy(pawn.OwnerClientId);
            bool hasCell = pawn.Inventory.Has("cell", 1) || (ship != null && ship.Cargo != null && ship.Cargo.Has("cell", 1));
            if (hasCell) InsertServerRpc();
        }

        
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void InsertServerRpc(RpcParams rp = default)
        {
            if (Powered) return;
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (inv == null || Vector3.Distance(inv.transform.position, transform.position) > 180f) return;
            int need = CellsRequired.Value - CellsInserted.Value;
            int taken = 0;
            while (taken < need && inv.Remove("cell", 1)) taken++;
            var ship = ShipCore.OwnedBy(c);
            while (taken < need && ship != null && ship.Cargo != null && ship.Cargo.Remove("cell", 1)) taken++;
            if (taken == 0)
            {
                FeedbackClientRpc("No Energy Cells - craft them (2 Crystal + 2 Scrap) or buy at a station", c, false);
                return;
            }
            CellsInserted.Value += taken;
            FeedbackClientRpc(Powered ? "JUMP GATE POWERED! Everyone gather at the gate." : $"Cells inserted ({CellsInserted.Value}/{CellsRequired.Value})", ulong.MaxValue, Powered);
        }

        [ClientRpc]
        void FeedbackClientRpc(string msg, ulong onlyFor, bool powered)
        {
            if (onlyFor != ulong.MaxValue && NetworkManager.LocalClientId != onlyFor) return;
            EventBus.RaiseNotification(msg);
            Sfx.PlayUI(powered ? "jump_charge" : "ui_click");
        }

        /// <summary>Interaction also works from a ship cockpit: the gate has a large trigger collider.</summary>
        public static float DistanceTo(Vector3 p) => Instance != null ? Vector3.Distance(Instance.transform.position, p) : float.MaxValue;
    }
}
