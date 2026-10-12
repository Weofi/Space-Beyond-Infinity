using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Data
{
    public enum PlanetNoiseKind { Simple, Ridged, Billow }

    /// <summary>
    /// Reusable procedural planet configuration (archetype preset): terrain noise layers, craters, ocean and surface shading.
    /// Generation is deterministic for (seed, radius, shape). Heights are in fractions of the planet radius.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Planet Shape", fileName = "Planet_")]
    public class PlanetShapeDef : ScriptableObject
    {
        [Serializable]
        public class NoiseLayer
        {
            public bool Enabled = true;
            public PlanetNoiseKind Kind = PlanetNoiseKind.Simple;
            [Tooltip("Height contribution as a fraction of the planet radius.")]
            public float Strength = 0.02f;
            [Tooltip("Base frequency on the unit sphere.")]
            public float Frequency = 1.5f;
            [Tooltip("Per-axis frequency multiplier. E.g. (1,4,1) gives latitude-aligned bands like wind-driven dunes.")]
            public Vector3 Stretch = Vector3.one;
            [Range(1, 8)] public int Octaves = 4;
            [Tooltip("Amplitude falloff per octave (roughness).")]
            [Range(0f, 1f)] public float Persistence = 0.5f;
            [Tooltip("Frequency gain per octave.")]
            public float Lacunarity = 2f;
            [Tooltip("Noise below this value is cut off (flat lowlands / isolated ranges). Noise is in [0,1].")]
            [Range(0f, 1f)] public float MinValue = 0f;
            [Tooltip("Ridged only: how strongly lower ridges suppress finer octaves (higher = sharper, more isolated ridges).")]
            public float RidgeWeight = 1.5f;
            [Tooltip("Masks this layer with the first layer (e.g. mountains only on continents).")]
            public bool MaskByFirstLayer;
            [Tooltip("First-layer value range mapped to mask 0..1.")]
            public Vector2 MaskRange = new Vector2(0f, 0.25f);
            [Tooltip("0 = off. Quantizes into plateaus with steep steps (mesas).")]
            [Range(0, 12)] public int Terraces;
        }

        public string DisplayName = "Planet";

        [Header("Geometry")]
        [Tooltip("Quads per cube-face edge for the near LOD (also used for the collider).")]
        [Range(16, 160)] public int Resolution = 96;
        [Tooltip("Quads per cube-face edge for the far LOD.")]
        [Range(8, 64)] public int FarResolution = 32;

        [Header("Terrain")]
        [Tooltip("Global multiplier for all layer and crater heights.")]
        public float HeightMultiplier = 1f;
        [Tooltip("Domain warp (fraction of the unit sphere) for irregular, less blobby shapes.")]
        [Range(0f, 0.5f)] public float Warp = 0.1f;
        public List<NoiseLayer> Layers = new List<NoiseLayer>();

        [Header("Craters")]
        public int CraterCount;
        [Tooltip("Crater radius range as a fraction of the planet radius.")]
        public Vector2 CraterSize = new Vector2(0.03f, 0.2f);
        [Tooltip("Size distribution exponent: higher = more small craters.")]
        [Range(1f, 6f)] public float CraterSizeBias = 3f;
        [Tooltip("Bowl depth relative to crater radius.")]
        [Range(0f, 1f)] public float CraterDepth = 0.22f;
        [Tooltip("Rim height relative to crater radius.")]
        [Range(0f, 0.3f)] public float CraterRim = 0.06f;
        [Tooltip("Rim falloff width relative to crater radius.")]
        [Range(0.05f, 1.5f)] public float CraterRimWidth = 0.5f;
        [Tooltip("Flat floor level (0 = no floor, 1 = very shallow). Randomized per crater around this value.")]
        [Range(0f, 0.9f)] public float CraterFloor = 0.35f;
        [Tooltip("Wall steepness exponent range (2 = smooth bowl, 6 = steep walls).")]
        public Vector2 CraterSteepness = new Vector2(2f, 5f);
        [Tooltip("Craters larger than this (fraction of radius) get a central peak.")]
        public float CentralPeakSize = 0.12f;
        [Tooltip("Rim irregularity.")]
        [Range(0f, 0.5f)] public float CraterIrregularity = 0.15f;

        [Header("Ocean")]
        public bool HasOcean;
        [Tooltip("Fraction of the surface below sea level, picked per seed in this range. The sea radius is derived from it.")]
        public Vector2 OceanCoverage = new Vector2(0.5f, 0.7f);
        [Tooltip("Multiplies terrain depth below sea level (past a short coastal shelf): deeper oceans, crisper coastlines.")]
        [Range(1f, 8f)] public float OceanFloorDepth = 1f;
        public Color ShallowWater = new Color(0.25f, 0.85f, 0.85f);
        public Color DeepWater = new Color(0.04f, 0.18f, 0.45f);
        [Tooltip("Water depth (m) at which it reaches the deep color / full opacity.")]
        public float WaterDepth = 14f;
        [ColorUsage(false, true)] public Color WaterEmission = Color.black;
        [Range(0f, 1f)] public float WaterGloss = 0.9f;
        [Range(0f, 2f)] public float WaveStrength = 0.5f;

        [Header("Surface colors")]
        [Tooltip("Lowest seabed (0) to sea level (1).")]
        public Gradient Underwater = new Gradient();
        [Tooltip("Shore / lowest land (0) to highest peak (1).")]
        public Gradient Land = new Gradient();
        [Tooltip("Steep-slope color by elevation (0 = lowest, 1 = highest).")]
        public Gradient Cliff = new Gradient();
        [Tooltip("Slope (1 - dot(normal, up)) where cliff blending starts / is complete.")]
        public Vector2 CliffSlope = new Vector2(0.12f, 0.3f);
        [Tooltip("Per-seed hue shift range (0-1 hue units).")]
        [Range(0f, 0.2f)] public float HueJitter = 0.03f;

        [Header("Surface material")]
        [Tooltip("Triplanar detail texture tiling in meters.")]
        public float DetailScale = 6f;
        [Range(0f, 1f)] public float DetailStrength = 0.35f;
        [Range(0f, 2f)] public float DetailNormalStrength = 0.6f;
        [Range(0f, 1f)] public float Gloss = 0.08f;
        [Tooltip("Optional overrides for the shared detail textures.")]
        public Texture2D DetailTexture;
        public Texture2D DetailNormal;
    }
}
