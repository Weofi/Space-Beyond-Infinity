using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Voidward.UI;

namespace Voidward.EditorTools
{
    /// <summary>Bakes the static TMP SDF font assets used by the UI (Rajdhani + Material Icons) into Resources/UI.</summary>
    public static partial class ContentBuilder
    {
        const string FontDir = ArtDir + "/Fonts";
        const string UIResDir = ResourcesDir + "/UI";

        [MenuItem("Voidward/Build UI Fonts")]
        static void BuildFonts()
        {
            if (!AssetDatabase.IsValidFolder(UIResDir)) AssetDatabase.CreateFolder(ResourcesDir, "UI");

            var ascii = new System.Text.StringBuilder();
            for (char c = (char)32; c < 127; c++) ascii.Append(c);

            var bold = BakeFont(FontDir + "/Rajdhani-Bold.ttf", UIResDir + "/VW_Font_Bold.asset", ascii.ToString(), 90, 10, 1024);
            var regular = BakeFont(FontDir + "/Rajdhani-SemiBold.ttf", UIResDir + "/VW_Font.asset", ascii.ToString(), 90, 10, 1024);
            var icons = BakeFont(FontDir + "/MaterialIcons-Regular.ttf", UIResDir + "/VW_Icons.asset", Icons.All, 96, 10, 1024);
            if (bold == null || regular == null || icons == null) return;

            // Real bold face for FontStyles.Bold / <b>, and a fallback for non-ASCII player names.
            regular.fontWeightTable[7].regularTypeface = bold;
            regular.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            bold.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            EditorUtility.SetDirty(regular);
            EditorUtility.SetDirty(bold);
            AssetDatabase.SaveAssets();
        }

        static TMP_FontAsset BakeFont(string src, string dst, string chars, int pointSize, int padding, int atlas)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(src);
            if (font == null) { Debug.LogError("[Voidward] Font not found: " + src); return null; }
            AssetDatabase.DeleteAsset(dst);
            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, false);
            fa.name = System.IO.Path.GetFileNameWithoutExtension(dst);
            if (!fa.TryAddCharacters(chars, out string missing)) Debug.LogWarning($"[Voidward] {fa.name}: missing glyphs '{missing}'");
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(fa, dst);
            var tex = fa.atlasTextures[0];
            tex.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(tex, fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            return fa;
        }
    }
}
