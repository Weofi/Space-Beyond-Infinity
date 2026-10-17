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
        public int SalvageValue = 5;
        [Tooltip("Accent colour for item names and fallback swatches.")]
        public Color Tint = Color.white;
        [Tooltip("Generated (ContentBuilder item art): UI icon rendered from Model.")]
        public Sprite Icon;
        [Tooltip("Generated (ContentBuilder item art): world pickup model, ~0.5 m, vertex-coloured for VW_Hull.")]
        public Mesh Model;
        [Tooltip("Generated: materials of an asset model (#99, one per submesh); empty = the procedural VW_Hull model.")]
        public Material[] ModelMaterials;
        [Tooltip("Set when Kind=Module: which module this item installs.")]
        public ModuleDef Module;
        [Tooltip("Set when Kind=DriveComponent: which warp drive component this item installs (#78).")]
        public DriveComponentDef Drive;
        [Tooltip("Set when Kind=Gear.")]
        public GearKind Gear;
        [Range(1, 3)] public int GearTier = 1;
        [Tooltip("Kind=Consumable: HP restored.")]
        public float HealAmount;
        [Tooltip("Can be bought at trader stations.")]
        public bool SoldAtTrader = true;
    }
}
