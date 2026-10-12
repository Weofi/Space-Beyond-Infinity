using System.Collections.Generic;
using UnityEngine;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Deterministic terrain height field for one planet (seed + radius + shape). Single source of truth for
    /// meshes, collider, decor placement and gameplay surface queries. Construct once per planet and reuse.
    /// </summary>
    public sealed class PlanetTerrain
    {
        public readonly PlanetShapeDef Shape;
        public readonly int Seed;
        public readonly float Radius;

        public bool HasOcean { get; private set; }
        /// <summary>Ocean surface radius (local units). Equals the lowest terrain radius when there is no ocean.</summary>
        public float SeaRadius => Radius * (1f + seaElevation);
        /// <summary>Fraction of the surface below sea level.</summary>
        public float OceanCoverage { get; private set; }
        public float MinRadius => Radius * (1f + minElevation);
        public float MaxRadius => Radius * (1f + maxElevation);
        public float HueShift { get; private set; }

        struct Layer
        {
            public PlanetShapeDef.NoiseLayer Def;
            public Noise3D Noise;
            public Vector3 Offset;
        }

        struct Crater
        {
            public Vector3 Center;
            public float Size, Depth, Floor, Steepness, RimSteepness, RimWidth, Peak, MaxChord;
        }

        const int GridSize = 10;
        const int CoverageSamples = 3000;
        const float CraterSmoothing = 0.06f;
        const float ShelfWidth = 0.008f;

        readonly Layer[] layers;
        readonly Noise3D warpNoise, rimNoise;
        readonly Crater[] craters;
        readonly int[][] craterGrid;
        readonly float heightMul;
        float minElevation, maxElevation, seaElevation;
        float oceanDeepen = 1f; // stays 1 while sampling raw elevations for the sea level

        public PlanetTerrain(PlanetShapeDef shape, int seed, float radius)
        {
            Shape = shape;
            Seed = seed;
            Radius = radius;
            heightMul = shape.HeightMultiplier;
            var rng = new System.Random(seed);

            var list = new List<Layer>();
            for (int i = 0; i < shape.Layers.Count; i++)
            {
                var def = shape.Layers[i];
                if (def == null || !def.Enabled) continue;
                list.Add(new Layer
                {
                    Def = def,
                    Noise = new Noise3D(rng.Next()),
                    Offset = new Vector3(Range(rng, -100f, 100f), Range(rng, -100f, 100f), Range(rng, -100f, 100f)),
                });
            }
            layers = list.ToArray();
            warpNoise = new Noise3D(rng.Next());
            rimNoise = new Noise3D(rng.Next());

            craters = BuildCraters(shape, rng);
            craterGrid = BuildGrid(craters);

            HueShift = Range(rng, -1f, 1f) * shape.HueJitter;
            OceanCoverage = shape.HasOcean ? Mathf.Clamp01(Range(rng, shape.OceanCoverage.x, shape.OceanCoverage.y)) : 0f;
            AnalyzeElevations();
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ---------------------------------------------------------------- queries

        /// <summary>Terrain elevation above the base radius in local units.</summary>
        public float ElevationAt(Vector3 unitDir) => ElevationFrac(unitDir) * Radius;

        /// <summary>Distance from the planet center to the terrain surface along a unit direction.</summary>
        public float SurfaceRadius(Vector3 unitDir) => Radius * (1f + ElevationFrac(unitDir));

        public bool IsUnderwater(Vector3 unitDir) => HasOcean && ElevationFrac(unitDir) < seaElevation;

        /// <summary>Color ramp coordinate: 0..0.5 = seabed to sea level, 0.5..1 = shore to highest peak.</summary>
        public float GradientCoord(float elevationFrac)
        {
            if (HasOcean && elevationFrac < seaElevation)
                return 0.5f * Mathf.InverseLerp(minElevation, seaElevation, elevationFrac);
            return 0.5f + 0.5f * Mathf.InverseLerp(seaElevation, maxElevation, elevationFrac);
        }

        /// <summary>Elevation above the base radius as a fraction of the radius.</summary>
        public float ElevationFrac(Vector3 d)
        {
            Vector3 p = d;
            float warp = Shape.Warp;
            if (warp > 0f)
            {
                p.x += warpNoise.Eval(d.x * 2.1f + 11.3f, d.y * 2.1f, d.z * 2.1f) * warp;
                p.y += warpNoise.Eval(d.x * 2.1f, d.y * 2.1f + 37.1f, d.z * 2.1f) * warp;
                p.z += warpNoise.Eval(d.x * 2.1f, d.y * 2.1f, d.z * 2.1f + 71.7f) * warp;
            }

            float sum = 0f, first = 0f;
            for (int i = 0; i < layers.Length; i++)
            {
                var def = layers[i].Def;
                float v = LayerValue(ref layers[i], p);
                if (i == 0) first = v;
                float mask = 1f;
                if (def.MaskByFirstLayer && i > 0)
                    mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(def.MaskRange.x, def.MaskRange.y, first));
                sum += v * def.Strength * mask;
            }
            float e = (sum + CraterHeight(d)) * heightMul;
            return oceanDeepen > 1f && e < seaElevation ? seaElevation - Deepen(seaElevation - e) : e;
        }

        // Monotonic, so coverage and coastlines are unchanged; a short shelf keeps beaches gentle.
        float Deepen(float depth) => depth * (1f + (oceanDeepen - 1f) * Mathf.SmoothStep(0f, 1f, depth / ShelfWidth));

        static float LayerValue(ref Layer layer, Vector3 p)
        {
            var def = layer.Def;
            p = Vector3.Scale(p, def.Stretch);
            float freq = def.Frequency, amp = 1f, sum = 0f, norm = 0f, weight = 1f;
            for (int o = 0; o < def.Octaves; o++)
            {
                float n = layer.Noise.Eval(p.x * freq + layer.Offset.x, p.y * freq + layer.Offset.y, p.z * freq + layer.Offset.z);
                float v;
                switch (def.Kind)
                {
                    case PlanetNoiseKind.Ridged:
                        v = 1f - Mathf.Abs(n);
                        v *= v * weight;
                        weight = Mathf.Clamp01(v * def.RidgeWeight);
                        break;
                    case PlanetNoiseKind.Billow:
                        v = Mathf.Abs(n);
                        break;
                    default:
                        v = (n + 1f) * 0.5f;
                        break;
                }
                sum += v * amp;
                norm += amp;
                amp *= def.Persistence;
                freq *= def.Lacunarity;
            }
            float r = sum / norm;
            r = Mathf.Max(0f, r - def.MinValue) / Mathf.Max(0.001f, 1f - def.MinValue);
            if (def.Terraces > 0)
            {
                float s = r * def.Terraces;
                float step = Mathf.Floor(s);
                r = (step + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, s - step))) / def.Terraces;
            }
            return r;
        }

        // ---------------------------------------------------------------- craters

        static Crater[] BuildCraters(PlanetShapeDef s, System.Random rng)
        {
            int count = s.CraterCount > 0 ? Mathf.RoundToInt(s.CraterCount * Range(rng, 0.75f, 1.25f)) : 0;
            var result = new Crater[count];
            for (int i = 0; i < count; i++)
            {
                float size = Mathf.Lerp(s.CraterSize.x, s.CraterSize.y, Mathf.Pow((float)rng.NextDouble(), s.CraterSizeBias));
                float rimW = s.CraterRimWidth * Range(rng, 0.8f, 1.2f);
                float rim = s.CraterRim * Range(rng, 0.6f, 1.4f);
                float depth = s.CraterDepth * Range(rng, 0.7f, 1.3f);
                result[i] = new Crater
                {
                    Center = OnSphere(rng),
                    Size = size,
                    Depth = depth,
                    Floor = -depth * (1f - Mathf.Clamp(s.CraterFloor + Range(rng, -0.2f, 0.2f), 0f, 0.95f)),
                    Steepness = Range(rng, s.CraterSteepness.x, s.CraterSteepness.y),
                    RimSteepness = rim / (rimW * rimW),
                    RimWidth = rimW,
                    Peak = size >= s.CentralPeakSize ? depth * Range(rng, 0.35f, 0.7f) : 0f,
                    MaxChord = size * (1f + rimW) / Mathf.Max(0.3f, 1f - s.CraterIrregularity * 1.1f) + 0.001f,
                };
            }
            return result;
        }

        static Vector3 OnSphere(System.Random rng)
        {
            float z = Range(rng, -1f, 1f);
            float a = Range(rng, 0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        static int Cell(float v) => Mathf.Clamp((int)((v + 1f) * 0.5f * GridSize), 0, GridSize - 1);

        static int[][] BuildGrid(Crater[] craters)
        {
            if (craters.Length == 0) return null;
            var cells = new List<int>[GridSize * GridSize * GridSize];
            for (int k = 0; k < craters.Length; k++)
            {
                var c = craters[k];
                float r = c.MaxChord;
                int x0 = Cell(c.Center.x - r), x1 = Cell(c.Center.x + r);
                int y0 = Cell(c.Center.y - r), y1 = Cell(c.Center.y + r);
                int z0 = Cell(c.Center.z - r), z1 = Cell(c.Center.z + r);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        for (int z = z0; z <= z1; z++)
                        {
                            int idx = (x * GridSize + y) * GridSize + z;
                            (cells[idx] ??= new List<int>()).Add(k);
                        }
            }
            var grid = new int[cells.Length][];
            for (int i = 0; i < cells.Length; i++) grid[i] = cells[i]?.ToArray();
            return grid;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01((b - a + k) / (2f * k));
            return a * h + b * (1f - h) - k * h * (1f - h);
        }

        float CraterHeight(Vector3 d)
        {
            if (craterGrid == null) return 0f;
            var list = craterGrid[(Cell(d.x) * GridSize + Cell(d.y)) * GridSize + Cell(d.z)];
            if (list == null) return 0f;
            float wobble = 1f + rimNoise.Eval(d.x * 9f, d.y * 9f, d.z * 9f) * Shape.CraterIrregularity;
            float sum = 0f;
            for (int i = 0; i < list.Length; i++)
            {
                ref var c = ref craters[list[i]];
                float dx = d.x - c.Center.x, dy = d.y - c.Center.y, dz = d.z - c.Center.z;
                float chordSqr = dx * dx + dy * dy + dz * dz;
                if (chordSqr >= c.MaxChord * c.MaxChord) continue;
                float x = Mathf.Sqrt(chordSqr) / c.Size * wobble;
                float cavity = (Mathf.Pow(x, c.Steepness) - 1f) * c.Depth;
                float rimX = Mathf.Min(x - 1f - c.RimWidth, 0f);
                float rim = c.RimSteepness * rimX * rimX;
                float shape = -SmoothMin(-cavity, -c.Floor, CraterSmoothing * c.Depth);
                shape = SmoothMin(shape, rim, CraterSmoothing * c.Depth);
                if (c.Peak > 0f) shape += c.Peak * Mathf.Exp(-x * x * 16f);
                sum += shape * c.Size;
            }
            return sum;
        }

        // ---------------------------------------------------------------- analysis

        void AnalyzeElevations()
        {
            var samples = new float[CoverageSamples];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < CoverageSamples; i++)
            {
                float y = 1f - (i + 0.5f) / CoverageSamples * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float a = golden * i;
                samples[i] = ElevationFrac(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
            }
            System.Array.Sort(samples);
            float range = Mathf.Max(1e-4f, samples[CoverageSamples - 1] - samples[0]);
            // Mesh vertices can land slightly outside the sampled extremes.
            minElevation = samples[0] - range * 0.03f;
            maxElevation = samples[CoverageSamples - 1] + range * 0.03f;
            if (OceanCoverage > 0.001f)
            {
                HasOcean = true;
                int idx = Mathf.Clamp(Mathf.RoundToInt(OceanCoverage * (CoverageSamples - 1)), 0, CoverageSamples - 1);
                seaElevation = Mathf.Max(samples[idx], samples[0] + range * 0.04f);
                oceanDeepen = Mathf.Max(1f, Shape.OceanFloorDepth);
                minElevation = seaElevation - Deepen(seaElevation - samples[0]) - range * 0.03f;
            }
            else
            {
                HasOcean = false;
                seaElevation = minElevation;
            }
        }

        // ---------------------------------------------------------------- shading

        /// <summary>256x2 sRGB ramp: row 0 = ground (underwater | land), row 1 = cliff. Caller owns the texture.</summary>
        public Texture2D CreateRampTexture()
        {
            const int w = 256;
            var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false, false)
            {
                name = "PlanetRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color[w * 2];
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f);
                Color ground = u < 0.5f ? Shape.Underwater.Evaluate(u * 2f) : Shape.Land.Evaluate((u - 0.5f) * 2f);
                px[x] = Shift(ground);
                px[w + x] = Shift(Shape.Cliff.Evaluate(u));
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        Color Shift(Color c)
        {
            if (Mathf.Abs(HueShift) < 1e-4f) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + HueShift, 1f);
            var r = Color.HSVToRGB(h, s, v);
            r.a = 1f;
            return r;
        }
    }
}
