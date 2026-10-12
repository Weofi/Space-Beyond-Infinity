using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>
    /// Networked item drop. Static (no physics) — snapped to the ground on spawn when near a planet.
    /// Collected by interacting or by walking close (auto-magnet driven by the local pawn).
    /// </summary>
    public class WorldPickup : NetworkBehaviour, IInteractable
    {
        public NetworkVariable<FixedString64Bytes> ItemId = new NetworkVariable<FixedString64Bytes>();
        public NetworkVariable<int> Amount = new NetworkVariable<int>(1);

        [SerializeField] Transform visual;

        public static readonly List<WorldPickup> All = new List<WorldPickup>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        bool requested;
        float requestTime;
        Vector3 basePos;

        public ItemDef Def => ContentCatalog.GetItem(ItemId.Value.ToString());

        public string Prompt
        {
            get
            {
                var d = Def;
                return $"Pick up {(d != null ? d.DisplayName : "item")} x{Amount.Value}";
            }
        }

        public bool CanInteract(IInteractAgent agent) => IsSpawned;
        public void Interact(IInteractAgent agent) => RequestPickup();

        /// <summary>Local: ask the server to collect (throttled).</summary>
        public void RequestPickup()
        {
            if (requested && Time.time - requestTime < 1f) return;
            requested = true;
            requestTime = Time.time;
            PickupServerRpc();
        }

        public static WorldPickup SpawnServer(string id, int count, Vector3 pos)
        {
            var cat = ContentCatalog.Instance;
            var nm = NetworkManager.Singleton;
            if (cat == null || cat.WorldPickup == null || nm == null || !nm.IsServer || count <= 0) return null;

            // Snap to ground if a planet is close below.
            var src = GravitySource.Dominant(pos);
            Quaternion rot = Quaternion.identity;
            if (src != null)
            {
                Vector3 down = (src.Center - pos).normalized;
                if (Physics.Raycast(pos - down * 3f, down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    pos = hit.point - down * 0.5f;
                else if (src.SurfaceDistance(pos) < 30f)
                    pos = src.GroundPoint(pos) - down * 0.5f;
                rot = Quaternion.FromToRotation(Vector3.up, -down);
            }
            pos += Random.insideUnitSphere * 0.6f;

            var go = Instantiate(cat.WorldPickup, pos, rot);
            var p = go.GetComponent<WorldPickup>();
            p.pendingId = id;
            p.pendingAmount = count;
            p.NetworkObject.Spawn(true);
            return p;
        }

        string pendingId;
        int pendingAmount;

        public override void OnNetworkSpawn()
        {
            if (IsServer && pendingId != null)
            {
                ItemId.Value = pendingId;
                Amount.Value = pendingAmount;
            }
            All.Add(this);
            basePos = visual != null ? visual.localPosition : Vector3.zero;
            ApplyTint();
            ItemId.OnValueChanged += (a, b) => ApplyTint();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        /// <summary>Shows the item's own model (fallback: the generic capsule tinted in the item colour).</summary>
        void ApplyTint()
        {
            var d = Def;
            if (d == null || visual == null) return;
            var mf = visual.GetComponentInChildren<MeshFilter>();
            if (mf != null && genericMesh == null) genericMesh = mf.sharedMesh;
            bool model = d.Model != null && mf != null;
            if (mf != null) mf.sharedMesh = model ? d.Model : genericMesh;
            if (mf != null) mf.transform.localScale = Vector3.one * (model ? 1.25f : 1f);
            if (mf != null) mf.transform.localPosition = model ? new Vector3(0f, -0.25f, 0f) : Vector3.zero;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", model ? Color.white : d.Tint);
            mpb.SetColor("_EmissionColor", model ? Color.black : d.Tint * 0.35f);
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }

        Mesh genericMesh;

        void Update()
        {
            if (visual == null) return;
            visual.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
            visual.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * 2.5f + NetworkObjectId) * 0.12f);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PickupServerRpc(RpcParams rp = default)
        {
            if (!IsSpawned) return;
            var inv = NetInventory.ForClient(rp.Receive.SenderClientId);
            if (inv == null) return;
            if (Vector3.Distance(inv.transform.position, transform.position) > 8f) return;
            string id = ItemId.Value.ToString();
            int left = inv.Add(id, Amount.Value);
            int taken = Amount.Value - left;
            if (taken > 0)
            {
                PickedUpClientRpc(transform.position, rp.Receive.SenderClientId, id, taken);
                LootStats.Collected?.Invoke(rp.Receive.SenderClientId, taken);
            }
            if (left <= 0) NetworkObject.Despawn(true);
            else
            {
                Amount.Value = left;
                NetInventory.EventBusNotify(rp.Receive.SenderClientId, "Inventory full");
            }
        }

        [ClientRpc]
        void PickedUpClientRpc(Vector3 pos, ulong who, FixedString64Bytes id, int count)
        {
            var d = ContentCatalog.GetItem(id.ToString());
            Vfx.PickupSparkle(pos, d != null ? d.Tint : Color.white);
            if (NetworkManager.LocalClientId == who)
            {
                Sfx.PlayUI("pickup");
                EventBus.RaiseNotification($"+{count} {(d != null ? d.DisplayName : id.ToString())}");
            }
        }
    }

    /// <summary>Server-side loot bookkeeping hooks (run stats).</summary>
    public static class LootStats
    {
        public static System.Action<ulong, int> Collected;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Collected = null;
    }
}
