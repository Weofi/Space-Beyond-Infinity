using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>Openable loot container. Contents rolled on the server from a tiered loot table.</summary>
    public class LootCrate : NetworkBehaviour, IInteractable
    {
        public NetworkVariable<bool> Opened = new NetworkVariable<bool>();
        public NetworkVariable<int> Tier = new NetworkVariable<int>(1);
        [SerializeField] Transform lid;

        public string Prompt => Opened.Value ? "Empty" : "Open crate";
        public bool CanInteract(IInteractAgent agent) => !Opened.Value;
        public void Interact(IInteractAgent agent) => OpenServerRpc();

        public override void OnNetworkSpawn()
        {
            if (IsServer && pendingTier > 0) Tier.Value = pendingTier;
            Opened.OnValueChanged += (a, b) => UpdateLid();
            UpdateLid();
        }

        void UpdateLid()
        {
            if (lid != null) lid.localRotation = Opened.Value ? Quaternion.Euler(-110f, 0f, 0f) : Quaternion.identity;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void OpenServerRpc(RpcParams rp = default)
        {
            if (Opened.Value) return;
            var inv = NetInventory.ForClient(rp.Receive.SenderClientId);
            if (inv == null || Vector3.Distance(inv.transform.position, transform.position) > 8f) return;
            Opened.Value = true;

            var cat = ContentCatalog.Instance;
            var results = new List<KeyValuePair<ItemDef, int>>();
            int t = Mathf.Clamp(Tier.Value, 1, 3);
            if (cat != null && cat.LootTables.Length >= t && cat.LootTables[t - 1] != null)
                cat.LootTables[t - 1].Roll(new System.Random((int)NetworkObjectId * 7919 + t), results);

            int credits = Random.Range(10, 30) * t;
            inv.Credits.Value += credits;
            Vector3 up = transform.up;
            foreach (var kv in results)
                WorldPickup.SpawnServer(kv.Key.Id, kv.Value, transform.position + up * 1.2f + Random.insideUnitSphere);
            OpenedClientRpc(transform.position, rp.Receive.SenderClientId, credits);
        }

        [ClientRpc]
        void OpenedClientRpc(Vector3 pos, ulong who, int credits)
        {
            Sfx.Play("crate", pos);
            Vfx.PickupSparkle(pos + transform.up, new Color(1f, 0.85f, 0.4f));
            if (NetworkManager.LocalClientId == who) EventBus.RaiseNotification($"+{credits} Credits");
        }

        public static LootCrate SpawnServer(Vector3 pos, Quaternion rot, int tier)
        {
            var cat = ContentCatalog.Instance;
            if (cat == null || cat.LootCrate == null) return null;
            var go = Instantiate(cat.LootCrate, pos, rot);
            var c = go.GetComponent<LootCrate>();
            c.pendingTier = tier;
            c.NetworkObject.Spawn(true);
            return c;
        }

        int pendingTier;
    }
}
