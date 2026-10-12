using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>What players find at a planet site.</summary>
    public enum SiteKind { Ruins, CrashSite, Hive, CrystalGrove, OreVein, FuelVent, Salvage, Cache }

    /// <summary>
    /// Networked spherical planet. Only Seed, Radius and BiomeIndex are synced; every client
    /// generates identical terrain, sites and landmarks locally from the seed (zero terrain bandwidth).
    /// </summary>
    public class PlanetBody : NetworkBehaviour
    {
        // ---- Synced state ----

        /// <summary>Deterministic world-generation seed shared across all clients.</summary>
        public NetworkVariable<int> Seed = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Base planet radius in meters (before terrain displacement).</summary>
        public NetworkVariable<float> Radius = new NetworkVariable<float>(
            1500f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Index into ContentCatalog.Instance.Biomes for this planet's biome.</summary>
        public NetworkVariable<int> BiomeIndex = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ---- Runtime state ----

        /// <summary>Biome definition resolved from BiomeIndex on spawn.</summary>
        public BiomeDef Biome { get; private set; }

        /// <summary>True when the planet has a breathable atmosphere.</summary>
        public bool Breathable => Biome != null && Biome.Breathable;

        /// <summary>True when the planet has any atmosphere shell.</summary>
        public bool HasAtmosphere => Biome != null && Biome.HasAtmosphere;

        /// <summary>All active planet bodies in the scene.</summary>
        public static readonly List<PlanetBody> All = new List<PlanetBody>();

        /// <summary>Deterministic height field of this planet (null until built).</summary>
        public PlanetTerrain Terrain => _visuals != null ? _visuals.Terrain : null;
        public PlanetVisuals Visuals => _visuals;

        /// <summary>Kind of each terrain site (same order as Terrain.Sites), identical on every peer.</summary>
        public SiteKind[] SiteKinds { get; private set; } = new SiteKind[0];

        public float AtmosphereRadius => Terrain != null ? Terrain.AtmosphereRadius : Radius.Value * 1.25f;

        PlanetVisuals _visuals;
        GravitySource _gravity;

        // ---- Spawn helper ----

        /// <summary>
        /// Server-only: instantiates the planet prefab, sets synced variables, and spawns the NetworkObject.
        /// </summary>
        public static PlanetBody SpawnServer(int seed, float radius, BiomeDef biome, Vector3 position)
        {
            var catalog = ContentCatalog.Instance;
            if (catalog == null || catalog.PlanetPrefab == null)
            {
                Debug.LogError("[PlanetBody] ContentCatalog or PlanetPrefab missing - cannot spawn planet.");
                return null;
            }

            int idx = -1;
            if (catalog.Biomes != null)
            {
                for (int i = 0; i < catalog.Biomes.Length; i++)
                {
                    if (catalog.Biomes[i] == biome) { idx = i; break; }
                }
            }

            var go = Instantiate(catalog.PlanetPrefab, position, Quaternion.identity);
            var body = go.GetComponent<PlanetBody>();
            body.pendingSeed = seed;
            body.pendingRadius = radius;
            body.pendingBiome = idx;
            body.NetworkObject.Spawn();
            return body;
        }

        int pendingSeed;
        float pendingRadius;
        int pendingBiome = -1;

        // ---- Network lifecycle ----

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && pendingBiome >= 0)
            {
                Seed.Value = pendingSeed;
                Radius.Value = pendingRadius;
                BiomeIndex.Value = pendingBiome;
            }

            var catalog = ContentCatalog.Instance;
            if (catalog != null && catalog.Biomes != null &&
                BiomeIndex.Value >= 0 && BiomeIndex.Value < catalog.Biomes.Length)
            {
                Biome = catalog.Biomes[BiomeIndex.Value];
            }

            if (Biome == null || Biome.Shape == null)
            {
                Debug.LogWarning($"[PlanetBody] Biome or planet shape not resolved (index {BiomeIndex.Value}) on {name}.");
                return;
            }

            BuildPlanet();
            All.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            All.Remove(this);
            if (_visuals != null) _visuals.Clear();
            if (_gravity != null) _gravity.GroundRadiusAt = null;
        }

        // ---- Public queries ----

        /// <summary>True when a world position is inside the atmosphere shell.</summary>
        public bool IsInsideAtmosphere(Vector3 pos)
        {
            return HasAtmosphere && Vector3.Distance(pos, transform.position) < AtmosphereRadius;
        }

        /// <summary>World-space surface position at a direction from the planet center.</summary>
        public Vector3 SurfaceWorldPoint(Vector3 unitDirFromCenter)
        {
            return transform.TransformPoint(unitDirFromCenter * TerrainRadiusAt(unitDirFromCenter));
        }

        /// <summary>Displaced terrain radius at a direction (distance from center to surface).</summary>
        public float TerrainRadiusAt(Vector3 unitDir)
        {
            var t = Terrain;
            return t != null ? t.SurfaceRadius(unitDir) : Radius.Value;
        }

        /// <summary>World-space terrain normal at a direction from the planet center.</summary>
        public Vector3 SurfaceNormal(Vector3 unitDir, float step = 2f)
        {
            var t = Terrain;
            return t != null ? transform.TransformDirection(t.NormalAt(transform.InverseTransformDirection(unitDir), step)) : unitDir;
        }

        /// <summary>True when the terrain at this world position lies below the ocean surface.</summary>
        public bool IsUnderwater(Vector3 worldPos)
        {
            var t = Terrain;
            return t != null && t.IsUnderwater(transform.InverseTransformDirection((worldPos - transform.position).normalized));
        }

        /// <summary>Ray vs. terrain/sea using the height field (no colliders needed, any distance).</summary>
        public bool Raycast(Ray ray, float maxDistance, out Vector3 point)
        {
            point = default;
            var t = Terrain;
            if (t == null) return false;
            Vector3 o = transform.InverseTransformPoint(ray.origin);
            Vector3 d = transform.InverseTransformDirection(ray.direction).normalized;
            if (!t.Raycast(o, d, maxDistance, out float dist)) return false;
            point = ray.origin + ray.direction.normalized * dist;
            return true;
        }

        /// <summary>World position of site i (center of its clearing).</summary>
        public Vector3 SiteWorldPoint(int i)
        {
            var s = Terrain.Sites[i];
            return transform.TransformPoint(s.Dir * Terrain.Radius * (1f + s.Elevation));
        }

        public static string SiteLabel(SiteKind k) => k switch
        {
            SiteKind.Ruins => "RUINS",
            SiteKind.CrashSite => "CRASH SITE",
            SiteKind.Hive => "HIVE",
            SiteKind.CrystalGrove => "CRYSTAL GROVE",
            SiteKind.OreVein => "ORE VEIN",
            SiteKind.FuelVent => "FUEL VENT",
            SiteKind.Salvage => "SALVAGE",
            _ => "SUPPLY CACHE",
        };

        /// <summary>Nearest planet body to a world position, or null if none exist.</summary>
        public static PlanetBody Nearest(Vector3 pos)
        {
            PlanetBody best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                float sqr = (All[i].transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = All[i]; }
            }
            return best;
        }

        /// <summary>Ray vs. every planet; nearest hit.</summary>
        public static bool RaycastAll(Ray ray, float maxDistance, out Vector3 point, out PlanetBody planet)
        {
            point = default;
            planet = null;
            float best = float.MaxValue;
            foreach (var p in All)
            {
                if (p == null || !p.Raycast(ray, maxDistance, out var hit)) continue;
                float d = (hit - ray.origin).sqrMagnitude;
                if (d < best) { best = d; point = hit; planet = p; }
            }
            return planet != null;
        }

        // ---- Build ----

        void BuildPlanet()
        {
            int seed = Seed.Value;
            float radius = Radius.Value;
            var biome = Biome;

            _visuals = gameObject.GetComponent<PlanetVisuals>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PlanetVisuals>();
            _visuals.Build(biome.Shape, seed, radius, true);
            var terrain = _visuals.Terrain;

            _gravity = gameObject.GetComponent<GravitySource>();
            if (_gravity == null) _gravity = gameObject.AddComponent<GravitySource>();
            _gravity.SurfaceRadius = terrain.MaxRadius;
            _gravity.SurfaceGravity = 9.81f;
            _gravity.InfluenceRadius = terrain.AtmosphereRadius + radius * 0.35f;
            _gravity.GroundRadiusAt = dir => terrain.GroundRadius(transform.InverseTransformDirection(dir));

            SiteKinds = AssignSiteKinds(terrain, biome, seed);

            if (biome.HasAtmosphere) BuildAtmosphereTrigger(terrain.AtmosphereRadius);
        }

        /// <summary>
        /// Deterministic site roles: the biome's set pieces (ruins, crash sites, hives) first, then resource fields
        /// matching the biome's scatter table.
        /// </summary>
        static SiteKind[] AssignSiteKinds(PlanetTerrain terrain, BiomeDef biome, int seed)
        {
            int n = terrain.Sites.Count;
            var kinds = new List<SiteKind>();
            var fields = new List<SiteKind>();
            foreach (var e in biome.Scatter)
            {
                switch (e.Kind)
                {
                    case ScatterKind.Ruin: for (int i = 0; i < e.Count; i++) kinds.Add(SiteKind.Ruins); break;
                    case ScatterKind.CrashedShip: for (int i = 0; i < e.Count; i++) kinds.Add(SiteKind.CrashSite); break;
                    case ScatterKind.Nest: for (int i = 0; i < Mathf.Min(2, e.Count); i++) kinds.Add(SiteKind.Hive); break;
                    case ScatterKind.OreNode: fields.Add(SiteKind.OreVein); break;
                    case ScatterKind.CrystalNode: fields.Add(SiteKind.CrystalGrove); break;
                    case ScatterKind.FuelNode: fields.Add(SiteKind.FuelVent); break;
                    case ScatterKind.ScrapNode: fields.Add(SiteKind.Salvage); break;
                }
            }
            kinds.Add(SiteKind.Cache);
            if (fields.Count == 0) fields.Add(SiteKind.OreVein);
            var rng = new System.Random(seed ^ 0x3c6ef372);
            for (int i = 0; kinds.Count < n; i++) kinds.Add(fields[(i + rng.Next(0, fields.Count)) % fields.Count]);
            // Shuffle so set pieces are spread over the planet.
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            var result = new SiteKind[n];
            for (int i = 0; i < n; i++) result[i] = kinds[i];
            return result;
        }

        void BuildAtmosphereTrigger(float atmoRadius)
        {
            var triggerGo = new GameObject("AtmosphereTrigger");
            triggerGo.transform.SetParent(transform, false);
            var sc = triggerGo.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = atmoRadius;
            var trigger = triggerGo.AddComponent<AtmosphereTrigger>();
            trigger.Planet = this;
        }
    }
}
