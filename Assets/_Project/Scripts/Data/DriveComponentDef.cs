using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// One component of the crew's warp drive (#78): core, navigation computer, cooling or Exotic Matter tank, with a
    /// tier 1-6 (Mk I - Mk V, tier 6 = Home Drive core / Warden-grade parts). What a tier does is balanced centrally in
    /// WarpDriveDef; the item that carries the component points here via ItemDef.Drive.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Drive Component", fileName = "Drive_")]
    public class DriveComponentDef : ScriptableObject
    {
        public const int MaxTier = 6;

        public string Id = "drive_core_mk1";
        public string DisplayName = "Warp Core Mk I";
        [TextArea] public string Description = "";
        public DriveSlot Slot = DriveSlot.Core;
        [Range(1, MaxTier)] public int Tier = 1;

        static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI" };

        /// <summary>Stable item id of a component, e.g. drive_core_mk3, drive_tank_mk6.</summary>
        public static string ItemId(DriveSlot slot, int tier) => $"drive_{SlotKey(slot)}_mk{Mathf.Clamp(tier, 1, MaxTier)}";

        public static string SlotKey(DriveSlot slot) =>
            slot == DriveSlot.Core ? "core" : slot == DriveSlot.Navigation ? "nav" : slot == DriveSlot.Cooling ? "cooling" : "tank";

        public static string SlotName(DriveSlot slot) =>
            slot == DriveSlot.Core ? "Warp Core" : slot == DriveSlot.Navigation ? "Nav Computer" : slot == DriveSlot.Cooling ? "Cooling" : "Exotic Tank";

        /// <summary>"Mk III"; tier 6 = "Home Drive" for the core and "Warden" for the other parts.</summary>
        public static string TierName(DriveSlot slot, int tier)
        {
            if (tier >= MaxTier) return slot == DriveSlot.Core ? "Home Drive" : "Warden";
            return "Mk " + Roman[Mathf.Clamp(tier, 1, MaxTier) - 1];
        }

        public static string FullName(DriveSlot slot, int tier) =>
            tier >= MaxTier && slot == DriveSlot.Core ? "Home Drive Core" :
            tier >= MaxTier ? $"Warden {SlotName(slot)}" : $"{SlotName(slot)} {TierName(slot, tier)}";
    }
}
