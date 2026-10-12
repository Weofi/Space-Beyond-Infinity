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
    /// Dev tools for the art pass (#26). A throwaway studio scene with the game's sun, skybox, palette ambient and
    /// post stack renders review PNGs to Temp/HeroShots:
    ///  - Hero Preview Shots: rebuilds only the hull material + Dart/astronaut meshes (fast model iteration).
    ///  - PreviewPrefabs(filter): stages built prefabs (after Build All Content), auto-framed from three angles.
    ///  - PreviewShot(...): one custom camera inside/around a prefab (interiors, close-ups).
    /// </summary>
    public static partial class ContentBuilder
    {
        const string HeroShotDir = "Temp/HeroShots";

        static GameObject studioSun, studioCam;
        static ReflectionProbe studioProbe;

        static bool OpenStudio()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Voidward] Preview: leave Play Mode first."); return false; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(ResourcesDir + "/ContentCatalog.asset");
            if (MatSkybox == null) MatSkybox = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/VW_Skybox.mat");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            studioSun = new GameObject("Sun");
            var sun = studioSun.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            RenderSettings.skybox = MatSkybox;
            if (catalog != null && catalog.Palettes != null && catalog.Palettes.Length > 0) catalog.Palettes[0].ApplyEnvironment();
            var vol = new GameObject("Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtDir + "/VW_PostProcess.asset");

            studioProbe = new GameObject("Probe").AddComponent<ReflectionProbe>();
            studioProbe.mode = ReflectionProbeMode.Realtime;
            studioProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            studioProbe.size = new Vector3(400f, 400f, 400f);

            studioCam = new GameObject("ShotCam");
            var cam = studioCam.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            Directory.CreateDirectory(HeroShotDir);
            return true;
        }

        /// <summary>Renders one 1600x900 PNG. Key light comes from over the camera's shoulder unless keepSun is set.</summary>
        static void StudioShot(string name, Vector3 pos, Vector3 target, float fov = 35f, bool keepSun = false)
        {
            Vector3 view = target - pos;
            if (!keepSun)
                studioSun.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(40f, Vector3.up) * new Vector3(view.x, 0f, view.z).normalized + Vector3.down * 0.8f);
            studioProbe.transform.position = target;
            studioProbe.RenderProbe();
            var cam = studioCam.GetComponent<Camera>();
            cam.fieldOfView = fov;
            studioCam.transform.position = pos;
            studioCam.transform.rotation = Quaternion.LookRotation(view, Vector3.up);
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

        static GameObject StagePrefab(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + prefabName + ".prefab");
            if (prefab == null) { Debug.LogWarning("[Voidward] Preview: prefab not found: " + prefabName); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // Review the hull, not the boss shield bubble.
            var shield = go.transform.Find("Shield");
            if (shield != null) shield.gameObject.SetActive(false);
            return go;
        }

        static Bounds VisualBounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial == MatPlume || r.sharedMaterial == MatGlow) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        [MenuItem("Voidward/Dev/Hero Preview Shots", priority = 200)]
        public static void HeroPreviewShots()
        {
            if (!OpenStudio()) return;
            EnsureFolders();
            BuildMaterials();
            var hull = DartHullMesh();
            var canopy = DartCanopyMesh();
            var astro = AstronautMesh();
            AssetDatabase.SaveAssets();

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

            StudioShot("dart_front34", new Vector3(-8.5f, 3.2f, 9f), new Vector3(0f, 0f, 0.3f));
            StudioShot("dart_rear34", new Vector3(7.5f, 3.8f, -10f), new Vector3(0f, 0f, -1f));
            StudioShot("dart_side", new Vector3(-14f, 1.2f, 0f), new Vector3(0f, 0f, 0f));
            StudioShot("dart_top", new Vector3(0.01f, 15f, -1f), new Vector3(0f, 0f, -0.5f));
            StudioShot("dart_cockpit", new Vector3(-2.2f, 2.2f, 3.6f), new Vector3(0f, 0.9f, 1.0f), 40f);
            StudioShot("dart_engines", new Vector3(2.4f, 1.0f, -8.2f), new Vector3(0f, 0f, -4.4f), 40f);
            StudioShot("astronaut", new Vector3(-4.6f, 0.4f, 7.6f), pilot.transform.position + Vector3.up * 0.9f, 30f);
            StudioShot("astronaut_back", new Vector3(-1.6f, 0.6f, 1.2f), pilot.transform.position + Vector3.up * 0.9f, 30f);
            Debug.Log("[Voidward] Hero preview shots written to " + Path.GetFullPath(HeroShotDir));
        }

        /// <summary>Stages every prefab whose name contains `filter` (all if empty) and renders three framed angles each.</summary>
        public static void PreviewPrefabs(string filter)
        {
            if (!OpenStudio()) return;
            if (MatPlume == null) MatPlume = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/VW_Plume.mat");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (!string.IsNullOrEmpty(filter) && !name.Contains(filter)) continue;
                var go = StagePrefab(name);
                if (go == null) continue;
                var b = VisualBounds(go);
                if (b.size.sqrMagnitude < 1e-4f) { Object.DestroyImmediate(go); continue; }
                float r = b.extents.magnitude;
                float d = r / Mathf.Tan(17f * Mathf.Deg2Rad) * 0.72f;
                Vector3 c = b.center;
                StudioShot(name + "_front34", c + new Vector3(-0.62f, 0.35f, 0.7f).normalized * d, c);
                StudioShot(name + "_rear34", c + new Vector3(0.6f, 0.45f, -0.66f).normalized * d, c);
                StudioShot(name + "_side", c + new Vector3(-1f, 0.12f, 0.05f).normalized * d, c);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[Voidward] Prefab preview shots written to " + Path.GetFullPath(HeroShotDir));
        }

        /// <summary>One custom shot of a prefab; camera position and target are in the prefab's local space.</summary>
        public static void PreviewShot(string prefabName, Vector3 camLocal, Vector3 targetLocal, float fov, string outName, bool keepSun = false)
        {
            if (!OpenStudio()) return;
            var go = StagePrefab(prefabName);
            if (go == null) return;
            studioSun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            StudioShot(outName, go.transform.TransformPoint(camLocal), go.transform.TransformPoint(targetLocal), fov, keepSun);
            Object.DestroyImmediate(go);
        }
    }
}
