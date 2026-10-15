using UnityEditor;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Sky + VFX art (#96): the NASA SVS star map as the skybox's star layer (cubemap) and the textured particle
    /// materials for VfxManager (Unity Labs Paris flipbooks, Kenney particle sprites; both CC0). Sources are converted by
    /// Tools/Blender/space_textures.py into Art/Sky and Art/VFX; importer settings and materials are generated here.
    /// Without the textures the skybox keeps its procedural stars and the effects their untextured look.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string SkyDir = ArtDir + "/Sky";
        const string VfxDir = ArtDir + "/VFX";

        /// <summary>Star map cubemap (Cylindrical import of the 8K plate carree map, 2048 faces) into the skybox material.</summary>
        static void ApplyStarMap(Material sky)
        {
            string path = SkyDir + "/Starmap_8k.jpg";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null)
            {
                sky.DisableKeyword("_STARMAP");
                sky.SetTexture("_StarMap", null);
                return;
            }
            if (imp.textureShape != TextureImporterShape.TextureCube || imp.maxTextureSize != 2048 || !imp.sRGBTexture
                || imp.textureCompression != TextureImporterCompression.CompressedHQ || !imp.mipmapEnabled)
            {
                imp.textureType = TextureImporterType.Default;
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                var s = new TextureImporterSettings();
                imp.ReadTextureSettings(s);
                s.cubemapConvolution = TextureImporterCubemapConvolution.None;
                s.seamlessCubemap = true;
                imp.SetTextureSettings(s);
                imp.sRGBTexture = true;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = 2048; // cube face size: 4 x 2048 = the 8K map's own density
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.filterMode = FilterMode.Trilinear;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            // right after the first import as a cube the typed load can still miss it
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null) cube = AssetDatabase.LoadMainAssetAtPath(path) as Cubemap;
            sky.SetTexture("_StarMap", cube);
            if (cube != null) sky.EnableKeyword("_STARMAP"); else sky.DisableKeyword("_STARMAP");
            sky.SetFloat("_StarMapStrength", 1.2f);
            sky.SetFloat("_StarMapBoost", 10f);
        }

        static Texture2D VfxTexture(string file, int maxSize = 1024)
        {
            string path = VfxDir + "/" + file;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return null;
            if (!imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp || imp.maxTextureSize != maxSize || !imp.sRGBTexture)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = maxSize;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Flipbook/sprite particle material; null when the texture is missing (VfxManager keeps its fallback).</summary>
        static Material VfxMat(string name, string file, bool additive, float heat = 0f, float soft = 0.6f, int maxSize = 1024)
        {
            var tex = VfxTexture(file, maxSize);
            if (tex == null) return null;
            var m = Mat(name, "VOIDWARD/FlipbookParticle");
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Additive", additive ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_Heat", heat);
            m.SetFloat("_SoftFade", soft);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Unit quad for VW_GlowSprite with generous bounds (the shader turns it to the camera).</summary>
        static Mesh GlowQuadMesh()
        {
            string path = MeshDir + "/GlowQuad.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = new Mesh { name = "GlowQuad" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.Clear();
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Camera-facing glow sprite (VW_GlowSprite) with a Kenney texture; null when the texture is missing.</summary>
        static Material GlowMat(string name, string file, Color tint, float twinkle = 0.15f)
        {
            var tex = VfxTexture(file, 256);
            if (tex == null) return null;
            var m = Mat(name, "VOIDWARD/GlowSprite");
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Twinkle", twinkle);
            return m;
        }

        /// <summary>Projectile materials (#96) into the catalog: head glow/flare, energy trail, missile smoke trail.</summary>
        static void AssignProjectileMaterials(ContentCatalog cat)
        {
            cat.FxGlowMaterial = GlowMat("VW_Fx_HeadGlow", "K_Dot.png", Color.white);
            cat.FxFlareMaterial = GlowMat("VW_Fx_HeadFlare", "K_Glint.png", Color.white, 0.3f);
            cat.FxBeamTrailMaterial = VfxMat("VW_Fx_BeamTrail", "K_Dot.png", true, 0f, 0.2f, 256);
            cat.FxSmokeTrailMaterial = VfxMat("VW_Fx_SmokeTrail", "K_Dot.png", false, 0f, 0.6f, 256);
            cat.FxStreakMaterial = VfxMat("VW_Fx_Streak", "K_Spark.png", true, 0f, 0f, 256);
            if (cat.FxStreakMaterial != null) cat.FxStreakMaterial.SetFloat("_NearFade", 4f);
        }

        /// <summary>
        /// Journey sky images (#96): destination galaxy per GalaxyDef (index 1..4 -> M31, NGC 6822, Leo I, LMC) and the
        /// Milky Way, converted by Tools/Blender/galaxy_textures.py; additive, unfogged material.
        /// </summary>
        static void AssignSkyImages(ContentCatalog cat, GalaxyDef[] galaxies)
        {
            Texture2D Img(string name)
            {
                string path = SkyDir + "/Galaxy_" + name + ".png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) return null;
                int max = name == "MilkyWay" ? 2048 : 1024;
                if (imp.wrapMode != TextureWrapMode.Clamp || imp.maxTextureSize != max || imp.textureCompression != TextureImporterCompression.CompressedHQ)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.sRGBTexture = true;
                    imp.alphaSource = TextureImporterAlphaSource.None;
                    imp.mipmapEnabled = true;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.maxTextureSize = max;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var next = new[] { "M31", "NGC6822", "LeoI", "LMC", null };
            for (int i = 0; i < galaxies.Length && i < next.Length; i++)
            {
                galaxies[i].NextGalaxyImage = next[i] != null ? Img(next[i]) : null;
                EditorUtility.SetDirty(galaxies[i]);
            }
            cat.MilkyWayImage = Img("MilkyWay");
            var m = Mat("VW_SkyImage", "VOIDWARD/FlipbookParticle");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Additive", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_Heat", 0f);
            m.SetFloat("_SoftFade", 0f);
            m.SetFloat("_NearFade", 0f);
            m.SetFloat("_FogAmount", 0f);
            cat.SkyImageMaterial = m;
        }

        /// <summary>Textured layers of the pooled effects (#96).</summary>
        static void AssignVfxMaterials(VfxManager vfx)
        {
            vfx.FireballMaterial = VfxMat("VW_Fx_Fireball", "FB_Explosion02_5x5.png", false, 3.5f, 1.2f);
            vfx.FireballBMaterial = VfxMat("VW_Fx_FireballB", "FB_Explosion01_5x5.png", false, 3f, 1.2f);
            vfx.SmokeSheetMaterial = VfxMat("VW_Fx_Smoke", "FB_WispySmoke01_8x8.png", false, 0f, 1.5f);
            vfx.PuffMaterial = VfxMat("VW_Fx_Puff", "K_SmokeSheet.png", false, 0f, 0.8f);
            vfx.FlashMaterial = VfxMat("VW_Fx_Flash", "K_Flash.png", true, 0f, 0.3f, 256);
            vfx.GlintMaterial = VfxMat("VW_Fx_Glint", "K_Glint.png", true, 0f, 0.2f, 256);
            vfx.RingMaterial = VfxMat("VW_Fx_Ring", "K_Ring.png", true, 0f, 0.5f, 256);
            vfx.RippleMaterial = VfxMat("VW_Fx_Ripple", "K_Ripple.png", true, 0f, 0.3f, 256);
            vfx.SparkMaterial = VfxMat("VW_Fx_Spark", "K_Spark.png", true, 0f, 0.1f, 256);
            vfx.MuzzleMaterial = VfxMat("VW_Fx_Muzzle", "K_MuzzleStrip.png", true, 0f, 0.1f);
            // the hard Kenney rings read as a thin shock front, not a solid disc: keep them faint
            if (vfx.RingMaterial != null) vfx.RingMaterial.SetColor("_BaseColor", new Color(0.18f, 0.18f, 0.18f, 1f));
            if (vfx.RippleMaterial != null) vfx.RippleMaterial.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f, 1f));
        }
    }
}
