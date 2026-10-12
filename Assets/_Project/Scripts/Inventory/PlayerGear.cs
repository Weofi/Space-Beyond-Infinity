using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>
    /// Player gear tiers (suit, oxygen tank, jetpack, mining tool, rifle) and crafting.
    /// Lives on the player pawn next to its NetInventory. Server-written.
    /// </summary>
    public class PlayerGear : NetworkBehaviour
    {
        public NetworkVariable<int> SuitTier = new NetworkVariable<int>(1);
        public NetworkVariable<int> OxygenTier = new NetworkVariable<int>(1);
        public NetworkVariable<int> JetpackTier = new NetworkVariable<int>(1);
        public NetworkVariable<int> DrillTier = new NetworkVariable<int>(1);
        public NetworkVariable<bool> HasRifle = new NetworkVariable<bool>(false);
        /// <summary>Meta-perk bonuses (applied at run start).</summary>
        public NetworkVariable<float> BonusHp = new NetworkVariable<float>(0f);
        public NetworkVariable<float> BonusOxygen = new NetworkVariable<float>(0f);

        public float MaxHp => 100f + (SuitTier.Value - 1) * 40f + BonusHp.Value;
        public float Armor => 1f - (SuitTier.Value - 1) * 0.12f;
        public float MaxOxygen => (90f + (OxygenTier.Value - 1) * 60f) * (1f + BonusOxygen.Value);
        public float JetpackThrust => 11f + (JetpackTier.Value - 1) * 4f;
        public float JetpackEnergy => 100f + (JetpackTier.Value - 1) * 60f;
        public float MiningPower => 1.2f + (DrillTier.Value - 1) * 0.9f;
        public float MiningDamage => 4f + (DrillTier.Value - 1) * 3f;

        /// <summary>Server: apply a gear item. Returns false if it would not be an upgrade.</summary>
        public bool ApplyGear(ItemDef def)
        {
            if (!IsServer || def == null || def.Kind != ItemKind.Gear) return false;
            switch (def.Gear)
            {
                case GearKind.Suit: return Up(SuitTier, def.GearTier);
                case GearKind.Oxygen: return Up(OxygenTier, def.GearTier);
                case GearKind.Jetpack: return Up(JetpackTier, def.GearTier);
                case GearKind.MiningTool: return Up(DrillTier, def.GearTier);
                case GearKind.Rifle:
                    if (HasRifle.Value) return false;
                    HasRifle.Value = true;
                    return true;
            }
            return false;
        }

        static bool Up(NetworkVariable<int> v, int tier)
        {
            if (tier <= v.Value) return false;
            v.Value = tier;
            return true;
        }

        public void ResetServer()
        {
            if (!IsServer) return;
            SuitTier.Value = 1; OxygenTier.Value = 1; JetpackTier.Value = 1; DrillTier.Value = 1;
            HasRifle.Value = false; BonusHp.Value = 0f; BonusOxygen.Value = 0f;
        }

        // ------------------------------------------------------------ Crafting

        /// <summary>Client: craft a recipe (by index in ContentCatalog.Recipes). Requires a fabricator nearby.</summary>
        [ServerRpc]
        public void CraftServerRpc(int recipeIndex)
        {
            var cat = ContentCatalog.Instance;
            var inv = GetComponent<NetInventory>();
            if (cat == null || inv == null || recipeIndex < 0 || recipeIndex >= cat.Recipes.Length) return;
            var r = cat.Recipes[recipeIndex];
            if (r == null || r.Output == null) return;
            if (!Fabricators.AnyNear(transform.position, Fabricators.UseRange))
            {
                NetInventory.EventBusNotify(OwnerClientId, "Need a ship fabricator nearby");
                return;
            }
            foreach (var ing in r.Inputs)
                if (ing.Item == null || !inv.Has(ing.Item.Id, ing.Count))
                {
                    NetInventory.EventBusNotify(OwnerClientId, "Missing materials");
                    return;
                }
            foreach (var ing in r.Inputs) inv.Remove(ing.Item.Id, ing.Count);
            NetInventory.GiveOrDrop(OwnerClientId, r.Output.Id, r.OutputCount, transform.position);
            CraftedClientRpc(r.Output.DisplayName);
        }

        [ClientRpc]
        void CraftedClientRpc(string name)
        {
            Sfx.Play("craft", transform.position);
            if (IsOwner) EventBus.RaiseNotification("Crafted " + name);
        }

        public static bool CanAfford(NetInventory inv, RecipeDef r)
        {
            if (inv == null || r == null) return false;
            foreach (var ing in r.Inputs) if (ing.Item == null || !inv.Has(ing.Item.Id, ing.Count)) return false;
            return true;
        }
    }

    /// <summary>Registry of fabricator positions (ships register theirs). Crafting needs one nearby.</summary>
    public static class Fabricators
    {
        public const float UseRange = 14f;
        static readonly List<Transform> list = new List<Transform>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => list.Clear();

        public static void Register(Transform t) { if (t != null && !list.Contains(t)) list.Add(t); }
        public static void Unregister(Transform t) => list.Remove(t);

        public static bool AnyNear(Vector3 pos, float range)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) { list.RemoveAt(i); continue; }
                if ((list[i].position - pos).sqrMagnitude < range * range) return true;
            }
            return false;
        }
    }
}
