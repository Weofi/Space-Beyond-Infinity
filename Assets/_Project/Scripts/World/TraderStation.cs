using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// Trader / repair station. Stock is generated deterministically from the synced seed and
    /// sector so every client shows the same list; all transactions run on the server.
    /// </summary>
    public class TraderStation : NetworkBehaviour, IInteractable, ISurface
    {
        public SurfaceKind Surface => SurfaceKind.Metal;

        public const float UseRange = 70f;
        public const float SellRatio = 0.5f;
        public const int ServiceCost = 60;

        public NetworkVariable<int> Seed = new NetworkVariable<int>(0);
        public NetworkVariable<int> Sector = new NetworkVariable<int>(1);
        /// <summary>Galaxy 1-5: drive parts on offer match its tier (#78).</summary>
        public NetworkVariable<int> Galaxy = new NetworkVariable<int>(1);

        [SerializeField] Transform spinner;

        public static TraderStation Instance { get; private set; }
        readonly List<ItemDef> stock = new List<ItemDef>();
        int stockSeed = int.MinValue;

        public string Prompt => "Trade (buy / sell / repair)";
        public bool CanInteract(IInteractAgent agent) => true;
        public void Interact(IInteractAgent agent) => EventBus.RaiseTradePanelRequested(NetworkObjectId);

        public override void OnNetworkSpawn() => Instance = this;
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (spinner) spinner.Rotate(0f, 6f * Time.deltaTime, 0f, Space.Self);
        }

        public IReadOnlyList<ItemDef> Stock
        {
            get
            {
                if (stockSeed != Seed.Value * 8 + Galaxy.Value) BuildStock();
                return stock;
            }
        }

        void BuildStock()
        {
            stockSeed = Seed.Value * 8 + Galaxy.Value;
            stock.Clear();
            var cat = ContentCatalog.Instance;
            if (cat == null) return;
            foreach (var id in new[] { "fuel", "exotic", "medkit", "repair_kit" })
            {
                var d = ContentCatalog.GetItem(id);
                if (d != null) stock.Add(d);
            }
            var rng = new System.Random(Seed.Value);
            var pool = new List<ItemDef>();
            int maxTier = Mathf.Clamp(Sector.Value + 1, 1, 3);
            foreach (var it in cat.Items)
            {
                if (it == null || !it.SoldAtTrader) continue;
                if (it.Kind == ItemKind.Module && it.Module != null && it.Module.Tier <= maxTier && it.Module.Tier >= Sector.Value - 1) pool.Add(it);
                if (it.Kind == ItemKind.Gear && it.GearTier <= maxTier) pool.Add(it);
            }
            for (int i = 0; i < 7 && pool.Count > 0; i++)
            {
                int k = rng.Next(pool.Count);
                stock.Add(pool[k]);
                pool.RemoveAt(k);
            }
            // Two warp drive parts (nav / cooling / tank) at the galaxy's tier or one below.
            for (int i = 0; i < 2; i++)
            {
                var d = ContentCatalog.GetItem(DriveComponentDef.ItemId((DriveSlot)(1 + rng.Next(3)), Mathf.Max(1, Galaxy.Value + 1 - rng.Next(2))));
                if (d != null && d.SoldAtTrader && !stock.Contains(d)) stock.Add(d);
            }
        }

        public static int BuyPrice(ItemDef d) => Mathf.Max(1, d.CreditValue);
        public static int SellPrice(ItemDef d) => Mathf.Max(1, Mathf.RoundToInt(d.CreditValue * SellRatio));

        bool InRange(NetInventory inv) =>
            inv != null && (Vector3.Distance(inv.transform.position, transform.position) < UseRange);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void BuyServerRpc(int stockIndex, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (!InRange(inv) || stockIndex < 0 || stockIndex >= Stock.Count) return;
            var d = Stock[stockIndex];
            int price = BuyPrice(d);
            if (inv.Credits.Value < price) { Feedback(c, "Not enough Credits", "ui_back"); return; }
            if (inv.SpaceFor(d.Id) < 1) { Feedback(c, "Inventory full", "ui_back"); return; }
            inv.Credits.Value -= price;
            inv.Add(d.Id, 1);
            Feedback(c, "Bought " + d.DisplayName, "trade");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SellServerRpc(int invSlot, bool all, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (!InRange(inv) || invSlot < 0 || invSlot >= inv.Slots.Count) return;
            var st = inv.Slots[invSlot];
            var d = st.Def;
            if (d == null) return;
            int n = all ? st.Count : 1;
            if (!inv.Remove(d.Id, n)) return;
            int gain = SellPrice(d) * n;
            inv.Credits.Value += gain;
            Feedback(c, $"Sold {n}x {d.DisplayName} (+{gain})", "trade");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ServiceServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var inv = NetInventory.ForClient(c);
            if (!InRange(inv)) return;
            var ship = ShipCore.OwnedBy(c);
            if (ship == null || Vector3.Distance(ship.transform.position, transform.position) > 300f)
                ship = ShipCore.Nearest(transform.position, 300f);
            if (ship == null) { Feedback(c, "Bring a ship close to the station", "ui_back"); return; }
            if (inv.Credits.Value < ServiceCost) { Feedback(c, "Not enough Credits", "ui_back"); return; }
            inv.Credits.Value -= ServiceCost;
            ship.RestoreServer(true, true);
            Feedback(c, $"{ship.ShipName.Value} repaired & refueled", "heal");
        }

        void Feedback(ulong c, string msg, string sfx) => FeedbackClientRpc(msg, sfx, c);

        [ClientRpc]
        void FeedbackClientRpc(string msg, string sfx, ulong who)
        {
            if (NetworkManager.LocalClientId != who) return;
            EventBus.RaiseNotification(msg);
            Sfx.PlayUI(sfx);
        }
    }
}
