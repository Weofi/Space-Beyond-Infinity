using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Networked spherical planet. Only Seed, Radius and BiomeIndex are synced; every client
    /// generates identical terrain geometry locally from the seed (zero terrain bandwidth).
    /// </summary>
    public class PlanetBody : NetworkBehaviour
    {
        // ---- Synced state ----

        /// <summary>Deterministic world-generation seed shared across all clients.</summary>
        public NetworkVariable<int> Seed = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Base planet radius in meters (before terrain displacement).</summary>
        public NetworkVariable<float> Radius = new NetworkVariable<float>(
            200f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

        Mesh _lod0Mesh;
        Mesh _lod1Mesh;
        Mesh _colliderMesh;
        Mesh _atmosphereMesh;
        GameObject _visual0;
        GameObject _visual1;
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
                Debug.LogError("[PlanetBody] ContentCatalog or PlanetPrefab missing — cannot spawn planet.");
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
            body.Seed.Value = seed;
            body.Radius.Value = radius;
            body.BiomeIndex.Value = idx;
            body.NetworkObject.Spawn();
            return body;
        }

        // ---- Network lifecycle ----

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var catalog = ContentCatalog.Instance;
            if (catalog != null && catalog.Biomes != null &&
                BiomeIndex.Value >= 0 && BiomeIndex.Value < catalog.Biomes.Length)
            {
                Biome = catalog.Biomes[BiomeIndex.Value];
            }

            if (Biome == null)
            {
                Debug.LogWarning($"[PlanetBody] Biome not resolved (index {BiomeIndex.Value}) on {name}.");
                return;
            }

            BuildPlanet();
            All.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            All.Remove(this);

            if (_lod0Mesh != null) { Destroy(_lod0Mesh); _lod0Mesh = null; }
            if (_lod1Mesh != null) { Destroy(_lod1Mesh); _lod1Mesh = null; }
            if (_colliderMesh != null) { Destroy(_colliderMesh); _colliderMesh = null; }
            if (_atmosphereMesh != null) { Destroy(_atmosphereMesh); _atmosphereMesh = null; }
        }

        // ---- Public queries ----

        /// <summary>True when a world position is inside the atmosphere shell (within Radius*1.6 of center and biome has atmosphere).</summary>
        public bool IsInsideAtmosphere(Vector3 pos)
        {
            return HasAtmosphere && Vector3.Distance(pos, transform.position) < Radius.Value * 1.6f;
        }

        /// <summary>World-space surface position at a direction from the planet center.</summary>
        public Vector3 SurfaceWorldPoint(Vector3 unitDirFromCenter)
        {
            Vector3 local = PlanetMeshGenerator.SurfacePoint(Seed.Value, Radius.Value, Biome, unitDirFromCenter);
            return transform.TransformPoint(local);
        }

        /// <summary>Displaced terrain radius at a direction (distance from center to surface).</summary>
        public float TerrainRadiusAt(Vector3 unitDir)
        {
            if (Biome == null) return Radius.Value;
            float h = PlanetMeshGenerator.SampleHeight01(Seed.Value, Biome, unitDir);
            return Radius.Value * (1f + Biome.Amplitude * h);
        }

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

        // ---- Build ----

        void BuildPlanet()
        {
            int seed = Seed.Value;
            float radius = Radius.Value;
            var biome = Biome;

            // Generate meshes.
            _lod0Mesh = PlanetMeshGenerator.GenerateVisual(seed, radius, biome);
            _lod1Mesh = PlanetMeshGenerator.GenerateVisualAt(seed, radius, biome, 2);
            _colliderMesh = PlanetMeshGenerator.GenerateCollider(seed, radius, biome);

            var catalog = ContentCatalog.Instance;
            Material toonMat = catalog != null ? catalog.ToonMaterial : null;

            // Visual0 (LOD0 — subdiv 4).
            _visual0 = CreateVisualChild("Visual0", _lod0Mesh, toonMat);
            // Visual1 (LOD1 — subdiv 2).
            _visual1 = CreateVisualChild("Visual1", _lod1Mesh, toonMat);

            // Start with LOD0 active; Update will swap by camera distance.
            _visual0.SetActive(true);
            _visual1.SetActive(false);

            // MeshCollider on the root.
            var mc = gameObject.GetComponent<MeshCollider>();
            if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = _colliderMesh;

            // Gravity source.
            _gravity = gameObject.GetComponent<GravitySource>();
            if (_gravity == null) _gravity = gameObject.AddComponent<GravitySource>();
            _gravity.SurfaceRadius = radius;
            _gravity.SurfaceGravity = 9.81f * Mathf.Clamp(radius / 200f, 0.7f, 1.4f);
            _gravity.InfluenceRadius = radius * 2.5f;

            // Atmosphere shell.
            if (biome.HasAtmosphere)
                BuildAtmosphere(radius, catalog);
        }

        GameObject CreateVisualChild(string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return child;
        }

        void BuildAtmosphere(float radius, ContentCatalog catalog)
        {
            // Atmosphere visual: icosphere subdiv 2, radius*1.08, additive fresnel material.
            var atmoGo = new GameObject("Atmosphere");
            atmoGo.transform.SetParent(transform, false);
            var mf = atmoGo.AddComponent<MeshFilter>();
            var mr = atmoGo.AddComponent<MeshRenderer>();
            _atmosphereMesh = IcoSphere.Create(radius * 1.08f, 2, "Atmosphere");
            mf.sharedMesh = _atmosphereMesh;
            mr.sharedMaterial = catalog != null ? catalog.AtmosphereMaterial : null;

            // Atmosphere trigger: sphere collider (Radius*1.6) with AtmosphereTrigger.
            var triggerGo = new GameObject("AtmosphereTrigger");
            triggerGo.transform.SetParent(transform, false);
            var sc = triggerGo.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = radius * 1.6f;
            var trigger = triggerGo.AddComponent<AtmosphereTrigger>();
            trigger.Planet = this;
        }

        // ---- LOD swap ----

        void Update()
        {
            if (_visual0 == null || _visual1 == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            float dist = Vector3.Distance(cam.transform.position, transform.position);
            bool useLOD0 = dist < Radius.Value * 2.5f;

            if (_visual0.activeSelf != useLOD0)
            {
                _visual0.SetActive(useLOD0);
                _visual1.SetActive(!useLOD0);
            }
        }
    }

    /// <summary>
    /// Trigger volume that fires atmosphere-entry VFX/SFX when the local player pawn or ship enters.
    /// 3-second re-entry cooldown prevents repeated triggers.
    /// </summary>
    public class AtmosphereTrigger : MonoBehaviour
    {
        /// <summary>The planet this atmosphere belongs to.</summary>
        public PlanetBody Planet;

        float _lastEntryTime = -10f;

        void OnTriggerEnter(Collider other)
        {
            if (Planet == null || !Planet.HasAtmosphere) return;

            // Skip objects that are part of this planet (scatter, atmosphere visuals).
            if (other.transform.IsChildOf(transform.root)) return;

            // Check for the local player pawn or its ship: any NetworkBehaviour in the
            // parent chain with IsOwner.
            if (!IsLocalOwned(other.transform)) return;

            // 3-second re-entry cooldown.
            if (Time.time - _lastEntryTime < 3f) return;
            _lastEntryTime = Time.time;

            Vfx.AtmosphereEntry();
            Sfx.Play("atmo_entry", other.transform.position);
            Vfx.Shake(0.4f);
        }

        static bool IsLocalOwned(Transform t)
        {
            Transform current = t;
            while (current != null)
            {
                var behaviours = current.GetComponents<NetworkBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i].IsOwner)
                        return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
