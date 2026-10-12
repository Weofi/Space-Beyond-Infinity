using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// One sector of the route: corridor dimensions, biome pool, enemy mix, palette and
    /// threat speed. Difficulty/rewards scale with sector index.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Sector", fileName = "Sector_")]
    public class SectorDef : ScriptableObject
    {
        public int Index = 1;                    // 1-based
        public string DisplayName = "Verdant Drift";
        [TextArea] public string Intro = "";

        [Header("Layout")]
        public float CorridorLength = 6000f;     // meters along +X
        public int MinPlanets = 3;
        public int MaxPlanets = 5;
        public int AsteroidBelts = 3;
        public int WreckCount = 2;
        public bool HasStation = true;

        [Header("Content")]
        public BiomeDef[] BiomePool;
        public EnemyKind[] SpaceEnemies = { EnemyKind.PirateFighter };
        [Tooltip("Max simultaneously alive space enemies in this sector.")]
        public int SpaceEnemyCap = 6;
        public EnemyKind[] SurfaceEnemies = { EnemyKind.Crawler };
        public float EnemyTier = 1f;             // stat multiplier

        [Header("Threat")]
        [Tooltip("Meters per second the Collapse advances.")]
        public float CollapseSpeed = 6f;
        [Tooltip("Starting distance of the Collapse behind the corridor start.")]
        public float CollapseStartOffset = 2500f;

        [Header("Events")]
        public int EventCount = 2;

        [Header("Visuals")]
        public PaletteDef Palette;

        [Header("Jump Gate")]
        [Tooltip("Energy cells required to activate this sector's gate.")]
        public int GateCellCost = 2;
        public bool IsBossSector;
    }
}
