using UnityEngine;

namespace Voidward.Data
{
    /// <summary>
    /// Shared color palette: drives skybox, fog, lighting and UI accents per sector,
    /// keeping the whole sector visually cohesive.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Palette", fileName = "Palette_")]
    public class PaletteDef : ScriptableObject
    {
        public string DisplayName = "Palette";

        [Header("Sky / space")]
        public Color SkyZenith = new Color(0.05f, 0.07f, 0.18f);
        public Color SkyHorizon = new Color(0.25f, 0.16f, 0.38f);
        public Color NebulaA = new Color(0.9f, 0.4f, 0.7f);
        public Color NebulaB = new Color(0.3f, 0.5f, 0.95f);
        public Color StarColor = new Color(1f, 0.95f, 0.9f);

        [Header("Lighting / fog")]
        public Color SunColor = new Color(1f, 0.9f, 0.8f);
        public Color AmbientColor = new Color(0.35f, 0.4f, 0.55f);
        public Color FogColor = new Color(0.12f, 0.1f, 0.22f);
        [Tooltip("Exp2 fog density in space.")]
        public float FogDensity = 0.00035f;

        [Header("UI accents")]
        public Color UiPrimary = new Color(0.2f, 0.85f, 0.9f);
        public Color UiAccent = new Color(1f, 0.65f, 0.3f);
    }
}
