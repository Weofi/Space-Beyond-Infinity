using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// The crew's shared warp drive (#78; lives on the GameDirector next to the RunManager until the home ship of #49
    /// carries it). Server-owned state: the tier of each component (core, nav computer, cooling, Exotic tank) and the
    /// Exotic Matter in the tank. Anyone at a ship can swap a component from their backpack (the old one comes back)
    /// or load Exotic Matter. Clients derive the build stats, the Home ETA and the fuel need locally (DriveMath).
    /// </summary>
    public class WarpDrive : NetworkBehaviour
    {
        public static WarpDrive Instance { get; private set; }

        public const string ExoticId = "exotic";
        /// <summary>How close to a ship (or seated in one) a player must be to work on the drive.</summary>
        public const float ServiceRange = 30f;

        /// <summary>Tier per DriveSlot (index = (int)DriveSlot).</summary>
        public NetworkList<int> Tiers;
        public NetworkVariable<float> Exotic = new NetworkVariable<float>(0f);

        DriveStats cached;
        int cachedKey = int.MinValue;
        float etaTimer;

        public static WarpDriveDef Def => ContentCatalog.Instance != null ? ContentCatalog.Instance.WarpDrive : null;

        void Awake() => Tiers = new NetworkList<int>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer && Tiers.Count == 0) for (int i = 0; i < 4; i++) Tiers.Add(1);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            GameState.HomeEta = "";
        }

        public int TierOf(DriveSlot slot) => (int)slot < Tiers.Count ? Tiers[(int)slot] : 1;

        /// <summary>Effective stats of the installed build (cached until a component changes).</summary>
        public DriveStats Stats
        {
            get
            {
                int key = TierOf(DriveSlot.Core) | TierOf(DriveSlot.Navigation) << 4 | TierOf(DriveSlot.Cooling) << 8 | TierOf(DriveSlot.Tank) << 12;
                if (key != cachedKey)
                {
                    cachedKey = key;
                    cached = DriveMath.Compute(Def, TierOf(DriveSlot.Core), TierOf(DriveSlot.Navigation), TierOf(DriveSlot.Cooling), TierOf(DriveSlot.Tank));
                }
                return cached;
            }
        }

        public int Capacity => Stats.Capacity;

        static int Galaxy => RunManager.Instance != null ? RunManager.Instance.GalaxyIndex.Value : GameState.GalaxyIndex;

        /// <summary>Flight minutes from the current galaxy to stop `to` (DriveMath.Home = home) with this build (or `with`).</summary>
        public float EtaTo(int to) => DriveMath.Minutes(Stats, Galaxy, to);
        public static float EtaTo(DriveStats s, int to) => DriveMath.Minutes(s, Galaxy, to);
        public static int FuelTo(int to) => DriveMath.Fuel(Def, Galaxy, to);
        /// <summary>Flight minutes of a warp to another system of this galaxy (#80).</summary>
        public float HopMinutes => DriveMath.HopMinutes(Def, Stats);

        /// <summary>Exotic Matter needed to reach `to` is in the tank (or the tank could hold it at all).</summary>
        public bool HasFuelFor(int to) => Exotic.Value + 0.001f >= FuelTo(to);
        public bool TankCanHold(int to) => Capacity >= FuelTo(to);

        // ------------------------------------------------------------ client: HUD/cockpit summary

        void Update()
        {
            WarpBubble.CoreTier = TierOf(DriveSlot.Core);
            etaTimer -= Time.deltaTime;
            if (etaTimer > 0f) return;
            etaTimer = 0.5f;
            if (RunManager.Instance == null || RunManager.Instance.RunSeed.Value == 0) { GameState.HomeEta = ""; return; }
            GameState.HomeEta = DriveMath.Eta(EtaTo(DriveMath.Home));
        }

        // ------------------------------------------------------------ server

        /// <summary>Server: a new loop starts with the mission ship's Mk I drive and a little Exotic Matter.</summary>
        public void ServerReset(int bonusExotic)
        {
            if (!IsServer) return;
            ServerSetTiers(1, 1, 1, 1);
            var d = Def;
            Exotic.Value = Mathf.Min(Capacity, (d != null ? d.StartExotic : 4) + bonusExotic);
        }

        public void ServerSetTiers(int core, int nav, int cooling, int tank)
        {
            if (!IsServer) return;
            while (Tiers.Count < 4) Tiers.Add(1);
            int[] t = { core, nav, cooling, tank };
            for (int i = 0; i < 4; i++)
            {
                int v = Mathf.Clamp(t[i], 1, DriveComponentDef.MaxTier);
                if (Tiers[i] != v) Tiers[i] = v;
            }
            if (Exotic.Value > Capacity) Exotic.Value = Capacity;
        }

        /// <summary>Server: burn Exotic Matter for a warp. False (and nothing burnt) if there is not enough.</summary>
        public bool ServerConsume(int amount)
        {
            if (!IsServer) return false;
            if (CheatManager.GodMode) return true;
            if (Exotic.Value + 0.001f < amount) return false;
            Exotic.Value = Mathf.Max(0f, Exotic.Value - amount);
            return true;
        }

        static bool AtShip(ulong client, NetInventory inv)
        {
            var seat = ShipSeat.SeatOf(client);
            if (seat != null && seat.Ship != null) return true;
            return ShipCore.Nearest(inv.transform.position, ServiceRange) != null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void InstallFromInventoryServerRpc(int invSlot, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (inv == null || invSlot < 0 || invSlot >= inv.Slots.Count) return;
            if (!AtShip(c, inv)) { FeedbackRpc("Get to a ship to work on the warp drive", "ui_back", RpcTarget.Single(c, RpcTargetUse.Temp)); return; }
            if (WarpFlight.Instance != null && WarpFlight.Instance.Active.Value) { FeedbackRpc("Drive parts can't be swapped during a warp", "ui_back", RpcTarget.Single(c, RpcTargetUse.Temp)); return; }
            var item = inv.Slots[invSlot].Def;
            if (item == null || item.Kind != ItemKind.DriveComponent || item.Drive == null) return;
            if (!inv.Remove(item.Id, 1)) return;
            var slot = item.Drive.Slot;
            int old = TierOf(slot);
            float before = EtaTo(DriveMath.Home);
            Tiers[(int)slot] = item.Drive.Tier;
            NetInventory.GiveOrDrop(c, DriveComponentDef.ItemId(slot, old), 1, inv.transform.position);
            // A smaller tank spills what no longer fits back into the backpack.
            int spill = Mathf.FloorToInt(Exotic.Value - Capacity);
            if (spill > 0)
            {
                Exotic.Value -= spill;
                NetInventory.GiveOrDrop(c, ExoticId, spill, inv.transform.position);
            }
            float after = EtaTo(DriveMath.Home);
            FeedbackRpc($"Installed {item.DisplayName}\nHome ETA {DriveMath.Eta(after)} ({DriveMath.Delta(after - before)})", "craft", RpcTarget.Everyone);
            var story = ContentCatalog.Instance != null ? ContentCatalog.Instance.Story : null;
            if (RunManager.Instance != null && story != null && item.Drive.Tier > old) RunManager.Instance.ServerLog(story.LogDrive, null, item.DisplayName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void LoadExoticServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (inv == null) return;
            if (!AtShip(c, inv)) { FeedbackRpc("Get to a ship to load the warp drive", "ui_back", RpcTarget.Single(c, RpcTargetUse.Temp)); return; }
            int room = Mathf.FloorToInt(Capacity - Exotic.Value + 0.001f);
            int n = Mathf.Min(room, inv.Count(ExoticId));
            if (n <= 0 || !inv.Remove(ExoticId, n))
            {
                FeedbackRpc(room <= 0 ? "Exotic tank is full" : "No Exotic Matter in your backpack", "ui_back", RpcTarget.Single(c, RpcTargetUse.Temp));
                return;
            }
            Exotic.Value += n;
            FeedbackRpc($"Loaded {n} Exotic Matter ({Mathf.FloorToInt(Exotic.Value)}/{Capacity})", "pickup", RpcTarget.Single(c, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void FeedbackRpc(string msg, string sfx, RpcParams rp = default)
        {
            Sfx.PlayUI(sfx);
            EventBus.RaiseNotification(msg);
        }
    }
}
