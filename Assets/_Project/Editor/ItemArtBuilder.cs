using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Item art (#24): one procedural hero model per item (resources, consumables, gear, ship modules), used by
    /// world pickups, plus a transparent 256px icon per item rendered from that model in a throwaway studio scene.
    /// Icons land in Resources/UI/Icons and are referenced by ItemDef.Icon; the HUD tool slots get their own icons.
    /// Models face +Z, sit on the origin and are roughly 0.5 m across.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string ItemMeshDir = MeshDir + "/Items";
        const string IconDir = ResourcesDir + "/UI/Icons";
        const int IconSize = 256;

        static readonly Color TierGrey = new Color(0.55f, 0.6f, 0.68f);
        static readonly Color TierCyan = new Color(0.22f, 0.74f, 0.97f);
        static readonly Color TierPurple = new Color(0.66f, 0.33f, 0.97f);

        static Color TierColor(int tier) => tier >= 3 ? TierPurple : tier == 2 ? TierCyan : TierGrey;

        /// <summary>Hero builder without the triplanar panel/grime detail (too noisy at item scale).</summary>
        static MeshBuilder ItemMB(float bevel) { var mb = Hero(bevel); mb.Panel = 0f; return mb; }

        [MenuItem("Voidward/Dev/Rebuild Item Art", priority = 210)]
        public static void RebuildItemArtMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Voidward] Item art: leave Play Mode first."); return; }
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(ResourcesDir + "/ContentCatalog.asset");
            if (MatHull == null) MatHull = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/VW_Hull.mat");
            string scene = EditorSceneManager.GetActiveScene().path;
            BuildItemArt();
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene);
        }

        /// <summary>Models + icons for every catalog item and the HUD tools. Opens an empty scene (callers reopen theirs).</summary>
        static void BuildItemArt()
        {
            foreach (var d in new[] { ItemMeshDir, ResourcesDir + "/UI", IconDir })
                if (!AssetDatabase.IsValidFolder(d))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(d).Replace('\\', '/'), Path.GetFileName(d));

            var models = new List<(ItemDef item, Mesh mesh)>();
            foreach (var it in catalog.Items)
            {
                if (it == null) continue;
                var mb = ItemModel(it);
                var mesh = SaveMeshAsset(mb.Build("Item_" + it.Id), "Items/Item_" + it.Id);
                it.Model = mesh;
                EditorUtility.SetDirty(it);
                models.Add((it, mesh));
            }
            var tools = new[]
            {
                ("tool_drill", ToolDrill(1)), ("tool_blaster", ToolBlaster()), ("tool_rifle", ToolRifle()),
            };
            var toolMeshes = new List<(string id, Mesh mesh)>();
            foreach (var (id, mb) in tools) toolMeshes.Add((id, SaveMeshAsset(mb.Build(id), "Items/" + id)));
            AssetDatabase.SaveAssets();

            // Studio: empty scene, key + rim light, transparent clear, no post (keeps alpha).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.5f; key.color = new Color(1f, 0.97f, 0.92f);
            key.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional; rim.intensity = 0.9f; rim.color = new Color(0.55f, 0.75f, 1f);
            rim.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.46f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var camGo = new GameObject("IconCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 50f;
            cam.allowHDR = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            var stage = new GameObject("Item");
            stage.AddComponent<MeshFilter>();
            stage.AddComponent<MeshRenderer>().sharedMaterial = MatHull;

            foreach (var (item, mesh) in models)
            {
                // drills and the rifle as gear items show the asset tools (#95)
                int tool = item.Kind == ItemKind.Gear ? (item.Gear == GearKind.MiningTool ? 0 : item.Gear == GearKind.Rifle ? 2 : -1) : -1;
                item.Icon = tool >= 0 ? RenderToolIcon(cam, stage, tool, mesh, item.Id) : RenderIcon(cam, stage, mesh, item.Id);
            }
            catalog.IconDrill = RenderToolIcon(cam, stage, 0, toolMeshes[0].mesh, toolMeshes[0].id);
            catalog.IconBlaster = RenderToolIcon(cam, stage, 1, toolMeshes[1].mesh, toolMeshes[1].id);
            catalog.IconRifle = RenderToolIcon(cam, stage, 2, toolMeshes[2].mesh, toolMeshes[2].id);
            foreach (var (item, _) in models) EditorUtility.SetDirty(item);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voidward] Item art: {models.Count} item models + icons, {toolMeshes.Count} tool icons.");
        }

        /// <summary>Three-quarter hero angle, auto-framed on the mesh bounds; 2x supersampled then box-filtered.</summary>
        static Sprite RenderIcon(Camera cam, GameObject stage, Mesh mesh, string id)
        {
            stage.GetComponent<MeshFilter>().sharedMesh = mesh;
            stage.transform.rotation = Quaternion.Euler(0f, 215f, 0f);
            return CaptureIcon(cam, stage.GetComponent<MeshRenderer>().bounds, id);
        }

        /// <summary>A tool icon from its asset model (#95), else from the procedural mesh on the stage.</summary>
        static Sprite RenderToolIcon(Camera cam, GameObject stage, int tool, Mesh fallback, string id)
        {
            var model = ToolModel(null, tool, "IconModel");
            if (model == null) return RenderIcon(cam, stage, fallback, id);
            stage.SetActive(false);
            model.transform.rotation = Quaternion.Euler(0f, 215f, 0f);
            Bounds b = default;
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            var icon = CaptureIcon(cam, b, id);
            Object.DestroyImmediate(model);
            stage.SetActive(true);
            return icon;
        }

        static Sprite CaptureIcon(Camera cam, Bounds b, string id)
        {
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
            cam.transform.position = b.center - Quaternion.Euler(24f, 0f, 0f) * Vector3.forward * dist;
            cam.transform.LookAt(b.center);

            const int S = IconSize * 2;
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var big = new Texture2D(S, S, TextureFormat.RGBA32, false);
            big.ReadPixels(new Rect(0, 0, S, S), 0, 0);
            big.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            var px = big.GetPixels();
            var outPx = new Color[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
                for (int x = 0; x < IconSize; x++)
                {
                    Color sum = Color.clear;
                    for (int k = 0; k < 4; k++)
                    {
                        var c = px[(y * 2 + (k >> 1)) * S + x * 2 + (k & 1)];
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);   // premultiply for a clean edge
                    }
                    sum *= 0.25f;
                    outPx[y * IconSize + x] = sum.a > 0.001f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                }
            Object.DestroyImmediate(big);
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            tex.SetPixels(outPx);
            tex.Apply();
            string path = IconDir + "/" + id + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ================================================================ models

        static MeshBuilder ItemModel(ItemDef it)
        {
            if (it.Kind == ItemKind.Module && it.Module != null) return ModuleModel(it.Module, it.Id);
            if (it.Kind == ItemKind.DriveComponent && it.Drive != null) return DriveModel(it.Drive);
            if (it.Kind == ItemKind.Gear)
            {
                switch (it.Gear)
                {
                    case GearKind.Suit: return GearHelmet(it.GearTier);
                    case GearKind.Oxygen: return GearOxygen(it.GearTier);
                    case GearKind.Jetpack: return GearJetpack(it.GearTier);
                    case GearKind.MiningTool: return ToolDrill(it.GearTier);
                    case GearKind.Rifle: return ToolRifle();
                }
            }
            switch (it.Id)
            {
                case "ore": return ItemOre();
                case "crystal": return ItemCrystal();
                case "scrap": return ItemScrap();
                case "fuel": return ItemFuel();
                case "exotic": return ItemExotic(new Color(0.7f, 0.4f, 1f));
                case "medkit": return ItemMedkit();
                case "repair_kit": return ItemRepairKit();
            }
            var mb = ItemMB(0.2f);
            mb.AddBox(T(0, 0.18f, 0), new Vector3(0.36f, 0.36f, 0.36f), it.Tint);
            return mb;
        }

        static MeshBuilder ModuleModel(ModuleDef m, string id)
        {
            Color tier = TierColor(m.Tier);
            if (id == "module_engine_ark") return ModEngine(3, tier, 1.25f);
            switch (m.Slot)
            {
                case ModuleSlot.Engine: return ModEngine(m.Tier, tier, 1f);
                case ModuleSlot.WeaponA:
                case ModuleSlot.WeaponB:
                    return m.Weapon == WeaponKind.Missile ? ModMissile(tier) : m.Weapon == WeaponKind.Flak ? ModFlak(tier) : ModLaser(m.Tier, tier);
                case ModuleSlot.Shield: return ModShield(m.Tier, tier);
                case ModuleSlot.Hull: return ModHull(m.Tier, tier);
                case ModuleSlot.Cargo: return ModCargo(tier);
                case ModuleSlot.Scanner: return ModScanner(tier);
                case ModuleSlot.FuelTank: return ModFuelTank(tier);
                case ModuleSlot.Targeting: return ModTargeting(tier);
                case ModuleSlot.WeaponMod:
                    if (id.Contains("splitter")) return ModSplitter(tier);
                    if (id.Contains("focus")) return ModFocus(tier);
                    return ModCapacitor(tier);
            }
            var mb = ItemMB(0.2f);
            mb.AddBox(T(0, 0.18f, 0), new Vector3(0.4f, 0.3f, 0.4f), Chalk);
            return mb;
        }

        /// <summary>Common mounting base for modules: dark chassis plate with a tier-coloured status strip.</summary>
        static void ModBase(MeshBuilder mb, float w, float d, Color tier)
        {
            With(mb, Satin, 0.6f, 1f, () => mb.AddBox(T(0, 0.03f, 0), new Vector3(w, 0.06f, d), Slate));
            Glow(mb, () => mb.AddBox(T(0, 0.035f, d * 0.5f), new Vector3(w * 0.6f, 0.02f, 0.012f), tier), 1.2f);
        }

        // ---------------------------------------------------------------- resources

        static MeshBuilder ItemOre()
        {
            var mb = new MeshBuilder { Gloss = 0.12f, Panel = 1f, SmoothAngle = 30f };
            Color stone = new Color(0.58f, 0.53f, 0.5f);
            mb.AddRock(Matrix4x4.TRS(new Vector3(0f, 0.15f, 0f), Quaternion.Euler(10, 20, 0), new Vector3(1f, 0.8f, 1f)), 0.19f, 11, stone);
            mb.AddRock(Matrix4x4.TRS(new Vector3(0.17f, 0.09f, 0.06f), Quaternion.Euler(0, 60, 30), Vector3.one), 0.1f, 12, stone * 1.1f);
            mb.AddRock(Matrix4x4.TRS(new Vector3(-0.15f, 0.08f, 0.08f), Quaternion.Euler(30, 0, 10), Vector3.one), 0.09f, 13, stone * 0.95f);
            // copper veins: glossy metallic nuggets poking out of the stone
            Color copper = new Color(1f, 0.52f, 0.2f);
            With(mb, 0.85f, 1f, 0f, () =>
            {
                var rng = new System.Random(5);
                for (int i = 0; i < 9; i++)
                {
                    var dir = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.2f - 0.2f, (float)rng.NextDouble() * 2 - 1).normalized;
                    mb.AddRock(Matrix4x4.TRS(new Vector3(0f, 0.15f, 0f) + dir * 0.16f, Quaternion.Euler(i * 47f, i * 83f, 0f), Vector3.one), 0.045f + (float)rng.NextDouble() * 0.035f, 20 + i, copper);
                }
            });
            return mb;
        }

        static MeshBuilder ItemCrystal()
        {
            var mb = new MeshBuilder { Gloss = 0.9f, Panel = 0f, SmoothAngle = 10f };
            Color stone = new Color(0.3f, 0.32f, 0.4f);
            mb.AddRock(Matrix4x4.TRS(new Vector3(0, 0.05f, 0), Quaternion.identity, new Vector3(1.3f, 0.45f, 1.2f)), 0.15f, 31, stone);
            Color c = new Color(0.45f, 0.88f, 1f);
            var shards = new[] { (0f, 0f, 0f, 0f, 0.46f, 0.07f), (0.09f, 0.03f, -25f, 15f, 0.3f, 0.05f), (-0.08f, -0.02f, 22f, -10f, 0.34f, 0.055f), (0.02f, 0.09f, -8f, 30f, 0.24f, 0.045f), (-0.03f, -0.09f, 12f, -28f, 0.22f, 0.04f) };
            foreach (var (x, z, rz, rx, h, r) in shards)
            {
                var m = Matrix4x4.TRS(new Vector3(x, 0.06f, z), Quaternion.Euler(rx, 0, rz), Vector3.one);
                Glow(mb, () =>
                {
                    mb.AddPrism(m * T(0, h * 0.4f, 0), r, r, h * 0.8f, 6, c);
                    mb.AddPrism(m * T(0, h * 0.9f, 0), r, 0.002f, h * 0.2f, 6, c * 1.15f);
                }, 0.55f);
            }
            return mb;
        }

        static MeshBuilder ItemScrap()
        {
            var mb = ItemMB(0.12f);
            mb.AddSlab(TR(0, 0.04f, 0, 0, 20, 4), new[] { new Vector2(-0.2f, -0.13f), new Vector2(0.18f, -0.16f), new Vector2(0.22f, 0.1f), new Vector2(0.02f, 0.18f), new Vector2(-0.2f, 0.1f) }, 0.035f, 0.01f, Chalk);
            Hazard(mb, TR(0.0f, 0.062f, -0.02f, 0, 20, 4), 0.3f, 0.06f, 4, 0.006f);
            With(mb, Satin, 0.8f, 0.5f, () =>
            {
                mb.AddSlab(TR(0.04f, 0.12f, 0.03f, 25, -30, 15), new[] { new Vector2(-0.14f, -0.08f), new Vector2(0.14f, -0.06f), new Vector2(0.1f, 0.09f), new Vector2(-0.12f, 0.07f) }, 0.025f, 0.008f, Gunmetal);
                mb.AddTube(new Vector3(-0.2f, 0.07f, -0.1f), new Vector3(0.16f, 0.2f, 0.12f), 0.03f, 0.03f, 10, Gunmetal * 0.85f);
                mb.AddLathe(TR(-0.1f, 0.13f, 0.12f, 70, 0, 20), new[] { new Vector2(0.04f, -0.015f), new Vector2(0.075f, -0.015f), new Vector2(0.075f, 0.015f), new Vector2(0.04f, 0.015f) }, 12, Gunmetal);
            });
            Glow(mb, () => mb.AddBox(TR(0.1f, 0.075f, -0.05f, 0, 20, 4), new Vector3(0.05f, 0.012f, 0.02f), Cyan), 1f);
            return mb;
        }

        static MeshBuilder ItemFuel()
        {
            var mb = ItemMB(0.1f);
            Color amber = new Color(1f, 0.74f, 0.15f);
            mb.AddLathe(Matrix4x4.identity, new[]
            {
                new Vector2(0f, 0f), new Vector2(0.13f, 0f), new Vector2(0.15f, 0.03f), new Vector2(0.15f, 0.36f), new Vector2(0.12f, 0.41f), new Vector2(0.05f, 0.43f), new Vector2(0f, 0.43f),
            }, 18, amber);
            Hazard(mb, Matrix4x4.TRS(new Vector3(0, 0.2f, 0.152f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.18f, 0.07f, 4, 0.006f);
            With(mb, Satin, 0.8f, 0f, () =>
            {
                mb.AddPrism(T(0, 0.08f, 0), 0.155f, 0.155f, 0.02f, 18, Slate);
                mb.AddPrism(T(0, 0.33f, 0), 0.155f, 0.155f, 0.02f, 18, Slate);
                mb.AddPrism(T(0, 0.45f, 0), 0.04f, 0.035f, 0.05f, 10, Gunmetal);
                Polyline(mb, new[] { new Vector3(-0.08f, 0.42f, 0f), new Vector3(-0.06f, 0.5f, 0f), new Vector3(0.06f, 0.5f, 0f), new Vector3(0.08f, 0.42f, 0f) }, 0.014f, 8, Rubber);
            });
            return mb;
        }

        /// <summary>Exotic Matter (#78): a glowing violet core in a containment flask with gold caps.</summary>
        static MeshBuilder ItemExotic(Color g)
        {
            var mb = ItemMB(0.1f);
            Glow(mb, () => mb.AddPrism(T(0, 0.22f, 0), 0.085f, 0.085f, 0.3f, 12, g), 1.4f);
            With(mb, 0.7f, 0.2f, 0.6f, () =>
            {
                mb.AddLathe(T(0, 0f, 0), new[] { new Vector2(0f, 0f), new Vector2(0.12f, 0f), new Vector2(0.13f, 0.02f), new Vector2(0.13f, 0.08f), new Vector2(0.1f, 0.09f), new Vector2(0f, 0.09f) }, 16, Chalk);
                mb.AddLathe(T(0, 0.35f, 0), new[] { new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.13f, 0.01f), new Vector2(0.13f, 0.07f), new Vector2(0.11f, 0.09f), new Vector2(0f, 0.09f) }, 16, Chalk);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 90f * Mathf.Deg2Rad + 0.785f;
                    mb.AddBox(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.105f, 0.22f, Mathf.Sin(a) * 0.105f), Quaternion.Euler(0, -k * 90f - 45f, 0), Vector3.one), new Vector3(0.02f, 0.3f, 0.03f), Slate);
                }
            });
            With(mb, 0.9f, 1f, 0f, () => mb.AddPrism(T(0, 0.46f, 0), 0.035f, 0.03f, 0.03f, 10, Gold));
            return mb;
        }

        static MeshBuilder ItemMedkit()
        {
            var mb = ItemMB(0.18f);
            mb.AddBox(T(0, 0.12f, 0), new Vector3(0.4f, 0.24f, 0.28f), Chalk);
            With(mb, Satin, 0.6f, 0f, () =>
            {
                mb.AddBox(T(0, 0.12f, 0), new Vector3(0.41f, 0.03f, 0.29f), Slate);
                Polyline(mb, new[] { new Vector3(-0.08f, 0.24f, 0f), new Vector3(-0.07f, 0.3f, 0f), new Vector3(0.07f, 0.3f, 0f), new Vector3(0.08f, 0.24f, 0f) }, 0.016f, 8, Rubber);
            });
            Color red = new Color(0.95f, 0.22f, 0.3f);
            Glow(mb, () =>
            {
                mb.AddBox(T(0, 0.17f, 0.142f), new Vector3(0.05f, 0.13f, 0.01f), red);
                mb.AddBox(T(0, 0.17f, 0.142f), new Vector3(0.13f, 0.05f, 0.01f), red);
                mb.AddBox(T(0.202f, 0.17f, 0), new Vector3(0.01f, 0.1f, 0.04f), red);
                mb.AddBox(T(0.202f, 0.17f, 0), new Vector3(0.01f, 0.04f, 0.1f), red);
            }, 0.4f);
            return mb;
        }

        static MeshBuilder ItemRepairKit()
        {
            var mb = ItemMB(0.16f);
            Color orange = new Color(1f, 0.5f, 0.12f);
            mb.AddBox(T(0, 0.1f, 0), new Vector3(0.42f, 0.2f, 0.24f), orange);
            With(mb, Satin, 0.7f, 0f, () =>
            {
                mb.AddBox(T(0, 0.205f, 0), new Vector3(0.36f, 0.02f, 0.2f), Slate);
                mb.AddBox(T(-0.15f, 0.11f, 0.122f), new Vector3(0.04f, 0.05f, 0.01f), Gunmetal);
                mb.AddBox(T(0.15f, 0.11f, 0.122f), new Vector3(0.04f, 0.05f, 0.01f), Gunmetal);
                // wrench lying on the lid
                mb.AddBox(TR(0f, 0.23f, 0f, 0, 25, 0), new Vector3(0.3f, 0.02f, 0.035f), Mid);
                mb.AddLathe(TR(0.15f * Mathf.Cos(25 * Mathf.Deg2Rad), 0.23f, -0.15f * Mathf.Sin(25 * Mathf.Deg2Rad), 0, 0, 0),
                    new[] { new Vector2(0.025f, -0.012f), new Vector2(0.05f, -0.012f), new Vector2(0.05f, 0.012f), new Vector2(0.025f, 0.012f) }, 10, Mid, 40f, 280f);
            });
            Hazard(mb, Matrix4x4.TRS(new Vector3(0, 0.05f, 0.121f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.36f, 0.04f, 7, 0.006f);
            return mb;
        }

        // ---------------------------------------------------------------- gear / tools

        static MeshBuilder GearHelmet(int tier)
        {
            var mb = ItemMB(0.1f);
            mb.SmoothAngle = 70f;
            mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.22f, 0), Quaternion.identity, new Vector3(1f, 1.05f, 1.02f)), 0.18f, 3, Chalk);
            With(mb, Mirror, 0.6f, 0f, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.235f, 0.075f), Quaternion.identity, new Vector3(0.95f, 0.66f, 0.85f)), 0.16f, 3, Gold * 0.85f));
            With(mb, Satin, 0.6f, 0.5f, () =>
            {
                mb.AddLathe(T(0, 0.04f, 0), new[] { new Vector2(0.13f, 0f), new Vector2(0.17f, 0f), new Vector2(0.18f, 0.04f), new Vector2(0.15f, 0.06f) }, 20, Slate);
                mb.AddBox(T(0.18f, 0.24f, -0.02f), new Vector3(0.04f, 0.08f, 0.1f), Slate);
            });
            Glow(mb, () => mb.AddBox(T(0.2f, 0.24f, 0.01f), new Vector3(0.01f, 0.02f, 0.05f), TierColor(tier)), 1.3f);
            Glow(mb, () => mb.AddBox(T(0, 0.405f, 0.02f), new Vector3(0.08f, 0.012f, 0.03f), Blue), 0.6f);
            return mb;
        }

        static MeshBuilder GearOxygen(int tier)
        {
            var mb = ItemMB(0.1f);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.AddLathe(T(s * 0.085f, 0, 0), new[] { new Vector2(0f, 0f), new Vector2(0.065f, 0f), new Vector2(0.075f, 0.03f), new Vector2(0.075f, 0.36f), new Vector2(0.05f, 0.41f), new Vector2(0f, 0.42f) }, 16, Chalk);
                Glow(mb, () => mb.AddPrism(T(s * 0.085f, 0.24f, 0), 0.077f, 0.077f, 0.025f, 16, new Color(0.15f, 0.8f, 0.95f)), 0.8f);
                With(mb, 0.9f, 1f, 0f, () => mb.AddPrism(T(s * 0.085f, 0.44f, 0), 0.025f, 0.02f, 0.04f, 10, Gunmetal));
            }
            With(mb, Satin, 0.6f, 0f, () =>
            {
                mb.AddBox(T(0, 0.12f, 0), new Vector3(0.12f, 0.04f, 0.17f), Slate);
                mb.AddBox(T(0, 0.3f, 0), new Vector3(0.12f, 0.04f, 0.17f), Slate);
                Polyline(mb, new[] { new Vector3(-0.085f, 0.46f, 0f), new Vector3(0f, 0.5f, 0.02f), new Vector3(0.085f, 0.46f, 0f) }, 0.012f, 8, Rubber);
            });
            Glow(mb, () => mb.AddBox(T(0, 0.12f, 0.087f), new Vector3(0.05f, 0.015f, 0.01f), TierColor(tier)), 1.3f);
            return mb;
        }

        static MeshBuilder GearJetpack(int tier)
        {
            var mb = ItemMB(0.14f);
            mb.AddBox(T(0, 0.22f, 0), new Vector3(0.3f, 0.36f, 0.14f), Chalk);
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(0, 0.22f, -0.075f), new Vector3(0.24f, 0.3f, 0.03f), Slate));
            for (int s = -1; s <= 1; s += 2)
            {
                mb.AddLathe(T(s * 0.1f, 0.04f, -0.02f), new[] { new Vector2(0.03f, -0.06f), new Vector2(0.06f, -0.04f), new Vector2(0.065f, 0.05f), new Vector2(0.05f, 0.08f), new Vector2(0.0f, 0.08f) }, 14, Gunmetal);
                Glow(mb, () => mb.AddPrism(T(s * 0.1f, -0.022f, -0.02f), 0.032f, 0.032f, 0.01f, 12, new Color(1f, 0.6f, 0.2f)), 1.5f);
            }
            mb.AddBox(T(0, 0.33f, 0.075f), new Vector3(0.2f, 0.07f, 0.03f), Blue);
            Glow(mb, () => mb.AddBox(T(0, 0.33f, 0.091f), new Vector3(0.12f, 0.018f, 0.005f), TierColor(tier)), 1.3f);
            return mb;
        }

        static MeshBuilder ToolDrill(int tier)
        {
            var mb = ItemMB(0.12f);
            // body along +Z, grip below
            mb.AddBox(T(0, 0.2f, -0.06f), new Vector3(0.13f, 0.14f, 0.28f), Chalk);
            With(mb, Satin, 0.5f, 0.3f, () =>
            {
                mb.AddBox(TR(0, 0.08f, -0.14f, -15, 0, 0), new Vector3(0.07f, 0.16f, 0.08f), Rubber);
                mb.AddBox(T(0, 0.28f, -0.06f), new Vector3(0.09f, 0.03f, 0.2f), Slate);
            });
            With(mb, 0.85f, 1f, 0f, () =>
            {
                mb.AddLathe(Matrix4x4.TRS(new Vector3(0, 0.2f, 0.08f), Quaternion.Euler(90, 0, 0), Vector3.one),
                    new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0f), new Vector2(0.055f, 0.04f), new Vector2(0.035f, 0.14f), new Vector2(0.0f, 0.2f) }, 10, Gunmetal);
                for (int k = 0; k < 3; k++)
                    mb.AddTube(new Vector3(0, 0.2f, 0.1f + k * 0.04f) + Quaternion.Euler(0, 0, k * 60f) * new Vector3(0.05f - k * 0.01f, 0, 0),
                        new Vector3(0, 0.2f, 0.12f + k * 0.04f) + Quaternion.Euler(0, 0, k * 60f + 120f) * new Vector3(0.05f - k * 0.01f, 0, 0), 0.007f, 0.007f, 6, Gold);
            });
            Glow(mb, () => mb.AddBox(T(0.067f, 0.2f, -0.06f), new Vector3(0.006f, 0.03f, 0.12f), TierColor(tier)), 1.3f);
            return mb;
        }

        static MeshBuilder ToolBlaster()
        {
            var mb = ItemMB(0.1f);
            mb.AddBox(T(0, 0.2f, 0f), new Vector3(0.09f, 0.1f, 0.28f), Chalk);
            With(mb, Satin, 0.5f, 0f, () =>
            {
                mb.AddBox(TR(0, 0.1f, -0.08f, -18, 0, 0), new Vector3(0.07f, 0.17f, 0.08f), Rubber);
                mb.AddBox(T(0, 0.12f, 0.0f), new Vector3(0.02f, 0.04f, 0.08f), Slate);
            });
            With(mb, 0.85f, 1f, 0f, () => mb.AddPrism(Matrix4x4.TRS(new Vector3(0, 0.21f, 0.18f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.03f, 0.03f, 0.1f, 10, Gunmetal));
            Glow(mb, () =>
            {
                mb.AddPrism(Matrix4x4.TRS(new Vector3(0, 0.21f, 0.232f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.022f, 0.022f, 0.006f, 10, Cyan);
                mb.AddBox(T(0.046f, 0.22f, 0.02f), new Vector3(0.006f, 0.02f, 0.14f), Cyan);
            }, 1.4f);
            return mb;
        }

        static MeshBuilder ToolRifle()
        {
            var mb = ItemMB(0.08f);
            mb.AddBox(T(0, 0.2f, 0f), new Vector3(0.08f, 0.11f, 0.34f), Chalk);
            With(mb, Satin, 0.5f, 0.4f, () =>
            {
                mb.AddBox(T(0, 0.19f, -0.24f), new Vector3(0.06f, 0.09f, 0.16f), Slate);
                mb.AddBox(TR(0, 0.1f, -0.06f, -15, 0, 0), new Vector3(0.05f, 0.14f, 0.06f), Rubber);
                mb.AddBox(TR(0, 0.11f, 0.06f, 10, 0, 0), new Vector3(0.04f, 0.12f, 0.05f), Rubber);
                mb.AddBox(T(0, 0.28f, 0.0f), new Vector3(0.03f, 0.04f, 0.12f), Slate);
            });
            With(mb, 0.85f, 1f, 0f, () => mb.AddPrism(Matrix4x4.TRS(new Vector3(0, 0.21f, 0.27f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.022f, 0.02f, 0.2f, 10, Gunmetal));
            Glow(mb, () =>
            {
                mb.AddBox(T(0.041f, 0.21f, 0.03f), new Vector3(0.005f, 0.018f, 0.2f), Gold);
                mb.AddPrism(Matrix4x4.TRS(new Vector3(0, 0.29f, 0.07f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.016f, 0.016f, 0.004f, 10, Cyan);
            }, 1.2f);
            return mb;
        }

        // ---------------------------------------------------------------- ship modules

        // ---------------------------------------------------------------- warp drive components (#78)

        /// <summary>Tier colours 1-6 of drive parts: grey, cyan, purple, orange, gold, white-hot (Home Drive / Warden).</summary>
        static Color DriveTierColor(int tier)
        {
            switch (tier)
            {
                case 1: return TierGrey;
                case 2: return TierCyan;
                case 3: return TierPurple;
                case 4: return new Color(1f, 0.55f, 0.2f);
                case 5: return new Color(1f, 0.8f, 0.25f);
                default: return new Color(1f, 0.97f, 0.9f);
            }
        }

        static MeshBuilder DriveModel(DriveComponentDef d)
        {
            Color tier = DriveTierColor(d.Tier);
            switch (d.Slot)
            {
                case DriveSlot.Core: return DriveCore(d.Tier, tier);
                case DriveSlot.Navigation: return ModScanner(tier);
                case DriveSlot.Cooling: return DriveCooling(d.Tier, tier);
                default:
                {
                    var mb = ItemExotic(Color.Lerp(new Color(0.7f, 0.4f, 1f), tier, 0.35f));
                    ModBase(mb, 0.32f, 0.32f, tier);
                    return mb;
                }
            }
        }

        /// <summary>Warp core: a standing containment ring around a glowing singularity, one gold band per tier.</summary>
        static MeshBuilder DriveCore(int tierN, Color tier)
        {
            var mb = ItemMB(0.08f);
            ModBase(mb, 0.36f, 0.3f, tier);
            var ring = Matrix4x4.TRS(new Vector3(0, 0.22f, 0), Quaternion.Euler(90, 0, 0), Vector3.one);
            With(mb, Satin, 0.8f, 0.6f, () => mb.AddLathe(ring, new[] { new Vector2(0.13f, -0.035f), new Vector2(0.17f, -0.03f), new Vector2(0.175f, 0.03f), new Vector2(0.13f, 0.035f), new Vector2(0.12f, 0f) }, 24, Chalk));
            With(mb, 0.8f, 1f, 0f, () =>
            {
                mb.AddBox(T(0, 0.08f, 0), new Vector3(0.08f, 0.1f, 0.06f), Gunmetal);
                for (int k = 0; k < Mathf.Min(tierN, 5); k++)
                {
                    float a = (k - (Mathf.Min(tierN, 5) - 1) * 0.5f) * 22f;
                    mb.AddBox(Matrix4x4.TRS(new Vector3(0, 0.22f, 0), Quaternion.Euler(0, 0, a), Vector3.one) * T(0, 0.168f, 0), new Vector3(0.03f, 0.016f, 0.08f), Gold);
                }
            });
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.22f, 0), Quaternion.identity, Vector3.one), 0.07f + tierN * 0.006f, 2, Color.Lerp(new Color(0.55f, 0.45f, 1f), tier, 0.4f)), 2.2f);
            Glow(mb, () => mb.AddLathe(ring, new[] { new Vector2(0.115f, -0.01f), new Vector2(0.125f, -0.01f), new Vector2(0.125f, 0.01f), new Vector2(0.115f, 0.01f) }, 24, tier), 1.4f);
            return mb;
        }

        /// <summary>Cooling: a block of radiator fins with a glowing coolant line.</summary>
        static MeshBuilder DriveCooling(int tierN, Color tier)
        {
            var mb = ItemMB(0.06f);
            ModBase(mb, 0.38f, 0.28f, tier);
            With(mb, Satin, 0.8f, 0f, () => mb.AddBox(T(0, 0.09f, 0), new Vector3(0.3f, 0.06f, 0.2f), Gunmetal));
            int fins = 5 + tierN;
            for (int k = 0; k < fins; k++)
            {
                float x = -0.14f + k * (0.28f / (fins - 1));
                With(mb, 0.7f, 1f, 0f, () => mb.AddBox(T(x, 0.19f, 0), new Vector3(0.012f, 0.14f, 0.22f), Chalk));
            }
            Glow(mb, () => mb.AddTube(new Vector3(-0.16f, 0.13f, 0.11f), new Vector3(0.16f, 0.13f, 0.11f), 0.012f, 0.012f, 8, new Color(0.45f, 0.85f, 1f)), 1.6f);
            return mb;
        }

        static MeshBuilder ModEngine(int tierN, Color tier, float scale)
        {
            var mb = ItemMB(0.1f);
            var s = Matrix4x4.Scale(Vector3.one * scale);
            ModBase(mb, 0.36f * scale, 0.3f * scale, tier);
            With(mb, Satin, 0.8f, 0.6f, () => mb.AddBox(s * T(0, 0.12f, -0.02f), new Vector3(0.24f, 0.12f, 0.2f), Chalk));
            var bell = s * Matrix4x4.TRS(new Vector3(0, 0.15f, 0.06f), Quaternion.Euler(90, 0, 0), Vector3.one);
            With(mb, 0.8f, 1f, 0f, () => mb.AddLathe(bell, new[] { new Vector2(0.06f, 0f), new Vector2(0.08f, 0.04f), new Vector2(0.11f + tierN * 0.01f, 0.16f), new Vector2(0.1f + tierN * 0.01f, 0.17f), new Vector2(0.05f, 0.04f) }, 18, Gunmetal));
            Glow(mb, () => mb.AddPrism(bell * T(0, 0.06f, 0), 0.055f, 0.055f, 0.01f, 16, new Color(0.35f, 0.75f, 1f)), 1.8f);
            for (int k = 0; k < tierN; k++)
            {
                float y = 0.09f + k * 0.025f, r = 0.085f + tierN * 0.01f * (y / 0.16f) + 0.012f;
                With(mb, 0.9f, 1f, 0f, () => mb.AddLathe(bell, new[] { new Vector2(r - 0.006f, y - 0.005f), new Vector2(r + 0.006f, y - 0.005f), new Vector2(r + 0.006f, y + 0.005f), new Vector2(r - 0.006f, y + 0.005f) }, 18, Gold));
            }
            return mb;
        }

        static MeshBuilder ModLaser(int tierN, Color tier)
        {
            var mb = ItemMB(0.1f);
            ModBase(mb, 0.3f, 0.3f, tier);
            mb.AddBox(T(0, 0.13f, -0.03f), new Vector3(0.16f, 0.13f, 0.2f), Chalk);
            With(mb, Satin, 0.6f, 0.5f, () => mb.AddBox(T(0, 0.13f, -0.03f), new Vector3(0.17f, 0.03f, 0.21f), Slate));
            var bar = Matrix4x4.TRS(new Vector3(0, 0.14f, 0.14f), Quaternion.Euler(90, 0, 0), Vector3.one);
            With(mb, 0.85f, 1f, 0f, () =>
            {
                mb.AddPrism(bar, 0.028f, 0.024f, 0.26f, 10, Gunmetal);
                for (int k = 0; k < 3; k++) mb.AddPrism(bar * T(0, -0.08f + k * 0.05f, 0), 0.036f, 0.036f, 0.012f, 10, Slate);
            });
            Glow(mb, () => mb.AddPrism(bar * T(0, 0.131f, 0), 0.02f, 0.02f, 0.006f, 10, Cyan), 1.8f);
            Glow(mb, () => mb.AddBox(T(0.082f, 0.15f, -0.03f), new Vector3(0.004f, 0.02f, 0.06f * tierN), Cyan), 1.2f);
            return mb;
        }

        static MeshBuilder ModMissile(Color tier)
        {
            var mb = ItemMB(0.1f);
            ModBase(mb, 0.32f, 0.3f, tier);
            mb.AddBox(T(0, 0.15f, -0.01f), new Vector3(0.26f, 0.18f, 0.24f), Chalk);
            Hazard(mb, TR(0, 0.242f, -0.08f, 0, 0, 0), 0.24f, 0.04f, 6, 0.005f);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    var at = new Vector3(-0.06f + x * 0.12f, 0.11f + y * 0.08f, 0.115f);
                    With(mb, Matte, 0f, 0f, () => mb.AddPrism(Matrix4x4.TRS(at, Quaternion.Euler(90, 0, 0), Vector3.one), 0.034f, 0.034f, 0.01f, 12, Rubber));
                    mb.AddLathe(Matrix4x4.TRS(at + Vector3.forward * 0.005f, Quaternion.Euler(90, 0, 0), Vector3.one),
                        new[] { new Vector2(0.024f, 0f), new Vector2(0.024f, 0.03f), new Vector2(0.012f, 0.055f), new Vector2(0f, 0.062f) }, 10, Red);
                }
            return mb;
        }

        static MeshBuilder ModFlak(Color tier)
        {
            var mb = ItemMB(0.1f);
            ModBase(mb, 0.32f, 0.3f, tier);
            mb.AddLathe(T(0, 0.06f, -0.02f), new[] { new Vector2(0.13f, 0f), new Vector2(0.13f, 0.05f), new Vector2(0.1f, 0.12f), new Vector2(0f, 0.13f) }, 16, Chalk);
            for (int s = -1; s <= 1; s += 2)
            {
                var m = Matrix4x4.TRS(new Vector3(s * 0.05f, 0.14f, 0.1f), Quaternion.Euler(80, 0, 0), Vector3.one);
                With(mb, 0.8f, 1f, 0f, () => mb.AddLathe(m, new[] { new Vector2(0.04f, -0.08f), new Vector2(0.04f, 0.1f), new Vector2(0.046f, 0.12f), new Vector2(0.03f, 0.12f), new Vector2(0.03f, -0.08f) }, 12, Gunmetal));
                Glow(mb, () => mb.AddPrism(m * T(0, 0.1f, 0), 0.029f, 0.029f, 0.005f, 12, new Color(1f, 0.75f, 0.25f)), 1.4f);
            }
            return mb;
        }

        static MeshBuilder ModShield(int tierN, Color tier)
        {
            var mb = ItemMB(0.08f);
            ModBase(mb, 0.32f, 0.32f, tier);
            mb.AddPrism(T(0, 0.09f, 0), 0.14f, 0.12f, 0.06f, 6, Chalk);
            With(mb, Satin, 0.7f, 0f, () => mb.AddPrism(T(0, 0.125f, 0), 0.11f, 0.1f, 0.015f, 6, Slate));
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.13f, 0), Quaternion.identity, new Vector3(1f, 0.6f, 1f)), 0.085f, 2, new Color(0.3f, 0.75f, 1f)), 1.1f);
            for (int k = 0; k < tierN; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad;
                With(mb, 0.9f, 1f, 0f, () => mb.AddTube(new Vector3(Mathf.Cos(a) * 0.12f, 0.12f, Mathf.Sin(a) * 0.12f), new Vector3(Mathf.Cos(a) * 0.05f, 0.25f, Mathf.Sin(a) * 0.05f), 0.01f, 0.006f, 6, Gold));
            }
            return mb;
        }

        static MeshBuilder ModHull(int tierN, Color tier)
        {
            var mb = ItemMB(0.14f);
            for (int k = 0; k < 3; k++)
            {
                var m = TR(0.01f * k, 0.03f + k * 0.055f, -0.01f * k, 0, 8f * k - 8f, 0);
                mb.AddSlab(m, new[] { new Vector2(-0.18f, -0.14f), new Vector2(0.14f, -0.14f), new Vector2(0.2f, -0.06f), new Vector2(0.2f, 0.14f), new Vector2(-0.18f, 0.14f) }, 0.045f, 0.012f, k == 2 ? Chalk : k == 1 ? Slate : Gunmetal);
            }
            With(mb, 0.9f, 1f, 0f, () =>
            {
                foreach (var p in new[] { new Vector2(-0.14f, -0.1f), new Vector2(0.15f, -0.1f), new Vector2(-0.14f, 0.1f), new Vector2(0.15f, 0.1f) })
                    mb.AddPrism(T(p.x + 0.02f, 0.19f, p.y - 0.02f), 0.012f, 0.012f, 0.012f, 8, Gunmetal);
            });
            for (int k = 0; k < tierN; k++)
                Glow(mb, () => mb.AddBox(T(-0.08f + k * 0.04f, 0.192f, 0.08f), new Vector3(0.025f, 0.006f, 0.012f), tier), 1.3f);
            return mb;
        }

        static MeshBuilder ModCargo(Color tier)
        {
            var mb = ItemMB(0.12f);
            mb.AddBox(T(0, 0.15f, 0), new Vector3(0.36f, 0.3f, 0.3f), new Color(0.25f, 0.45f, 0.85f));
            With(mb, Satin, 0.8f, 0f, () =>
            {
                foreach (var x in new[] { -0.17f, 0.17f })
                    foreach (var z in new[] { -0.14f, 0.14f })
                        mb.AddBox(T(x, 0.15f, z), new Vector3(0.035f, 0.31f, 0.035f), Gunmetal);
                mb.AddBox(T(0, 0.15f, 0.151f), new Vector3(0.3f, 0.04f, 0.006f), Slate);
            });
            Hazard(mb, Matrix4x4.TRS(new Vector3(0, 0.24f, 0.152f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.28f, 0.04f, 6, 0.005f);
            Glow(mb, () => mb.AddBox(T(0, 0.15f, 0.156f), new Vector3(0.06f, 0.015f, 0.004f), tier), 1.3f);
            return mb;
        }

        static MeshBuilder ModScanner(Color tier)
        {
            var mb = ItemMB(0.08f);
            ModBase(mb, 0.28f, 0.28f, tier);
            With(mb, Satin, 0.8f, 0f, () => mb.AddPrism(T(0, 0.12f, 0), 0.03f, 0.025f, 0.14f, 10, Gunmetal));
            var dish = Matrix4x4.TRS(new Vector3(0, 0.2f, 0.02f), Quaternion.Euler(55, 0, 0), Vector3.one);
            mb.AddLathe(dish, new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0.008f), new Vector2(0.13f, 0.04f), new Vector2(0.14f, 0.05f), new Vector2(0.12f, 0.05f), new Vector2(0.05f, 0.02f), new Vector2(0f, 0.015f) }, 20, Chalk);
            With(mb, 0.8f, 1f, 0f, () => mb.AddTube(dish.MultiplyPoint3x4(new Vector3(0, 0.02f, 0)), dish.MultiplyPoint3x4(new Vector3(0, 0.11f, 0)), 0.008f, 0.006f, 6, Gunmetal));
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(dish.MultiplyPoint3x4(new Vector3(0, 0.115f, 0)), Quaternion.identity, Vector3.one), 0.016f, 1, Red), 2f);
            return mb;
        }

        static MeshBuilder ModFuelTank(Color tier)
        {
            var mb = ItemMB(0.08f);
            ModBase(mb, 0.4f, 0.24f, tier);
            var tank = Matrix4x4.TRS(new Vector3(0, 0.15f, 0), Quaternion.Euler(0, 0, 90), Vector3.one);
            mb.AddLathe(tank, new[] { new Vector2(0f, -0.2f), new Vector2(0.05f, -0.19f), new Vector2(0.09f, -0.15f), new Vector2(0.095f, -0.1f), new Vector2(0.095f, 0.1f), new Vector2(0.09f, 0.15f), new Vector2(0.05f, 0.19f), new Vector2(0f, 0.2f) }, 18, new Color(1f, 0.74f, 0.15f));
            With(mb, Satin, 0.7f, 0f, () =>
            {
                foreach (var x in new[] { -0.09f, 0.09f })
                    mb.AddPrism(Matrix4x4.TRS(new Vector3(x, 0.15f, 0), Quaternion.Euler(0, 0, 90), Vector3.one), 0.1f, 0.1f, 0.025f, 18, Slate);
            });
            Glow(mb, () => mb.AddBox(T(0, 0.15f, 0.096f), new Vector3(0.08f, 0.02f, 0.005f), tier), 1.3f);
            return mb;
        }

        static MeshBuilder ModTargeting(Color tier)
        {
            var mb = ItemMB(0.06f);
            mb.AddBox(T(0, 0.05f, 0), new Vector3(0.3f, 0.06f, 0.3f), new Color(0.12f, 0.3f, 0.22f));
            With(mb, 0.9f, 1f, 0f, () =>
            {
                for (int k = 0; k < 6; k++)
                    foreach (var side in new[] { -1f, 1f })
                    {
                        mb.AddBox(T(-0.125f + k * 0.05f, 0.03f, side * 0.165f), new Vector3(0.018f, 0.012f, 0.04f), Gold);
                        mb.AddBox(T(side * 0.165f, 0.03f, -0.125f + k * 0.05f), new Vector3(0.04f, 0.012f, 0.018f), Gold);
                    }
            });
            With(mb, Satin, 0.5f, 0f, () => mb.AddBox(T(0, 0.095f, 0), new Vector3(0.18f, 0.03f, 0.18f), Slate));
            Glow(mb, () =>
            {
                mb.AddLathe(T(0, 0.11f, 0), new[] { new Vector2(0.05f, 0f), new Vector2(0.06f, 0f), new Vector2(0.06f, 0.006f), new Vector2(0.05f, 0.006f) }, 20, Red);
                mb.AddBox(T(0, 0.113f, 0), new Vector3(0.14f, 0.004f, 0.008f), Red);
                mb.AddBox(T(0, 0.113f, 0), new Vector3(0.008f, 0.004f, 0.14f), Red);
            }, 1.6f);
            Glow(mb, () => mb.AddBox(T(0.12f, 0.082f, 0.12f), new Vector3(0.02f, 0.004f, 0.02f), tier), 1.3f);
            return mb;
        }

        static MeshBuilder ModSplitter(Color tier)
        {
            var mb = ItemMB(0.08f);
            ModBase(mb, 0.3f, 0.26f, tier);
            mb.AddBox(T(0, 0.11f, -0.06f), new Vector3(0.12f, 0.1f, 0.1f), Chalk);
            Glow(mb, () => mb.AddPrism(Matrix4x4.TRS(new Vector3(0, 0.11f, 0.02f), Quaternion.Euler(90, 0, 0), Vector3.one), 0.045f, 0.01f, 0.08f, 3, Cyan), 1.2f);
            for (int k = -1; k <= 1; k++)
            {
                var d = Quaternion.Euler(0, k * 22f, 0) * Vector3.forward;
                With(mb, 0.85f, 1f, 0f, () => mb.AddTube(new Vector3(0, 0.11f, 0.06f), new Vector3(0, 0.11f, 0.06f) + d * 0.14f, 0.014f, 0.012f, 8, Gunmetal));
                Glow(mb, () => mb.AddSphere(Matrix4x4.Translate(new Vector3(0, 0.11f, 0.06f) + d * 0.15f), 0.018f, 1, Cyan), 1.6f);
            }
            return mb;
        }

        static MeshBuilder ModFocus(Color tier)
        {
            var mb = ItemMB(0.06f);
            ModBase(mb, 0.26f, 0.2f, tier);
            var ring = Matrix4x4.TRS(new Vector3(0, 0.17f, 0), Quaternion.Euler(90, 0, 0), Vector3.one);
            mb.AddLathe(ring, new[] { new Vector2(0.1f, -0.03f), new Vector2(0.13f, -0.03f), new Vector2(0.135f, 0f), new Vector2(0.13f, 0.03f), new Vector2(0.1f, 0.03f) }, 24, Chalk);
            With(mb, 0.9f, 1f, 0f, () => mb.AddLathe(ring, new[] { new Vector2(0.092f, -0.02f), new Vector2(0.1f, -0.02f), new Vector2(0.1f, 0.02f), new Vector2(0.092f, 0.02f) }, 24, Gold));
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.17f, 0), Quaternion.identity, new Vector3(1f, 1f, 0.25f)), 0.095f, 2, new Color(0.45f, 0.9f, 1f)), 0.7f);
            With(mb, Satin, 0.7f, 0f, () => mb.AddBox(T(0, 0.06f, 0), new Vector3(0.06f, 0.06f, 0.05f), Slate));
            return mb;
        }

        static MeshBuilder ModCapacitor(Color tier)
        {
            var mb = ItemMB(0.06f);
            ModBase(mb, 0.28f, 0.28f, tier);
            mb.AddLathe(T(0, 0.06f, 0), new[] { new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.09f, 0.03f), new Vector2(0.07f, 0.035f), new Vector2(0.07f, 0.22f), new Vector2(0.09f, 0.225f), new Vector2(0.09f, 0.25f), new Vector2(0f, 0.25f) }, 18, Chalk);
            for (int k = 0; k < 5; k++)
                With(mb, 0.9f, 1f, 0f, () => mb.AddPrism(T(0, 0.12f + k * 0.03f, 0), 0.078f, 0.078f, 0.014f, 18, Gold));
            Glow(mb, () => mb.AddPrism(T(0, 0.315f, 0), 0.03f, 0.02f, 0.012f, 12, new Color(0.6f, 0.85f, 1f)), 1.8f);
            return mb;
        }
    }
}
