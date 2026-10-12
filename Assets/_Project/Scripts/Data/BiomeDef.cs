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
        [Tooltip("Base terrain frequency (higher = more hills).")]
        public float Frequency = 3f;
        [Tooltip("Height variation as a fraction of planet radius (0.02-0.08 typical).")]
        public float Amplitude = 0.05f;
        [Range(2, 6)] public int Octaves = 4;
        [Tooltip("Noise below this becomes lowlands/flats.")]
        public float SeaLevel = -0.15f;

        [Header("Colors (vertex colors by elevation)")]
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
