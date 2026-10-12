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

        public static Material MatPlanet, MatOcean;
        static readonly Dictionary<string, PlanetShapeDef> planetShapes = new Dictionary<string, PlanetShapeDef>();
        static readonly string[] ArchetypeIds = { "CrateredMoon", "Rocky", "Ocean", "Desert" };

        // ================================================================ materials + textures

        static void BuildPlanetMaterials()
        {
            BuildDetailTextures(out var detail, out var normal);
            MatPlanet = Mat("VW_Planet", "VOIDWARD/Planet");
            MatPlanet.SetTexture("_DetailTex", detail);
            MatPlanet.SetTexture("_DetailNormal", normal);
            MatOcean = Mat("VW_Ocean", "VOIDWARD/Ocean");
            MatOcean.SetTexture("_DetailNormal", normal);
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

        /// <summary>Gray/brown rock, dense overlapping craters, rugged highlands.</summary>
        static void CrateredMoon(PlanetShapeDef s)
        {
            s.Warp = 0.08f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.05f, 1.1f, 4),                       // basins / highlands
                Layer(PlanetNoiseKind.Ridged, 0.03f, 2.4f, 5, 0.5f, 0.35f, ridgeWeight: 1.2f), // rugged ridges
                Layer(PlanetNoiseKind.Simple, 0.006f, 14f, 3),                       // roughness
            };
            s.CraterCount = 240;
            s.CraterSize = new Vector2(0.02f, 0.26f);
            s.CraterSizeBias = 3.5f;
            s.CraterDepth = 0.28f;
            s.CraterRim = 0.07f;
            s.CraterRimWidth = 0.45f;
            s.CraterFloor = 0.3f;
            s.CraterSteepness = new Vector2(2f, 5f);
            s.CentralPeakSize = 0.14f;
            s.CraterIrregularity = 0.15f;
            s.HasOcean = false;
            s.Land = Grad((0f, "#3b3734"), (0.35f, "#5f5a55"), (0.7f, "#8a847d"), (1f, "#b8b2aa"));
            s.Underwater = Grad((0f, "#3b3734"), (1f, "#3b3734"));
            s.Cliff = Grad((0f, "#2a2726"), (0.6f, "#46413e"), (1f, "#6a645f"));
            s.CliffSlope = new Vector2(0.14f, 0.32f);
            s.HueJitter = 0.04f;
            s.DetailScale = 5f;
            s.DetailStrength = 0.45f;
            s.DetailNormalStrength = 0.8f;
            s.Gloss = 0.03f;
        }

        /// <summary>Mountain ranges, valleys, exposed rock, snowy peaks and a few lakes.</summary>
        static void Rocky(PlanetShapeDef s)
        {
            s.Warp = 0.12f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.05f, 1.1f, 4),                                                 // continents / lowlands
                Layer(PlanetNoiseKind.Ridged, 0.12f, 2f, 6, 0.5f, 0.3f, 2.1f, 2.2f, new Vector2(0.48f, 0.62f)), // mountain ranges
                Layer(PlanetNoiseKind.Simple, 0.012f, 6f, 3, 0.5f),                                           // hills
                Layer(PlanetNoiseKind.Simple, 0.003f, 24f, 2),                                                // roughness
            };
            s.CraterCount = 10;
            s.CraterSize = new Vector2(0.03f, 0.1f);
            s.CraterDepth = 0.18f;
            s.CraterRim = 0.05f;
            s.HasOcean = true;
            s.OceanCoverage = new Vector2(0.1f, 0.25f);
            s.OceanFloorDepth = 2f;
            s.ShallowWater = MathUtils.Hex("#4fc6c9");
            s.DeepWater = MathUtils.Hex("#123e66");
            s.WaterDepth = 10f;
            s.Underwater = Grad((0f, "#2e3c40"), (0.8f, "#5f6b5c"), (1f, "#a59a76"));
            s.Land = Grad((0f, "#b3a274"), (0.03f, "#6f8a4a"), (0.3f, "#556a3a"), (0.5f, "#74664f"), (0.66f, "#8c8884"), (0.8f, "#e6ebf2"), (1f, "#ffffff"));
            s.Cliff = Grad((0f, "#4a3f37"), (0.6f, "#686059"), (0.85f, "#8a8682"), (1f, "#cfd3da"));
            s.CliffSlope = new Vector2(0.1f, 0.24f);
            s.DetailScale = 6f;
            s.DetailStrength = 0.35f;
            s.DetailNormalStrength = 0.7f;
            s.Gloss = 0.05f;
        }

        /// <summary>Deep oceans, continents and islands with beaches and snowy mountains.</summary>
        static void OceanWorld(PlanetShapeDef s)
        {
            s.Warp = 0.15f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.12f, 0.8f, 5, 0.5f),                                           // continents
                Layer(PlanetNoiseKind.Ridged, 0.06f, 2f, 5, 0.5f, 0.3f, 2f, 1.6f, new Vector2(0.55f, 0.68f)),  // coastal mountains
                Layer(PlanetNoiseKind.Simple, 0.006f, 9f, 3),                                                  // detail
            };
            s.CraterCount = 0;
            s.HasOcean = true;
            s.OceanCoverage = new Vector2(0.55f, 0.8f);
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
        }

        /// <summary>Warm sands, smooth dune fields and terraced red-rock mesas.</summary>
        static void Desert(PlanetShapeDef s)
        {
            s.Warp = 0.1f;
            s.Layers = new List<PlanetShapeDef.NoiseLayer>
            {
                Layer(PlanetNoiseKind.Simple, 0.04f, 1f, 4),                                             // broad basins
                Layer(PlanetNoiseKind.Ridged, 0.014f, 5f, 2, 0.35f, ridgeWeight: 0.6f, stretch: new Vector3(1f, 4.5f, 1f)), // wind-aligned dunes
                Layer(PlanetNoiseKind.Simple, 0.07f, 2.2f, 4, 0.5f, 0.5f, terraces: 3),                  // mesas
                Layer(PlanetNoiseKind.Simple, 0.002f, 22f, 2),                                           // grain
            };
            s.CraterCount = 6;
            s.CraterSize = new Vector2(0.03f, 0.09f);
            s.CraterDepth = 0.15f;
            s.CraterFloor = 0.5f;
            s.HasOcean = false;
            s.Land = Grad((0f, "#b4663f"), (0.2f, "#d48e58"), (0.5f, "#e9b676"), (0.8f, "#f2d39d"), (1f, "#f8e6c2"));
            s.Underwater = Grad((0f, "#b4663f"), (1f, "#b4663f"));
            s.Cliff = Grad((0f, "#8a432b"), (0.5f, "#a9583a"), (1f, "#c97f52"));
            s.CliffSlope = new Vector2(0.08f, 0.18f);
            s.HueJitter = 0.02f;
            s.DetailScale = 4f;
            s.DetailStrength = 0.25f;
            s.DetailNormalStrength = 0.45f;
            s.Gloss = 0.04f;
        }

        static void BuildPlanetShapes()
        {
            planetShapes.Clear();
            Shape("CrateredMoon", "Cratered Moon", CrateredMoon);
            Shape("Rocky", "Rocky Planet", Rocky);
            Shape("Ocean", "Ocean Planet", OceanWorld);
            Shape("Desert", "Desert Planet", Desert);

            // Game biomes reuse the archetypes with their own palettes.
            Shape("Ice", "Frost World", OceanWorld, s =>
            {
                s.OceanCoverage = new Vector2(0.3f, 0.5f);
                s.OceanFloorDepth = 3f;
                s.ShallowWater = MathUtils.Hex("#a6ecff");
                s.DeepWater = MathUtils.Hex("#1c4d8e");
                s.Underwater = Grad((0f, "#29466f"), (1f, "#9fc3e6"));
                s.Land = Grad((0f, "#cfe6fb"), (0.35f, "#b4d0ef"), (0.7f, "#e2eeff"), (1f, "#ffffff"));
                s.Cliff = Grad((0f, "#5577a6"), (1f, "#8fb0d8"));
                s.Gloss = 0.25f;
            });
            Shape("ToxicJungle", "Toxic Jungle", OceanWorld, s =>
            {
                s.OceanCoverage = new Vector2(0.35f, 0.55f);
                s.OceanFloorDepth = 3f;
                s.ShallowWater = MathUtils.Hex("#b4f25a");
                s.DeepWater = MathUtils.Hex("#0f3b2c");
                s.WaterEmission = new Color(0.05f, 0.16f, 0.02f);
                s.Underwater = Grad((0f, "#1f3a33"), (1f, "#4f7a4a"));
                s.Land = Grad((0f, "#2c5a48"), (0.25f, "#23784a"), (0.6f, "#3f9e45"), (0.85f, "#8fc94a"), (1f, "#c9e98a"));
                s.Cliff = Grad((0f, "#2f3d3a"), (1f, "#6b5a6e"));
            });
            Shape("Crystal", "Crystal World", CrateredMoon, s =>
            {
                s.CraterCount = 110;
                s.Land = Grad((0f, "#1e2a5c"), (0.5f, "#3a62c8"), (1f, "#8fb4ff"));
                s.Cliff = Grad((0f, "#141a3a"), (1f, "#3a4a8a"));
                s.Gloss = 0.3f;
            });
            Shape("Volcanic", "Ember World", Rocky, s =>
            {
                s.OceanCoverage = new Vector2(0.15f, 0.3f);
                s.OceanFloorDepth = 1f;
                s.ShallowWater = MathUtils.Hex("#ffa040");
                s.DeepWater = MathUtils.Hex("#b0300e");
                s.WaterDepth = 2.5f;
                s.WaterEmission = new Color(3.2f, 0.9f, 0.18f);
                s.WaterGloss = 0.3f;
                s.WaveStrength = 0.3f;
                s.Underwater = Grad((0f, "#3a1a14"), (1f, "#5a2a1a"));
                s.Land = Grad((0f, "#2a2224"), (0.4f, "#3a3036"), (0.8f, "#4f4448"), (1f, "#6a5a5a"));
                s.Cliff = Grad((0f, "#1a1618"), (1f, "#3a3034"));
                s.CraterCount = 25;
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
