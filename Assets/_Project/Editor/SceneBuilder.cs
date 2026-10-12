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

            // Menu backdrop: a slowly rotating crystal planet with a ring.
            var backdrop = new GameObject("MenuBackdrop");
            backdrop.transform.position = new Vector3(0, 0, 0);
            var biome = catalog.Biomes[3];
            var planetMesh = PlanetMeshGenerator.GenerateVisualAt(1234, 120f, biome, 5);
            planetMesh.name = "MenuPlanet";
            string pmPath = MeshDir + "/MenuPlanet.asset";
            AssetDatabase.DeleteAsset(pmPath);
            AssetDatabase.CreateAsset(planetMesh, pmPath);
            var planet = Visual(backdrop.transform, "Planet", planetMesh);
            var atmo = Visual(backdrop.transform, "Atmosphere", SaveMeshIco(130f, "MenuAtmosphere"), MatAtmosphere);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", biome.AtmosphereColor);
            atmo.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
            atmo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var ring = new MeshBuilder();
            var rng = new System.Random(5);
            for (int i = 0; i < 160; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 190f + (float)rng.NextDouble() * 70f;
                ring.AddRock(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, ((float)rng.NextDouble() - 0.5f) * 8f, Mathf.Sin(a) * r), Quaternion.Euler(i * 13, i * 7, 0), Vector3.one),
                    1.5f + (float)rng.NextDouble() * 4f, i, Color.Lerp(new Color(0.8f, 0.7f, 0.95f), new Color(0.6f, 0.85f, 1f), (float)rng.NextDouble()));
            }
            var ringGo = Visual(backdrop.transform, "Ring", SaveMesh(ring, "MenuRing"));
            ringGo.transform.localRotation = Quaternion.Euler(18f, 0f, 12f);
            var moonMb = new MeshBuilder();
            moonMb.AddRock(Matrix4x4.identity, 22f, 99, new Color(1f, 0.75f, 0.65f));
            var moon = Visual(backdrop.transform, "Moon", SaveMesh(moonMb, "MenuMoon"));
            moon.transform.localPosition = new Vector3(-260f, 90f, 160f);
            backdrop.AddComponent<MenuBackdrop>();

            var menuCamGo = new GameObject("MenuCamera");
            // Frames the planet right of center so the left-side menu console stays clear.
            menuCamGo.transform.position = new Vector3(-150f, 60f, -420f);
            menuCamGo.transform.LookAt(new Vector3(-80f, 10f, 0f));
            var menuCam = menuCamGo.AddComponent<CinemachineCamera>();
            menuCam.Priority = 10;
            var lens = menuCam.Lens;
            lens.FieldOfView = 50f; lens.FarClipPlane = 20000f; lens.NearClipPlane = 0.1f;
            menuCam.Lens = lens;
            menuCamGo.AddComponent<MenuCameraDrift>();

            // Sun
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.85f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
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
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = pal.AmbientColor * 1.2f;
            RenderSettings.ambientEquatorColor = Color.Lerp(pal.AmbientColor, pal.NebulaA, 0.3f);
            RenderSettings.ambientGroundColor = pal.AmbientColor * 0.6f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = pal.FogColor;
            RenderSettings.fogDensity = pal.FogDensity;

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

        static VolumeProfile BuildVolumeProfile()
        {
            string path = ArtDir + "/VW_PostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.72f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(18f);
            ca.postExposure.Override(-0.1f);
            ca.contrast.Override(14f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.5f);
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
                    EditorUtility.SetDirty(urp);
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
