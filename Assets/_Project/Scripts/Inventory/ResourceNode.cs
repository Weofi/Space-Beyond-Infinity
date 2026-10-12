using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>
    /// Mineable deposit (ore, crystal, scrap, fuel). The mining tool calls MineServerRpc while the
    /// beam is on it; progress converts to items in the miner's inventory.
    /// </summary>
    public class ResourceNode : NetworkBehaviour
    {
        public NetworkVariable<int> KindIndex = new NetworkVariable<int>((int)ScatterKind.OreNode);
        public NetworkVariable<int> Remaining = new NetworkVariable<int>(8);

        [Tooltip("Children named Ore/Crystal/Scrap/Fuel; only the matching one is shown.")]
        [SerializeField] Transform[] variants;

        float progress;
        int startAmount;

        public ScatterKind Kind => (ScatterKind)KindIndex.Value;

        public string ResourceItemId
        {
            get
            {
                switch (Kind)
                {
                    case ScatterKind.CrystalNode: return "crystal";
                    case ScatterKind.ScrapNode: return "scrap";
                    case ScatterKind.FuelNode: return "fuel";
                    default: return "ore";
                }
            }
        }

        public string DisplayName
        {
            get
            {
                var d = ContentCatalog.GetItem(ResourceItemId);
                return (d != null ? d.DisplayName : "Resource") + " deposit";
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && pendingAmount > 0)
            {
                KindIndex.Value = pendingKind;
                Remaining.Value = pendingAmount;
            }
            startAmount = Mathf.Max(1, Remaining.Value);
            KindIndex.OnValueChanged += (a, b) => Refresh();
            Remaining.OnValueChanged += (a, b) => Refresh();
            Refresh();
        }

        void Refresh()
        {
            if (variants == null) return;
            string want = ResourceItemId;
            foreach (var v in variants)
            {
                if (v == null) continue;
                bool on = v.name.ToLowerInvariant().StartsWith(want.Substring(0, 3));
                v.gameObject.SetActive(on);
                float s = Mathf.Lerp(0.45f, 1f, Remaining.Value / (float)Mathf.Max(1, startAmount));
                v.localScale = Vector3.one * s;
            }
        }

        /// <summary>Client: mining beam tick (power = resource units per second-ish).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MineServerRpc(float power, RpcParams rp = default)
        {
            if (Remaining.Value <= 0) return;
            var inv = NetInventory.ForClient(rp.Receive.SenderClientId);
            if (inv == null || Vector3.Distance(inv.transform.position, transform.position) > 14f) return;
            progress += Mathf.Clamp(power, 0f, 2f);
            if (progress < 1f) return;
            int units = Mathf.Min(Mathf.FloorToInt(progress), Remaining.Value);
            progress -= units;
            Remaining.Value -= units;
            NetInventory.GiveOrDrop(rp.Receive.SenderClientId, ResourceItemId, units, transform.position);
            LootStats.Collected?.Invoke(rp.Receive.SenderClientId, units);
            MinedClientRpc(transform.position + transform.up, rp.Receive.SenderClientId, units);
            if (Remaining.Value <= 0) NetworkObject.Despawn(true);
        }

        [ClientRpc]
        void MinedClientRpc(Vector3 pos, ulong who, int units)
        {
            var d = ContentCatalog.GetItem(ResourceItemId);
            Vfx.PickupSparkle(pos, d != null ? d.Tint : Color.white);
            if (NetworkManager.LocalClientId == who)
            {
                Sfx.PlayUI("pickup", 0.6f);
                EventBus.RaiseNotification($"+{units} {(d != null ? d.DisplayName : ResourceItemId)}");
            }
        }

        public static ResourceNode SpawnServer(ScatterKind kind, Vector3 pos, Quaternion rot, int amount)
        {
            var cat = ContentCatalog.Instance;
            if (cat == null || cat.ResourceNode == null) return null;
            var go = Instantiate(cat.ResourceNode, pos, rot);
            var n = go.GetComponent<ResourceNode>();
            n.pendingKind = (int)kind;
            n.pendingAmount = amount;
            n.NetworkObject.Spawn(true);
            return n;
        }

        int pendingKind;
        int pendingAmount;
    }
}
