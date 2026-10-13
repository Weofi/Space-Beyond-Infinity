using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// One galaxy of the journey home (#46, gameplan #72): fixed route position, distance to Sol, biome pool,
    /// enemy mix, difficulty/loot tier and palette. Its 4-6 star systems are generated per loop from the seed
    /// (GalaxyGenerator); every system reuses the content and difficulty set here.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Galaxy", fileName = "Galaxy_")]
    public class GalaxyDef : ScriptableObject
    {
        public const int Count = 5;

        public int Index = 1;                    // 1-based route position (1 = M33 ... 5 = LMC)
        public string DisplayName = "Triangulum";
        public string Designation = "M33";
        [Tooltip("Distance to Sol in million light-years.")]
        public float DistanceMly = 2.73f;
        [TextArea] public string Intro = "";
        [Tooltip("Best warp drive tier found here (2 = Mk II ... 6 = Home Drive); used by #73.")]
        public int DriveTier = 2;

        [Header("Systems")]
        public int MinSystems = 4;
        public int MaxSystems = 6;
        [Tooltip("Length of a system's flight corridor in meters along +X.")]
        public float SystemLength = 8000f;

        [Header("Content")]
        public BiomeDef[] BiomePool;
        public EnemyKind[] SpaceEnemies = { EnemyKind.PirateFighter };
        [Tooltip("Max simultaneously alive space enemies in a system.")]
        public int SpaceEnemyCap = 6;
        [Tooltip("Seconds after entering a system before space enemies spawn.")]
        public float SpaceEnemyGrace = 60f;
        [Tooltip("Seconds after the grace period over which the space enemy cap ramps from 1 to SpaceEnemyCap.")]
        public float SpaceEnemyRamp = 120f;
        public EnemyKind[] SurfaceEnemies = { EnemyKind.Crawler };
        [Tooltip("Enemy stat multiplier.")]
        public float EnemyTier = 1f;
        [Tooltip("Loot tier of crates and drops (1..3).")]
        public int LootTier = 1;
        [Tooltip("Random events per system.")]
        public int EventCount = 2;

        [Header("Visuals")]
        public PaletteDef Palette;
        [Tooltip("Tint of the destination galaxy on the sky (the next galaxy, or the Milky Way in the last one).")]
        public Color NextGalaxyTint = new Color(0.8f, 0.85f, 1f);
    }
}
