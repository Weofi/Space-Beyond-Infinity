using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voidward.Data;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Planet props + asteroids from free assets (#100, Tools/Blender/planet_assets.py): moon rock scans (rocks, boulders,
    /// asteroids), mossy rocks, crystal and ice clusters, alien plants, glowing mushrooms. Each FBX in Art/Models/Planet
    /// reads its materials from the texture folder of the source it was cut from (see GltfMaterials). The meshes are
    /// imported CPU-readable: PlanetDecor bakes them into chunk meshes on worker threads.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string PlanetModelDir = ModelDir + "/Planet";

        /// <summary>Built prop -> (catalog id, texture folder = its ingested source).</summary>
        static readonly (string fbx, string id, string textures)[] PlanetPropAssets = BuildPlanetPropTable();

        static (string, string, string)[] BuildPlanetPropTable()
        {
            var list = new List<(string, string, string)>();
            for (int i = 1; i <= 7; i++) list.Add(($"Planet_Rock{i:00}", $"Rock{i:00}", $"Planet_MoonRock{i:00}"));
            for (int i = 1; i <= 6; i++) list.Add(($"Planet_MossRock{i:00}", $"MossRock{i:00}", "Planet_MossRocks01"));
            for (int i = 1; i <= 2; i++) list.Add(($"Planet_Crystal{i:00}", $"Crystal{i:00}", $"Planet_CrystalCluster{i:00}"));
            for (int i = 1; i <= 3; i++) list.Add(($"Planet_Ice{i:00}", $"Ice{i:00}", $"Planet_IceCluster{i:00}"));
            list.Add(("Planet_AlienTree", "AlienTree", "Planet_AlienPlants"));
            list.Add(("Planet_FistPlant", "FistPlant", "Planet_AlienPlants"));
            list.Add(("Planet_MinePlant", "MinePlant", "Planet_AlienPlants"));
            list.Add(("Planet_AlienPlant", "AlienPlant", "Planet_AlienPlant"));
            for (int i = 1; i <= 2; i++) list.Add(($"Planet_Mushroom{i:00}", $"Mushroom{i:00}", "Planet_GlowMushrooms"));
            return list.ToArray();
        }

        /// <summary>Every planet prop whose FBX exists (missing ones are skipped; none = the procedural props).</summary>
        static PlanetPropModel[] BuildPlanetProps()
        {
            var props = new List<PlanetPropModel>();
            var matCache = new Dictionary<string, Dictionary<string, Material>>();
            foreach (var (fbx, id, textures) in PlanetPropAssets)
            {
                string path = PlanetModelDir + "/" + fbx + ".fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                if (imp.importAnimation || imp.animationType != ModelImporterAnimationType.None || imp.importTangents != ModelImporterTangents.CalculateMikk
                    || imp.importNormals != ModelImporterNormals.Import || imp.importCameras || imp.importLights || !imp.isReadable)
                {
                    imp.importAnimation = false;
                    imp.animationType = ModelImporterAnimationType.None;
                    imp.importNormals = ModelImporterNormals.Import;
                    imp.importTangents = ModelImporterTangents.CalculateMikk;
                    imp.importCameras = imp.importLights = false;
                    imp.isReadable = true;           // PlanetDecor copies the vertices on worker threads
                    imp.SaveAndReimport();
                }
                if (!matCache.TryGetValue(textures, out var mats))
                    matCache[textures] = mats = GltfMaterials(PlanetModelDir + "/" + textures, textures);
                RemapMaterials(imp, mats);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mf = src != null ? src.GetComponentInChildren<MeshFilter>() : null;
                var mr = src != null ? src.GetComponentInChildren<MeshRenderer>() : null;
                if (mf == null || mr == null) continue;
                props.Add(new PlanetPropModel { Id = id, Mesh = mf.sharedMesh, Materials = mr.sharedMaterials });
            }
            return props.ToArray();
        }
    }
}
