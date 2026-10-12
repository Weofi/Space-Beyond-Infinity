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
            foreach (var m in _decorMeshes) if (m != null) Destroy(m);
            _decorMeshes.Clear();
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

            // Generate meshes. The welded collider matches LOD0's geometry exactly so feet never float or sink.
            _lod0Mesh = PlanetMeshGenerator.GenerateVisualAt(seed, radius, biome, 5);
            _lod1Mesh = PlanetMeshGenerator.GenerateVisualAt(seed, radius, biome, 3);
            _colliderMesh = PlanetMeshGenerator.GenerateCollider(seed, radius, biome, 5);

            var catalog = ContentCatalog.Instance;
            Material toonMat = catalog == null ? null : catalog.TerrainMaterial != null ? catalog.TerrainMaterial : catalog.ToonMaterial;

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
            _gravity.InfluenceRadius = radius * 2.2f;

            // Atmosphere shell.
            if (biome.HasAtmosphere)
                BuildAtmosphere(radius, catalog);

            BuildDecor(seed, radius, biome, toonMat);
        }

        /// <summary>
        /// Client-local, seed-deterministic surface dressing (rocks, shards, trees, crystals, vents),
        /// batched into a few meshes. Large pieces get sphere colliders so you can't walk through them.
        /// </summary>
        void BuildDecor(int seed, float radius, BiomeDef biome, Material mat)
        {
            var rng = new System.Random(seed ^ 0x2b7e1516);
            int count = Mathf.RoundToInt(radius * 3f);
            const int perChunk = 70;
            MeshBuilder mb = null;
            GameObject chunk = null;
            int n = 0;
            Color rockCol = Color.Lerp(biome.LowColor, new Color(0.45f, 0.42f, 0.5f), 0.55f);
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = MathUtils.OnSphere(rng);
                float h01 = PlanetMeshGenerator.SampleHeight01(seed, biome, dir);
                Vector3 local = dir * (radius * (1f + biome.Amplitude * h01) - 0.4f);
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, MathUtils.Range(rng, 0f, 360f), 0f);
                if (mb == null)
                {
                    mb = new MeshBuilder { Gloss = 0.08f };
                    chunk = new GameObject("Decor");
                    chunk.transform.SetParent(transform, false);
                    n = 0;
                }
                float r = MathUtils.Range(rng, 0f, 1f);
                float size = Mathf.Lerp(0.6f, 3.2f, r * r);
                switch (biome.Biome)
                {
                    case Voidward.Core.Biome.Ice:
                        if (r < 0.5f) mb.AddPrism(Matrix4x4.TRS(local + dir * size, rot * Quaternion.Euler(MathUtils.Range(rng, -15f, 15f), 0, MathUtils.Range(rng, -15f, 15f)), Vector3.one), size * 0.45f, 0.05f, size * 2.6f, 5, Color.Lerp(biome.LowColor, Color.white, 0.4f));
                        else mb.AddRock(Matrix4x4.TRS(local, rot, new Vector3(1f, 0.7f, 1f)), size, rng.Next(), rockCol);
                        break;
                    case Voidward.Core.Biome.Desert:
                        if (r > 0.92f) mb.AddBox(Matrix4x4.TRS(local + dir * size * 1.5f, rot, Vector3.one), new Vector3(size * 2f, size * 3f, size * 1.6f), biome.HighColor);
                        else mb.AddRock(Matrix4x4.TRS(local, rot, new Vector3(1.2f, 0.6f, 1f)), size, rng.Next(), Color.Lerp(biome.HighColor, rockCol, 0.4f));
                        break;
                    case Voidward.Core.Biome.ToxicJungle:
                        if (r < 0.55f)
                        {
                            float th = size * 2.5f + 2f;
                            mb.AddPrism(Matrix4x4.TRS(local + dir * th * 0.5f, rot, Vector3.one), 0.25f + size * 0.08f, 0.15f, th, 5, new Color(0.45f, 0.32f, 0.4f));
                            mb.AddRock(Matrix4x4.TRS(local + dir * th, rot, new Vector3(1.3f, 0.8f, 1.3f)), size * 0.9f + 0.8f, rng.Next(), r < 0.3f ? biome.HighColor : new Color(0.95f, 0.55f, 0.75f));
                        }
                        else mb.AddRock(Matrix4x4.TRS(local, rot, new Vector3(1f, 0.6f, 1f)), size * 0.7f, rng.Next(), biome.MidColor * 0.8f);
                        break;
                    case Voidward.Core.Biome.Crystal:
                        for (int k = 0; k < 3; k++)
                            mb.AddPrism(Matrix4x4.TRS(local + dir * size, rot * Quaternion.Euler(MathUtils.Range(rng, -25f, 25f), k * 120f, MathUtils.Range(rng, -25f, 25f)), Vector3.one),
                                size * 0.35f, 0.04f, size * (2f + k * 0.6f), 5, k == 0 ? biome.HighColor : new Color(0.6f, 0.9f, 1f));
                        break;
                    default: // Volcanic
                        if (r > 0.85f) mb.AddPrism(Matrix4x4.TRS(local + dir * size, rot, Vector3.one), size * 1.2f, size * 0.4f, size * 2f, 7, new Color(1f, 0.5f, 0.3f));
                        else mb.AddRock(Matrix4x4.TRS(local, rot, new Vector3(1f, 0.8f, 1f)), size, rng.Next(), rockCol * 0.8f);
                        break;
                }
                if (size > 1.6f)
                {
                    var sc = chunk.AddComponent<SphereCollider>();
                    sc.center = local + dir * size * 0.5f;
                    sc.radius = size * 0.8f;
                }
                if (++n >= perChunk || i == count - 1)
                {
                    var mesh = mb.Build("PlanetDecor");
                    _decorMeshes.Add(mesh);
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = chunk.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    _decor.Add(chunk);
                    mb = null;
                }
            }
        }

        readonly List<Mesh> _decorMeshes = new List<Mesh>();
        readonly List<GameObject> _decor = new List<GameObject>();

        GameObject CreateVisualChild(string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            // Toon lighting provides the day/night terminator; self-shadowing a 300 m sphere only causes acne.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", Biome.AtmosphereColor);
            mr.SetPropertyBlock(mpb);

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
                foreach (var d in _decor) if (d != null) d.SetActive(useLOD0);
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
