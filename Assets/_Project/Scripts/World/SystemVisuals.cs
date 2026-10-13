using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>
    /// Client-local, deterministic system dressing built from the synced seed on every peer:
    /// asteroid fields (with colliders, identical everywhere so server physics matches; destructible,
    /// destroyed ids are replicated by RunManager), the galaxy palette (sky, fog, light), music and the
    /// journey on the sky (#46): the destination galaxy and the Milky Way grow from galaxy to galaxy.
    /// Nothing here is networked.
    /// </summary>
    public class SystemVisuals : MonoBehaviour
    {
        public static SystemVisuals Instance { get; private set; }

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Transform destination, milkyWay;
        float destinationDeg, milkyWayDeg;
        Material skyInstance;
        Light sun;
        Volume gradeVolume;
        VolumeProfile gradeProfile;
        int builtSeed = int.MinValue;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
            DestroySky(destination);
            DestroySky(milkyWay);
            if (gradeVolume) Destroy(gradeVolume.gameObject);
            if (gradeProfile) Destroy(gradeProfile);
        }

        /// <summary>Build the dressing of one system; a no-op when that system is already built.</summary>
        public void Build(int runSeed, int galaxy, int system)
        {
            int key = GalaxyGenerator.GalaxySeed(runSeed, galaxy) ^ (system * 7919 + 1);
            if (key == builtSeed) return;
            Clear();
            builtSeed = key;
            var layout = GalaxyGenerator.Layout(runSeed, galaxy, system);
            var def = layout.Def;
            var cat = ContentCatalog.Instance;
            Material mat = cat == null ? null : (cat.TerrainMaterial != null ? cat.TerrainMaterial : cat.ToonMaterial); // rock detail material

            for (int i = 0; i < layout.Fields.Length; i++) BuildField(layout.Fields[i], i, layout, mat);
            ApplyPalette(def != null ? def.Palette : null, layout.Seed);
            BuildJourneySky(cat, galaxy, def);
            // Music moods: calm start, rising tension, climax in the last two galaxies.
            Sfx.Music(new[] { 1, 1, 2, 2, 3, 3 }[Mathf.Clamp(galaxy, 0, 5)]);
        }

        public void Clear()
        {
            foreach (var g in spawned) if (g) Destroy(g);
            spawned.Clear();
            foreach (var m in meshes) if (m) Destroy(m);
            meshes.Clear();
            rocks.Clear();
            builtSeed = int.MinValue;
        }

        /// <summary>One asteroid: render data for its batched chunk plus its collider proxy.</summary>
        class Rock
        {
            public int Id;
            public Matrix4x4 Local;   // inside the chunk (field) space
            public float Radius;
            public int Seed;
            public Color Color;
            public RockChunk Chunk;
            public AsteroidRock Proxy;
            public bool Dead;
        }

        class RockChunk
        {
            public GameObject Go;
            public MeshFilter Filter;
            public readonly List<Rock> Rocks = new List<Rock>();
        }

        readonly Dictionary<int, Rock> rocks = new Dictionary<int, Rock>();
        static Mesh debrisMesh;

        /// <summary>Stable asteroid id: field index and rock index inside the field.</summary>
        public static int RockId(int field, int index) => field * 1000 + index;

        /// <summary>Radius of a live asteroid, 0 if unknown or destroyed.</summary>
        public float RockRadius(int id) => rocks.TryGetValue(id, out var r) && !r.Dead ? r.Radius : 0f;

        public Vector3 RockPosition(int id) =>
            rocks.TryGetValue(id, out var r) && r.Chunk.Go != null ? r.Chunk.Go.transform.TransformPoint(r.Local.GetColumn(3)) : Vector3.zero;

        void BuildField(SystemLayout.Field f, int fieldIndex, SystemLayout layout, Material mat)
        {
            var rng = new System.Random(f.Seed);
            var root = new GameObject("AsteroidField");
            root.transform.position = f.Center;
            spawned.Add(root);
            Color baseCol = new Color(0.6f, 0.62f, 0.68f);

            // Batch rocks into chunks of ~20 for few draw calls; each rock gets its own convex collider proxy
            // (shot at, rammed into) that matches the jittered rock shape.
            const int perChunk = 20;
            var chunks = new List<RockChunk>();
            for (int i = 0; i < f.Count; i++)
            {
                Vector3 local = new Vector3(
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.x,
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.y,
                    MathUtils.Range(rng, -1f, 1f) * f.Extents.z);
                float r = Mathf.Lerp(2.5f, 16f, Mathf.Pow((float)rng.NextDouble(), 2.2f));
                int rockSeed = rng.Next();
                Quaternion rot = Quaternion.Euler(MathUtils.Range(rng, 0f, 360f), MathUtils.Range(rng, 0f, 360f), 0f);
                float squash = MathUtils.Range(rng, 0.7f, 1f);
                float shade = MathUtils.Range(rng, 0.8f, 1.15f);
                Vector3 world = f.Center + local;
                bool blocked = false;
                foreach (var p in layout.Planets)
                    if (Vector3.Distance(world, p.Pos) < p.Radius * 1.5f + r) { blocked = true; break; }
                if (Vector3.Distance(world, layout.StationPos) < 160f || Vector3.Distance(world, layout.StartPoint) < 160f) blocked = true;
                if (blocked) continue;

                var chunk = chunks.Count > 0 ? chunks[chunks.Count - 1] : null;
                if (chunk == null || chunk.Rocks.Count >= perChunk)
                {
                    chunk = new RockChunk { Go = new GameObject("Rocks") };
                    chunk.Go.transform.SetParent(root.transform, false);
                    chunk.Filter = chunk.Go.AddComponent<MeshFilter>();
                    chunk.Go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    chunks.Add(chunk);
                }
                var rock = new Rock
                {
                    Id = RockId(fieldIndex, i), Local = Matrix4x4.TRS(local, rot, new Vector3(1f, squash, 1f)),
                    Radius = r, Seed = rockSeed, Color = baseCol * shade, Chunk = chunk
                };
                chunk.Rocks.Add(rock);
                rocks[rock.Id] = rock;

                var proxy = new GameObject("Rock");
                proxy.transform.SetParent(chunk.Go.transform, false);
                proxy.transform.localPosition = local;
                proxy.transform.localRotation = rot;
                var cb = new MeshBuilder();
                cb.AddRock(Matrix4x4.Scale(new Vector3(1f, squash, 1f)), r, rockSeed, Color.white);
                var cm = cb.Build("AsteroidCollider");
                meshes.Add(cm);
                var mc = proxy.AddComponent<MeshCollider>();
                mc.convex = true;
                mc.sharedMesh = cm;
                rock.Proxy = proxy.AddComponent<AsteroidRock>();
                rock.Proxy.Id = rock.Id;
                rock.Proxy.Radius = r;
            }
            foreach (var c in chunks) RebuildChunk(c);
        }

        void RebuildChunk(RockChunk chunk)
        {
            var mb = new MeshBuilder { Gloss = 0.05f, Panel = 1f };
            int live = 0;
            foreach (var r in chunk.Rocks)
            {
                if (r.Dead) continue;
                mb.AddRock(r.Local, r.Radius, r.Seed, r.Color);
                live++;
            }
            var old = chunk.Filter.sharedMesh;
            if (old != null) { meshes.Remove(old); Destroy(old); }
            if (live == 0) { chunk.Filter.sharedMesh = null; return; }
            var mesh = mb.Build("Asteroids");
            meshes.Add(mesh);
            chunk.Filter.sharedMesh = mesh;
        }

        /// <summary>Every peer: remove a destroyed asteroid (with explosion and fragments when <paramref name="fx"/>).</summary>
        public void DestroyRock(int id, bool fx)
        {
            if (!rocks.TryGetValue(id, out var r) || r.Dead) return;
            r.Dead = true;
            Vector3 pos = RockPosition(id);
            if (r.Proxy != null) Destroy(r.Proxy.gameObject);
            RebuildChunk(r.Chunk);
            if (!fx) return;
            Vfx.Explosion(pos, r.Radius * 1.3f, new Color(1f, 0.62f, 0.35f));
            Sfx.Play(r.Radius > 8f ? "explosion_big" : "explosion", pos, 1f, Random.Range(0.8f, 1f));
            SpawnDebris(pos, r);
        }

        void SpawnDebris(Vector3 pos, Rock r)
        {
            if (debrisMesh == null)
            {
                var mb = new MeshBuilder { Gloss = 0.05f, Panel = 1f };
                mb.AddRock(Matrix4x4.identity, 1f, 4242, new Color(0.6f, 0.62f, 0.68f));
                debrisMesh = mb.Build("AsteroidDebris");
            }
            var mat = r.Chunk.Go != null ? r.Chunk.Go.GetComponent<MeshRenderer>().sharedMaterial : null;
            int n = Mathf.Clamp(Mathf.RoundToInt(r.Radius * 0.7f), 4, 10);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Debris");
                go.transform.position = pos + Random.insideUnitSphere * r.Radius * 0.5f;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * r.Radius * Random.Range(0.12f, 0.3f);
                go.AddComponent<MeshFilter>().sharedMesh = debrisMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var d = go.AddComponent<AsteroidDebris>();
                d.Velocity = Random.onUnitSphere * Random.Range(6f, 18f) * Mathf.Sqrt(r.Radius / 4f);
                d.Spin = Random.insideUnitSphere * 160f;
                d.Life = Random.Range(3f, 5.5f);
            }
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
                RenderSettings.skybox = skyInstance;
            }
            pal.ApplyEnvironment();
            if (sun == null) sun = RenderSettings.sun != null ? RenderSettings.sun : FindSun();
            if (sun != null) sun.color = pal.SunColor;
            DynamicGI.UpdateEnvironment();
            ApplyGrade(pal);
        }

        /// <summary>Per-sector color grade: a runtime global volume above the scene volume overrides only grading parameters.</summary>
        void ApplyGrade(PaletteDef pal)
        {
            if (gradeVolume == null)
            {
                var go = new GameObject("SectorGrade");
                gradeVolume = go.AddComponent<Volume>();
                gradeVolume.isGlobal = true;
                gradeVolume.priority = 5f;
                gradeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                gradeProfile.Add<ColorAdjustments>(true);
                gradeProfile.Add<WhiteBalance>(true);
                gradeProfile.Add<Bloom>(true);
                gradeVolume.sharedProfile = gradeProfile;
            }
            if (gradeProfile.TryGet<ColorAdjustments>(out var ca))
            {
                ca.colorFilter.Override(pal.GradeFilter);
                ca.saturation.Override(pal.GradeSaturation);
                ca.contrast.Override(pal.GradeContrast);
                ca.postExposure.Override(pal.GradeExposure);
            }
            if (gradeProfile.TryGet<WhiteBalance>(out var wb)) wb.temperature.Override(pal.GradeTemperature);
            if (gradeProfile.TryGet<Bloom>(out var bloom)) bloom.tint.Override(pal.BloomTint);
        }

        static Light FindSun()
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) return l;
            return null;
        }

        // ---------------------------------------------------------------- journey on the sky

        const float SkyDistance = 15000f; // inside the 20 km far plane, beyond everything in a system
        public static readonly Vector3 DestinationDir = new Vector3(1f, 0.16f, -0.42f).normalized;
        static readonly Vector3 MilkyWayDir = new Vector3(1f, 0.38f, 0.52f).normalized;
        // Apparent diameter in degrees per galaxy (index 1..5): the destination comes closer, the Milky Way grows.
        static readonly float[] DestinationSize = { 0f, 9f, 6f, 7f, 13f, 0f };
        static readonly float[] MilkyWaySize = { 0f, 2.5f, 3f, 4.5f, 9f, 42f };

        void BuildJourneySky(ContentCatalog cat, int galaxy, GalaxyDef def)
        {
            var mat = cat != null ? cat.ParticleMaterial : null;
            if (mat == null) return;
            int g = Mathf.Clamp(galaxy, 1, 5);
            if (milkyWay == null) milkyWay = SkyGalaxy("MilkyWay", mat, 2, new Color(1f, 0.88f, 0.72f), 9001, Quaternion.LookRotation(MilkyWayDir) * Quaternion.Euler(64f, 0f, 18f));
            milkyWayDeg = MilkyWaySize[g];
            DestroySky(destination);
            destination = null;
            destinationDeg = DestinationSize[g];
            if (destinationDeg > 0f)
            {
                Color tint = def != null ? def.NextGalaxyTint : Color.white;
                destination = SkyGalaxy("NextGalaxy", mat, g % 2 == 0 ? 3 : 2, tint, 4200 + g, Quaternion.LookRotation(DestinationDir) * Quaternion.Euler(52f + g * 6f, 0f, -30f + g * 11f));
            }
        }

        /// <summary>A spiral galaxy made of soft additive dots (unit radius) with a bright core.</summary>
        Transform SkyGalaxy(string name, Material mat, int arms, Color tint, int seed, Quaternion rot)
        {
            var rng = new System.Random(seed);
            const int dots = 1400;
            var verts = new List<Vector3>(dots * 4);
            var cols = new List<Color>(dots * 4);
            var uvs = new List<Vector2>(dots * 4);
            var tris = new List<int>(dots * 6);
            Color core = new Color(1f, 0.93f, 0.8f);
            for (int i = 0; i < dots; i++)
            {
                bool bulge = i < 160;
                float t = bulge ? Mathf.Pow((float)rng.NextDouble(), 2f) * 0.22f : 0.08f + 0.92f * Mathf.Pow((float)rng.NextDouble(), 0.8f);
                float ang = bulge ? (float)rng.NextDouble() * Mathf.PI * 2f : (rng.Next(arms) * Mathf.PI * 2f / arms) + t * 4.2f;
                float spread = bulge ? 0f : 0.05f + 0.16f * t;
                Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * t + new Vector2(Gauss(rng), Gauss(rng)) * spread;
                float size = bulge ? 0.07f + 0.08f * (float)rng.NextDouble() : 0.025f + 0.05f * (float)rng.NextDouble();
                Color c = Color.Lerp(core, tint, Mathf.Clamp01(t * 1.6f));
                c.a = bulge ? 0.35f : Mathf.Lerp(0.3f, 0.08f, t);
                int b0 = verts.Count;
                verts.Add(new Vector3(pos.x - size, pos.y - size, 0f)); verts.Add(new Vector3(pos.x + size, pos.y - size, 0f));
                verts.Add(new Vector3(pos.x + size, pos.y + size, 0f)); verts.Add(new Vector3(pos.x - size, pos.y + size, 0f));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) cols.Add(c);
                tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 1); tris.Add(b0); tris.Add(b0 + 3); tris.Add(b0 + 2);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            var go = new GameObject("Sky_" + name);
            go.transform.rotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Sky galaxies outlive Clear() (the Milky Way is built once); they own their mesh.</summary>
        static void DestroySky(Transform t)
        {
            if (t == null) return;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(t.gameObject);
        }

        static float Gauss(System.Random r)
        {
            double u = 1.0 - r.NextDouble(), v = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v)) * 0.5f;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool show = cam != null && GameState.Phase == GamePhase.Playing && builtSeed != int.MinValue;
            PlaceSky(milkyWay, MilkyWayDir, milkyWayDeg, show, cam);
            PlaceSky(destination, DestinationDir, destinationDeg, show, cam);
        }

        static void PlaceSky(Transform t, Vector3 dir, float deg, bool show, Camera cam)
        {
            if (t == null) return;
            t.gameObject.SetActive(show && deg > 0f);
            if (!show || deg <= 0f) return;
            t.position = cam.transform.position + dir * SkyDistance;
            t.localScale = Vector3.one * (SkyDistance * Mathf.Tan(deg * 0.5f * Mathf.Deg2Rad));
        }
    }
}
