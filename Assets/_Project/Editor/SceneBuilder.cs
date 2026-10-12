using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Voidward.Core;
using Voidward.Data;
using Voidward.Networking;
using Voidward.Planet;
using Voidward.UI;
using MathUtils = Voidward.Core.MathUtils;

namespace Voidward.EditorTools
{
    /// <summary>Builds the single game scene and applies project settings.</summary>
    public static partial class ContentBuilder
    {
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera with Cinemachine brain + post-processing
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20000f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.2f);
            var urpCam = cam.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = true;
            urpCam.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.transform.position = new Vector3(0, 40, -420);

            // Menu backdrop: low-poly diorama (astronauts + landed Dart on a cratered moon) with the blue crystal planet rising behind.
            // MenuBackdrop spins child 0 (planet) and child 2 (ring).
            var backdrop = new GameObject("MenuBackdrop");
            backdrop.transform.position = new Vector3(0, 70f, 0);
            var biome = catalog.Biomes[3];
            AssetDatabase.DeleteAsset(MeshDir + "/MenuPlanet.asset");
            var planet = new GameObject("Planet");
            planet.transform.SetParent(backdrop.transform, false);
            var menuPlanet = planet.AddComponent<PlanetVisuals>();
            menuPlanet.SetAutoBuild(biome.Shape, 1234, 120f);
            // The planet builds its own atmosphere and clouds; this empty child keeps MenuBackdrop's child indices.
            new GameObject("AtmosphereSlot").transform.SetParent(backdrop.transform, false);
            var ring = new MeshBuilder { Gloss = 0.1f };
            var rng = new System.Random(5);
            for (int i = 0; i < 160; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 190f + (float)rng.NextDouble() * 70f;
                ring.AddRock(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, ((float)rng.NextDouble() - 0.5f) * 8f, Mathf.Sin(a) * r), Quaternion.Euler(i * 13, i * 7, 0), Vector3.one),
                    1.5f + (float)rng.NextDouble() * 4f, i, Color.Lerp(new Color(0.78f, 0.84f, 0.95f), new Color(0.45f, 0.62f, 1f), (float)rng.NextDouble()));
            }
            var ringGo = Visual(backdrop.transform, "Ring", SaveMesh(ring, "MenuRing"));
            ringGo.transform.localRotation = Quaternion.Euler(18f, 0f, 12f);
            ringGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var moonMb = new MeshBuilder { Gloss = 0.05f };
            moonMb.AddRock(Matrix4x4.identity, 22f, 99, new Color(0.86f, 0.9f, 1f));
            var moon = Visual(backdrop.transform, "Moon", SaveMesh(moonMb, "MenuMoon"));
            moon.transform.localPosition = new Vector3(-260f, 90f, 160f);

            var menuCamPos = new Vector3(-150f, 47f, -420f);
            var menuCamRot = Quaternion.LookRotation(new Vector3(-60f, 50f, 0f) - menuCamPos);
            BuildMenuDiorama(backdrop.transform, menuCamPos, menuCamRot);
            backdrop.AddComponent<MenuBackdrop>();

            var menuCamGo = new GameObject("MenuCamera");
            // Low over the moon surface: planet right of centre, figures right, left-side menu console stays clear.
            menuCamGo.transform.SetPositionAndRotation(menuCamPos, menuCamRot);
            var menuCam = menuCamGo.AddComponent<CinemachineCamera>();
            menuCam.Priority = 10;
            var lens = menuCam.Lens;
            lens.FieldOfView = 50f; lens.FarClipPlane = 20000f; lens.NearClipPlane = 0.1f;
            menuCam.Lens = lens;
            var drift = menuCamGo.AddComponent<MenuCameraDrift>();
            drift.PositionAmplitude = new Vector2(0.8f, 0.25f);
            drift.RotationAmplitude = new Vector2(0.6f, 1.2f);

            // Sun
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sunGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            RenderSettings.sun = sun;

            // Post-processing volume
            var volGo = new GameObject("GlobalVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildVolumeProfile();

            // Networking
            var netGo = new GameObject("NetworkManager");
            var nm = netGo.AddComponent<NetworkManager>();
            var utp = netGo.AddComponent<UnityTransport>();
            netGo.AddComponent<SteamTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                PlayerPrefab = catalog.PlayerPawn,
                NetworkTransport = utp,
                TickRate = 30,
                ConnectionApproval = false,
                EnableSceneManagement = true,
            };
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            // Services + UI
            var services = new GameObject("_Services");
            services.AddComponent<AudioManager>();
            var vfx = services.AddComponent<VfxManager>();
            vfx.ParticleMaterial = MatParticle;
            var ui = new GameObject("_UI");
            ui.AddComponent<UIRoot>();

            // Lighting / sky
            var pal = catalog.Palettes[0];
            MatSkybox.SetColor("_ZenithColor", pal.SkyZenith);
            MatSkybox.SetColor("_HorizonColor", pal.SkyHorizon);
            MatSkybox.SetColor("_NebulaColorA", pal.NebulaA);
            MatSkybox.SetColor("_NebulaColorB", pal.NebulaB);
            RenderSettings.skybox = MatSkybox;
            pal.ApplyEnvironment();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static Mesh SaveMeshIco(float r, string name)
        {
            var m = IcoSphere.Create(r, 3, name);
            string path = MeshDir + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>
        /// Moon-surface patch in front of the menu camera: faceted cratered ground that curves away at the horizon,
        /// three astronauts, a landed Dart and glowing crystal accents. Local frame: +Z away from the camera.
        /// </summary>
        static void BuildMenuDiorama(Transform parent, Vector3 camPos, Quaternion camRot)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(camRot * Vector3.forward, Vector3.up).normalized;
            var root = new GameObject("Diorama");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(camPos.x, camPos.y - 2.2f, camPos.z), Quaternion.LookRotation(fwd));

            float Crater(float x, float z, float cx, float cz, float rad)
            {
                float d = new Vector2(x - cx, z - cz).magnitude / rad;
                if (d > 1.7f) return 0f;
                return (d < 1f ? (d * d - 1f) * rad * 0.22f : 0f) + Mathf.Exp(-(d - 1f) * (d - 1f) * 10f) * rad * 0.08f;
            }
            float H(float x, float z)
            {
                float r = new Vector2(x, z).magnitude;
                float h = (Mathf.PerlinNoise(x * 0.05f + 3.1f, z * 0.05f + 7.7f) - 0.5f) * 2.6f + (Mathf.PerlinNoise(x * 0.17f + 11f, z * 0.17f) - 0.5f) * 0.7f;
                float fig = new Vector2(x - 5f, z - 15f).magnitude;
                h *= 0.2f + 0.8f * Mathf.Clamp01((Mathf.Min(fig, r) - 8f) / 12f); // calm around the camera and the figures
                h += Crater(x, z, -14f, 30f, 8f) + Crater(x, z, 24f, 52f, 12f) + Crater(x, z, -8f, 66f, 9f) + Crater(x, z, -26f, 16f, 6f) + Crater(x, z, 28f, 22f, 5f);
                float fall = Mathf.Max(0f, r - 48f);
                return h - fall * fall * 0.014f; // curves away like a small moon
            }

            const float cell = 2.5f;
            Color low = new Color(0.2f, 0.23f, 0.34f), high = new Color(0.58f, 0.62f, 0.74f);
            var ground = new MeshBuilder { Gloss = 0.04f };
            for (float x = -80f; x < 80f; x += cell)
                for (float z = -8f; z < 130f; z += cell)
                {
                    Vector3 P(float px, float pz) => new Vector3(px, H(px, pz), pz);
                    Vector3 a = P(x, z), b = P(x, z + cell), c = P(x + cell, z + cell), d = P(x + cell, z);
                    Color Shade(Vector3 p0, Vector3 p1, Vector3 p2)
                    {
                        Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                        float hAvg = (p0.y + p1.y + p2.y) / 3f;
                        var col = Color.Lerp(low, high, Mathf.InverseLerp(-2.6f, 1.2f, hAvg));
                        if (n.y < 0.85f) col *= 0.85f;
                        return col * (1f + (MathUtils.Hash3(77, Mathf.RoundToInt(x * 4f + p2.x), Mathf.RoundToInt(z * 4f + p2.z), 0) * 2f - 1f) * 0.05f);
                    }
                    // Alternate the split diagonal so the facets don't line up in stripes.
                    if (((int)(x / cell) + (int)(z / cell)) % 2 == 0)
                    {
                        ground.AddTriangle(a, b, c, Shade(a, b, c));
                        ground.AddTriangle(a, c, d, Shade(a, c, d));
                    }
                    else
                    {
                        ground.AddTriangle(a, b, d, Shade(a, b, d));
                        ground.AddTriangle(b, c, d, Shade(b, c, d));
                    }
                }
            Visual(root.transform, "Ground", SaveMesh(ground, "MenuGround"));

            // Rocks and glowing crystal clusters
            var props = new MeshBuilder { Bevel = 0.2f };
            var rng = new System.Random(31);
            for (int i = 0; i < 90; i++)
            {
                float x = MathUtils.Range(rng, -60f, 60f), z = MathUtils.Range(rng, 3f, 100f);
                if (x > -3f && x < 20f && z > 6f && z < 34f) continue; // keep the figures and ship clear
                float s = Mathf.Lerp(0.3f, 2.6f, Mathf.Pow((float)rng.NextDouble(), 2f));
                props.Surface(0.05f);
                props.AddRock(Matrix4x4.TRS(new Vector3(x, H(x, z) + s * 0.2f, z), Quaternion.Euler(0, i * 37f, 0), new Vector3(1f, 0.65f, 1f)), s, 300 + i,
                    Color.Lerp(low, high, 0.35f + (float)rng.NextDouble() * 0.3f));
            }
            Vector2[] clusters = { new Vector2(-3f, 12f), new Vector2(13f, 10f), new Vector2(-10f, 30f), new Vector2(22f, 34f) };
            for (int c = 0; c < clusters.Length; c++)
            {
                props.Surface(0.9f, 0.45f);
                var p = new Vector3(clusters[c].x, H(clusters[c].x, clusters[c].y), clusters[c].y);
                for (int k = 0; k < 4; k++)
                    props.AddPrism(Matrix4x4.TRS(p + new Vector3((k - 1.5f) * 0.35f, 0.4f, (k % 2) * 0.3f), Quaternion.Euler((k - 1.5f) * 14f, k * 70f, (k % 2 - 0.5f) * 30f), Vector3.one),
                        0.22f + k * 0.04f, 0.02f, 1.1f + (k % 3) * 0.5f, 5, k % 2 == 0 ? Cyan : new Color(0.45f, 0.55f, 1f));
            }
            props.Surface();
            Visual(root.transform, "Props", SaveMesh(props, "MenuProps"));

            // Landed Dart on struts
            var ship = Child(root.transform, "Ship", new Vector3(10f, H(10f, 26f) + 1.65f, 26f), Quaternion.Euler(-2f, -140f, 3f));
            Visual(ship.transform, "Hull", AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Ship_Dart.asset"));
            Visual(ship.transform, "Canopy", AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Ship_Dart_Canopy.asset"), MatGlass);
            var struts = MB(0.2f);
            Shiny(struts, Matte, () =>
            {
                foreach (var sp in new[] { new Vector3(-1.1f, -1.1f, 1.8f), new Vector3(1.1f, -1.1f, 1.8f), new Vector3(0f, -1.1f, -2.4f) })
                {
                    struts.AddPrism(T(sp.x, sp.y, sp.z), 0.12f, 0.12f, 1.1f, 6, Mid);
                    struts.AddBox(T(sp.x, sp.y - 0.55f, sp.z), new Vector3(0.6f, 0.12f, 0.6f), Dark);
                }
            });
            Visual(ship.transform, "Struts", SaveMesh(struts, "MenuShipStruts"));

            // Three astronauts: one pointing at the planet, two looking back at the camera
            var blue = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Astronaut.asset");
            var orange = AstronautMesh(new Color(1f, 0.5f, 0.12f), "Astronaut_Orange");
            var white = AstronautMesh(new Color(0.82f, 0.85f, 0.9f), "Astronaut_White");
            (Mesh mesh, float x, float z, float yaw)[] crew = { (blue, 2.2f, 12f, 195f), (orange, 4f, 13.6f, 165f), (white, 5.7f, 12.4f, 30f) };
            foreach (var (mesh, x, z, yaw) in crew)
            {
                var a = Visual(root.transform, "Astronaut", mesh);
                a.transform.localPosition = new Vector3(x, H(x, z) - 0.03f, z);
                a.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        static VolumeProfile BuildVolumeProfile()
        {
            string path = ArtDir + "/VW_PostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(0.85f, 0.93f, 1f));
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(10f);
            ca.postExposure.Override(0.1f);
            ca.contrast.Override(18f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-4f);
            // Crushed, slightly cool shadows; highlights stay warm (sunlight).
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.95f, 0.98f, 1.06f, -0.04f));
            smh.highlights.Override(new Vector4(1.04f, 1.02f, 0.97f, 0f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Color.black);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void ConfigureProject()
        {
            PlayerSettings.productName = "Voidward";
            PlayerSettings.companyName = "Voidward Team";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;

            var dyn = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (dyn.Length > 0)
            {
                var so = new SerializedObject(dyn[0]);
                var g = so.FindProperty("m_Gravity");
                if (g != null) { g.vector3Value = Vector3.zero; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            foreach (var rp in new[] { GraphicsSettings.defaultRenderPipeline, QualitySettings.renderPipeline })
            {
                if (rp is UniversalRenderPipelineAsset urp)
                {
                    urp.shadowDistance = 350f;
                    urp.supportsHDR = true;
                    // Ocean shader reads scene depth (shore/water depth) and color (refraction).
                    urp.supportsCameraDepthTexture = true;
                    urp.supportsCameraOpaqueTexture = true;
                    EditorUtility.SetDirty(urp);
                }
            }
            ConfigureAmbientOcclusion();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Soft contact shadows: tune the SSAO renderer feature (VW_ToonFlat provides the DepthNormals pass it samples).</summary>
        static void ConfigureAmbientOcclusion()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/SourceFiles/Settings/PC_Renderer.asset");
            if (data == null) { Debug.LogWarning("[Voidward] PC_Renderer not found - SSAO not configured."); return; }
            foreach (var f in data.rendererFeatures)
            {
                if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                f.SetActive(true);
                var so = new SerializedObject(f);
                void SetF(string p, float v) { var sp = so.FindProperty("m_Settings." + p); if (sp != null) sp.floatValue = v; }
                SetF("Intensity", 1.1f);
                SetF("Radius", 0.6f);
                SetF("DirectLightingStrength", 0.35f);
                SetF("Falloff", 120f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }
        }
    }
}
