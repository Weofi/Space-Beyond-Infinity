using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// Central registry of all content definitions and network prefabs.
    /// A single instance lives in Resources/ContentCatalog.asset and is populated by the
    /// editor content builder (Voidward menu). Access via ContentCatalog.Instance.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Content Catalog", fileName = "ContentCatalog")]
    public class ContentCatalog : ScriptableObject
    {
        [Header("Definitions")]
        public ItemDef[] Items = new ItemDef[0];
        public ModuleDef[] Modules = new ModuleDef[0];
        public EnemyDef[] Enemies = new EnemyDef[0];
        public SectorDef[] Sectors = new SectorDef[0];
        public BiomeDef[] Biomes = new BiomeDef[0];
        public PaletteDef[] Palettes = new PaletteDef[0];
        public RecipeDef[] Recipes = new RecipeDef[0];
        public ShipDef[] Ships = new ShipDef[0];
        public LootTable[] LootTables = new LootTable[0];

        [Header("Network prefabs (built by Voidward > Build All Content)")]
        public GameObject PlayerPawn;
        public GameObject PlanetPrefab;
        public GameObject ShipDart;
        public GameObject ShipMule;
        public GameObject ShipArk;
        public GameObject EnemyPirateFighter;
        public GameObject EnemyDrone;
        public GameObject EnemyGunship;
        public GameObject EnemyCrawler;
        public GameObject EnemySpitter;
        public GameObject EnemyCritter;
        public GameObject EnemyGuardian;
        public GameObject BossMothership;
        public GameObject WorldPickup;
        public GameObject LootCrate;
        public GameObject ResourceNode;
        public GameObject NestPrefab;
        public GameObject RuinPrefab;
        public GameObject CrashedShipPrefab;
        public GameObject TraderStation;
        public GameObject JumpGate;
        public GameObject LaserBolt;
        public GameObject Missile;
        public GameObject FlakBurst;
        public GameObject SpitProjectile;
        public GameObject Meteor;
        public GameObject DerelictArk;
        public GameObject GameDirector;
        public GameObject Wreck;

        [Header("Shared rendering")]
        public Material ToonMaterial;
        public Material TerrainMaterial;
        public Material PlanetMaterial;
        public Material OceanMaterial;
        public Material SkyboxMaterial;
        public Material CollapseMaterial;
        public Material AtmosphereMaterial;
        public Material CloudMaterial;
        public Material UnlitMaterial;
        public Material ParticleMaterial;
        public Material GlassMaterial;

        static ContentCatalog instance;
        public static ContentCatalog Instance
        {
            get
            {
                if (instance == null)
                    instance = Resources.Load<ContentCatalog>("ContentCatalog");
                if (instance == null)
                    Debug.LogError("[ContentCatalog] Resources/ContentCatalog.asset missing — run 'Voidward > Build All Content'.");
                return instance;
            }
        }

        // ---------- Lookups ----------
        static Dictionary<string, ItemDef> itemById;
        static Dictionary<string, ModuleDef> moduleById;

        void OnEnable() => RebuildLookup();

        public void RebuildLookup()
        {
            itemById = new Dictionary<string, ItemDef>();
            foreach (var i in Items) if (i != null && !string.IsNullOrEmpty(i.Id)) itemById[i.Id] = i;
            moduleById = new Dictionary<string, ModuleDef>();
            foreach (var m in Modules) if (m != null && !string.IsNullOrEmpty(m.Id)) moduleById[m.Id] = m;
        }

        public static ItemDef GetItem(string id)
        {
            var c = Instance;
            if (c == null) return null;
            if (itemById == null) c.RebuildLookup();
            return itemById.TryGetValue(id, out var d) ? d : null;
        }

        public static ModuleDef GetModule(string id)
        {
            var c = Instance;
            if (c == null) return null;
            if (moduleById == null) c.RebuildLookup();
            return moduleById.TryGetValue(id, out var d) ? d : null;
        }

        public static EnemyDef GetEnemy(EnemyKind kind)
        {
            var c = Instance;
            if (c == null) return null;
            foreach (var e in c.Enemies) if (e != null && e.Kind == kind) return e;
            return null;
        }

        public static ShipDef GetShip(ShipClass shipClass)
        {
            var c = Instance;
            if (c == null) return null;
            foreach (var s in c.Ships) if (s != null && s.Class == shipClass) return s;
            return null;
        }

        public static SectorDef GetSector(int index1Based)
        {
            var c = Instance;
            if (c == null) return null;
            foreach (var s in c.Sectors) if (s != null && s.Index == index1Based) return s;
            return null;
        }

        public static GameObject GetShipPrefab(ShipClass shipClass)
        {
            var c = Instance;
            if (c == null) return null;
            switch (shipClass)
            {
                case ShipClass.Dart: return c.ShipDart;
                case ShipClass.Mule: return c.ShipMule;
                case ShipClass.Ark: return c.ShipArk;
                default: return null;
            }
        }

        public static GameObject GetEnemyPrefab(EnemyKind kind)
        {
            var c = Instance;
            if (c == null) return null;
            switch (kind)
            {
                case EnemyKind.PirateFighter: return c.EnemyPirateFighter;
                case EnemyKind.DroneSwarm: return c.EnemyDrone;
                case EnemyKind.PirateGunship: return c.EnemyGunship;
                case EnemyKind.Crawler: return c.EnemyCrawler;
                case EnemyKind.Spitter: return c.EnemySpitter;
                case EnemyKind.FlyingCritter: return c.EnemyCritter;
                case EnemyKind.NestGuardian: return c.EnemyGuardian;
                case EnemyKind.BossMothership: return c.BossMothership;
                default: return null;
            }
        }

        /// <summary>Prefab used for a given scatter kind on planet surfaces.</summary>
        public static GameObject GetScatterPrefab(ScatterKind kind)
        {
            var c = Instance;
            if (c == null) return null;
            switch (kind)
            {
                case ScatterKind.OreNode:
                case ScatterKind.CrystalNode:
                case ScatterKind.ScrapNode:
                case ScatterKind.FuelNode: return c.ResourceNode;
                case ScatterKind.LootCrate: return c.LootCrate;
                case ScatterKind.Nest: return c.NestPrefab;
                case ScatterKind.Ruin: return c.RuinPrefab;
                case ScatterKind.CrashedShip: return c.CrashedShipPrefab;
                default: return null;
            }
        }
    }
}
