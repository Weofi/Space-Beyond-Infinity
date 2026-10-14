using System.IO;
using UnityEditor;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Ready-made art assets (#91): textured models (Fab, CC0 sources) rendered with URP Lit PBR materials instead of the
    /// procedural MeshBuilder layout. Sources stay in the repo (models normalized by a Tools/Blender script, textures next
    /// to them); importer settings, packed maps and materials are generated here, so Build All Content stays reproducible.
    /// </summary>
    public static partial class ContentBuilder
    {
        public const string ShipModelDir = ModelDir + "/Ships";
        const string FabShipDir = ShipModelDir + "/FabSpaceShip";

        /// <summary>The model's first mesh with its own UVs/normals (MikkTSpace tangents for normal maps). Null if missing.</summary>
        static Mesh ModelMesh(string fbxPath)
        {
            var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null) return null;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || imp.importAnimation || imp.importCameras || imp.importLights
                || imp.importNormals != ModelImporterNormals.Import || imp.importTangents != ModelImporterTangents.CalculateMikk || imp.isReadable)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.isReadable = false;
                imp.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is Mesh m) return m;
            return null;
        }

        /// <summary>Texture with the importer set for its role (colour = sRGB, data = linear, normal map optionally green-flipped).</summary>
        static Texture2D ImportTexture(string path, bool srgb, bool normal = false, bool flipGreen = false, int maxSize = 4096)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogError("[Voidward] Texture missing: " + path); return null; }
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != type || imp.sRGBTexture != srgb || imp.flipGreenChannel != flipGreen || imp.maxTextureSize != maxSize || !imp.mipmapEnabled)
            {
                imp.textureType = type;
                imp.sRGBTexture = srgb;
                imp.flipGreenChannel = flipGreen;
                imp.maxTextureSize = maxSize;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 4;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// URP Lit wants metallic in R and smoothness in A of one map; Substance exports metallic + roughness separately.
        /// Writes `outPath` (PNG) from the two source images unless it is newer than both.
        /// </summary>
        static Texture2D PackMetallicSmoothness(string metallicPath, string roughnessPath, string outPath)
        {
            string full = Path.GetFullPath(outPath);
            if (!File.Exists(full) || File.GetLastWriteTimeUtc(full) < File.GetLastWriteTimeUtc(Path.GetFullPath(metallicPath))
                || File.GetLastWriteTimeUtc(full) < File.GetLastWriteTimeUtc(Path.GetFullPath(roughnessPath)))
            {
                var met = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                met.LoadImage(File.ReadAllBytes(metallicPath));
                rough.LoadImage(File.ReadAllBytes(roughnessPath));
                if (rough.width != met.width || rough.height != met.height) rough = Resized(rough, met.width, met.height);
                var a = met.GetPixels32();
                var r = rough.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    byte m = a[i].r;
                    a[i] = new Color32(m, m, m, (byte)(255 - r[i].r));
                }
                var packed = new Texture2D(met.width, met.height, TextureFormat.RGBA32, false, true);
                packed.SetPixels32(a);
                File.WriteAllBytes(full, packed.EncodeToPNG());
                Object.DestroyImmediate(met); Object.DestroyImmediate(rough); Object.DestroyImmediate(packed);
                AssetDatabase.ImportAsset(outPath);
            }
            return ImportTexture(outPath, false);
        }

        static Texture2D Resized(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(src);
            return dst;
        }

        /// <summary>URP Lit material from a Substance-style PBR texture set (base colour, metallic, roughness, DirectX normal, AO, emissive).</summary>
        static Material PbrMaterial(string name, string dir, string prefix, float emission = 1.5f, float bump = 1f)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", ImportTexture(dir + "/" + prefix + "_Base_color.jpg", true));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", PackMetallicSmoothness(dir + "/" + prefix + "_Metallic.jpg", dir + "/" + prefix + "_Roughness.jpg",
                dir + "/" + prefix + "_MetallicSmoothness.png"));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", ImportTexture(dir + "/" + prefix + "_Normal_DirectX.jpg", false, true, true));
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_OcclusionMap", ImportTexture(dir + "/" + prefix + "_Mixed_AO.jpg", false, maxSize: 2048));
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetTexture("_EmissionMap", ImportTexture(dir + "/" + prefix + "_Emissive.jpg", true, maxSize: 2048));
            m.SetColor("_EmissionColor", Color.white * emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Fab "SpaceShip" by JazOone3D (Tools/Blender/fab_spaceship.py): 15 m long, nose +Z, origin = bounds centre, canopy cut out.</summary>
        static Mesh FabShipMesh() => ModelMesh(ShipModelDir + "/Ship_Dart_Fab.fbx");

        /// <summary>The canopy panes cut out of the hull (original UVs), rendered as glass.</summary>
        static Mesh FabShipCanopyMesh() => ModelMesh(ShipModelDir + "/Ship_Dart_Fab_Canopy.fbx");

        /// <summary>Cockpit tub that follows the hull's inside between the two bulkheads (vw_blender vertex contract, VW_Hull).</summary>
        static Mesh FabShipCockpitMesh() => ImportedMesh(ShipModelDir + "/Ship_Dart_Fab_Cockpit.fbx", "Ship_Dart_Fab_Cockpit");

        static Material FabShipMaterial() => PbrMaterial("Ship_Dart_Fab", FabShipDir, "FabShip");

        /// <summary>Smoked canopy glass: dark blue tint, faint so the cockpit shows through, soft rim.</summary>
        static Material FabShipGlassMaterial()
        {
            var m = Mat("Ship_Dart_Fab_Glass", "VOIDWARD/Glass");
            m.SetColor("_BaseColor", new Color(0.16f, 0.27f, 0.38f, 0.32f));
            m.SetColor("_RimColor", new Color(0.55f, 0.75f, 0.95f, 1f));
            m.SetFloat("_RimPower", 3.5f);
            return m;
        }

        /// <summary>VW_Hull for interiors: same panel detail, no blue rim (it reads as glow inside a cockpit), softer spec.</summary>
        static Material InteriorMaterial()
        {
            var m = Mat("VW_HullInterior", "VOIDWARD/ToonFlat");
            m.CopyPropertiesFromMaterial(MatHull);
            m.shaderKeywords = MatHull.shaderKeywords;
            m.SetFloat("_RimStrength", 0f);
            m.SetFloat("_SpecStrength", 0.6f);
            m.SetFloat("_ReflStrength", 0.5f);
            m.enableInstancing = true;
            return m;
        }
    }
}
