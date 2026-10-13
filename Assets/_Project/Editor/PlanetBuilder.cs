using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Voidward.Core;
using Voidward.Data;
using Voidward.Planet;
using MathUtils = Voidward.Core.MathUtils;

namespace Voidward.EditorTools
{
    /// <summary>Procedural planet content: planet/ocean materials, detail textures, archetype presets and the preview scene.</summary>
    public static partial class ContentBuilder
    {
        public const string PlanetDir = DataDir + "/Planets";
        public const string TextureDir = ArtDir + "/Textures";
        public const string PlanetPreviewScenePath = SceneDir + "/PlanetPreview.unity";

        public static Material MatPlanet, MatOcean, MatClouds;
        static readonly Dictionary<string, PlanetShapeDef> planetShapes = new Dictionary<string, PlanetShapeDef>();
        static readonly string[] ArchetypeIds = { "CrateredMoon", "Rocky", "Ocean", "Desert" };

        // ================================================================ materials + textures

        static void BuildPlanetMaterials()
        {
            BuildDetailTextures(out var detail, out var normal);
            MatPlanet = Mat("VW_Planet", "VOIDWARD/Planet");
            MatPlanet.SetTexture("_DetailTex", detail);
            MatPlanet.SetTexture("_DetailNormal", normal);
            // Soft lit terrain (#70): explicit, because Mat() keeps values of an existing material.
            MatPlanet.SetFloat("_Wrap", 0.25f);
            MatPlanet.SetFloat("_AmbientBoost", 0.45f);
            MatPlanet.SetFloat("_MacroStrength", 0.22f);
            MatPlanet.SetFloat("_CavityStrength", 2.5f);
            MatPlanet.SetFloat("_StrataStrength", 0.14f);
            MatOcean = Mat("VW_Ocean", "VOIDWARD/Ocean");
            MatOcean.SetTexture("_DetailNormal", normal);
            MatOcean.SetTexture("_DetailTex", detail);
            MatClouds = Mat("VW_Clouds", "VOIDWARD/Clouds");
        }

        /// <summary>Tileable rock-noise albedo detail and the matching normal map (both linear, 512x512).</summary>
        static void BuildDetailTextures(out Texture2D detail, out Texture2D normal)
        {
            const int size = 512;
            var h = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float sum = 0f, amp = 1f;
                    for (int o = 0; o < 6; o++)
                    {
                        int period = 4 << o;
                        float n = TileNoise(u * period, v * period, period, 71 + o);
                        // Mix smooth and ridged noise: pebbly grain with crack-like creases.
                        sum += (o < 3 ? n : 1f - Mathf.Abs(n * 2f - 1f)) * amp;
                        amp *= 0.55f;
                    }
                    h[y * size + x] = sum;
                    min = Mathf.Min(min, sum); max = Mathf.Max(max, sum);
                }
            var albedo = new Color[size * size];
            var normals = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = Mathf.InverseLerp(min, max, h[y * size + x]);
                    albedo[y * size + x] = new Color(v, v, v, 1f);
                    float dx = H(x + 1, y) - H(x - 1, y);
                    float dy = H(x, y + 1) - H(x, y - 1);
                    var n = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                    normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            float H(int x, int y) => Mathf.InverseLerp(min, max, h[((y + size) % size) * size + (x + size) % size]);

            detail = SaveTexture("PlanetDetail", size, albedo);
            normal = SaveTexture("PlanetDetailNormal", size, normals);
        }

