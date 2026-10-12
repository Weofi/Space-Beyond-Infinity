using System;
using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>Planet biome: terrain shape, colors, atmosphere and surface scatter table.</summary>
    [CreateAssetMenu(menuName = "Voidward/Biome", fileName = "Biome_")]
    public class BiomeDef : ScriptableObject
    {
        [Serializable]
        public class ScatterEntry
        {
            public ScatterKind Kind = ScatterKind.OreNode;
            public int Count = 6;
        }

        public Biome Biome = Biome.Ice;
        public string DisplayName = "Ice";

        [Header("Terrain")]
        public PlanetShapeDef Shape;

        [Header("Decor colors")]
        public Color LowColor = new Color(0.75f, 0.85f, 0.95f);
        public Color MidColor = new Color(0.9f, 0.93f, 0.97f);
        public Color HighColor = new Color(1f, 1f, 1f);
        public Color ScatterTint = new Color(0.8f, 0.9f, 1f);

        [Header("Atmosphere")]
        public bool HasAtmosphere = true;
        public Color AtmosphereColor = new Color(0.5f, 0.8f, 1f, 0.25f);
        [Tooltip("Oxygen refill while on this planet's surface.")]
        public bool Breathable = true;

        [Header("Surface scatter")]
        public List<ScatterEntry> Scatter = new List<ScatterEntry>();
    }
}
