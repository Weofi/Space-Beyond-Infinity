using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Voidward.Data;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Dev tool for the art pass (#26): rebuilds only the hull material and hero meshes, stages them in a throwaway
    /// scene with the game's sun, skybox, palette ambient and post stack, and renders a set of PNGs to Temp/HeroShots.
    /// Much faster than Build All Content when iterating on a model or the shader.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string HeroShotDir = "Temp/HeroShots";

        [MenuItem("Voidward/Dev/Hero Preview Shots", priority = 200)]
        public static void HeroPreviewShots()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Voidward] Hero preview: leave Play Mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(ResourcesDir + "/ContentCatalog.asset");
            EnsureFolders();
            BuildMaterials();
            var hull = DartHullMesh();
            var canopy = DartCanopyMesh();
            var astro = AstronautMesh();
            AssetDatabase.SaveAssets();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = MatSkybox;
            if (catalog != null && catalog.Palettes != null && catalog.Palettes.Length > 0) catalog.Palettes[0].ApplyEnvironment();
            var vol = new GameObject("Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtDir + "/VW_PostProcess.asset");

            var ship = new GameObject("Dart");
            Visual(ship.transform, "Hull", hull, MatHull);
            Visual(ship.transform, "Canopy", canopy, MatGlass);
            var pilot = new GameObject("Astronaut");
            Visual(pilot.transform, "Body", astro, MatHull);
            pilot.transform.position = new Vector3(-3.2f, -0.9f, 4.2f);
            pilot.transform.rotation = Quaternion.Euler(0f, -25f, 0f);
            var pad = new GameObject("Pad");
            var padMb = new Core.MeshBuilder { Bevel = 0.05f };
            padMb.AddBox(Matrix4x4.Translate(new Vector3(0f, -1.0f, 0f)), new Vector3(16f, 0.2f, 16f), new Color(0.22f, 0.24f, 0.3f));
            Visual(pad.transform, "Mesh", padMb.Build("PreviewPad"));

            var probe = new GameObject("Probe").AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(60f, 60f, 60f);
            probe.transform.position = new Vector3(0f, 2f, 0f);
            probe.RenderProbe();

            Directory.CreateDirectory(HeroShotDir);
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 5000f; cam.fieldOfView = 35f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            void Shot(string name, Vector3 pos, Vector3 target, float fov = 35f)
            {
                // Key light comes from over the camera's shoulder so reviews show the lit side of the asset.
                Vector3 view = target - pos;
                sunGo.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(40f, Vector3.up) * new Vector3(view.x, 0f, view.z).normalized + Vector3.down * 0.8f);
                probe.RenderProbe();
                cam.fieldOfView = fov;
                camGo.transform.position = pos;
                camGo.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(HeroShotDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            Shot("dart_front34", new Vector3(-8.5f, 3.2f, 9f), new Vector3(0f, 0f, 0.3f));
            Shot("dart_rear34", new Vector3(7.5f, 3.8f, -10f), new Vector3(0f, 0f, -1f));
            Shot("dart_side", new Vector3(-14f, 1.2f, 0f), new Vector3(0f, 0f, 0f));
            Shot("dart_top", new Vector3(0.01f, 15f, -1f), new Vector3(0f, 0f, -0.5f));
            Shot("dart_cockpit", new Vector3(-2.2f, 2.2f, 3.6f), new Vector3(0f, 0.9f, 1.0f), 40f);
            Shot("dart_engines", new Vector3(2.4f, 1.0f, -8.2f), new Vector3(0f, 0f, -4.4f), 40f);
            Shot("astronaut", new Vector3(-4.6f, 0.4f, 7.6f), pilot.transform.position + Vector3.up * 0.9f, 30f);
            Shot("astronaut_back", new Vector3(-1.6f, 0.6f, 1.2f), pilot.transform.position + Vector3.up * 0.9f, 30f);
            Debug.Log("[Voidward] Hero preview shots written to " + Path.GetFullPath(HeroShotDir));
        }
    }
}
