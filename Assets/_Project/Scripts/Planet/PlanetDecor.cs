using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Per-chunk surface props, generated on worker threads from the chunk's coordinates (deterministic on every
    /// peer). Props cluster through noise masks (forests, rock fields, crystal groves) and respect slope, sea level,
    /// regions and site clearings, so the surface reads as composed rather than uniformly sprinkled.
    /// </summary>
    public static class PlanetDecor
    {
        enum Prop { Rock, Boulder, Pebbles, Shrub, Conifer, Cactus, DryBush, Hoodoo, IceShard, IceSpike, Tree, Mushroom, Fern, Pod, Crystal, CrystalSpire, Basalt, Vent, EmberRock, DeadTree }

        struct Rule
        {
            public Prop P;
            public bool Large;
            public float Density;      // candidates per hectare
            public float SizeMin, SizeMax;
            public float ClusterScale; // meters
            public float ClusterCut;   // -1 = everywhere, 0.3 = sparse patches
            public float MaxSlope;
            public float Region;       // -1 prefers region 0, +1 prefers region 1
            public float Collider;     // 0 = none
        }

        static Rule R(Prop p, bool large, float density, float sMin, float sMax, float cluster, float cut, float slope = 0.35f, float region = 0f, float col = 0f) =>
            new Rule { P = p, Large = large, Density = density, SizeMin = sMin, SizeMax = sMax, ClusterScale = cluster, ClusterCut = cut, MaxSlope = slope, Region = region, Collider = col };

        static readonly Dictionary<PlanetDecorStyle, Rule[]> Styles = new Dictionary<PlanetDecorStyle, Rule[]>
        {
            [PlanetDecorStyle.Barren] = new[]
            {
                R(Prop.Rock, false, 70, 0.3f, 1.2f, 25, -0.3f, 0.6f),
                R(Prop.Pebbles, false, 40, 0.2f, 0.5f, 15, 0f, 0.5f),
                R(Prop.Boulder, true, 10, 2f, 6f, 60, 0.1f, 0.45f, 0f, 0.8f),
            },
            [PlanetDecorStyle.Rocky] = new[]
            {
                R(Prop.Rock, false, 45, 0.3f, 1.2f, 25, -0.2f, 0.6f),
                R(Prop.Shrub, false, 80, 0.4f, 1.1f, 30, 0f, 0.3f, 0.6f),
                R(Prop.Boulder, true, 6, 2f, 6f, 70, 0.15f, 0.45f, 0f, 0.8f),
                R(Prop.Conifer, true, 30, 5f, 11f, 90, 0.05f, 0.28f, 0.7f, 0.15f),
            },
            [PlanetDecorStyle.Desert] = new[]
            {
                R(Prop.Rock, false, 35, 0.3f, 1.3f, 30, 0f, 0.6f, -0.3f),
                R(Prop.DryBush, false, 35, 0.4f, 1f, 40, 0.1f, 0.3f, 0.4f),
                R(Prop.Pebbles, false, 25, 0.2f, 0.5f, 20, 0.1f, 0.5f),
                R(Prop.Cactus, true, 12, 2.5f, 6f, 70, 0.1f, 0.3f, 0.5f, 0.25f),
                R(Prop.Hoodoo, true, 5, 6f, 16f, 140, 0.25f, 0.35f, -0.5f, 0.45f),
                R(Prop.Boulder, true, 5, 2f, 5f, 60, 0.1f, 0.45f, -0.4f, 0.8f),
            },
            [PlanetDecorStyle.Ice] = new[]
            {
                R(Prop.IceShard, false, 60, 0.4f, 1.6f, 25, 0.05f, 0.6f, 0.4f),
                R(Prop.Rock, false, 25, 0.3f, 1.1f, 30, 0f, 0.6f, -0.5f),
                R(Prop.IceSpike, true, 12, 4f, 13f, 90, 0.1f, 0.45f, 0.3f, 0.35f),
                R(Prop.Boulder, true, 4, 2f, 5f, 60, 0.15f, 0.45f, -0.5f, 0.8f),
            },
            [PlanetDecorStyle.Jungle] = new[]
            {
                R(Prop.Fern, false, 160, 0.6f, 1.6f, 22, -0.25f, 0.4f),
                R(Prop.Pod, false, 28, 0.4f, 0.9f, 18, 0.25f, 0.35f, 0.5f),
                R(Prop.Rock, false, 15, 0.3f, 1f, 30, 0f, 0.6f, -0.5f),
                R(Prop.Tree, true, 38, 7f, 17f, 70, -0.15f, 0.32f, 0f, 0.12f),
                R(Prop.Mushroom, true, 9, 4f, 11f, 60, 0.2f, 0.3f, 0.6f, 0.3f),
            },
            [PlanetDecorStyle.Crystal] = new[]
            {
                R(Prop.Crystal, false, 100, 0.5f, 1.8f, 20, 0f, 0.6f, 0.3f),
                R(Prop.Rock, false, 30, 0.3f, 1.1f, 30, -0.1f, 0.6f, -0.4f),
                R(Prop.Pod, false, 12, 0.3f, 0.7f, 15, 0.3f, 0.4f, 0.6f),
                R(Prop.CrystalSpire, true, 16, 5f, 18f, 80, 0.05f, 0.45f, 0.3f, 0.35f),
                R(Prop.Boulder, true, 3, 2f, 5f, 60, 0.2f, 0.45f, -0.5f, 0.8f),
            },
            [PlanetDecorStyle.Volcanic] = new[]
            {
                R(Prop.EmberRock, false, 35, 0.3f, 1.1f, 25, 0.05f, 0.6f, 0.3f),
                R(Prop.Rock, false, 40, 0.3f, 1.2f, 30, -0.2f, 0.6f, -0.3f),
                R(Prop.Basalt, true, 10, 3f, 9f, 80, 0.1f, 0.4f, -0.3f, 0.45f),
                R(Prop.Vent, true, 2.5f, 2.5f, 5f, 90, 0.25f, 0.3f, 0.4f, 0.5f),
                R(Prop.DeadTree, true, 4, 3f, 7f, 70, 0.2f, 0.3f, 0.5f, 0.12f),
            },
        };

        /// <summary>Fills data.Decor / data.DecorColliders. Thread-safe.</summary>
        public static void Generate(PlanetTerrain terrain, ChunkData data, bool small, bool large)
        {
            var shape = terrain.Shape;
            if (!Styles.TryGetValue(shape.Decor, out var rules) || shape.DecorDensity <= 0f) return;
            float R = terrain.Radius;
            float size = R * Mathf.PI * 0.5f / (1 << data.Level);
            float hectares = size * size / 10000f;
            int seed = terrain.Seed ^ (data.Face * 73856093) ^ (data.Level * 19349663) ^ (data.X * 83492791) ^ (data.Y * 4256249);
            var rng = new System.Random(seed);
            var mb = new MeshBuilder { Gloss = 0.06f };
            List<Vector4> colliders = null;
            var cols = shape.DecorColors;
            Color C(int i) => cols != null && cols.Length > i ? terrain.Shift(cols[i]) : Color.gray;
            float cells = 1 << data.Level;
            float a0 = 2f * data.X / cells - 1f, b0 = 2f * data.Y / cells - 1f, span = 2f / cells;
            var sites = terrain.Sites;

            for (int ri = 0; ri < rules.Length; ri++)
            {
                var rule = rules[ri];
                if (rule.Large ? !large : !small) continue;
                int count = Mathf.RoundToInt(rule.Density * hectares * shape.DecorDensity * (float)(0.8 + rng.NextDouble() * 0.4));
                int clusterSeed = terrain.Seed * 31 + ri * 977;
                for (int k = 0; k < count; k++)
                {
                    float a = a0 + (float)rng.NextDouble() * span, b = b0 + (float)rng.NextDouble() * span;
                    float sizeT = (float)rng.NextDouble();
                    float yaw = (float)rng.NextDouble() * 360f;
                    int propSeed = rng.Next();
                    float accept = (float)rng.NextDouble();
                    Vector3 dir = PlanetMesh.CubeDir(data.Face, a, b);

                    // Cluster mask: patches of forest / rock fields instead of uniform sprinkling.
                    float cluster = MathUtils.ValueNoise3D(clusterSeed, dir * (R / rule.ClusterScale));
                    cluster += 0.5f * MathUtils.ValueNoise3D(clusterSeed + 7, dir * (R / rule.ClusterScale * 2.7f));
                    float p = Mathf.InverseLerp(rule.ClusterCut, rule.ClusterCut + 0.45f, cluster * 0.67f);
                    if (rule.Region != 0f)
                    {
                        float reg = terrain.RegionAt(dir);
                        p *= Mathf.Lerp(1f, rule.Region > 0f ? reg : 1f - reg, Mathf.Abs(rule.Region));
                    }
                    if (accept > p) continue;

                    float e = terrain.ElevationFrac(dir);
                    if (terrain.HasOcean && e < terrain.SeaElevationFrac + 0.6f / R) continue;
                    bool inClearing = false;
                    for (int s = 0; s < sites.Count; s++)
                    {
                        float clear = sites[s].RadiusMeters * (rule.Large ? 1.05f : 0.55f) / R;
                        if ((sites[s].Dir - dir).sqrMagnitude < clear * clear) { inClearing = true; break; }
                    }
                    if (inClearing) continue;
                    Vector3 normal = terrain.NormalAt(dir, rule.Large ? 2.5f : 1.2f);
                    if (1f - Vector3.Dot(normal, dir) > rule.MaxSlope) continue;

                    float sz = Mathf.Lerp(rule.SizeMin, rule.SizeMax, sizeT * sizeT);
                    Vector3 pos = dir * (R * (1f + e)) - data.Origin;
                    Vector3 up = Vector3.Slerp(dir, normal, rule.Large ? 0.25f : 0.6f);
                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
                    float shade = 0.88f + (propSeed & 255) / 255f * 0.24f;
                    int first = mb.VertexCount;
                    float col = Emit(mb, rule.P, pos, rot, up, sz, propSeed, shade, C(0), C(1), C(2), C(3));
                    // Contact shading: darker where the prop meets the ground, slightly sun-bleached at the top.
                    mb.Grade(first, pos, up, sz * (rule.Large ? 0.9f : 0.6f), 0.55f, 1.06f);
                    if (rule.Collider > 0f && col > 0f)
                    {
                        colliders ??= new List<Vector4>();
                        Vector3 c = pos + up * (col * 0.8f);
                        colliders.Add(new Vector4(c.x, c.y, c.z, col));
                    }
                }
            }
            if (mb.VertexCount > 0) data.Decor = mb;
            data.DecorColliders = colliders;
        }

        static Matrix4x4 M(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix4x4.TRS(pos, rot, scale);

        /// <summary>Adds one prop; returns its collider radius (0 = none).</summary>
        static float Emit(MeshBuilder mb, Prop prop, Vector3 pos, Quaternion rot, Vector3 up, float s, int seed, float shade,
            Color rock, Color flora, Color accent, Color glow)
        {
            var r = new System.Random(seed);
            float Rn(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Quaternion Tilt(float deg) => rot * Quaternion.Euler(Rn(-deg, deg), Rn(0f, 360f), Rn(-deg, deg));
            mb.Surface(0.06f, 0f);
            switch (prop)
            {
                case Prop.Rock:
                    mb.AddSphere(M(pos + up * s * 0.15f, Tilt(25f), new Vector3(Rn(0.8f, 1.3f), Rn(0.5f, 0.8f), Rn(0.8f, 1.2f))), s, 0, rock * shade);
                    return 0f;
                case Prop.Pebbles:
                    for (int i = 0; i < 3; i++)
                        mb.AddSphere(M(pos + rot * new Vector3(Rn(-1f, 1f), 0f, Rn(-1f, 1f)) * s * 2f, Tilt(30f), new Vector3(1f, 0.6f, 1f)), s * Rn(0.5f, 1f), 0, rock * shade * 0.95f);
                    return 0f;
                case Prop.Boulder:
                    mb.AddRock(M(pos + up * s * 0.25f, Tilt(20f), new Vector3(Rn(0.9f, 1.3f), Rn(0.55f, 0.85f), Rn(0.9f, 1.2f))), s, seed, rock * shade);
                    return s * 0.75f;
                case Prop.Shrub:
                    mb.AddSphere(M(pos + up * s * 0.3f, Tilt(10f), new Vector3(1.2f, 0.7f, 1.1f)), s, 0, flora * shade);
                    mb.AddSphere(M(pos + up * s * 0.6f + rot * Vector3.right * s * 0.4f, Tilt(10f), new Vector3(0.9f, 0.7f, 0.9f)), s * 0.7f, 0, flora * shade * 1.1f);
                    return 0f;
                case Prop.Conifer:
                {
                    float h = s;
                    mb.AddPrism(M(pos + up * h * 0.15f, rot, Vector3.one), h * 0.05f, h * 0.04f, h * 0.3f, 5, rock * 0.7f, false, false);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = h * (0.3f + i * 0.22f);
                        float rad = h * (0.28f - i * 0.07f);
                        mb.AddPrism(M(pos + up * (y + h * 0.15f), rot * Quaternion.Euler(0f, i * 25f, 0f), Vector3.one), rad, 0f, h * 0.38f, 6, flora * shade * (0.9f + i * 0.08f), true, false);
                    }
                    return h * 0.06f + 0.4f;
                }
                case Prop.Cactus:
                {
                    float h = s;
                    mb.AddPrism(M(pos + up * h * 0.5f, rot, Vector3.one), h * 0.11f, h * 0.09f, h, 6, flora * shade);
                    int arms = r.Next(1, 3);
                    for (int i = 0; i < arms; i++)
                    {
                        Quaternion side = rot * Quaternion.Euler(0f, i * 160f + Rn(-20f, 20f), 0f);
                        Vector3 outDir = side * Vector3.right;
                        float y = h * Rn(0.35f, 0.6f);
                        mb.AddPrism(M(pos + up * y + outDir * h * 0.16f, side * Quaternion.Euler(0f, 0f, 90f), Vector3.one), h * 0.07f, h * 0.07f, h * 0.22f, 6, flora * shade * 0.95f);
                        mb.AddPrism(M(pos + up * (y + h * 0.15f) + outDir * h * 0.27f, side, Vector3.one), h * 0.07f, h * 0.05f, h * 0.3f, 6, flora * shade);
                    }
                    mb.Surface(0.1f, 0.8f);
                    mb.AddSphere(M(pos + up * h * 1.02f, rot, Vector3.one), h * 0.06f, 0, accent);
                    return h * 0.12f + 0.2f;
                }
                case Prop.DryBush:
                    for (int i = 0; i < 5; i++)
                        mb.AddPrism(M(pos + up * s * 0.4f, Tilt(35f), Vector3.one), s * 0.06f, 0f, s * Rn(0.7f, 1.1f), 3, Color.Lerp(rock, accent, 0.4f) * shade);
                    return 0f;
                case Prop.Hoodoo:
                {
                    float h = s;
                    float y = 0f;
                    for (int i = 0; i < 4; i++)
                    {
                        float seg = h * Rn(0.2f, 0.3f);
                        float rad = h * (i % 2 == 0 ? 0.16f : 0.11f) * (1f - i * 0.08f);
                        Color c = Color.Lerp(rock, accent, i * 0.22f) * shade;
                        mb.AddPrism(M(pos + up * (y + seg * 0.5f), rot * Quaternion.Euler(0f, i * 17f, 0f), Vector3.one), rad * 1.05f, rad, seg, 7, c);
                        y += seg;
                    }
                    mb.AddRock(M(pos + up * (y + h * 0.05f), Tilt(8f), new Vector3(1.4f, 0.45f, 1.2f)), h * 0.17f, seed, rock * shade * 0.85f);
                    return h * 0.17f;
                }
                case Prop.IceShard:
                    mb.Surface(0.5f, 0f);
                    mb.AddPrism(M(pos + up * s * 0.6f, Tilt(22f), Vector3.one), s * 0.22f, s * 0.03f, s * 1.4f, 5, Color.Lerp(flora, Color.white, 0.25f) * shade);
                    return 0f;
                case Prop.IceSpike:
                {
                    mb.Surface(0.55f, 0f);
                    int n = r.Next(2, 5);
                    for (int i = 0; i < n; i++)
                    {
                        float h = s * Rn(0.6f, 1.2f);
                        Vector3 off = rot * new Vector3(Rn(-1f, 1f), 0f, Rn(-1f, 1f)) * s * 0.25f;
                        mb.AddPrism(M(pos + off + up * h * 0.45f, Tilt(18f), Vector3.one), h * 0.18f, h * 0.01f, h, 5, Color.Lerp(flora, accent, Rn(0f, 0.5f)) * shade);
                    }
                    return s * 0.3f;
                }
                case Prop.Tree:
                {
                    float h = s;
                    float lean = Rn(-8f, 8f);
                    Quaternion trunkRot = rot * Quaternion.Euler(lean, 0f, Rn(-8f, 8f));
                    Vector3 top = pos + trunkRot * Vector3.up * h;
                    mb.AddPrism(M(pos + trunkRot * Vector3.up * h * 0.5f, trunkRot, Vector3.one), h * 0.05f, h * 0.03f, h, 5, rock * 0.75f, false, false);
                    int blobs = r.Next(2, 4);
                    for (int i = 0; i < blobs; i++)
                    {
                        Vector3 off = trunkRot * new Vector3(Rn(-1f, 1f), Rn(-0.3f, 0.2f), Rn(-1f, 1f)) * h * 0.18f;
                        Color c = (i == 0 ? flora : Color.Lerp(flora, accent, Rn(0.2f, 0.6f))) * shade;
                        mb.AddSphere(M(top + off, Tilt(15f), new Vector3(Rn(1.2f, 1.6f), Rn(0.45f, 0.7f), Rn(1.1f, 1.5f))), h * Rn(0.2f, 0.28f), 0, c);
                    }
                    if (r.NextDouble() < 0.4)
                    {
                        mb.Surface(0.1f, 1f);
                        mb.AddSphere(M(top - trunkRot * Vector3.up * h * 0.12f + trunkRot * Vector3.right * h * 0.1f, rot, Vector3.one), h * 0.035f, 0, glow);
                    }
                    return h * 0.05f + 0.35f;
                }
                case Prop.Mushroom:
                {
                    float h = s;
                    mb.AddPrism(M(pos + up * h * 0.4f, Tilt(6f), Vector3.one), h * 0.09f, h * 0.06f, h * 0.8f, 6, Color.Lerp(Color.white, flora, 0.3f) * shade);
                    mb.AddPrism(M(pos + up * h * 0.86f, rot, Vector3.one), h * 0.45f, h * 0.12f, h * 0.24f, 8, accent * shade);
                    mb.Surface(0.1f, 1f);
                    mb.AddPrism(M(pos + up * h * 0.73f, rot, Vector3.one), h * 0.4f, h * 0.42f, h * 0.03f, 8, glow);
                    return h * 0.1f + 0.3f;
                }
                case Prop.Fern:
                {
                    int n = r.Next(4, 7);
                    for (int i = 0; i < n; i++)
                    {
                        Quaternion q = rot * Quaternion.Euler(0f, i * 360f / n + Rn(-15f, 15f), 0f) * Quaternion.Euler(0f, 0f, Rn(35f, 60f));
                        mb.AddPrism(M(pos + q * Vector3.up * s * 0.45f, q, new Vector3(1f, 1f, 0.35f)), s * 0.12f, 0f, s, 3, Color.Lerp(flora, accent, Rn(0f, 0.3f)) * shade);
                    }
                    return 0f;
                }
                case Prop.Pod:
                    mb.AddPrism(M(pos + up * s * 0.4f, Tilt(12f), Vector3.one), s * 0.06f, s * 0.04f, s * 0.8f, 4, flora * 0.8f);
                    mb.Surface(0.2f, 1f);
                    mb.AddSphere(M(pos + up * s * 0.9f, Tilt(10f), new Vector3(1f, 1.3f, 1f)), s * 0.22f, 0, glow);
                    return 0f;
                case Prop.Crystal:
                {
                    int n = r.Next(2, 5);
                    for (int i = 0; i < n; i++)
                    {
                        float h = s * Rn(0.6f, 1.4f);
                        Quaternion q = Tilt(28f);
                        Color c = Color.Lerp(flora, accent, Rn(0f, 0.6f));
                        mb.Surface(0.6f, 0.35f);
                        mb.AddPrism(M(pos + q * Vector3.up * h * 0.4f, q, Vector3.one), h * 0.16f, h * 0.16f, h * 0.8f, 6, c, false, false);
                        mb.AddPrism(M(pos + q * Vector3.up * h * 0.92f, q, Vector3.one), h * 0.16f, 0f, h * 0.24f, 6, c);
                    }
                    return 0f;
                }
                case Prop.CrystalSpire:
                {
                    int n = r.Next(1, 4);
                    for (int i = 0; i < n; i++)
                    {
                        float h = s * (i == 0 ? 1f : Rn(0.35f, 0.6f));
                        Quaternion q = i == 0 ? Tilt(10f) : Tilt(35f);
                        Vector3 off = i == 0 ? Vector3.zero : rot * new Vector3(Rn(-1f, 1f), 0f, Rn(-1f, 1f)) * s * 0.2f;
                        Color c = Color.Lerp(flora, accent, i * 0.3f);
                        mb.Surface(0.7f, 0.5f);
                        mb.AddPrism(M(pos + off + q * Vector3.up * h * 0.38f, q, Vector3.one), h * 0.13f, h * 0.15f, h * 0.76f, 6, c, false, false);
                        mb.AddPrism(M(pos + off + q * Vector3.up * h * 0.88f, q, Vector3.one), h * 0.15f, 0f, h * 0.24f, 6, c);
                    }
                    return s * 0.15f;
                }
                case Prop.Basalt:
                {
                    int n = r.Next(4, 9);
                    for (int i = 0; i < n; i++)
                    {
                        float h = s * Rn(0.4f, 1f);
                        float w = s * 0.14f;
                        Vector3 off = rot * new Vector3(Rn(-1f, 1f), 0f, Rn(-1f, 1f)) * s * 0.28f;
                        mb.AddPrism(M(pos + off + up * h * 0.5f, rot * Quaternion.Euler(0f, Rn(0f, 60f), 0f), Vector3.one), w, w, h, 6, rock * Rn(0.75f, 1.05f));
                    }
                    return s * 0.35f;
                }
                case Prop.Vent:
                    mb.AddPrism(M(pos + up * s * 0.3f, rot, Vector3.one), s * 0.6f, s * 0.22f, s * 0.6f, 7, rock * shade);
                    mb.Surface(0.2f, 1.6f);
                    mb.AddPrism(M(pos + up * s * 0.61f, rot, Vector3.one), s * 0.19f, s * 0.12f, s * 0.04f, 7, glow);
                    return s * 0.45f;
                case Prop.EmberRock:
                    mb.AddSphere(M(pos + up * s * 0.2f, Tilt(25f), new Vector3(1.1f, 0.7f, 1f)), s, 0, rock * shade * 0.7f);
                    mb.Surface(0.2f, 1.4f);
                    mb.AddSphere(M(pos + up * s * 0.05f + rot * Vector3.forward * s * 0.6f, Tilt(30f), new Vector3(1f, 0.3f, 0.6f)), s * 0.4f, 0, glow);
                    return 0f;
                case Prop.DeadTree:
                {
                    float h = s;
                    Quaternion q = Tilt(10f);
                    mb.AddPrism(M(pos + q * Vector3.up * h * 0.5f, q, Vector3.one), h * 0.06f, h * 0.02f, h, 5, rock * 0.6f, false, true);
                    for (int i = 0; i < 2; i++)
                    {
                        Quaternion b = q * Quaternion.Euler(0f, i * 150f + Rn(0f, 40f), Rn(40f, 65f));
                        mb.AddPrism(M(pos + q * Vector3.up * h * (0.5f + i * 0.15f) + b * Vector3.up * h * 0.15f, b, Vector3.one), h * 0.025f, h * 0.008f, h * 0.35f, 4, rock * 0.55f, false, true);
                    }
                    return h * 0.06f + 0.25f;
                }
            }
            return 0f;
        }
    }
}
