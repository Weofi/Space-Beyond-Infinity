using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Core;
using Voidward.Data;
using Voidward.Planet;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// Client-local warp corridor (#79), identical on every peer: shows the obstacles of the WarpRoute inside the
    /// nav computer's scanner range streaming past the bubble along -X (they grow out of the warp haze at the
    /// scanner edge), speed streaks, signal beacons and the gas cloud of a signal the crew dropped out at. It also
    /// detects corridor impacts for every ship this peer simulates (its own piloted ship, unpiloted ones on the
    /// server) and reports them to WarpFlight, and feeds gravity wells into WarpBubble.Pull.
    /// </summary>
    public class WarpCorridorView : MonoBehaviour
    {
        public WarpFlight Flight;

        class Item
        {
            public WarpObstacle O;
            public GameObject Go;
            public Transform T;
            public Vector3 Spin;
        }

        readonly Dictionary<int, Item> items = new Dictionary<int, Item>();
        readonly Dictionary<WarpHazard, Stack<GameObject>> pools = new Dictionary<WarpHazard, Stack<GameObject>>();
        readonly List<WarpObstacle> query = new List<WarpObstacle>();
        readonly List<int> drop = new List<int>();
        readonly HashSet<long> reported = new HashSet<long>();
        readonly List<GameObject> cloud = new List<GameObject>();
        Mesh[] rockMeshes, debrisMeshes;
        // asset rocks (#100): the moon rock scans with their own materials; null = the procedural rocks (owned meshes)
        Material[] rockMats;
        Mesh cometMesh, cometTail, wellCore, wellDisc, beaconMesh, puffMesh;
        GameObject beacon;
        ParticleSystem streaks;
        int shownSeed = int.MinValue;
        float streakBudget;
        int cloudSignal = -1;
        Material rockMat, toonMat, glowMat;
        Material streakMat; // #96: textured streak (Kenney) for comet tails, falls back to glowMat

        static readonly Color WellColor = new Color(1f, 0.62f, 0.3f);

        void OnDestroy()
        {
            ClearAll();
            foreach (var m in new[] { cometMesh, cometTail, wellCore, wellDisc, beaconMesh, puffMesh }) if (m) Destroy(m);
            if (rockMeshes != null && rockMats == null) foreach (var m in rockMeshes) if (m) Destroy(m);
            if (debrisMeshes != null) foreach (var m in debrisMeshes) if (m) Destroy(m);
            WarpBubble.Pull = null;
        }

        void ClearAll()
        {
            foreach (var it in items.Values) Release(it);
            items.Clear();
            foreach (var p in pools.Values) while (p.Count > 0) { var g = p.Pop(); if (g) Destroy(g); }
            ClearCloud();
            reported.Clear();
            if (beacon) beacon.SetActive(false);
            if (streaks) streaks.Clear();
        }

        // ================================================================ per frame

        void LateUpdate()
        {
            var f = Flight;
            var r = f != null && f.IsSpawned ? f.Route : null;
            if (r == null || !WarpBubble.Active)
            {
                if (shownSeed != int.MinValue) { ClearAll(); shownSeed = int.MinValue; WarpBubble.Pull = null; }
                // Entry spool-up out of a jump zone (#108): the speed lines build up along the lead's flight path.
                float e = JumpZones.Entering;
                if (e > 0f) { if (glowMat == null) EnsureAssets(); UpdateStreaks(JumpZones.EntryDir.normalized, Mathf.Lerp(400f, 1400f, e), Mathf.Sqrt(e), 120f, 1.4f, 3f); }
                else if (streaks) streaks.gameObject.SetActive(false);
                return;
            }
            if (shownSeed != f.Seed.Value) { ClearAll(); shownSeed = f.Seed.Value; EnsureAssets(); }

            float p = f.LocalProgress;
            float range = f.ScanRange;
            r.Query(p - 450f, p + range * 2f, query);
            drop.Clear();
            foreach (var kv in items) if (kv.Value.O.S < p - 450f || kv.Value.O.S > p + range * 2.1f || f.IsDestroyed(kv.Key)) drop.Add(kv.Key);
            foreach (int id in drop) { Release(items[id]); items.Remove(id); }

            foreach (var o in query)
            {
                float ahead = o.S - p;
                float seen = o.Kind == WarpHazard.GravityWell || o.Kind == WarpHazard.StrayRock ? range * 1.8f : range;
                if (ahead > seen || f.IsDestroyed(o.Id)) continue;
                if (!items.TryGetValue(o.Id, out var it)) { it = Create(o); items[o.Id] = it; }
                Place(it, ahead, seen);
            }

            WarpBubble.Pull = PullAt;
            DetectImpacts(f, r);
            UpdateBeacon(f, p);
            UpdateCloud(f, r);
            UpdateStreaks(WarpBubble.Axis, WarpBubble.Speed, WarpBubble.Intensity);
        }

        void Place(Item it, float ahead, float seen)
        {
            var o = it.O;
            Vector2 lat = o.LatAt(ahead);
            it.T.position = new Vector3(ahead, lat.x, lat.y);
            // Out of the warp haze: obstacles grow in over the outer quarter of the scanner range.
            bool big = o.Kind == WarpHazard.GravityWell;
            float fade = big ? 1f : Mathf.Clamp01((seen - ahead) / (seen * 0.25f));
            it.T.localScale = Vector3.one * o.Radius * Mathf.Max(0.02f, fade);
            if (it.Spin != Vector3.zero) it.T.Rotate(it.Spin * Time.deltaTime, Space.Self);
        }

        // ================================================================ impacts (ships this peer simulates)

        void DetectImpacts(WarpFlight f, WarpRoute r)
        {
            if (!WarpBubble.Flying) return;
            float p0 = f.LocalProgressPrev, p1 = f.LocalProgress;
            if (p1 <= p0 || p1 - p0 > 400f) return; // stopped or jumped (dev / slingshot)
            foreach (var ship in ShipCore.All)
            {
                if (ship == null || ship.Flight == null || !ship.Flight.IsSimulating || ship.Derelict.Value || ship.Docked) continue; // docked ships ride inside the home ship's hull
                Vector3 sp = ship.transform.position;
                float sr = ship.Flight.Radius;
                // Obstacle x = S - p: it sweeps from S - p0 to S - p1 this frame; hit if it passes the ship's x close enough.
                r.Query(p0 + sp.x - 120f, p1 + sp.x + 120f, query);
                foreach (var o in query)
                {
                    if (f.IsDestroyed(o.Id)) continue;
                    float x0 = o.S - p0 - sp.x, x1 = o.S - p1 - sp.x;
                    bool crossed = x0 >= 0f && x1 < 0f;
                    Vector2 lat = o.LatAt(o.S - p1);
                    float d = (lat - new Vector2(sp.y, sp.z)).magnitude;
                    bool slingshot = false, hit;
                    switch (o.Kind)
                    {
                        case WarpHazard.GravityWell:
                            hit = crossed && d < o.Radius + sr;
                            slingshot = crossed && !hit && d < o.Radius * 3f;
                            break;
                        default:
                            float reach = o.Radius + sr;
                            hit = x0 >= -reach && x1 <= reach && d < reach;
                            break;
                    }
                    if (!hit && !slingshot) continue;
                    long key = (long)o.Id << 20 ^ (long)ship.NetworkObjectId << 1 ^ (slingshot ? 1 : 0);
                    if (!reported.Add(key)) continue;
                    f.ReportImpactServerRpc(o.Id, ship.NetworkObjectId, slingshot);
                }
            }
        }

        /// <summary>Lateral drift (m/s) of gravity wells the bubble passes.</summary>
        Vector3 PullAt(Vector3 pos)
        {
            Vector3 v = Vector3.zero;
            foreach (var it in items.Values)
            {
                if (it.O.Kind != WarpHazard.GravityWell) continue;
                Vector3 wp = it.T.position;
                float reach = it.O.Reach;
                if (Mathf.Abs(wp.x - pos.x) > reach) continue;
                Vector3 lat = new Vector3(0f, wp.y - pos.y, wp.z - pos.z);
                float d = lat.magnitude;
                if (d > reach || d < 1f) continue;
                v += lat / d * 70f * (1f - d / reach) * (1f - Mathf.Abs(wp.x - pos.x) / reach);
            }
            return v;
        }

        // ================================================================ obstacles

        /// <summary>Every peer: an obstacle was shot apart or rammed.</summary>
        public void DestroyObstacle(int id, bool fx)
        {
            if (!items.TryGetValue(id, out var it)) return;
            Vector3 pos = it.T.position;
            float radius = it.O.Radius;
            Release(it);
            items.Remove(id);
            if (!fx) return;
            Vfx.Explosion(pos, Mathf.Clamp(radius * 1.2f, 2f, 20f), new Color(1f, 0.62f, 0.35f));
            Sfx.Play(radius > 8f ? "explosion_big" : "explosion", pos, 1f, Random.Range(0.8f, 1f));
        }

        Item Create(WarpObstacle o)
        {
            var it = new Item { O = o };
            var rng = new System.Random(o.Seed);
            GameObject go = null;
            if (pools.TryGetValue(o.Kind, out var pool) && pool.Count > 0) go = pool.Pop();
            if (go == null) go = Build(o, rng);
            else if (o.Kind == WarpHazard.Rock || o.Kind == WarpHazard.Debris || o.Kind == WarpHazard.StrayRock)
            {
                // Pooled rocks get a new shape variant.
                var meshes = o.Kind == WarpHazard.Debris ? debrisMeshes : rockMeshes;
                int v = rng.Next(meshes.Length);
                go.GetComponent<MeshFilter>().sharedMesh = meshes[v];
                if (o.Kind != WarpHazard.Debris && rockMats != null) go.GetComponent<MeshRenderer>().sharedMaterial = rockMats[v];
            }
            go.SetActive(true);
            it.Go = go;
            it.T = go.transform;
            var proxy = go.GetComponent<WarpObstacleProxy>();
            if (proxy != null) proxy.Id = o.Id;
            if (o.Kind == WarpHazard.Rock || o.Kind == WarpHazard.Debris || o.Kind == WarpHazard.StrayRock || o.Kind == WarpHazard.Comet)
            {
                it.T.rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                float spin = o.Kind == WarpHazard.Debris ? 90f : o.Kind == WarpHazard.Rock ? 25f : 6f;
                it.Spin = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * spin;
            }
            if (o.Kind == WarpHazard.Comet) { it.T.rotation = Quaternion.identity; it.Spin = Vector3.zero; } // the tail points back along the corridor
            return it;
        }

        void Release(Item it)
        {
            if (it.Go == null) return;
            it.Go.SetActive(false);
            if (!pools.TryGetValue(it.O.Kind, out var pool)) pools[it.O.Kind] = pool = new Stack<GameObject>();
            pool.Push(it.Go);
        }

        GameObject Build(WarpObstacle o, System.Random rng)
        {
            var go = new GameObject("Warp_" + o.Kind);
            go.transform.SetParent(transform, false);
            switch (o.Kind)
            {
                case WarpHazard.Rock:
                case WarpHazard.StrayRock:
                {
                    int v = rng.Next(rockMeshes.Length);
                    Renderer(go, rockMeshes[v], rockMats != null ? rockMats[v] : rockMat, true);
                }
                    Proxy(go, SurfaceKind.Rock);
                    break;
                case WarpHazard.Debris:
                    Renderer(go, debrisMeshes[rng.Next(debrisMeshes.Length)], toonMat, false);
                    Proxy(go, SurfaceKind.Metal);
                    break;
                case WarpHazard.Comet:
                {
                    var body = new GameObject("Body");
                    body.transform.SetParent(go.transform, false);
                    Renderer(body, cometMesh, toonMat, true);
                    var tail = new GameObject("Tail");
                    tail.transform.SetParent(go.transform, false);
                    Renderer(tail, cometTail, streakMat, false);
                    Proxy(go, SurfaceKind.Rock);
                    break;
                }
                case WarpHazard.GravityWell:
                {
                    var core = new GameObject("Core");
                    core.transform.SetParent(go.transform, false);
                    Renderer(core, wellCore, toonMat, false);
                    var disc = new GameObject("Disc");
                    disc.transform.SetParent(go.transform, false);
                    disc.transform.localRotation = Quaternion.Euler(0f, 90f, 25f);
                    Renderer(disc, wellDisc, glowMat, false);
                    break;
                }
            }
            return go;
        }

        static void Renderer(GameObject go, Mesh mesh, Material mat, bool shadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
        }

        static void Proxy(GameObject go, SurfaceKind kind)
        {
            var c = go.AddComponent<SphereCollider>();
            c.isTrigger = true;
            c.radius = 1.15f; // unit mesh, scaled by the obstacle radius; a bit generous for fast passes
            go.AddComponent<WarpObstacleProxy>().Kind = kind;
        }

        // ================================================================ assets

        void EnsureAssets()
        {
            var cat = ContentCatalog.Instance;
            if (cat == null) return;
            rockMat = cat.TerrainMaterial != null ? cat.TerrainMaterial : cat.ToonMaterial;
            toonMat = cat.ToonMaterial;
            glowMat = cat.ParticleMaterial;
            streakMat = cat.FxStreakMaterial != null ? cat.FxStreakMaterial : glowMat;
            if (rockMeshes != null) return;

            var lib = PlanetPropLibrary.Get(cat);
            var assetRocks = lib.FamilyModels("Rock");
            if (assetRocks.Length > 0)
            {
                rockMeshes = new Mesh[assetRocks.Length];
                rockMats = new Material[assetRocks.Length];
                for (int i = 0; i < assetRocks.Length; i++)
                {
                    rockMeshes[i] = lib.Models[assetRocks[i]].Mesh;
                    rockMats[i] = lib.Models[assetRocks[i]].Materials[0];
                }
            }
            else rockMeshes = new Mesh[6];
            for (int i = 0; i < rockMeshes.Length && rockMats == null; i++)
            {
                var mb = new MeshBuilder { Gloss = 0.05f, Panel = 1f };
                float shade = 0.82f + 0.07f * i;
                mb.AddRock(Matrix4x4.Scale(new Vector3(1f, 0.75f + 0.05f * i, 1f)), 1f, 7100 + i * 31, new Color(0.6f, 0.61f, 0.66f) * shade);
                rockMeshes[i] = mb.Build("WarpRock" + i);
            }
            debrisMeshes = new Mesh[3];
            for (int i = 0; i < debrisMeshes.Length; i++)
            {
                var mb = new MeshBuilder { Gloss = 0.4f };
                var hull = new Color(0.55f, 0.57f, 0.62f);
                var burnt = new Color(0.22f, 0.2f, 0.2f);
                mb.AddBox(Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 15f * i, 8f), Vector3.one), new Vector3(1.6f, 0.25f, 0.9f), hull);
                mb.AddWedge(Matrix4x4.TRS(new Vector3(0.4f, 0.3f, 0.1f), Quaternion.Euler(20f, 40f + 30f * i, 0f), Vector3.one), new Vector3(0.6f, 0.5f, 0.7f), burnt);
                if (i > 0) mb.AddBox(Matrix4x4.TRS(new Vector3(-0.5f, -0.2f, 0.2f), Quaternion.Euler(0f, 70f, 30f), Vector3.one), new Vector3(0.9f, 0.12f, 0.5f), new Color(0.85f, 0.55f, 0.2f));
                debrisMeshes[i] = mb.Build("WarpDebris" + i);
            }
            {
                var mb = new MeshBuilder { Gloss = 0.5f };
                mb.AddRock(Matrix4x4.identity, 1f, 9091, new Color(0.78f, 0.88f, 0.98f));
                cometMesh = mb.Build("WarpComet");
            }
            bool textured = streakMat != glowMat;
            // the textured streak is thin: wider quads and HDR vertex colour keep comet tails as bright as before
            float wide = textured ? 3f : 1f, hot = textured ? 3f : 1f;
            cometTail = GlowStreak("WarpCometTail", 14f, 1.8f * wide, new Color(0.55f * hot, 0.85f * hot, 1f * hot, 0.55f), textured);
            {
                var mb = new MeshBuilder { Gloss = 0.9f };
                mb.AddSphere(Matrix4x4.identity, 1f, 2, new Color(0.02f, 0.015f, 0.03f));
                wellCore = mb.Build("WarpWellCore");
            }
            wellDisc = Ring("WarpWellDisc", new[] { 1.25f, 2.2f, 4.2f }, new[] { 0.7f, 0.35f, 0f }, WellColor, 48);
            beaconMesh = GlowStreak("WarpBeacon", 1f, 1f, Color.white);
            puffMesh = GlowStreak("WarpPuff", 1f, 1f, Color.white);
        }

        /// <summary>
        /// Two crossed soft quads along +X (length in X from 0 to len; unit size = a round glow when len = width = 1).
        /// along: UV u runs along the streak (for the textured streak material, #96) instead of the soft-dot mapping.
        /// </summary>
        static Mesh GlowStreak(string name, float len, float width, Color col, bool along = false)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            bool round = Mathf.Approximately(len, 1f) && Mathf.Approximately(width, 1f);
            for (int k = 0; k < (round ? 3 : 2); k++)
            {
                Quaternion q = Quaternion.Euler(k * 90f, round ? k * 60f : 0f, 0f);
                int b = verts.Count;
                float x0 = round ? -1f : -width * 0.4f, x1 = round ? 1f : len;
                verts.Add(q * new Vector3(x0, -width, 0f)); verts.Add(q * new Vector3(x1, -width, 0f));
                verts.Add(q * new Vector3(x1, width, 0f)); verts.Add(q * new Vector3(x0, width, 0f));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                Color head = col, end = round ? col : new Color(col.r, col.g, col.b, 0f);
                cols.Add(head); cols.Add(end); cols.Add(end); cols.Add(head);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            if (!round && !along) for (int i = 0; i < uvs.Count; i++) uvs[i] = new Vector2(0.5f, uvs[i].y); // soft across, full along
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetColors(cols); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat additive ring in the YZ plane (faces the corridor): radii and alpha per ring step, soft by UV.</summary>
        static Mesh Ring(string name, float[] radii, float[] alpha, Color col, int segments)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int rings = radii.Length;
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                Vector3 dir = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                for (int k = 0; k < rings; k++)
                {
                    verts.Add(dir * radii[k]);
                    cols.Add(new Color(col.r, col.g, col.b, alpha[k]));
                    uvs.Add(new Vector2(0.5f, 0.5f + 0.5f * k / Mathf.Max(1, rings - 1) * 0.9f));
                }
            }
            for (int s = 0; s < segments; s++)
                for (int k = 0; k < rings - 1; k++)
                {
                    int a = s * rings + k, b = (s + 1) * rings + k;
                    tris.Add(a); tris.Add(b); tris.Add(b + 1); tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                    tris.Add(a); tris.Add(b + 1); tris.Add(b); tris.Add(a); tris.Add(a + 1); tris.Add(b + 1);
                }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetColors(cols); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ================================================================ signals, gas cloud, streaks

        void UpdateBeacon(WarpFlight f, float p)
        {
            bool show = f.NextSignal(out var s) && WarpBubble.Flying;
            if (!show) { if (beacon) beacon.SetActive(false); return; }
            if (beacon == null)
            {
                beacon = new GameObject("Warp_Signal");
                beacon.transform.SetParent(transform, false);
                Renderer(beacon, beaconMesh, glowMat, false);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(1f, 0.82f, 0.35f, 0.9f));
                beacon.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            }
            beacon.SetActive(true);
            float ahead = Mathf.Max(s.S - p, 60f);
            beacon.transform.position = new Vector3(ahead, s.Lat.x, s.Lat.y);
            beacon.transform.localScale = Vector3.one * (40f + 25f * Mathf.Sin(Time.time * 4f));
            var cam = Camera.main;
            if (cam != null) beacon.transform.rotation = Quaternion.LookRotation(cam.transform.position - beacon.transform.position);
        }

        void UpdateCloud(WarpFlight f, WarpRoute r)
        {
            int id = f.DroppedOut.Value ? f.DropSignal.Value : -1;
            WarpSignal sig = default;
            bool gas = false;
            if (id >= 0) foreach (var s in r.Signals) if (s.Id == id) { sig = s; gas = s.Kind == WarpSignalKind.GasCloud; }
            if (!gas) { if (cloudSignal != -1) ClearCloud(); return; }
            if (cloudSignal != id)
            {
                ClearCloud();
                cloudSignal = id;
                var rng = new System.Random(sig.Seed ^ 0x6a5);
                Vector3 c = WarpFlight.SitePosition(sig);
                for (int i = 0; i < 36; i++)
                {
                    var go = new GameObject("Warp_GasPuff");
                    go.transform.SetParent(transform, false);
                    go.transform.position = c + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 260f;
                    go.transform.localScale = Vector3.one * (40f + 70f * (float)rng.NextDouble());
                    go.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                    Renderer(go, puffMesh, glowMat, false);
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", new Color(0.62f, 0.4f, 1f, 0.16f + 0.1f * (float)rng.NextDouble()));
                    go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                    cloud.Add(go);
                }
            }
        }

        void ClearCloud()
        {
            foreach (var g in cloud) if (g) Destroy(g);
            cloud.Clear();
            cloudSignal = -1;
        }

        float streakScale = 1f;

        void UpdateStreaks(Vector3 dir, float speed, float intensity, float spread = 260f, float reach = 1f, float width = 1f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (streaks == null)
            {
                var go = new GameObject("Warp_Streaks");
                go.transform.SetParent(transform, false);
                streaks = go.AddComponent<ParticleSystem>();
                streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = streaks.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 1500;
                main.playOnAwake = false;
                main.startLifetime = 1.2f;
                var em = streaks.emission;
                em.enabled = false;
                var shape = streaks.shape;
                shape.enabled = false;
                var pr = go.GetComponent<ParticleSystemRenderer>();
                // #96: a thin textured streak (Kenney) instead of a stretched soft dot; same look without it
                var cat = ContentCatalog.Instance;
                bool textured = cat != null && cat.FxStreakMaterial != null;
                streakScale = textured ? 1.8f : 1f;
                pr.sharedMaterial = textured ? cat.FxStreakMaterial : glowMat;
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.velocityScale = textured ? 0.045f : 0.035f;
                pr.lengthScale = 1f;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                streaks.Play();
            }
            streaks.gameObject.SetActive(true);
            if (speed < 5f) return;
            var basis = Quaternion.LookRotation(dir);
            streakBudget += (120f + 520f * intensity) * Time.deltaTime;
            int n = Mathf.Min(40, Mathf.FloorToInt(streakBudget));
            streakBudget -= n;
            var cp = cam.transform.position;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector2 lat = Random.insideUnitCircle * spread;
                if (lat.sqrMagnitude < 400f) lat = lat.normalized * 20f;
                ep.position = cp + basis * new Vector3(lat.x, lat.y, Random.Range(250f, 900f) * reach);
                ep.velocity = -dir * speed * Random.Range(0.85f, 1.2f);
                ep.startSize = Random.Range(0.35f, 0.9f) * streakScale * width;
                ep.startLifetime = 1.6f;
                ep.startColor = Color.Lerp(new Color(0.6f, 0.85f, 1f, 0.5f), new Color(0.85f, 0.7f, 1f, 0.7f), Random.value) * (0.5f + 0.6f * intensity);
                streaks.Emit(ep, 1);
            }
        }
    }
}
