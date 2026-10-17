using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    public enum Rarity { Common, Uncommon, Rare, Epic }

    /// <summary>
    /// Loot rarity (#43), derived from an item's salvage value so it needs no extra data: raw resources are Common,
    /// cells / consumables Uncommon, Mk II gear and modules Rare, Mk III and better Epic. Drives the pickup
    /// colour, light beam, sparkle and sound.
    /// </summary>
    public static class LootRarity
    {
        public static Rarity Of(ItemDef d)
        {
            if (d == null) return Rarity.Common;
            int v = d.SalvageValue;
            if (d.Kind == ItemKind.Gear && d.GearTier >= 3) return Rarity.Epic;
            if (v >= 250) return Rarity.Epic;
            if (v >= 100) return Rarity.Rare;
            if (v >= 20) return Rarity.Uncommon;
            return Rarity.Common;
        }

        /// <summary>Display colour (sRGB, LDR) for beams, sparkles and UI accents.</summary>
        public static Color ColorOf(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return new Color(0.45f, 1f, 0.55f);
                case Rarity.Rare: return new Color(0.35f, 0.72f, 1f);
                case Rarity.Epic: return new Color(1f, 0.74f, 0.25f);
                default: return new Color(0.82f, 0.87f, 0.95f);
            }
        }

        public static string NameOf(Rarity r) => r.ToString();
    }
}