        // Periodic 2D gradient noise in [0,1] (gradient noise avoids the blocky look of value noise).
        static float TileNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float G(int ix, int iy, float dx, float dy)
            {
                float a = MathUtils.Hash3(seed, ((ix % period) + period) % period, ((iy % period) + period) % period, 0) * Mathf.PI * 2f;
                return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
            }
            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float n = Mathf.Lerp(Mathf.Lerp(G(x0, y0, fx, fy), G(x0 + 1, y0, fx - 1f, fy), u),
                                 Mathf.Lerp(G(x0, y0 + 1, fx, fy - 1f), G(x0 + 1, y0 + 1, fx - 1f, fy - 1f), u), v);
            return Mathf.Clamp01(n * 0.7f + 0.5f);
        }

        static Texture2D SaveTexture(string name, int size, Color[] pixels)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels(pixels);
            tex.Apply(true);
            string path = TextureDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(tex, path);
                return tex;
            }
            EditorUtility.CopySerialized(tex, existing);
            Object.DestroyImmediate(tex);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ================================================================ presets

        static Gradient Grad(params (float t, string hex)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) colors[i] = new GradientColorKey(MathUtils.Hex(keys[i].hex), keys[i].t);
            var g = new Gradient { mode = GradientMode.Blend };
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static PlanetShapeDef.NoiseLayer Layer(PlanetNoiseKind kind, float strength, float freq, int octaves, float persistence = 0.5f,
            float min = 0f, float lacunarity = 2f, float ridgeWeight = 1.5f, Vector2? mask = null, int terraces = 0, Vector3? stretch = null) => new PlanetShapeDef.NoiseLayer
        {
            Kind = kind, Strength = strength, Frequency = freq, Octaves = octaves, Persistence = persistence, MinValue = min,
            Lacunarity = lacunarity, RidgeWeight = ridgeWeight, MaskByFirstLayer = mask.HasValue, MaskRange = mask ?? new Vector2(0f, 0.25f), Terraces = terraces,
            Stretch = stretch ?? Vector3.one,
        };

        static PlanetShapeDef Shape(string id, string name, System.Action<PlanetShapeDef> archetype, System.Action<PlanetShapeDef> tweak = null)
        {
            var s = Asset<PlanetShapeDef>($"{PlanetDir}/Planet_{id}.asset");
            // Reset to defaults so removed builder settings don't linger in the asset.
            var fresh = ScriptableObject.CreateInstance<PlanetShapeDef>();
            EditorUtility.CopySerialized(fresh, s);
            Object.DestroyImmediate(fresh);
            s.name = "Planet_" + id;
            s.DisplayName = name;
            archetype(s);
            tweak?.Invoke(s);
            EditorUtility.SetDirty(s);
            planetShapes[id] = s;
            return s;
        }

        static PlanetShapeDef.FeatureSet Feature(PlanetFeatureKind kind, int min, int max, float sizeMin, float sizeMax, float hMin, float hMax,
            float sharp = 0.5f, int terraces = 0, float rough = 0.3f, float caldera = 0.2f) => new PlanetShapeDef.FeatureSet
        {
            Kind = kind, Count = new Vector2Int(min, max), Size = new Vector2(sizeMin, sizeMax), Height = new Vector2(hMin, hMax),
            Sharpness = sharp, Terraces = terraces, Roughness = rough, Caldera = caldera,
        };

        static void Canyons(PlanetShapeDef s, float freq, float width, float depth, float floor, float steep, float coverage, float warp, int terraces = 0, bool rivers = false)
        {
            s.Canyons = true;
            s.CanyonFrequency = freq; s.CanyonWidth = width; s.CanyonDepth = depth; s.CanyonFloor = floor; s.CanyonSteepness = steep;
            s.CanyonCoverage = coverage; s.CanyonWarp = warp; s.CanyonTerraces = terraces; s.CanyonRivers = rivers;
        }

        static void Atmosphere(PlanetShapeDef s, string color, string sunset, string haze, float height, float density, float falloff)
        {
            s.HasAtmosphere = true;
            s.AtmosphereColor = MathUtils.Hex(color); s.SunsetColor = MathUtils.Hex(sunset); s.HazeColor = MathUtils.Hex(haze);
            s.AtmosphereHeight = height; s.AtmosphereDensity = density; s.AtmosphereFalloff = falloff;
        }

        static void Clouds(PlanetShapeDef s, float coverage, string lit, string shade, float scale = 3f, float stretch = 1f, float altitude = 0.8f)
        {
            s.CloudCoverage = coverage; s.CloudColor = MathUtils.Hex(lit); s.CloudShadeColor = MathUtils.Hex(shade);
            s.CloudScale = scale; s.CloudStretch = stretch; s.CloudAltitude = altitude;
        }

        static void Environment(PlanetShapeDef s, PlanetDecorStyle decor, float density, string rock, string flora, string accent, string glow,
            PlanetParticles particles, Color particleColor, PlanetWeather weather, string ambient, float windPitch)
        {
            s.Decor = decor; s.DecorDensity = density;
            s.DecorColors = new[] { MathUtils.Hex(rock), MathUtils.Hex(flora), MathUtils.Hex(accent), MathUtils.Hex(glow) };
            s.Particles = particles; s.ParticleColor = particleColor; s.Weather = weather;
            s.SurfaceAmbient = MathUtils.Hex(ambient); s.WindPitch = windPitch;
        }

        /// <summary>Gray/brown rock, dense overlapping craters, rugged highlands, giant impact basins.</summary>
        static void CrateredMoon(PlanetShapeDef s)
        {
            s.Warp = 0.08f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.04f, 1.1f, 4),                       // basins / highlands
                Layer(PlanetNoiseKind.Ridged, 0.025f, 2.4f, 5, 0.5f, 0.35f, ridgeWeight: 1.2f), // rugged ridges
                Layer(PlanetNoiseKind.Simple, 0.003f, 18f, 3),                       // roughness
            };
            s.Features = new List<PlanetShapeDef.FeatureSet>
            {
                Feature(PlanetFeatureKind.Basin, 1, 3, 0.16f, 0.28f, 0.08f, 0.14f),
            };
            s.CraterCount = 220;
            s.CraterSize = new Vector2(0.008f, 0.2f);
            s.CraterSizeBias = 3.5f;
            s.CraterDepth = 0.24f;
            s.CraterRim = 0.07f;
            s.CraterRimWidth = 0.45f;
            s.CraterFloor = 0.3f;
            s.CraterSteepness = new Vector2(2f, 5f);
            s.CentralPeakSize = 0.1f;
            s.CraterIrregularity = 0.15f;
            s.HasOcean = false;
            s.HasAtmosphere = false;
            s.CloudCoverage = 0f;
            s.Land = Grad((0f, "#3b3734"), (0.35f, "#5f5a55"), (0.7f, "#8a847d"), (1f, "#b8b2aa"));
            s.Underwater = Grad((0f, "#3b3734"), (1f, "#3b3734"));
            s.Cliff = Grad((0f, "#2a2726"), (0.6f, "#46413e"), (1f, "#6a645f"));
            s.Region = Grad((0f, "#4a4038"), (1f, "#9a8b7a"));
            s.RegionStrength = 0.35f;
            s.CliffSlope = new Vector2(0.14f, 0.32f);
            s.HueJitter = 0.04f;
            s.DetailScale = 5f;
            s.DetailStrength = 0.45f;
            s.DetailNormalStrength = 0.8f;
            s.Gloss = 0.03f;
            Environment(s, PlanetDecorStyle.Barren, 1f, "#6a645f", "#6a645f", "#8a847d", "#9be7ff", PlanetParticles.None, Color.clear, PlanetWeather.None, "#3a3a44", 1f);
        }

        /// <summary>Mountain ranges, river valleys, exposed rock, snowy peaks and lakes.</summary>
        static void Rocky(PlanetShapeDef s)
        {
            s.Warp = 0.12f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.05f, 1.1f, 4),                                                 // continents / lowlands
                Layer(PlanetNoiseKind.Ridged, 0.1f, 2f, 6, 0.5f, 0.3f, 2.1f, 2.2f, new Vector2(0.48f, 0.62f)),  // mountain ranges
                Layer(PlanetNoiseKind.Simple, 0.01f, 6f, 3, 0.5f),                                            // hills
                Layer(PlanetNoiseKind.Simple, 0.0015f, 30f, 2),                                               // roughness
            };
            s.Features = new List<PlanetShapeDef.FeatureSet>
            {
                Feature(PlanetFeatureKind.Spire, 2, 4, 0.03f, 0.05f, 0.9f, 1.4f, 0.7f, rough: 0.6f),
            };
            Canyons(s, 1.4f, 0.08f, 0.035f, 0.3f, 1.6f, 0.65f, 0.5f, rivers: true);
            s.CraterCount = 8;
            s.CraterSize = new Vector2(0.02f, 0.08f);
            s.CraterDepth = 0.18f;
            s.CraterRim = 0.05f;
            s.HasOcean = true;
            s.OceanCoverage = new Vector2(0.12f, 0.25f);
            s.OceanFloorDepth = 2f;
            s.ShallowWater = MathUtils.Hex("#4fc6c9");
            s.DeepWater = MathUtils.Hex("#123e66");
            s.WaterDepth = 10f;
            s.Underwater = Grad((0f, "#2e3c40"), (0.8f, "#5f6b5c"), (1f, "#a59a76"));
            s.Land = Grad((0f, "#b3a274"), (0.03f, "#6f8a4a"), (0.3f, "#556a3a"), (0.5f, "#74664f"), (0.66f, "#8c8884"), (0.8f, "#e6ebf2"), (1f, "#ffffff"));
            s.Region = Grad((0f, "#b3a274"), (0.05f, "#9aa556"), (0.35f, "#8a9a4a"), (0.6f, "#7a6d58"), (0.8f, "#e6ebf2"), (1f, "#ffffff"));
            s.RegionStrength = 0.5f;
            s.Cliff = Grad((0f, "#4a3f37"), (0.6f, "#686059"), (0.85f, "#8a8682"), (1f, "#cfd3da"));
            s.CliffSlope = new Vector2(0.1f, 0.24f);
            s.DetailScale = 6f;
            s.DetailStrength = 0.35f;
            s.DetailNormalStrength = 0.7f;
            s.Gloss = 0.05f;
            Atmosphere(s, "#78b4ff", "#ff9a6a", "#a9c8f0", 0.22f, 1.6f, 4f);
            Clouds(s, 0.34f, "#ffffff", "#8f9fc4");
            Environment(s, PlanetDecorStyle.Rocky, 1f, "#6e665e", "#4f7a3a", "#c9b27a", "#ffe08a", PlanetParticles.Dust, new Color(1f, 1f, 0.9f, 0.25f), PlanetWeather.None, "#6a7486", 1f);
        }

        /// <summary>Deep oceans, continents and islands with beaches and snowy mountains.</summary>
        static void OceanWorld(PlanetShapeDef s)
        {
            s.Warp = 0.15f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.1f, 0.8f, 5, 0.5f),                                            // continents
                Layer(PlanetNoiseKind.Ridged, 0.06f, 2f, 5, 0.5f, 0.3f, 2f, 1.6f, new Vector2(0.55f, 0.68f)),  // coastal mountains
                Layer(PlanetNoiseKind.Simple, 0.004f, 12f, 3),                                                 // detail
            };
            s.CraterCount = 0;
            s.HasOcean = true;
            s.OceanCoverage = new Vector2(0.55f, 0.75f);
            s.OceanFloorDepth = 4f;
            s.ShallowWater = MathUtils.Hex("#3fd9cf");
            s.DeepWater = MathUtils.Hex("#0a2c66");
            s.WaterDepth = 12f;
            s.Underwater = Grad((0f, "#142a44"), (0.75f, "#2f6a72"), (1f, "#9fae86"));
            s.Land = Grad((0f, "#e6d59a"), (0.02f, "#e6d59a"), (0.05f, "#6cb757"), (0.35f, "#3f8e4a"), (0.6f, "#6c7954"), (0.76f, "#8a8683"), (0.86f, "#f1f4f9"), (1f, "#ffffff"));
            s.Cliff = Grad((0f, "#5b5048"), (0.7f, "#77706a"), (1f, "#d8dce2"));
            s.CliffSlope = new Vector2(0.12f, 0.28f);
            s.DetailScale = 6f;
            s.DetailStrength = 0.3f;
            s.DetailNormalStrength = 0.6f;
            s.Gloss = 0.05f;
            Atmosphere(s, "#6fb0ff", "#ff9a7a", "#b5d4f5", 0.24f, 1.8f, 4f);
            Clouds(s, 0.4f, "#ffffff", "#93a6c8", 2.6f, 1.5f);
            Environment(s, PlanetDecorStyle.Rocky, 1.2f, "#6e665e", "#3f8e4a", "#e6d59a", "#ffe08a", PlanetParticles.Dust, new Color(1f, 1f, 1f, 0.2f), PlanetWeather.None, "#6a7a90", 1f);
        }

        /// <summary>Warm sands, wind-aligned dunes, terraced canyon networks, buttes and hoodoo spires.</summary>
        static void Desert(PlanetShapeDef s)
        {
            s.Warp = 0.12f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.035f, 1f, 4),                                                                   // broad basins
                Layer(PlanetNoiseKind.Ridged, 0.004f, 9f, 2, 0.35f, ridgeWeight: 0.6f, mask: new Vector2(0.62f, 0.38f), stretch: new Vector3(1f, 4f, 1f)), // dunes in the lowlands
                Layer(PlanetNoiseKind.Simple, 0.03f, 2.2f, 4, 0.5f, 0.52f, terraces: 4),                                       // stepped plateaus
                Layer(PlanetNoiseKind.Ridged, 0.008f, 11f, 3, 0.45f, 0.2f, ridgeWeight: 1.2f),                                 // rocky ridges
                Layer(PlanetNoiseKind.Simple, 0.0012f, 30f, 2),                                                                 // grain
            };
            s.Features = new List<PlanetShapeDef.FeatureSet>
            {
                Feature(PlanetFeatureKind.Mesa, 6, 10, 0.05f, 0.12f, 0.4f, 0.7f, 0.9f, 3, 0.5f),      // buttes
                Feature(PlanetFeatureKind.Spire, 8, 14, 0.01f, 0.02f, 1.5f, 2.6f, 0.75f, 0, 0.4f),   // hoodoo towers
                Feature(PlanetFeatureKind.Basin, 1, 3, 0.12f, 0.2f, 0.08f, 0.12f),
            };
            Canyons(s, 1.6f, 0.085f, 0.07f, 0.45f, 3f, 0.65f, 0.45f, 4);
            s.CraterCount = 4;
            s.CraterSize = new Vector2(0.02f, 0.06f);
            s.CraterDepth = 0.15f;
            s.CraterFloor = 0.5f;
            s.HasOcean = false;
            s.Land = Grad((0f, "#a85a36"), (0.25f, "#c47a48"), (0.55f, "#d99a5e"), (0.8f, "#e2b07a"), (1f, "#ecc598"));
            s.Region = Grad((0f, "#b8784c"), (0.4f, "#d6a878"), (1f, "#e4c49c"));
            s.RegionStrength = 0.4f;
            s.Underwater = Grad((0f, "#a85a36"), (1f, "#a85a36"));
            s.Cliff = Grad((0f, "#7a3a26"), (0.35f, "#9a4d33"), (0.6f, "#b0603d"), (1f, "#c98558"));
            s.CliffSlope = new Vector2(0.08f, 0.18f);
            s.HueJitter = 0.025f;
            s.DetailScale = 4f;
            s.DetailStrength = 0.25f;
            s.DetailNormalStrength = 0.45f;
            s.Gloss = 0.04f;
            Atmosphere(s, "#ffb27a", "#ff6a4a", "#e0a878", 0.2f, 1.4f, 4f);
            Clouds(s, 0.12f, "#fff2de", "#d9b08f", 2.4f, 2.2f, 0.88f);
            Environment(s, PlanetDecorStyle.Desert, 1f, "#a8573a", "#6f9e80", "#e0aa70", "#ffd36b",
                PlanetParticles.Dust, new Color(1f, 0.85f, 0.6f, 0.35f), PlanetWeather.Sandstorm, "#6e5444", 0.9f);
        }

        static void BuildPlanetShapes()
        {
            planetShapes.Clear();
            Shape("CrateredMoon", "Cratered Moon", CrateredMoon);
            Shape("Rocky", "Rocky Planet", Rocky);
            Shape("Ocean", "Ocean Planet", OceanWorld);
            Shape("Desert", "Desert Planet", Desert);

            // Game biomes: each has its own geology, atmosphere and surface composition.
            Shape("Ice", "Frost World", OceanWorld, s =>
            {
                s.Warp = 0.14f;
                s.Layers = new List<PlanetShapeDef.NoiseLayer>
                {
                    Layer(PlanetNoiseKind.Simple, 0.06f, 0.9f, 5),                                                   // continents / ice sheets
                    Layer(PlanetNoiseKind.Ridged, 0.09f, 2.2f, 6, 0.5f, 0.35f, 2.1f, 2.4f, new Vector2(0.5f, 0.65f)), // jagged ranges
                    Layer(PlanetNoiseKind.Simple, 0.012f, 8f, 3),                                                    // hills
                    Layer(PlanetNoiseKind.Ridged, 0.006f, 16f, 3, 0.45f, 0.25f, ridgeWeight: 1.4f),                  // pressure ridges
                    Layer(PlanetNoiseKind.Ridged, 0.002f, 26f, 2, ridgeWeight: 1f),                                   // ice ridges
                };
                s.Features = new List<PlanetShapeDef.FeatureSet>
                {
                    Feature(PlanetFeatureKind.Spire, 3, 5, 0.04f, 0.08f, 1f, 1.6f, 0.8f, 0, 0.7f),   // lone jagged peaks
                    Feature(PlanetFeatureKind.Dome, 3, 6, 0.08f, 0.15f, 0.2f, 0.35f),                // glacier domes
                };
                Canyons(s, 2.2f, 0.03f, 0.06f, 0.1f, 4f, 0.5f, 0.6f);                            // frozen chasms
                s.OceanCoverage = new Vector2(0.2f, 0.35f);
                s.OceanFloorDepth = 3f;
                s.ShallowWater = MathUtils.Hex("#7fd0f0");
                s.DeepWater = MathUtils.Hex("#1c3f78");
                s.WaveStrength = 0.15f;
                s.Underwater = Grad((0f, "#29466f"), (1f, "#7fa6d0"));
                s.Land = Grad((0f, "#a9c6e8"), (0.35f, "#9dbde3"), (0.7f, "#c4d8f0"), (1f, "#e8f1fc"));
                s.Region = Grad((0f, "#7fb2e0"), (0.5f, "#8fc0ea"), (1f, "#cfe4f7"));
                s.RegionStrength = 0.45f;
                s.Cliff = Grad((0f, "#3f5f90"), (0.6f, "#5a7fb5"), (1f, "#8fb0db"));
                s.CliffSlope = new Vector2(0.12f, 0.26f);
                s.Gloss = 0.15f;
                Atmosphere(s, "#8ac6ff", "#e59ad9", "#a9cff2", 0.22f, 1.8f, 4f);
                Clouds(s, 0.3f, "#ffffff", "#9fb0d0", 4f, 1f);
                Environment(s, PlanetDecorStyle.Ice, 1f, "#4f5f78", "#9fd6ff", "#6fb5f0", "#9be7ff",
                    PlanetParticles.Snow, new Color(1f, 1f, 1f, 0.8f), PlanetWeather.Blizzard, "#5f6f8a", 1.1f);
            });
            Shape("ToxicJungle", "Toxic Jungle", OceanWorld, s =>
            {
                s.Warp = 0.15f;
                s.Layers = new List<PlanetShapeDef.NoiseLayer>
                {
                    Layer(PlanetNoiseKind.Simple, 0.09f, 0.8f, 5, 0.5f),                                           // continents
                    Layer(PlanetNoiseKind.Ridged, 0.05f, 2.2f, 5, 0.5f, 0.3f, 2f, 1.6f, new Vector2(0.55f, 0.7f)), // coastal ranges
                    Layer(PlanetNoiseKind.Billow, 0.012f, 5f, 3),                                                  // rolling hills
                    Layer(PlanetNoiseKind.Simple, 0.008f, 12f, 3),                                                 // knolls
                    Layer(PlanetNoiseKind.Simple, 0.003f, 16f, 2),                                                 // detail
                };
                s.Features = new List<PlanetShapeDef.FeatureSet>
                {
                    Feature(PlanetFeatureKind.Spire, 8, 14, 0.015f, 0.03f, 1.2f, 2f, 0.15f, 0, 0.2f),  // karst towers
                };
                Canyons(s, 1.4f, 0.09f, 0.05f, 0.25f, 1.5f, 0.7f, 0.5f, rivers: true);                  // river valleys
                s.OceanCoverage = new Vector2(0.3f, 0.45f);
                s.OceanFloorDepth = 3f;
                s.ShallowWater = MathUtils.Hex("#8fd16a");
                s.DeepWater = MathUtils.Hex("#0f3b2c");
                s.WaterEmission = new Color(0.015f, 0.05f, 0.01f);
                s.Underwater = Grad((0f, "#1f3a33"), (1f, "#4f7a4a"));
                s.Land = Grad((0f, "#2c5a48"), (0.25f, "#23784a"), (0.6f, "#3f9e45"), (0.85f, "#8fc94a"), (1f, "#c9e98a"));
                s.Region = Grad((0f, "#2e6b4f"), (0.5f, "#6a4f8a"), (1f, "#b98fd0"));
                s.RegionStrength = 0.5f;
                s.Cliff = Grad((0f, "#2f3d3a"), (0.6f, "#4f5a4c"), (1f, "#6b5a6e"));
                Atmosphere(s, "#86cfc8", "#ffd36b", "#c2d99a", 0.24f, 2.1f, 3f);
                Clouds(s, 0.32f, "#e6f7dc", "#7a9a80", 4.5f, 0.6f);
                Environment(s, PlanetDecorStyle.Jungle, 1f, "#4a3f4f", "#2f9e5a", "#c86bd8", "#9dff6a",
                    PlanetParticles.Spores, new Color(0.6f, 1f, 0.4f, 0.6f), PlanetWeather.SporeBloom, "#5f7a5c", 0.7f);
            });
            Shape("Crystal", "Crystal World", CrateredMoon, s =>
            {
                s.CraterCount = 60;
                s.CraterSize = new Vector2(0.02f, 0.18f);
                s.Features = new List<PlanetShapeDef.FeatureSet>
                {
                    Feature(PlanetFeatureKind.Basin, 2, 3, 0.15f, 0.25f, 0.08f, 0.12f),
                    Feature(PlanetFeatureKind.Spire, 5, 8, 0.025f, 0.05f, 1.4f, 2.2f, 1f, 0, 0.6f),    // crystal mountains
                    Feature(PlanetFeatureKind.Mesa, 2, 4, 0.05f, 0.09f, 0.25f, 0.4f, 0.85f, 2, 0.4f),
                };
                s.Layers = new List<PlanetShapeDef.NoiseLayer>
                {
                    Layer(PlanetNoiseKind.Simple, 0.05f, 1.1f, 4),
                    Layer(PlanetNoiseKind.Ridged, 0.05f, 2.6f, 5, 0.5f, 0.35f, ridgeWeight: 1.8f),       // shard ridges
                    Layer(PlanetNoiseKind.Ridged, 0.01f, 10f, 3, 0.45f, 0.2f, ridgeWeight: 1.4f),        // crystal hills
                    Layer(PlanetNoiseKind.Simple, 0.003f, 18f, 3),
                };
                Canyons(s, 1.2f, 0.035f, 0.04f, 0.2f, 3f, 0.4f, 0.4f);                                  // rifts
                s.HasOcean = true;
                s.OceanCoverage = new Vector2(0.06f, 0.12f);
                s.OceanFloorDepth = 1f;
                s.ShallowWater = MathUtils.Hex("#4cc4e6");
                s.DeepWater = MathUtils.Hex("#1d3596");
                s.WaterEmission = new Color(0.02f, 0.08f, 0.14f);
                s.WaterDepth = 3f;
                s.Land = Grad((0f, "#232a5a"), (0.4f, "#3b4f9e"), (0.7f, "#5f63bc"), (1f, "#968fdc"));
                s.Region = Grad((0f, "#4a2f78"), (0.5f, "#7a4fa8"), (1f, "#d29ae6"));
                s.RegionStrength = 0.55f;
                s.Cliff = Grad((0f, "#1a1f45"), (0.6f, "#3a3570"), (1f, "#5a4a9a"));
                s.Gloss = 0.12f;
                Atmosphere(s, "#8a9cff", "#e08aff", "#7f7fd8", 0.2f, 1.2f, 4f);
                Clouds(s, 0.25f, "#e0e8ff", "#7f80c8", 3.5f, 1.8f);
                Environment(s, PlanetDecorStyle.Crystal, 1.6f, "#22264a", "#79a8ff", "#e08cff", "#7ff6ff",
                    PlanetParticles.Motes, new Color(0.6f, 0.85f, 1f, 0.7f), PlanetWeather.Shimmer, "#4a4a78", 1.4f);
            });
            Shape("Volcanic", "Ember World", Rocky, s =>
            {
                s.Layers = new List<PlanetShapeDef.NoiseLayer>
                {
                    Layer(PlanetNoiseKind.Simple, 0.05f, 1.1f, 4),                                                 // continents
                    Layer(PlanetNoiseKind.Ridged, 0.08f, 2f, 6, 0.5f, 0.3f, 2.1f, 2.2f, new Vector2(0.48f, 0.62f)), // black mountains
                    Layer(PlanetNoiseKind.Simple, 0.012f, 8f, 3, 0.5f),                                           // hills
                    Layer(PlanetNoiseKind.Ridged, 0.006f, 14f, 3, 0.45f, 0.25f, ridgeWeight: 1.4f),               // lava ridges
                    Layer(PlanetNoiseKind.Ridged, 0.0015f, 30f, 2, ridgeWeight: 1f),                               // lava crust
                };
                s.Features = new List<PlanetShapeDef.FeatureSet>
                {
                    Feature(PlanetFeatureKind.Volcano, 3, 5, 0.12f, 0.22f, 0.45f, 0.75f, 0.6f, 0, 0.6f, 0.18f),
                    Feature(PlanetFeatureKind.Mesa, 3, 5, 0.04f, 0.08f, 0.2f, 0.35f, 0.95f, 2, 0.3f),       // basalt plateaus
                };
                Canyons(s, 1.8f, 0.04f, 0.03f, 0.3f, 2.5f, 0.65f, 0.55f, rivers: true);                     // lava channels
                s.OceanCoverage = new Vector2(0.12f, 0.22f);
                s.OceanFloorDepth = 1f;
                s.Lava = true;
                s.ShallowWater = MathUtils.Hex("#ff5a1a");
                s.DeepWater = MathUtils.Hex("#a0200a");
                s.WaterDepth = 2.5f;
                s.WaterEmission = new Color(2.4f, 2.4f, 2.4f); // lava glow intensity (tinted by the shallow/deep colors)
                s.WaterGloss = 0.3f;
                s.WaveStrength = 0.3f;
                s.Underwater = Grad((0f, "#3a1a14"), (1f, "#5a2a1a"));
                s.Land = Grad((0f, "#2a2224"), (0.4f, "#3a3036"), (0.8f, "#4f4448"), (1f, "#6a5a5a"));
                s.Region = Grad((0f, "#3f3638"), (0.5f, "#5a5052"), (1f, "#7a7072"));
                s.RegionStrength = 0.35f;
                s.Cliff = Grad((0f, "#1a1618"), (1f, "#3a3034"));
                s.CraterCount = 12;
                Atmosphere(s, "#f0907a", "#ff5a3a", "#8a6058", 0.22f, 1.7f, 3.5f);
                Clouds(s, 0.26f, "#8a7f84", "#3a3034", 3.2f, 0.8f, 0.7f);
                Environment(s, PlanetDecorStyle.Volcanic, 1f, "#2b2629", "#4a3f3c", "#7a5a50", "#ff7a2a",
                    PlanetParticles.Ash, new Color(1f, 0.55f, 0.25f, 0.6f), PlanetWeather.AshFall, "#735450", 0.6f);
            });
        }

        // ================================================================ preview scene

        static void BuildPlanetPreviewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20000f;
            cam.fieldOfView = 60f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var urpCam = cam.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = true;
            urpCam.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(25f, -50f, 0f);
            RenderSettings.sun = sun;

            var vol = new GameObject("GlobalVolume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtDir + "/VW_PostProcess.asset");

            RenderSettings.skybox = MatSkybox;
            catalog.Palettes[0].ApplyEnvironment();

            var preview = new GameObject("PlanetPreview").AddComponent<PlanetPreview>();
            var presets = new List<PlanetShapeDef>();
            foreach (var id in ArchetypeIds) presets.Add(planetShapes[id]);
            foreach (var kv in planetShapes) if (!presets.Contains(kv.Value)) presets.Add(kv.Value);
            preview.Presets = presets.ToArray();
            preview.Cam = cam;

            EditorSceneManager.SaveScene(scene, PlanetPreviewScenePath);
        }

        [MenuItem("Voidward/Open Planet Preview")]
        public static void OpenPlanetPreview()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlanetPreviewScenePath) == null)
            {
                Debug.LogWarning("[Voidward] Planet preview scene missing - run 'Voidward/Build All Content' first.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(PlanetPreviewScenePath);
        }
    }
}
