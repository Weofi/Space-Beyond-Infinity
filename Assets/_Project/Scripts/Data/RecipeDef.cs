using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Data
{
    /// <summary>Crafting recipe used at a ship fabricator.</summary>
    [CreateAssetMenu(menuName = "Voidward/Recipe", fileName = "Recipe_")]
    public class RecipeDef : ScriptableObject
    {
        [Serializable]
        public class Ingredient
        {
            public ItemDef Item;
            public int Count = 1;
        }

        public string Id = "recipe";
        public List<Ingredient> Inputs = new List<Ingredient>();
        public ItemDef Output;
        public int OutputCount = 1;

        public string InputSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var i in Inputs)
                if (i.Item != null) sb.Append(i.Count).Append("x ").Append(i.Item.DisplayName).Append("  ");
            return sb.ToString().TrimEnd();
        }
    }
}
