using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>Definition of an inventory item (resource, module or consumable).</summary>
    [CreateAssetMenu(menuName = "Voidward/Item", fileName = "Item_")]
    public class ItemDef : ScriptableObject
    {
        [Tooltip("Unique stable id, e.g. 'ore', 'crystal', 'module_engine_mk2'.")]
        public string Id = "item";
        public string DisplayName = "Item";
        [TextArea] public string Description = "";
        public ItemKind Kind = ItemKind.Resource;
        [Tooltip("Only for Kind=Resource.")]
        public ResourceType Resource;
        public int MaxStack = 99;
        public int CreditValue = 5;
        [Tooltip("Tint used for inventory icons and world pickups.")]
        public Color Tint = Color.white;
        [Tooltip("Set when Kind=Module: which module this item installs.")]
        public ModuleDef Module;
        [Tooltip("Set when Kind=Gear.")]
        public GearKind Gear;
        [Range(1, 3)] public int GearTier = 1;
        [Tooltip("Kind=Consumable: HP restored.")]
        public float HealAmount;
        [Tooltip("Can be bought at trader stations.")]
        public bool SoldAtTrader = true;
    }
}
