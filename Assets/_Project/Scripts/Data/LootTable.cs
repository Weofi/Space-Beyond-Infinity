using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Data
{
    /// <summary>Weighted loot table. Roll returns item id + count pairs.</summary>
    [CreateAssetMenu(menuName = "Voidward/Loot Table", fileName = "Loot_")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public ItemDef Item;
            public int Min = 1;
            public int Max = 1;
            public float Weight = 1f;
        }

        public List<Entry> Entries = new List<Entry>();
        [Tooltip("How many times the table is rolled when opened/killed.")]
        public int Rolls = 2;

        public void Roll(System.Random rng, List<KeyValuePair<ItemDef, int>> results)
        {
            if (Entries.Count == 0) return;
            float total = 0f;
            foreach (var e in Entries) total += Mathf.Max(0f, e.Weight);
            if (total <= 0f) return;

            for (int r = 0; r < Rolls; r++)
            {
                float pick = (float)rng.NextDouble() * total;
                foreach (var e in Entries)
                {
                    pick -= Mathf.Max(0f, e.Weight);
                    if (pick <= 0f)
                    {
                        if (e.Item != null)
                            results.Add(new KeyValuePair<ItemDef, int>(e.Item, rng.Next(e.Min, e.Max + 1)));
                        break;
                    }
                }
            }
        }
    }
}
