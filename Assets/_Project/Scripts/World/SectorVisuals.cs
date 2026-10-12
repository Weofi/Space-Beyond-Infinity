using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>
    /// Client-local, deterministic sector dressing built from the synced seed on every peer:
    /// asteroid fields (with colliders, identical everywhere so server physics matches), the
    /// Collapse wall, palette (sky, fog, light) and music. Nothing here is networked.
    /// </summary>
    public class SectorVisuals : MonoBehaviour
    {
        public static SectorVisuals Instance { get; private set; }

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Transform collapse;
        Material skyInstance;
        Light sun;
        int builtSeed = int.MinValue;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
            if (collapse) Destroy(collapse.gameObject);
        }

        public void Build(int seed, int sectorIndex)
        {
            if (seed == builtSeed) return;
            builtSeed = seed;
            Clear();
            var def = ContentCatalog.GetSector(sectorIndex);
            var layout = SectorLayout.Build(seed, def);
            var cat = ContentCatalog.Instance;
            Material mat = cat != null ? cat.ToonMaterial : null;

            foreach (var f in layout.Fields) BuildField(f, layout, mat, def);
            ApplyPalette(def != null ? def.Palette : null, seed);
            EnsureCollapse(cat);
            Sfx.Music(sectorIndex);
        }

        public void Clear()
        {
            foreach (var g in spawned) if (g) Destroy(g);
            spawned.Clear();
            foreach (var m in meshes) if (m) Destroy(m);
            meshes.Clear();
            builtSeed = int.MinValue;
        }

        void BuildField(SectorLayout.Field f, SectorLayout layout, Material mat, SectorDef def)
        {
            var rng = new System.Random(f.Seed);
            var root = new GameObject("AsteroidField");
            root.transform.position = f.Center;
            spawned.Add(root);
            Color baseCol = def != null && def.Palette != null ? Color.Lerp(def.Palette.AmbientColor, new Color(0.55f, 0.58f, 0.66f), 0.65f) : new Color(0.55f, 0.58f, 0.66f);

            // Batch rocks into chunks of ~20 for few draw calls; each rock gets its own sphere collider.
            const int perChunk = 20;
            MeshBuilder mb = null;
            GameObject chunk = null;
            int inChunk = 0;
            for (int i = 0; i < f.Count; i++)
            {
                Vector3 local = new Vector3(
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.x,
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.y,
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.z);
                float r = Mathf.Lerp(2.5f, 16f, Mathf.Pow((float)rng.NextDouble(), 2.2f));
                int rockSeed = rng.Next();
                Quaternion rot = Quaternion.Euler(MathUtils.Range(rng, 0f, 360f), MathUtils.Range(rng, 0f, 360f), 0f);
                Vector3 world = f.Center + local;
                bool blocked = false;
                foreach (var p in layout.Planets)
                    if (Vector3.Distance(world, p.Pos) < p.Radius * 1.5f + r) { blocked = true; break; }
                if (Vector3.Distance(world, layout.StationPos) < 140f || Vector3.Distance(world, layout.StartPoint) < 120f) blocked = true;
                if (blocked) continue;

                if (mb == null)
                {
                    mb = new MeshBuilder { Gloss = 0.05f };
                    chunk = new GameObject("Rocks");
                    chunk.transform.SetParent(root.transform, false);
                    inChunk = 0;
                }
                float shade = MathUtils.Range(rng, 0.8f, 1.15f);
                mb.AddRock(Matrix4x4.TRS(local, rot, new Vector3(1f, MathUtils.Range(rng, 0.7f, 1f), 1f)), r, rockSeed, baseCol * shade);
                var sc = chunk.AddComponent<SphereCollider>();
                sc.center = local;
                sc.radius = r * 0.85f;
                inChunk++;
                if (inChunk >= perChunk) { FinishChunk(chunk, mb, mat); mb = null; }
            }
            if (mb != null) FinishChunk(chunk, mb, mat);
        }

        void FinishChunk(GameObject chunk, MeshBuilder mb, Material mat)
        {
            var mesh = mb.Build("Asteroids");
            meshes.Add(mesh);
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = chunk.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        void ApplyPalette(PaletteDef pal, int seed)
        {
            var cat = ContentCatalog.Instance;
            if (pal == null || cat == null) return;
            if (skyInstance == null && cat.SkyboxMaterial != null) skyInstance = new Material(cat.SkyboxMaterial);
            if (skyInstance != null)
            {
                skyInstance.SetColor("_ZenithColor", pal.SkyZenith);
                skyInstance.SetColor("_HorizonColor", pal.SkyHorizon);
                skyInstance.SetColor("_NebulaColorA", pal.NebulaA);
                skyInstance.SetColor("_NebulaColorB", pal.NebulaB);
                skyInstance.SetColor("_StarColor", pal.StarColor);
                skyInstance.SetFloat("_Seed", (seed % 1000) * 0.37f);
                skyInstance.SetFloat("_NebulaStrength", 0.55f);
                RenderSettings.skybox = skyInstance;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = pal.FogColor;
            RenderSettings.fogDensity = pal.FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = pal.AmbientColor * 1.2f;
            RenderSettings.ambientEquatorColor = Color.Lerp(pal.AmbientColor, pal.NebulaA, 0.3f);
            RenderSettings.ambientGroundColor = pal.AmbientColor * 0.6f;
            if (sun == null) sun = RenderSettings.sun != null ? RenderSettings.sun : FindSun();
            if (sun != null) sun.color = pal.SunColor;
            DynamicGI.UpdateEnvironment();
        }

        static Light FindSun()
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) return l;
            return null;
        }

        void EnsureCollapse(ContentCatalog cat)
        {
            if (collapse != null) return;
            var go = new GameObject("TheCollapse");
            collapse = go.transform;
            var mesh = IcoSphere.Create(1f, 3, "CollapseSphere");
            meshes.Remove(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cat != null ? cat.CollapseMaterial : null;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            const float R = 9000f;
            go.transform.localScale = Vector3.one * R;
        }

        void Update()
        {
            if (collapse == null) return;
            const float R = 9000f;
            bool show = GameState.Phase == GamePhase.Playing || GameState.Phase == GamePhase.GameOver;
            collapse.gameObject.SetActive(show && GameState.CollapseX > -50000f);
            collapse.position = new Vector3(GameState.CollapseX - R, 0f, 0f);
            collapse.Rotate(0f, 0f, 1.5f * Time.deltaTime, Space.World);
        }
    }
}
