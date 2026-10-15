using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Crew suit and hand-tool assets (#95, Tools/Blender/crew_assets.py): the CoP spacesuit (tnnv), the AR-H470 rifle
    /// (Frostoise), the sci-fi pistol (Eugen Vahrushin) as the blaster and the drill kitbashed from the rifle. The FBX
    /// files keep their source material names; the importer remaps them onto URP Lit materials built here from the
    /// texture sets next to them. Tools: barrel along +Z, origin = palm on the grip, muzzle at <see cref="ToolMuzzle"/>.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string ToolModelDir = ModelDir + "/Tools";
        const string SuitDir = CrewModelDir + "/Suit";
        static readonly string[] ToolFbx = { "Tool_Drill", "Tool_Blaster", "Tool_Rifle" };

        /// <summary>Muzzle relative to the grip origin per tool (printed by crew_assets.py), index = PlayerPawn.Tool.</summary>
        static readonly Vector3[] ToolMuzzle = { new Vector3(0f, 0.094f, 0.493f), new Vector3(0f, 0.057f, 0.191f), new Vector3(0f, 0.095f, 0.609f) };

        /// <summary>URP Lit from separate maps (any may be null): base colour, metallic + roughness (packed), normal, AO, emission.</summary>
        static Material LitMaterial(string name, string baseMap, string metal, string rough, string normal, bool flipGreen,
            string ao = null, string emissive = null, float emission = 1f, float metallic = 1f, float smoothness = 1f)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white);
            if (baseMap != null) m.SetTexture("_BaseMap", ImportTexture(baseMap, true));
            if (metal != null && rough != null)
            {
                string packed = System.IO.Path.ChangeExtension(baseMap ?? metal, null) + "_MetallicSmoothness.png";
                m.SetTexture("_MetallicGlossMap", PackMetallicSmoothness(metal, rough, packed));
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                metallic = smoothness = 1f;
            }
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", ImportTexture(normal, false, true, flipGreen));
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (ao != null)
            {
                m.SetTexture("_OcclusionMap", ImportTexture(ao, false, maxSize: 2048));
                m.SetFloat("_OcclusionStrength", 1f);
                m.EnableKeyword("_OCCLUSIONMAP");
            }
            if (emissive != null)
            {
                m.SetTexture("_EmissionMap", ImportTexture(emissive, true, maxSize: 2048));
                m.SetColor("_EmissionColor", Color.white * emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material SolidLit(string name, Color color, float metallic, float smoothness)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Suit materials by FBX material name (suit cloth, helmet shell, visor glass, the face inside).</summary>
        static Dictionary<string, Material> SuitMaterials()
        {
            string t(string f) => SuitDir + "/" + f + ".png";
            var visor = Mat("Crew_Visor", "VOIDWARD/Glass");
            visor.SetColor("_BaseColor", new Color(0.12f, 0.2f, 0.28f, 0.55f));
            visor.SetColor("_RimColor", new Color(1f, 0.62f, 0.3f, 1f));
            visor.SetFloat("_RimPower", 2.5f);
            return new Dictionary<string, Material>
            {
                ["Spacesuit"] = LitMaterial("Crew_Suit", t("Spacesuit_Albedo"), t("Spacesuit_Met"), t("Spacesuit_R"), t("Spacesuit_Normal"), false),
                ["Helmet"] = LitMaterial("Crew_Helmet", t("Helmet_Albedo"), t("Helmet_Met"), t("Helmet_R"), t("Helmet_Normal"), false),
                ["Glass"] = visor,
                ["Body"] = LitMaterial("Crew_Face", t("Human_D"), null, null, null, false, metallic: 0f, smoothness: 0.3f),
            };
        }

        /// <summary>Tool materials by FBX material name; index = PlayerPawn.Tool (0 drill, 1 blaster, 2 rifle).</summary>
        static Dictionary<string, Material> ToolMaterials(int tool)
        {
            string ar(string f) => ToolModelDir + "/ARH470/" + f + ".png";
            string ps(string f) => ToolModelDir + "/Pistol/" + f + ".png";
            Material arSet(string part, string emissive = null) => LitMaterial("Tool_AR_" + part, ar(part + "_Base_color"), ar(part + "_Metallic"),
                ar(part + "_Roughness"), ar(part + (part == "Scope" ? "_Normal" : "_Normal_OpenGL")), false, ar(part + "_Mixed_AO"), emissive, 2f);
            var mats = new Dictionary<string, Material>();
            if (tool == 1)
            {
                var p = LitMaterial("Tool_Pistol", ps("Pistol_Base_Color"), ps("Pistol_Metallic"), ps("Pistol_Roughness"), ps("Pistol_Normal_DirectX"), true, ps("Pistol_Mixed_AO"));
                mats["Pistol"] = p;
                mats["Sight"] = p;
                return mats;
            }
            mats["MainWeapon"] = arSet("MainWeapon");
            mats["Scope"] = arSet("Scope", ar("Scope_Emissive"));
            if (tool == 2) mats["Clip"] = arSet("Clip");
            if (tool == 0)
            {
                mats["DrillHead"] = SolidLit("Tool_DrillHead", new Color(0.1f, 0.11f, 0.12f), 0.85f, 0.55f);
                mats["DrillBit"] = SolidLit("Tool_DrillBit", new Color(0.5f, 0.52f, 0.55f), 1f, 0.7f);
                mats["DrillGlow"] = GlowMaterial("VW_DrillGlow", new Color(1f, 0.42f, 0.1f) * 1.6f);
            }
            return mats;
        }

        /// <summary>
        /// Points the FBX importer's materials at ours (by source material name). Animation/rig settings stay as they are.
        /// Reimports only when the remap changed.
        /// </summary>
        static void RemapMaterials(ModelImporter imp, Dictionary<string, Material> mats)
        {
            bool dirty = imp.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription
                || imp.materialLocation != ModelImporterMaterialLocation.InPrefab;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            var map = imp.GetExternalObjectMap();
            foreach (var kv in mats)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                if (map.TryGetValue(id, out var cur) && cur == kv.Value) continue;
                imp.RemoveRemap(id);
                imp.AddRemap(id, kv.Value);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>A tool model (index = PlayerPawn.Tool) under parent, origin = grip; null when its FBX is missing.</summary>
        static GameObject ToolModel(Transform parent, int tool, string name)
        {
            if (tool < 0 || tool >= ToolFbx.Length) return null;
            string path = ToolModelDir + "/" + ToolFbx[tool] + ".fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return null;
            if (imp.importAnimation || imp.animationType != ModelImporterAnimationType.None || imp.importTangents != ModelImporterTangents.CalculateMikk)
            {
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.importCameras = imp.importLights = false;
                imp.SaveAndReimport();
            }
            RemapMaterials(imp, ToolMaterials(tool));
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src, parent, false);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>A tool's visual: the asset model, else the procedural mesh (MatHull) at `fallbackScale`.</summary>
        static GameObject ToolVisual(Transform parent, int tool, Mesh fallback, float fallbackScale, out bool asset)
        {
            var go = ToolModel(parent, tool, "Model");
            asset = go != null;
            if (go != null) return go;
            go = Visual(parent, "Mesh", fallback, MatHull);
            go.transform.localScale = Vector3.one * fallbackScale;
            return go;
        }

        /// <summary>Renderers of a tool visual without shadows (viewmodels sit in front of the camera).</summary>
        static void NoShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static bool HasToolModel(int tool) => tool >= 0 && tool < ToolFbx.Length && AssetImporter.GetAtPath(ToolModelDir + "/" + ToolFbx[tool] + ".fbx") is ModelImporter;
    }
}
