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

        [Header("Color grading (runtime sector volume)")]
        public Color GradeFilter = Color.white;
        [Range(-50f, 50f)] public float GradeSaturation = 10f;
        [Range(-50f, 50f)] public float GradeContrast = 18f;
        [Range(-2f, 2f)] public float GradeExposure = 0.1f;
        [Range(-50f, 50f)] public float GradeTemperature = -4f;
        public Color BloomTint = new Color(0.85f, 0.93f, 1f);

        [Header("UI accents")]
        public Color UiPrimary = new Color(0.2f, 0.85f, 0.9f);
        public Color UiAccent = new Color(1f, 0.65f, 0.3f);

        /// <summary>Writes ambient light and fog to RenderSettings (used by the scene builder and the runtime sector dressing).</summary>
        public void ApplyEnvironment()
        {
            RenderSettings.fog = FogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientColor;
            RenderSettings.ambientEquatorColor = Color.Lerp(AmbientColor, NebulaA, 0.25f) * 0.7f;
            RenderSettings.ambientGroundColor = AmbientColor * 0.35f;
        }
    }
}
