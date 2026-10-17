using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>One inventory slot. Empty when Count == 0.</summary>
    public struct ItemStack : INetworkSerializable, IEquatable<ItemStack>
    {
        public FixedString64Bytes Id;
        public int Count;

        public bool IsEmpty => Count <= 0;
        public string IdString => Id.ToString();
        public ItemDef Def => IsEmpty ? null : ContentCatalog.GetItem(Id.ToString());

        public ItemStack(string id, int count) { Id = id; Count = count; }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Id);
            s.SerializeValue(ref Count);
        }

        public bool Equals(ItemStack o) => Count == o.Count && Id.Equals(o.Id);
        public override bool Equals(object obj) => obj is ItemStack o && Equals(o);
        public override int GetHashCode() => Id.GetHashCode() ^ Count;
    }

    /// <summary>
    /// Server-authoritative grid inventory (players and ship cargo). Slots are a NetworkList so
    /// every client renders the grid; all mutations go through the server.
    /// Players additionally hold Salvage (the run currency).
    /// </summary>
    public class NetInventory : NetworkBehaviour
    {
        public int Width = 6;
        public int Height = 4;
        [Tooltip("Player inventories carry salvage; ship cargo does not.")]
        public bool IsPlayerInventory = true;

        public NetworkList<ItemStack> Slots;
        public NetworkVariable<int> Salvage = new NetworkVariable<int>(0);

        /// <summary>Every client: slots or salvage changed.</summary>
        public event Action Changed;
        /// <summary>Server: a slot's item was "used" (consumables/gear). Return true if consumed.</summary>
        public Func<ItemDef, bool> UseHandler;

        public int SlotCount => Slots != null ? Slots.Count : 0;

        static readonly List<NetInventory> all = new List<NetInventory>();
        public static IReadOnlyList<NetInventory> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void Awake()
        {
            Slots = new NetworkList<ItemStack>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && Slots.Count == 0) Resize(Width * Height);
            Slots.OnListChanged += OnListChanged;
            Salvage.OnValueChanged += OnSalvageChanged;
            all.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            Slots.OnListChanged -= OnListChanged;
            Salvage.OnValueChanged -= OnSalvageChanged;
            all.Remove(this);
        }

        void OnListChanged(NetworkListEvent<ItemStack> e) => Changed?.Invoke();
        void OnSalvageChanged(int a, int b) => Changed?.Invoke();

        /// <summary>Server: grow/shrink slot count (cargo modules). Shrinking drops nothing — overflow slots stay until emptied.</summary>
        public void Resize(int count)
        {
            if (!IsServer) return;
            while (Slots.Count < count) Slots.Add(default);
            for (int i = Slots.Count - 1; i >= count; i--)
            {
                if (!Slots[i].IsEmpty) break;
                Slots.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------ Queries (any peer)

        public int Count(string id)
        {
            int n = 0;
            for (int i = 0; i < Slots.Count; i++)
                if (!Slots[i].IsEmpty && Slots[i].Id.ToString() == id) n += Slots[i].Count;
            return n;
        }

        public bool Has(string id, int count) => Count(id) >= count;

        /// <summary>How many of an item could still fit.</summary>
        public int SpaceFor(string id)
        {
            var def = ContentCatalog.GetItem(id);
            int max = def != null ? Mathf.Max(1, def.MaxStack) : 99;
            int space = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty) space += max;
                else if (s.Id.ToString() == id) space += Mathf.Max(0, max - s.Count);
            }
            return space;
        }

        // ------------------------------------------------------------ Server mutations

        /// <summary>Server: add items, returns the amount that did NOT fit.</summary>
        public int Add(string id, int count)
        {
            if (!IsServer || count <= 0 || string.IsNullOrEmpty(id)) return count;
            var def = ContentCatalog.GetItem(id);
            if (def == null) { Debug.LogWarning("[Inventory] Unknown item " + id); return count; }
            int max = Mathf.Max(1, def.MaxStack);

            for (int i = 0; i < Slots.Count && count > 0; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty || s.Id.ToString() != id || s.Count >= max) continue;
                int add = Mathf.Min(max - s.Count, count);
                s.Count += add;
                count -= add;
                Slots[i] = s;
            }
            for (int i = 0; i < Slots.Count && count > 0; i++)
            {
                if (!Slots[i].IsEmpty) continue;
                int add = Mathf.Min(max, count);
                Slots[i] = new ItemStack(id, add);
                count -= add;
            }
            return count;
        }

        /// <summary>Server: remove items if enough are present.</summary>
        public bool Remove(string id, int count)
        {
            if (!IsServer || count <= 0) return count <= 0;
            if (Count(id) < count) return false;
            for (int i = Slots.Count - 1; i >= 0 && count > 0; i--)
            {
                var s = Slots[i];
                if (s.IsEmpty || s.Id.ToString() != id) continue;
                int take = Mathf.Min(s.Count, count);
                s.Count -= take;
                count -= take;
                Slots[i] = s.Count > 0 ? s : default;
            }
            return true;
        }

        public void ClearAll()
        {
            if (!IsServer) return;
            for (int i = 0; i < Slots.Count; i++) Slots[i] = default;
        }

        // ------------------------------------------------------------ Client requests

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MoveSlotServerRpc(int from, int to)
        {
            if (!Valid(from) || !Valid(to) || from == to) return;
            var a = Slots[from];
            var b = Slots[to];
            if (!a.IsEmpty && !b.IsEmpty && a.Id.Equals(b.Id))
            {
                var def = a.Def;
                int max = def != null ? def.MaxStack : 99;
                int move = Mathf.Min(max - b.Count, a.Count);
                b.Count += move; a.Count -= move;
                Slots[to] = b;
                Slots[from] = a.Count > 0 ? a : default;
            }
            else
            {
                Slots[from] = b;
                Slots[to] = a;
            }
        }

        /// <summary>Move a whole stack into another inventory (player &lt;-&gt; ship cargo).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TransferServerRpc(int slot, NetworkObjectReference target, RpcParams rp = default)
        {
            if (!Valid(slot) || !target.TryGet(out var no)) return;
            var dest = no.GetComponent<NetInventory>();
            if (dest == null || dest == this) return;
            if (Vector3.Distance(dest.transform.position, transform.position) > 60f) return;
            var s = Slots[slot];
            if (s.IsEmpty) return;
            int left = dest.Add(s.Id.ToString(), s.Count);
            s.Count = left;
            Slots[slot] = left > 0 ? s : default;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DropSlotServerRpc(int slot, Vector3 pos)
        {
            if (!Valid(slot)) return;
            var s = Slots[slot];
            if (s.IsEmpty) return;
            Slots[slot] = default;
            WorldPickup.SpawnServer(s.Id.ToString(), s.Count, pos);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UseSlotServerRpc(int slot)
        {
            if (!Valid(slot)) return;
            var s = Slots[slot];
            var def = s.Def;
            if (def == null || UseHandler == null) return;
            if (UseHandler(def))
            {
                s.Count--;
                Slots[slot] = s.Count > 0 ? s : default;
            }
        }

        bool Valid(int i) => i >= 0 && i < Slots.Count;

        // ------------------------------------------------------------ Helpers

        /// <summary>Server: the player inventory of a client (on their player object).</summary>
        public static NetInventory ForClient(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return null;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var cc) || cc.PlayerObject == null) return null;
            return cc.PlayerObject.GetComponent<NetInventory>();
        }

        /// <summary>Server: add to player, overflow drops as pickup at their feet.</summary>
        public static void GiveOrDrop(ulong clientId, string id, int count, Vector3 fallbackPos)
        {
            var inv = ForClient(clientId);
            int left = inv != null ? inv.Add(id, count) : count;
            if (left > 0)
            {
                WorldPickup.SpawnServer(id, left, inv != null ? inv.transform.position + inv.transform.up : fallbackPos);
                EventBusNotify(clientId, "Inventory full - items dropped");
            }
        }

        /// <summary>Server: targeted toast to one client (implemented by the player object).</summary>
        public static Action<ulong, string> NotifyClient;
        public static void EventBusNotify(ulong clientId, string msg) => NotifyClient?.Invoke(clientId, msg);
    }
}
