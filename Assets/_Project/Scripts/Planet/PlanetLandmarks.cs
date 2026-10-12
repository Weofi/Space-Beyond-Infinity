using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Large, memorable set pieces (arches, giant crystals, hoodoos, giant trees, basalt organs, alien monolith rings,
    /// floating rocks, giant bones). Some mark site clearings so points of interest are visible from the air, the rest
    /// are free-standing discoveries. Deterministic per seed; static colliders on every peer.
    /// </summary>
    public sealed class PlanetLandmarks
    {
        public struct Info
        {
            public Vector3 LocalPos;
            public string Name;
            public float Height;
            /// <summary>True for set pieces marking a site clearing (the site itself carries the label).</summary>
            public bool AtSite;
        }

        enum Kind { Arch, Monolith, Hoodoos, Bones, IcePillars, GiantTree, Mushrooms, CrystalTower, FloatingRocks, BasaltOrgan, ObsidianSpires, BoulderPile }

        readonly PlanetVisuals owner;
        readonly PlanetTerrain terrain;
        readonly Material mat;
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<Mesh> meshes = new List<Mesh>();
        public readonly List<Info> Landmarks = new List<Info>();

        public PlanetLandmarks(PlanetVisuals owner, PlanetTerrain terrain, Material mat)
        {
            this.owner = owner;
            this.terrain = terrain;
            this.mat = mat;
        }

        Color Col(int i)
        {
            var c = terrain.Shape.DecorColors;
            return c != null && c.Length > i ? terrain.Shift(c[i]) : Color.gray;
        }

        Kind[] Palette(PlanetDecorStyle s) => s switch
        {
            PlanetDecorStyle.Desert => new[] { Kind.Arch, Kind.Hoodoos, Kind.Bones, Kind.Hoodoos, Kind.Monolith },
            PlanetDecorStyle.Ice => new[] { Kind.IcePillars, Kind.Arch, Kind.IcePillars, Kind.Bones, Kind.Monolith },
            PlanetDecorStyle.Jungle => new[] { Kind.GiantTree, Kind.Mushrooms, Kind.GiantTree, Kind.FloatingRocks, Kind.Monolith },
            PlanetDecorStyle.Crystal => new[] { Kind.CrystalTower, Kind.FloatingRocks, Kind.CrystalTower, Kind.Arch, Kind.Monolith },
            PlanetDecorStyle.Volcanic => new[] { Kind.BasaltOrgan, Kind.ObsidianSpires, Kind.BasaltOrgan, Kind.Bones, Kind.Monolith },
            PlanetDecorStyle.Rocky => new[] { Kind.Arch, Kind.BoulderPile, Kind.Monolith, Kind.FloatingRocks },
            _ => new[] { Kind.BoulderPile, Kind.Arch, Kind.Monolith },
        };

        public void Build()
        {
            var rng = new System.Random(terrain.Seed ^ 0x51ab1e);
            var kinds = Palette(terrain.Shape.Decor);
            float R = terrain.Radius;
            // Scale set pieces with the planet so a menu-sized planet still reads right.
            float scale = Mathf.Clamp(R / 1500f, 0.08f, 1.4f);
            var sites = terrain.Sites;

            // Site markers: each clearing gets a set piece on its rim so it is visible from the air.
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                Vector3 t = Tangent(s.Dir);
                Vector3 side = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, s.Dir) * t;
                Vector3 dir = (s.Dir + side * (s.RadiusMeters * 1.35f / R)).normalized;
                var kind = kinds[(i + rng.Next(0, kinds.Length)) % kinds.Length];
                Place(kind, dir, side, scale * 0.6f, rng, true);
            }

            // Free-standing discoveries away from the sites.
            int extra = Mathf.RoundToInt(Mathf.Lerp(3f, 9f, Mathf.Clamp01(R / 2000f)));
            for (int k = 0; k < extra; k++)
            {
                for (int tries = 0; tries < 25; tries++)
                {
                    Vector3 dir = MathUtils.OnSphere(rng);
                    if (terrain.IsUnderwater(dir) || terrain.SlopeAt(dir, 6f) > 0.3f) continue;
                    bool clear = true;
                    for (int i = 0; i < sites.Count && clear; i++)
                        if ((sites[i].Dir - dir).magnitude * R < sites[i].RadiusMeters * 3f) clear = false;
                    foreach (var l in Landmarks)
                        if ((l.LocalPos.normalized - dir).magnitude * R < 150f * scale) { clear = false; break; }
                    if (!clear) continue;
                    var kind = kinds[rng.Next(0, kinds.Length)];
                    Place(kind, dir, Tangent(dir), scale * MathUtils.Range(rng, 0.9f, 1.4f), rng, false);
                    break;
                }
            }
        }

        static Vector3 Tangent(Vector3 d) => Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

        void Place(Kind kind, Vector3 dir, Vector3 forward, float scale, System.Random rng, bool atSite)
        {
            if (terrain.IsUnderwater(dir)) return;
            float ground = terrain.SurfaceRadius(dir);
            Vector3 basePos = dir * ground;
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, dir).normalized, dir);
            var mb = new MeshBuilder { Gloss = 0.08f };
            float sink = 1.5f + terrain.SlopeAt(dir, 8f) * 20f * scale;
            Vector3 origin = basePos - dir * sink;
            string label;
            float height = Emit(mb, kind, rot, scale, rng.Next(), out label);
            if (mb.VertexCount == 0) return;
            // Ground the set piece: darker at the (sunken) base, slightly lighter towards the top.
            mb.Grade(0, dir * sink * 0.5f, dir, Mathf.Max(height * 0.5f, 4f), 0.6f, 1.05f);

            var mesh = mb.Build("Landmark_" + kind);
            mesh.hideFlags = HideFlags.DontSave;
            meshes.Add(mesh);
            var go = new GameObject("Landmark_" + kind) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            objects.Add(go);
            Landmarks.Add(new Info { LocalPos = basePos + dir * height * 0.6f, Name = label, Height = height, AtSite = atSite });
        }

        static Matrix4x4 M(Vector3 p, Quaternion q, Vector3 s) => Matrix4x4.TRS(p, q, s);

        /// <summary>Builds the set piece around the local origin (up = rot * up). Returns its height.</summary>
        float Emit(MeshBuilder mb, Kind kind, Quaternion rot, float k, int seed, out string label)
        {
            var r = new System.Random(seed);
            float Rn(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Vector3 up = rot * Vector3.up, fwd = rot * Vector3.forward, right = rot * Vector3.right;
            Color rock = Col(0), body = Col(1), accent = Col(2), glow = Col(3);
            switch (kind)
            {
                case Kind.Arch:
                {
                    label = "Stone Arch";
                    bool ice = terrain.Shape.Decor == PlanetDecorStyle.Ice || terrain.Shape.Decor == PlanetDecorStyle.Crystal;
                    float span = Rn(28f, 55f) * k, rise = span * Rn(0.55f, 0.85f), thick = span * Rn(0.12f, 0.18f);
                    mb.Bevel = 0.2f;
                    if (ice) mb.Surface(0.5f, ice && terrain.Shape.Decor == PlanetDecorStyle.Crystal ? 0.3f : 0f);
                    const int segs = 11;
                    for (int i = 0; i < segs; i++)
                    {
                        float a0 = Mathf.PI * i / segs, a1 = Mathf.PI * (i + 1) / segs, am = (a0 + a1) * 0.5f;
                        Vector3 p0 = right * Mathf.Cos(a0) * span * 0.5f + up * Mathf.Sin(a0) * rise;
                        Vector3 p1 = right * Mathf.Cos(a1) * span * 0.5f + up * Mathf.Sin(a1) * rise;
                        Vector3 mid = (p0 + p1) * 0.5f;
                        float w = thick * (1f + 0.5f * (1f - Mathf.Sin(am))); // thicker legs
                        Quaternion q = Quaternion.LookRotation(fwd, (p1 - p0).normalized);
                        Color c = Color.Lerp(ice ? body : rock, accent, Mathf.Sin(am) * 0.35f) * Rn(0.92f, 1.06f);
                        mb.AddBox(M(mid, q, Vector3.one), new Vector3(w, (p1 - p0).magnitude * 1.15f, w * Rn(0.8f, 1.1f)), c);
                    }
                    mb.Bevel = 0f;
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 foot = right * (i == 0 ? -1f : 1f) * span * 0.5f;
                        mb.AddRock(M(foot, rot, new Vector3(1.3f, 0.6f, 1.3f)), thick * 1.3f, seed + i, rock * 0.9f);
                    }
                    return rise;
                }
                case Kind.Monolith:
                {
                    label = "Alien Monoliths";
                    int n = r.Next(5, 8);
                    float ring = Rn(12f, 20f) * k;
                    float h = Rn(14f, 24f) * k;
                    Color slab = Color.Lerp(new Color(0.12f, 0.12f, 0.18f), rock, 0.25f);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        Vector3 p = (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * ring;
                        Quaternion q = Quaternion.LookRotation(-p.normalized, up) * Quaternion.Euler(Rn(-6f, 6f), 0f, Rn(-6f, 6f));
                        float hh = h * Rn(0.75f, 1.15f);
                        mb.Bevel = 0.15f;
                        mb.Surface(0.35f, 0f);
                        mb.AddBox(M(p + up * hh * 0.45f, q, Vector3.one), new Vector3(hh * 0.28f, hh, hh * 0.12f), slab);
                        mb.Bevel = 0f;
                        mb.Surface(0.2f, 2f);
                        mb.AddBox(M(p + up * hh * 0.55f + q * Vector3.forward * hh * 0.061f, q, Vector3.one), new Vector3(hh * 0.05f, hh * 0.6f, 0.05f * k + 0.05f), glow);
                    }
                    mb.Surface(0.2f, 1.2f);
                    mb.AddPrism(M(up * 0.6f, rot, Vector3.one), ring * 0.18f, ring * 0.12f, 1.2f, 8, glow);
                    mb.Surface(0.08f, 0f);
                    return h;
                }
                case Kind.Hoodoos:
                {
                    label = "Hoodoo Spires";
                    int n = r.Next(3, 7);
                    float tallest = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float h = Rn(22f, 55f) * k;
                        tallest = Mathf.Max(tallest, h);
                        Vector3 p = (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 22f * k;
                        float y = 0f;
                        int segs = r.Next(4, 7);
                        for (int sgi = 0; sgi < segs; sgi++)
                        {
                            float seg = h / segs;
                            float rad = h * (sgi % 2 == 0 ? 0.13f : 0.095f) * (1f - sgi * 0.06f);
                            Color c = Color.Lerp(rock, accent, (float)sgi / segs) * Rn(0.9f, 1.05f);
                            mb.AddPrism(M(p + up * (y + seg * 0.5f), rot * Quaternion.Euler(0f, sgi * 23f, 0f), Vector3.one), rad * 1.08f, rad, seg, 8, c);
                            y += seg;
                        }
                        mb.AddRock(M(p + up * (y + h * 0.04f), rot, new Vector3(1.5f, 0.42f, 1.3f)), h * 0.14f, seed + i, rock * 0.85f);
                    }
                    return tallest;
                }
                case Kind.Bones:
                {
                    label = "Colossal Remains";
                    float len = Rn(45f, 80f) * k;
                    Color bone = new Color(0.93f, 0.9f, 0.82f);
                    mb.Surface(0.15f, 0f);
                    for (int i = 0; i < 9; i++)
                    {
                        float z = (i / 8f - 0.5f) * len;
                        float h = len * 0.32f * Mathf.Sin(Mathf.PI * (i + 1) / 10f);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            for (int j = 0; j < 4; j++)
                            {
                                float a0 = j / 4f * Mathf.PI * 0.5f, a1 = (j + 1) / 4f * Mathf.PI * 0.5f;
                                Vector3 p0 = fwd * z + right * s * Mathf.Sin(a0) * h * 0.8f + up * Mathf.Cos(a0) * h;
                                Vector3 p1 = fwd * z + right * s * Mathf.Sin(a1) * h * 0.8f + up * Mathf.Cos(a1) * h;
                                Quaternion q = Quaternion.LookRotation(fwd, (p1 - p0).normalized);
                                mb.AddPrism(M((p0 + p1) * 0.5f, q, Vector3.one), len * 0.018f, len * 0.015f, (p1 - p0).magnitude * 1.1f, 5, bone);
                            }
                        }
                        mb.AddSphere(M(fwd * z + up * h, rot, Vector3.one), len * 0.03f, 0, bone);
                    }
                    mb.AddPrism(M(up * len * 0.3f, Quaternion.LookRotation(up, fwd), Vector3.one), len * 0.03f, len * 0.025f, len * 1.05f, 6, bone);
                    mb.AddRock(M(fwd * len * 0.62f + up * len * 0.12f, rot, new Vector3(0.8f, 0.7f, 1.3f)), len * 0.11f, seed, bone * 0.95f);
                    return len * 0.32f;
                }
                case Kind.IcePillars:
                {
                    label = "Frozen Spires";
                    int n = r.Next(4, 8);
                    float tallest = 0f;
                    mb.Surface(0.6f, 0f);
                    for (int i = 0; i < n; i++)
                    {
                        float h = Rn(25f, 75f) * k * (i == 0 ? 1.3f : 1f);
                        tallest = Mathf.Max(tallest, h);
                        Vector3 p = i == 0 ? Vector3.zero : (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 18f * k;
                        Quaternion q = rot * Quaternion.Euler(Rn(-14f, 14f), Rn(0f, 360f), Rn(-14f, 14f));
                        Color c = Color.Lerp(body, accent, Rn(0f, 0.5f));
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.42f, q, Vector3.one), h * 0.13f, h * 0.06f, h * 0.84f, 6, c, false, false);
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.92f, q, Vector3.one), h * 0.06f, 0f, h * 0.16f, 6, c);
                    }
                    return tallest;
                }
                case Kind.GiantTree:
                {
                    label = "Elder Tree";
                    float h = Rn(55f, 90f) * k;
                    float trunk = h * 0.07f;
                    mb.AddPrism(M(up * h * 0.5f, rot, Vector3.one), trunk * 1.4f, trunk * 0.7f, h, 8, rock * 0.7f, false, false);
                    for (int i = 0; i < 5; i++)
                    {
                        Quaternion q = rot * Quaternion.Euler(0f, i * 72f + Rn(-10f, 10f), Rn(55f, 70f));
                        mb.AddPrism(M(q * Vector3.up * trunk * 1.6f + up * trunk * 0.6f, q, Vector3.one), trunk * 0.5f, trunk * 0.2f, trunk * 4f, 5, rock * 0.65f, false, true);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 off = (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * h * 0.22f + up * h * Rn(0.85f, 1.05f);
                        mb.AddSphere(M(off, rot * Quaternion.Euler(0f, Rn(0f, 360f), 0f), new Vector3(Rn(1.3f, 1.7f), Rn(0.4f, 0.55f), Rn(1.2f, 1.6f))), h * Rn(0.18f, 0.26f), 1, Color.Lerp(body, accent, Rn(0f, 0.5f)));
                    }
                    mb.Surface(0.1f, 1.5f);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 off = (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * h * 0.25f + up * h * Rn(0.7f, 0.85f);
                        mb.AddPrism(M(off - up * h * 0.06f, rot, Vector3.one), h * 0.004f, h * 0.004f, h * 0.12f, 3, glow * 0.8f);
                        mb.AddSphere(M(off - up * h * 0.13f, rot, Vector3.one), h * 0.018f, 0, glow);
                    }
                    mb.Surface(0.08f, 0f);
                    return h;
                }
                case Kind.Mushrooms:
                {
                    label = "Giant Fungi";
                    int n = r.Next(3, 6);
                    float tallest = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float h = Rn(16f, 40f) * k;
                        tallest = Mathf.Max(tallest, h);
                        Vector3 p = i == 0 ? Vector3.zero : (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 20f * k;
                        Quaternion q = rot * Quaternion.Euler(Rn(-10f, 10f), 0f, Rn(-10f, 10f));
                        mb.Surface(0.08f, 0f);
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.45f, q, Vector3.one), h * 0.08f, h * 0.05f, h * 0.9f, 7, Color.Lerp(Color.white, body, 0.3f));
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.95f, q, Vector3.one), h * 0.42f, h * 0.1f, h * 0.22f, 10, accent);
                        mb.Surface(0.1f, 1.4f);
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.83f, q, Vector3.one), h * 0.38f, h * 0.4f, h * 0.02f, 10, glow);
                    }
                    mb.Surface(0.08f, 0f);
                    return tallest;
                }
                case Kind.CrystalTower:
                {
                    label = "Crystal Colossus";
                    int n = r.Next(4, 8);
                    float tallest = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float h = Rn(30f, 110f) * k * (i == 0 ? 1.25f : 0.7f);
                        tallest = Mathf.Max(tallest, h);
                        Vector3 p = i == 0 ? Vector3.zero : (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * h * 0.25f;
                        Quaternion q = rot * Quaternion.Euler(Rn(-22f, 22f), Rn(0f, 360f), Rn(-22f, 22f));
                        Color c = Color.Lerp(body, accent, Rn(0f, 0.6f));
                        mb.Surface(0.75f, i == 0 ? 0.7f : 0.45f);
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.4f, q, Vector3.one), h * 0.1f, h * 0.12f, h * 0.8f, 6, c, false, false);
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.9f, q, Vector3.one), h * 0.12f, 0f, h * 0.2f, 6, c);
                    }
                    mb.Surface(0.08f, 0f);
                    return tallest;
                }
                case Kind.FloatingRocks:
                {
                    label = "Floating Isles";
                    int n = r.Next(3, 6);
                    float top = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float s = Rn(5f, 13f) * k;
                        float y = Rn(18f, 45f) * k;
                        top = Mathf.Max(top, y + s);
                        Vector3 p = (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 22f * k + up * y;
                        mb.Surface(0.08f, 0f);
                        mb.AddRock(M(p, rot * Quaternion.Euler(0f, Rn(0f, 360f), 180f), new Vector3(1.3f, 1.4f, 1.2f)), s, seed + i, rock);
                        mb.AddSphere(M(p + up * s * 0.35f, rot, new Vector3(1.3f, 0.35f, 1.2f)), s * 0.95f, 0, body);
                        mb.Surface(0.2f, 1.6f);
                        mb.AddSphere(M(p - up * s * 1.05f, rot, Vector3.one), s * 0.18f, 0, glow);
                    }
                    mb.Surface(0.08f, 0f);
                    return top;
                }
                case Kind.BasaltOrgan:
                {
                    label = "Basalt Organ";
                    int n = r.Next(14, 26);
                    float tallest = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float a = Rn(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rn(0f, 1f)) * 16f * k;
                        float h = Rn(10f, 42f) * k * (1f - d / (20f * k) * 0.5f);
                        tallest = Mathf.Max(tallest, h);
                        float w = 2.6f * k + 0.3f;
                        Vector3 p = (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * d;
                        mb.AddPrism(M(p + up * h * 0.5f, rot * Quaternion.Euler(0f, Rn(0f, 60f), 0f), Vector3.one), w, w, h, 6, rock * Rn(0.7f, 1f));
                    }
                    mb.Surface(0.2f, 1.8f);
                    mb.AddPrism(M(up * 0.4f, rot, Vector3.one), 5f * k, 4f * k, 0.8f, 9, glow);
                    mb.Surface(0.08f, 0f);
                    return tallest;
                }
                case Kind.ObsidianSpires:
                {
                    label = "Obsidian Spires";
                    int n = r.Next(3, 6);
                    float tallest = 0f;
                    mb.Surface(0.8f, 0f);
                    for (int i = 0; i < n; i++)
                    {
                        float h = Rn(25f, 60f) * k;
                        tallest = Mathf.Max(tallest, h);
                        Vector3 p = i == 0 ? Vector3.zero : (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 16f * k;
                        Quaternion q = rot * Quaternion.Euler(Rn(-12f, 12f), Rn(0f, 360f), Rn(-12f, 12f));
                        mb.AddPrism(M(p + q * Vector3.up * h * 0.5f, q, Vector3.one), h * 0.14f, h * 0.01f, h, 5, new Color(0.1f, 0.08f, 0.14f));
                    }
                    mb.Surface(0.2f, 1.8f);
                    mb.AddSphere(M(up * 1f, rot, new Vector3(1.5f, 0.3f, 1.5f)), 6f * k, 0, glow);
                    mb.Surface(0.08f, 0f);
                    return tallest;
                }
                default:
                {
                    label = "Boulder Field";
                    int n = r.Next(5, 9);
                    float top = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float s = Rn(5f, 14f) * k;
                        Vector3 p = (right * Rn(-1f, 1f) + fwd * Rn(-1f, 1f)) * 18f * k + up * s * Rn(0.2f, 0.9f);
                        top = Mathf.Max(top, p.magnitude + s);
                        mb.AddRock(M(p, rot * Quaternion.Euler(Rn(0f, 360f), Rn(0f, 360f), 0f), new Vector3(1f, Rn(0.7f, 1f), 1f)), s, seed + i, rock * Rn(0.85f, 1.1f));
                    }
                    return top;
                }
            }
        }

        public void Clear()
        {
            foreach (var g in objects) Kill(g);
            foreach (var m in meshes) Kill(m);
            objects.Clear();
            meshes.Clear();
            Landmarks.Clear();
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
