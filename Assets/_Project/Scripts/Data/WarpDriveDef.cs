using UnityEngine;

namespace Voidward.Data
{
    /// <summary>
    /// Balance of the crew's warp drive (#78, gameplan #72). Values come from ContentBuilder.BuildWarpDrive.
    /// Travel time of a leg = GalaxyDef.LegLength (minutes with a balanced Mk I drive) x build factor / core speed.
    /// A balanced build (navigation and cooling at the core's tier) has factor 1; weaker parts slow it down: the
    /// field share of every flight can only be flown as fast as the nav computer sees ahead, the free-space share
    /// only at the throttle the cooling can sustain.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Warp Drive", fileName = "WarpDrive")]
    public class WarpDriveDef : ScriptableObject
    {
        [Tooltip("Core speed per tier relative to Mk I (index 0 = Mk I ... 5 = Home Drive).")]
        public float[] CoreSpeed = { 1f, 3f, 10f, 25f, 60f, 240f };
        [Tooltip("Exotic Matter capacity per tank tier.")]
        public int[] TankCapacity = { 12, 20, 30, 42, 56, 75 };

        [Header("Build factor")]
        [Tooltip("Share of a flight spent in fields (halos, debris, anomalies).")]
        [Range(0f, 1f)] public float FieldShare = 0.3f;
        [Tooltip("Fraction of the core speed that is safe in fields with a nav computer of the core's tier.")]
        public float NavMatched = 0.6f;
        [Tooltip("Change of that fraction per tier the nav computer is above/below the core.")]
        public float NavStep = 0.12f;
        public float NavMin = 0.12f;
        [Tooltip("Sustainable throttle change per tier the cooling is above/below the core (1 = full throttle).")]
        public float CoolingStep = 0.15f;
        public float CoolingMin = 0.35f;

        [Header("Fuel")]
        [Tooltip("Extra Exotic Matter per additional leg when skipping galaxies (0.25 = +25 % per skipped galaxy).")]
        public float SkipFuelSurcharge = 0.25f;
        [Tooltip("Exotic Matter in the tank at the start of a loop.")]
        public int StartExotic = 4;

        /// <summary>Build factor of a balanced drive (nav and cooling at the core's tier).</summary>
        public float BalancedFactor => FieldShare / Mathf.Max(0.01f, NavMatched) + (1f - FieldShare);
    }
}
