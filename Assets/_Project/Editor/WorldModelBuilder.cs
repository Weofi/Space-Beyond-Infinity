using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>
    /// World objects + POIs from free Sketchfab assets (#99, Tools/Blender/world_assets.py): station, landing pad, wreck,
    /// ruin, nest, crate, rock, battery, medkit. Each asset has its FBX in Art/Models/World and a texture folder of the
    /// same name with the glTF set per material (see GltfMaterials). The ruin is untextured and uses the vertex contract
    /// (ImportedMesh + MatHull). Returns null when an FBX is missing; PrefabBuilder then keeps the procedural build.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string WorldModelDir = ModelDir + "/World";

        /// <summary>Parts cut from another asset share its texture folder.</summary>
        static readonly Dictionary<string, string> WorldTextureSet = new Dictionary<string, string>
        {
            ["World_Crate_Lid"] = "World_Crate", ["World_Wreck_Chunks"] = "World_Wreck", ["World_Scrap"] = "World_Wreck",
        };

        static string WorldTextures(string asset) => WorldModelDir + "/" + (WorldTextureSet.TryGetValue(asset, out var set) ? set : asset);

        static bool HasWorldModel(string asset) => AssetImporter.GetAtPath(WorldModelDir + "/" + asset + ".fbx") is ModelImporter;

        /// <summary>Static import (no rig/animation/cameras/lights, MikkTSpace tangents) with the asset's URP materials remapped.</summary>
        static GameObject WorldSource(string asset)
        {
            string set = WorldTextureSet.TryGetValue(asset, out var shared) ? shared : asset;
            return GltfModelSource(WorldModelDir + "/" + asset + ".fbx", WorldTextures(asset), set);
        }

        /// <summary>
        /// Static import of a Sketchfab-derived FBX (no rig/animation/cameras/lights, MikkTSpace tangents) with URP materials
        /// from its glTF texture folder (GltfMaterials, named `set_material`) remapped by material name. Null when missing.
        /// </summary>
        static GameObject GltfModelSource(string path, string texDir, string set)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return null;
            if (imp.importAnimation || imp.animationType != ModelImporterAnimationType.None || imp.importTangents != ModelImporterTangents.CalculateMikk
                || imp.importNormals != ModelImporterNormals.Import || imp.importCameras || imp.importLights)
            {
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.importCameras = imp.importLights = false;
                imp.SaveAndReimport();
            }
            RemapMaterials(imp, GltfMaterials(texDir, set));
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>The asset model under parent ("Model" child, origin as exported); null when the FBX is missing.</summary>
        static GameObject WorldModel(Transform parent, string asset, string name = "Model")
        {
            var src = WorldSource(asset);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src, parent, false);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>The asset's (single) mesh and its renderer materials, e.g. for colliders or a swappable pickup mesh.</summary>
        static Mesh WorldMesh(string asset, out Material[] materials)
        {
            materials = null;
            var src = WorldSource(asset);
            if (src == null) return null;
            var mf = src.GetComponentInChildren<MeshFilter>();
            var mr = src.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null) return null;
            materials = mr.sharedMaterials;
            return mf.sharedMesh;
        }

        static Mesh WorldMesh(string asset) => WorldMesh(asset, out _);

        /// <summary>The asset model with a static MeshCollider on its mesh (walkable / flyable surfaces).</summary>
        static GameObject WorldModelSolid(Transform parent, string asset, string name = "Model")
        {
            var go = WorldModel(parent, asset, name);
            if (go == null) return null;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return go;
        }

        /// <summary>Renderers of a model, for bounds (icons) or render settings.</summary>
        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>The precursor ruin (vertex contract: ivory ceramic, slate shade, cyan glow in the crevices). Null if missing.</summary>
        static Mesh WorldRuinMesh() => ImportedMesh(WorldModelDir + "/World_Ruin.fbx", "World_Ruin");
    }
}
