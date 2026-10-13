using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Data
{
    public enum PlanetNoiseKind { Simple, Ridged, Billow }

    /// <summary>Large point-shaped landforms placed on the sphere.</summary>
    public enum PlanetFeatureKind { Volcano, Mesa, Spire, Basin, Dome }

    /// <summary>Biome-specific surface composition (props, set pieces).</summary>
    public enum PlanetDecorStyle { Barren, Rocky, Desert, Ice, Jungle, Crystal, Volcanic }

    public enum PlanetParticles { None, Dust, Snow, Ash, Spores, Motes }

    public enum PlanetWeather { None, Sandstorm, Blizzard, AshFall, SporeBloom, Shimmer }

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

        [Serializable]
        public class FeatureSet
        {
            public bool Enabled = true;
            public PlanetFeatureKind Kind = PlanetFeatureKind.Mesa;
            [Tooltip("Number of features per planet (min, max).")]
            public Vector2Int Count = new Vector2Int(2, 4);
            [Tooltip("Footprint radius range as a fraction of the planet radius.")]
            public Vector2 Size = new Vector2(0.04f, 0.1f);
            [Tooltip("Height (or depth for basins) as a fraction of the footprint radius.")]
            public Vector2 Height = new Vector2(0.3f, 0.6f);
            [Tooltip("Profile steepness: 0 = soft hill, 1 = cliff-sided.")]
            [Range(0f, 1f)] public float Sharpness = 0.5f;
            [Range(0, 10)] public int Terraces;
            [Tooltip("Erosion gullies / outline irregularity.")]
            [Range(0f, 1f)] public float Roughness = 0.3f;
            [Tooltip("Volcano: caldera radius as a fraction of the footprint.")]
            [Range(0f, 0.6f)] public float Caldera = 0.2f;
        }

        public string DisplayName = "Planet";

        [Header("Geometry")]
        [Tooltip("Quads per chunk edge. Chunks subdivide around the camera (quadtree LOD).")]
        [Range(8, 64)] public int ChunkResolution = 40;
        [Tooltip("Target ground spacing (m) of the finest LOD; also the collider resolution.")]
        [Range(0.75f, 6f)] public float LeafSpacing = 1.6f;
        [Tooltip("Higher = finer distant terrain (split when distance < chunk size * this).")]
        [Range(1f, 4f)] public float LodQuality = 1.6f;

        [Header("Terrain")]
        [Tooltip("Global multiplier for all layer and crater heights.")]
        public float HeightMultiplier = 1f;
        [Tooltip("Domain warp (fraction of the unit sphere) for irregular, less blobby shapes.")]
        [Range(0f, 0.5f)] public float Warp = 0.1f;
        public List<NoiseLayer> Layers = new List<NoiseLayer>();
        [Tooltip("Per-seed jitter of layer strengths, feature counts and canyon settings (0 = identical planets).")]
        [Range(0f, 0.6f)] public float Variation = 0.2f;

        [Header("Landforms")]
        public List<FeatureSet> Features = new List<FeatureSet>();

        [Header("Canyons / valleys")]
        public bool Canyons;
        [Tooltip("Network density on the unit sphere.")]
        public float CanyonFrequency = 1.6f;
        [Tooltip("Channel width in noise units (~0.02 narrow chasm, ~0.12 broad valley).")]
        public float CanyonWidth = 0.06f;
        [Tooltip("Depth as a fraction of the planet radius.")]
        public float CanyonDepth = 0.03f;
        [Tooltip("Flat floor portion of the channel (0 = V-shaped).")]
        [Range(0f, 0.9f)] public float CanyonFloor = 0.35f;
        [Tooltip("Wall profile exponent (1 = straight slopes, 3+ = sheer walls).")]
        [Range(0.5f, 6f)] public float CanyonSteepness = 2f;
        [Range(0, 8)] public int CanyonTerraces;
        [Tooltip("Fraction of the planet the network cuts through.")]
        [Range(0f, 1f)] public float CanyonCoverage = 0.6f;
        [Tooltip("Winding (domain warp) of the channels.")]
        [Range(0f, 1f)] public float CanyonWarp = 0.35f;
        [Tooltip("Carve down to sea level so the ocean fills the channels (rivers, lava channels).")]
        public bool CanyonRivers;

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
        [Tooltip("The ocean is lava: dark flowing crust with glowing cracks instead of water optics.")]
        public bool Lava;
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
        [Tooltip("Alternate land colors (shore to peak) blended in by a large-scale region mask.")]
        public Gradient Region = new Gradient();
        public float RegionFrequency = 1.4f;
        [Range(0f, 1f)] public float RegionStrength;

        [Header("Sites")]
        [Tooltip("Points of interest per planet. Each gets a flattened landing clearing.")]
        public Vector2Int SiteCount = new Vector2Int(6, 8);
        [Tooltip("Clearing radius in meters.")]
        public float SiteRadius = 34f;

        [Header("Atmosphere")]
        public bool HasAtmosphere = true;
        [Tooltip("Scattering color (day sky / limb glow).")]
        public Color AtmosphereColor = new Color(0.45f, 0.7f, 1f);
        [Tooltip("Color near the day/night terminator.")]
        public Color SunsetColor = new Color(1f, 0.55f, 0.4f);
        [Tooltip("Shell height as a fraction of the radius.")]
        [Range(0.05f, 0.6f)] public float AtmosphereHeight = 0.22f;
        [Tooltip("Optical thickness: higher = hazier surface and denser sky.")]
        [Range(0f, 6f)] public float AtmosphereDensity = 1.6f;
        [Tooltip("How fast density drops with altitude (higher = thin upper layers).")]
        [Range(0.5f, 10f)] public float AtmosphereFalloff = 4f;

        [Header("Clouds")]
        [Range(0f, 1f)] public float CloudCoverage = 0.4f;
        public Color CloudColor = Color.white;
        public Color CloudShadeColor = new Color(0.6f, 0.68f, 0.85f);
        [Tooltip("Cloud layer altitude as a fraction of the atmosphere height.")]
        [Range(0.1f, 0.95f)] public float CloudAltitude = 0.55f;
        [Tooltip("Feature size of cloud banks (higher = smaller clouds).")]
        public float CloudScale = 3f;
        [Tooltip("Latitude banding / stretch of cloud shapes.")]
        [Range(0f, 4f)] public float CloudStretch = 1f;

        [Header("Environment")]
        public PlanetDecorStyle Decor = PlanetDecorStyle.Rocky;
        [Tooltip("Overall prop density multiplier.")]
        [Range(0f, 3f)] public float DecorDensity = 1f;
        [Tooltip("Decor palette: 0 rock, 1 flora/body, 2 accent, 3 glow.")]
        public Color[] DecorColors = { new Color(0.45f, 0.42f, 0.4f), new Color(0.4f, 0.6f, 0.35f), new Color(0.9f, 0.8f, 0.6f), new Color(0.4f, 0.9f, 1f) };
        public PlanetParticles Particles = PlanetParticles.Dust;
        public Color ParticleColor = new Color(1f, 0.9f, 0.75f, 0.5f);
        public PlanetWeather Weather = PlanetWeather.None;
        [Tooltip("Surface haze color (sky horizon, distance fog).")]
        public Color HazeColor = new Color(0.6f, 0.75f, 0.95f);
        [Tooltip("Daytime ambient boost tint on the surface.")]
        public Color SurfaceAmbient = new Color(0.45f, 0.5f, 0.6f);
        [Tooltip("Pitch of the procedural wind ambience.")]
        [Range(0.3f, 2f)] public float WindPitch = 1f;

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
