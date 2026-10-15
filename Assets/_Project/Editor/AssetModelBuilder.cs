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
        static Texture2D PackMetallicSmoothness(string metallicPath, string roughnessPath, string outPath) =>
            Generated(outPath, false, new[] { metallicPath, roughnessPath }, (px, i) =>
            {
                byte m = px[0][i].r;
                return new Color32(m, m, m, (byte)(255 - px[1][i].r));
            });

        /// <summary>glTF metallicRoughness (G = roughness, B = metal) -> URP metallic (R) + smoothness (A).</summary>
        static Texture2D PackGltfMetallicRoughness(string path, string outPath) =>
            Generated(outPath, false, new[] { path }, (px, i) =>
            {
                var c = px[0][i];
                return new Color32(c.b, c.b, c.b, (byte)(255 - c.g));
            });

        /// <summary>
        /// A texture computed per pixel from source images (resized to the size of the first), written as PNG next to them
        /// and only regenerated when a source is newer. `pixel(sources, index)` returns the output pixel.
        /// </summary>
        static Texture2D Generated(string outPath, bool srgb, string[] sources, System.Func<Color32[][], int, Color32> pixel, int maxSize = 4096)
        {
            string full = Path.GetFullPath(outPath);
            bool stale = !File.Exists(full);
            foreach (var src in sources)
                stale = stale || File.GetLastWriteTimeUtc(full) < File.GetLastWriteTimeUtc(Path.GetFullPath(src));
            if (stale)
            {
                var px = new Color32[sources.Length][];
                int w = 0, h = 0;
                for (int k = 0; k < sources.Length; k++)
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    t.LoadImage(File.ReadAllBytes(sources[k]));
                    if (k == 0) { w = t.width; h = t.height; }
                    else if (t.width != w || t.height != h) t = Resized(t, w, h);
                    px[k] = t.GetPixels32();
                    Object.DestroyImmediate(t);
                }
                var o = new Color32[px[0].Length];
                for (int i = 0; i < o.Length; i++) o[i] = pixel(px, i);
                var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                outTex.SetPixels32(o);
                File.WriteAllBytes(full, outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                AssetDatabase.ImportAsset(outPath);
            }
            return ImportTexture(outPath, srgb, maxSize: maxSize);
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

        /// <summary>
        /// URP Lit material from a Substance-style PBR texture set (base colour, metallic, roughness, normal, AO, emissive).
        /// The normal map is `prefix + normal`; DirectX normals (green down) are flipped by the importer.
        /// </summary>
        static Material PbrMaterial(string name, string dir, string prefix, float emission = 1.5f, float bump = 1f,
            string normal = "_Normal_DirectX.jpg", bool directXNormal = true)
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
            m.SetTexture("_BumpMap", ImportTexture(dir + "/" + prefix + normal, false, true, directXNormal));
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

        // ---------------------------------------------------------------- Mule (#97)

        const string MuleDir = ShipModelDir + "/DropshipR35";

        /// <summary>Fab "DROPSHIP R35" by GrafxBOX (Tools/Blender/r35_dropship.py): 15 m long, nose +Z, origin = bounds centre, canopy cut out.</summary>
        static Mesh MuleR35Mesh() => ModelMesh(ShipModelDir + "/Ship_Mule_R35.fbx");

        /// <summary>The upper nose cut out of the R35 hull as the Mule's canopy (original UVs).</summary>
        static Mesh MuleR35CanopyMesh() => ModelMesh(ShipModelDir + "/Ship_Mule_R35_Canopy.fbx");

        /// <summary>Cockpit tub inside the R35 nose, open under the canopy (vw_blender vertex contract, VW_Hull).</summary>
        static Mesh MuleR35CockpitMesh() => ImportedMesh(ShipModelDir + "/Ship_Mule_R35_Cockpit.fbx", "Ship_Mule_R35_Cockpit");

        /// <summary>R35 "Grey" livery (black / gunmetal plating, orange spine and pods): URP Lit, OpenGL normal map.</summary>
        static Material MuleR35Material() => PbrMaterial("Ship_Mule_R35", MuleDir, "R35", 1.6f, 1f, "_Normal.jpg", false);

        // ---------------------------------------------------------------- Ark (#97)

        const string ArkDir = ShipModelDir + "/CargoShip";

        /// <summary>Sketchfab "SPACESHIP - CARGO" by Kerem Kavalci (Tools/Blender/cargo_ark.py): 36 m, nose +Z, window and belly hatch cut out.</summary>
        static Mesh ArkCargoMesh() => ModelMesh(ShipModelDir + "/Ship_Ark_Cargo.fbx");

        /// <summary>The bridge window cut out of the cab front (original UVs).</summary>
        static Mesh ArkCargoGlassMesh() => ModelMesh(ShipModelDir + "/Ship_Ark_Cargo_Glass.fbx");

        /// <summary>
        /// Gunmetal hull with tan trim (URP Lit, DirectX normals). The source glow is violet: its brightness becomes a mask,
        /// tinted cyan by the emission colour (Dart palette: cyan/orange emissives).
        /// </summary>
        static Material ArkCargoMaterial()
        {
            var m = PbrMaterial("Ship_Ark_Cargo", ArkDir, "CargoShip", 1f);
            m.SetTexture("_EmissionMap", Generated(ArkDir + "/CargoShip_Emissive_vw.png", true, new[] { ArkDir + "/CargoShip_Emissive.jpg" }, (px, i) =>
            {
                var e = px[0][i];
                byte l = (byte)Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                return new Color32(l, l, l, 255);
            }, 2048));
            m.SetColor("_EmissionColor", new Color(0.3f, 0.85f, 1f) * 2.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// The Derelict Ark (#97): the same hull, dark and scorched (burn patches from noise over the albedo), rougher,
        /// no emission. ShipDerelictLook swaps it in while ShipCore.Derelict is set.
        /// </summary>
        static Material ArkDerelictMaterial()
        {
            var m = PbrMaterial("Ship_Ark_Cargo_Derelict", ArkDir, "CargoShip", 0f);
            m.SetTexture("_BaseMap", Generated(ArkDir + "/CargoShip_Base_color_derelict.png", true, new[] { ArkDir + "/CargoShip_Base_color.jpg" }, (px, i) =>
            {
                int w = Mathf.RoundToInt(Mathf.Sqrt(px[0].Length));
                float u = (i % w) / (float)w, v = (i / w) / (float)w;
                // scorch: low-frequency patches with a ragged edge; soot everywhere a little
                float n = Mathf.PerlinNoise(u * 7f + 3.1f, v * 7f + 1.7f) * 0.7f + Mathf.PerlinNoise(u * 31f, v * 31f) * 0.3f;
                float burn = Mathf.SmoothStep(0f, 1f, (n - 0.52f) * 5f);
                Color c = px[0][i];
                c *= Mathf.Lerp(0.55f, 0.12f, burn);
                c = Color.Lerp(c, new Color(0.05f, 0.035f, 0.025f), burn * 0.6f);
                c.a = 1f;
                return (Color32)c;
            }, 2048));
            m.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
            m.SetFloat("_Smoothness", 0.55f);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------- Dart cockpit (#94)

        const string CockpitDir = ShipModelDir + "/DartCockpit";

        /// <summary>Cockpit fittings from Tools/Blender/dart_cockpit.py: Console, Walls, Stick, Throttle, Seat. Null if missing.</summary>
        static Mesh DartCockpitMesh(string part) => ModelMesh(ShipModelDir + "/Ship_Dart_Cockpit_" + part + ".fbx");

        /// <summary>
        /// Wasp cockpit texture set ("center" / "cockpit") as URP Lit, recoloured to the Dart: the olive/cream panels become
        /// slate gunmetal, the red trim the orange accent; the faint green screen glow becomes a cyan emission mask.
        /// </summary>
        static Material WaspMaterial(string part)
        {
            string p = CockpitDir + "/Wasp_" + part + "_";
            var m = Mat("Ship_Dart_Cockpit_" + part, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", Generated(p + "albedo_vw.png", true, new[] { p + "albedo.jpg" }, (px, i) => Recolor(px[0][i]), 2048));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", PackMetallicSmoothness(p + "metallic.jpg", p + "roughness.jpg", p + "metallic_smoothness.png"));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", ImportTexture(p + "normal.jpg", false, true, false, 2048));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_OcclusionMap", ImportTexture(p + "AO.jpg", false, maxSize: 2048));
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            // the source glow is a faint green: brightness x 3 as a mask, tinted cyan by the emission colour
            m.SetTexture("_EmissionMap", Generated(p + "emissive_vw.png", true, new[] { p + "emissive.jpg" }, (px, i) =>
            {
                var e = px[0][i];
                byte l = (byte)Mathf.Min(255, Mathf.Max(e.r, Mathf.Max(e.g, e.b)) * 3);
                return new Color32(l, l, l, 255);
            }, 2048));
            m.SetColor("_EmissionColor", new Color(0.3f, 0.85f, 1f) * 1.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Dart palette for the Wasp textures: warm, lightly saturated panel colours -> slate; saturated reds -> orange.</summary>
        static Color32 Recolor(Color32 c32)
        {
            Color c = c32;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s > 0.35f && (h < 0.03f || h > 0.95f) && v > 0.12f)
                return (Color32)Color.HSVToRGB(0.065f, Mathf.Min(1f, s * 1.05f), Mathf.Min(1f, v * 1.35f));
            // olive / cream / tan: hue around 0.12, weak to mid saturation
            float hue = Mathf.Clamp01(1f - Mathf.Abs(h - 0.12f) / 0.09f);
            float w = Mathf.SmoothStep(0f, 1f, hue) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.18f, s));
            var slate = new Color(0.36f, 0.41f, 0.48f) * Mathf.Lerp(0.55f, 1.05f, v);
            var outc = Color.Lerp(c, slate, w);
            outc.a = 1f;
            return outc;
        }

        /// <summary>Sci-Fi Pilot Chair (glTF set: base colour, metallicRoughness, OpenGL normal).</summary>
        static Material ChairMaterial()
        {
            var m = Mat("Ship_Dart_Cockpit_Seat", "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", ImportTexture(CockpitDir + "/Chair_baseColor.jpg", true, maxSize: 2048));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", PackGltfMetallicRoughness(CockpitDir + "/Chair_metallicRoughness.png", CockpitDir + "/Chair_metallic_smoothness.png"));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", ImportTexture(CockpitDir + "/Chair_normal.jpg", false, true, false, 2048));
            m.EnableKeyword("_NORMALMAP");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Near-black gunmetal for the dashboard's panel seams (they read as grooves between the plates).</summary>
        static Material SeamMaterial()
        {
            var m = Mat("Ship_Dart_Cockpit_Seams", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", new Color(0.03f, 0.035f, 0.045f));
            m.SetFloat("_Metallic", 0.4f);
            m.SetFloat("_Smoothness", 0.25f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// The canopy glass seen from inside (#94): same mesh, front faces culled, nearly clear so the view stays open;
        /// a faint cool rim towards grazing angles shows the panes. (A sun glint was tried: the flat panes light up whole.)
        /// </summary>
        static Material FabShipGlassInnerMaterial()
        {
            var m = Mat("Ship_Dart_Fab_GlassInner", "VOIDWARD/Glass");
            m.SetColor("_BaseColor", new Color(0.2f, 0.32f, 0.42f, 0.04f));
            m.SetColor("_RimColor", new Color(0.45f, 0.65f, 0.85f, 1f));
            m.SetFloat("_RimPower", 5f);
            m.SetFloat("_RimAlpha", 0.08f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            return m;
        }

        /// <summary>The interior material of a dead ship (#97, Derelict Ark): light strips and panels almost dark, dimmer surfaces.</summary>
        static Material InteriorDerelictMaterial()
        {
            var m = Mat("VW_HullInterior_Derelict", "VOIDWARD/ToonFlat");
            var src = InteriorMaterial();
            m.CopyPropertiesFromMaterial(src);
            m.shaderKeywords = src.shaderKeywords;
            m.SetFloat("_GlowIntensity", 0.15f);
            m.enableInstancing = true;
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
