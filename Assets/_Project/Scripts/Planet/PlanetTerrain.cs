using System.Collections.Generic;
using UnityEngine;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Deterministic terrain height field for one planet (seed + radius + shape). Single source of truth for
    /// chunk meshes, colliders, decor, sites and gameplay surface queries. Read-only after construction, so it
    /// can be sampled from worker threads.
    /// Layers: noise continents/ranges -> canyon network -> landforms (volcanoes, mesas, spires, basins, domes)
    /// -> craters -> erosion (downhill gullies, #115) -> ocean deepening -> rivers -> flattened site clearings.
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
        /// <summary>Gully depth in meters (0 = no erosion): LOD seams can differ by up to about this much.</summary>
        public float ErosionDepthMeters => erosionDepth * Radius;
        /// <summary>Outer radius of the atmosphere shell.</summary>
        public float AtmosphereRadius => Radius * (1f + Mathf.Max(Shape.AtmosphereHeight, maxElevation + 0.05f));
        /// <summary>Lowest radius anything can stand on (sea surface or seabed).</summary>
        public float GroundFloorRadius => HasOcean ? SeaRadius : MinRadius;

        public struct Landform
        {
            public PlanetFeatureKind Kind;
            public Vector3 Center;
            public float Size, Height, Sharp, Rough, Caldera, MaxChord;
            public int Terraces;
        }

        public struct Site
        {
            public Vector3 Dir;
            /// <summary>Elevation fraction of the flattened clearing.</summary>
            public float Elevation;
            /// <summary>Index into <see cref="Landforms"/> of a nearby landform, or -1.</summary>
            public int Landform;
            /// <summary>Clearing radius in meters.</summary>
            public float RadiusMeters;
        }

        public IReadOnlyList<Landform> Landforms => landforms;
        public IReadOnlyList<Site> Sites => sites;

        struct Layer
        {
            public PlanetShapeDef.NoiseLayer Def;
            public float Strength;
            public Noise3D Noise;
            public Vector3 Offset;
        }

        struct Crater
        {
            public Vector3 Center;
            public float Size, Depth, Floor, Steepness, RimSteepness, RimWidth, Peak, MaxChord;
        }

        const int GridSize = 12;
        const int CoverageSamples = 3000;
        const float CraterSmoothing = 0.06f;
        const float ShelfWidth = 0.008f;

        readonly Layer[] layers;
        readonly Noise3D warpNoise, rimNoise, canyonNoise, canyonWarpNoise, canyonMaskNoise, regionNoise, gullyNoise;
        readonly Crater[] craters;
        readonly int[][] craterGrid;
        readonly Landform[] landforms;
        readonly int[][] landformGrid;
        readonly float heightMul, warp;
        readonly Vector3 canyonOffset;
        readonly float canyonFreq, canyonWidth, canyonDepth, canyonCoverage;
        Site[] sites = new Site[0];
        float[] siteChordSqr = new float[0];
        float minElevation, maxElevation, seaElevation;
        // Erosion (#115): gully depth (fraction of radius), largest wavelength (m), octaves, slope mask, branching.
        readonly float erosionDepth, erosionScale, erosionBranching;
        readonly int erosionOctaves;
        readonly Vector2 erosionSlope;
        readonly uint erosionSeed;
        readonly Vector3[] erosionOffsets;
        // Provinces (#115): warped Voronoi cells, each with one ProvinceDef.
        struct Province
        {
            public Vector3 Center;
            public int Def;
            public Vector3 Offset;
        }
        Province[] provinces = new Province[0];
        PlanetShapeDef.ProvinceDef[] provinceDefs = new PlanetShapeDef.ProvinceDef[0];
        Noise3D provinceNoise, provinceWarpNoise;
        float provinceBlend, provinceWarp;
        const int MaxProvinces = 32;

        // Slope field on the cube faces (6 x (res+1)^2 tangent gradients), sampled bilinearly by the erosion.
        Vector3[] slopeGrid;
        int slopeRes;
        float oceanDeepen = 1f; // stays 1 while sampling raw elevations for the sea level
        bool riversActive, sitesActive;

        public PlanetTerrain(PlanetShapeDef shape, int seed, float radius)
        {
            Shape = shape;
            Seed = seed;
            Radius = radius;
            var rng = new System.Random(seed);
            float v = shape.Variation;
            heightMul = shape.HeightMultiplier * (1f + Range(rng, -v, v) * 0.5f);
            warp = shape.Warp * (1f + Range(rng, -v, v));

            var list = new List<Layer>();
            for (int i = 0; i < shape.Layers.Count; i++)
            {
                var def = shape.Layers[i];
                if (def == null || !def.Enabled) continue;
                list.Add(new Layer
                {
                    Def = def,
                    Strength = def.Strength * (1f + Range(rng, -v, v)),
                    Noise = new Noise3D(rng.Next()),
                    Offset = new Vector3(Range(rng, -100f, 100f), Range(rng, -100f, 100f), Range(rng, -100f, 100f)),
                });
            }
            layers = list.ToArray();
            warpNoise = new Noise3D(rng.Next());
            rimNoise = new Noise3D(rng.Next());
            canyonNoise = new Noise3D(rng.Next());
            canyonWarpNoise = new Noise3D(rng.Next());
            canyonMaskNoise = new Noise3D(rng.Next());
            regionNoise = new Noise3D(rng.Next());
            gullyNoise = new Noise3D(rng.Next());
            canyonOffset = new Vector3(Range(rng, -50f, 50f), Range(rng, -50f, 50f), Range(rng, -50f, 50f));
            canyonFreq = shape.CanyonFrequency * (1f + Range(rng, -v, v) * 0.3f);
            canyonWidth = shape.CanyonWidth * (1f + Range(rng, -v, v) * 0.5f);
            canyonDepth = shape.Canyons ? shape.CanyonDepth * (1f + Range(rng, -v, v)) : 0f;
            canyonCoverage = Mathf.Clamp01(shape.CanyonCoverage + Range(rng, -v, v) * 0.5f);

            erosionDepth = Mathf.Max(0f, shape.ErosionDepth) / radius;
            erosionScale = Mathf.Max(4f, shape.ErosionScale);
            erosionOctaves = Mathf.Clamp(shape.ErosionOctaves, 1, 7);
            erosionSlope = shape.ErosionSlope;
            erosionBranching = shape.ErosionBranching;
            // Own generator: adding erosion must not move craters, landforms or sites of existing seeds.
            var erng = new System.Random(seed * 7919 + 17);
            erosionSeed = (uint)erng.Next();
            erosionOffsets = new Vector3[8];
            for (int i = 0; i < erosionOffsets.Length; i++)
                erosionOffsets[i] = new Vector3(Range(erng, -500f, 500f), Range(erng, -500f, 500f), Range(erng, -500f, 500f));

            BuildProvinces(shape, seed);

            craters = BuildCraters(shape, rng);
            craterGrid = BuildGrid(craters.Length, k => (craters[k].Center, craters[k].MaxChord));
            landforms = BuildLandforms(shape, rng, v);
            landformGrid = BuildGrid(landforms.Length, k => (landforms[k].Center, landforms[k].MaxChord));

            if (erosionDepth > 0f) BuildSlopeGrid();

            HueShift = Range(rng, -1f, 1f) * shape.HueJitter;
            OceanCoverage = shape.HasOcean ? Mathf.Clamp01(Range(rng, shape.OceanCoverage.x, shape.OceanCoverage.y)) : 0f;
            AnalyzeElevations();
            riversActive = shape.Canyons && shape.CanyonRivers && HasOcean && canyonDepth > 0f;
            PickSites(rng);
            sitesActive = true;
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ---------------------------------------------------------------- queries

        /// <summary>Terrain elevation above the base radius in local units.</summary>
        public float ElevationAt(Vector3 unitDir) => ElevationFrac(unitDir) * Radius;

        /// <summary>Distance from the planet center to the terrain surface along a unit direction.</summary>
        public float SurfaceRadius(Vector3 unitDir) => Radius * (1f + ElevationFrac(unitDir));

        /// <summary>Distance to whatever you stand on: terrain, or the sea surface above it.</summary>
        public float GroundRadius(Vector3 unitDir) => Mathf.Max(SurfaceRadius(unitDir), HasOcean ? SeaRadius : 0f);

        public bool IsUnderwater(Vector3 unitDir) => HasOcean && ElevationFrac(unitDir) < seaElevation;

        public float SeaElevationFrac => seaElevation;

        /// <summary>Large-scale region mask in [0,1] (alternate ground colors, decor variation).</summary>
        public float RegionAt(Vector3 d)
        {
            float f = Shape.RegionFrequency;
            float n = regionNoise.Eval(d.x * f + 3.1f, d.y * f, d.z * f) + 0.5f * regionNoise.Eval(d.x * f * 2.7f, d.y * f * 2.7f + 9.4f, d.z * f * 2.7f);
            float noiseRegion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.35f, n));
            if (provinces.Length == 0) return noiseRegion;
            // Colour zones follow the provinces (geology), the noise only breaks them up a little.
            ProvinceField(d, float.MaxValue, false, out _, out float region);
            return Mathf.Clamp01(Mathf.Lerp(region, noiseRegion, 0.25f));
        }

        /// <summary>Surface normal (local space) from finite differences over `step` meters.</summary>
        public Vector3 NormalAt(Vector3 d, float step = 2f)
        {
            Vector3 t1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(d, t1);
            float e = step / Radius;
            Vector3 a = (d + t1 * e).normalized, b = (d - t1 * e).normalized, c = (d + t2 * e).normalized, f = (d - t2 * e).normalized;
            Vector3 n = Vector3.Cross(a * SurfaceRadius(a) - b * SurfaceRadius(b), c * SurfaceRadius(c) - f * SurfaceRadius(f)).normalized;
            return Vector3.Dot(n, d) < 0f ? -n : n;
        }

        /// <summary>Slope 0 (flat) .. 1 (vertical) relative to the radial up.</summary>
        public float SlopeAt(Vector3 d, float step = 2f) => 1f - Vector3.Dot(NormalAt(d, step), d);

        /// <summary>Local-space ray vs. terrain (and sea surface). Works without colliders at any distance.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float distance)
        {
            distance = 0f;
            float outer = MaxRadius + 1f;
            float b = Vector3.Dot(origin, dir), c = origin.sqrMagnitude - outer * outer;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t = Mathf.Max(0f, -b - sq), tExit = Mathf.Min(maxDist, -b + sq);
            float prevT = t;
            for (int i = 0; i < 400 && t <= tExit; i++)
            {
                Vector3 p = origin + dir * t;
                float r = p.magnitude;
                float h = r - GroundRadius(p / r);
                if (h <= 0f)
                {
                    float lo = prevT, hi = t;
                    for (int k = 0; k < 10; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Vector3 q = origin + dir * mid;
                        float qr = q.magnitude;
                        if (qr - GroundRadius(q / qr) <= 0f) hi = mid; else lo = mid;
                    }
                    distance = hi;
                    return true;
                }
                prevT = t;
                t += Mathf.Max(0.5f, h * 0.5f);
            }
            return false;
        }

        /// <summary>Color ramp coordinate: 0..0.5 = seabed to sea level, 0.5..1 = shore to highest peak.</summary>
        public float GradientCoord(float elevationFrac)
        {
            if (HasOcean && elevationFrac < seaElevation)
                return 0.5f * Mathf.InverseLerp(minElevation, seaElevation, elevationFrac);
            return 0.5f + 0.5f * Mathf.InverseLerp(seaElevation, maxElevation, elevationFrac);
        }

        /// <summary>Elevation above the base radius as a fraction of the radius.</summary>
        public float ElevationFrac(Vector3 d) => ElevationFrac(d, 0f);

        /// <summary>
        /// Elevation with a band limit: erosion octaves shorter than minWavelength (m) are skipped. Coarse LOD chunks
        /// pass about twice their vertex spacing (no aliasing, cheaper builds); gameplay queries use the full detail.
        /// </summary>
        public float ElevationFrac(Vector3 d, float minWavelength)
        {
            float e = BaseElevation(d, out float carve, out float erosionMul, minWavelength, false);
            if (erosionDepth > 0f && erosionMul > 0.01f) e += Erosion(d, minWavelength) * erosionMul;
            if (oceanDeepen > 1f && e < seaElevation) e = seaElevation - Deepen(seaElevation - e);
            if (riversActive && carve > 0f)
            {
                // Channels cut down to just below sea level so the ocean (water or lava) fills them.
                float floor = seaElevation - canyonDepth * 0.25f;
                if (e > floor) e = Mathf.Lerp(e, floor, carve);
            }
            if (sitesActive) e = FlattenSites(d, e);
            return e;
        }

        /// <summary>
        /// Noise layers, canyons, landforms and craters (everything erosion carves into). minWavelength skips noise
        /// octaves finer than that (m); coarse = the cheap proxy erosion steers by (no craters).
        /// </summary>
        float BaseElevation(Vector3 d, out float carve, float minWavelength, bool coarse) => BaseElevation(d, out carve, out _, minWavelength, coarse);

        float BaseElevation(Vector3 d, out float carve, out float erosionMul, float minWavelength, bool coarse)
        {
            erosionMul = 1f;
            float maxFreq = minWavelength > 0f ? Radius / minWavelength : float.MaxValue;
            Vector3 p = d;
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
                float v = LayerValue(ref layers[i], p, maxFreq);
                if (i == 0) first = v;
                float mask = 1f;
                if (def.MaskByFirstLayer && i > 0)
                    mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(def.MaskRange.x, def.MaskRange.y, first));
                sum += v * layers[i].Strength * mask;
            }

            if (provinces.Length > 0) sum += ProvinceField(d, maxFreq, true, out erosionMul, out _);

            carve = 0f;
            if (canyonDepth > 0f)
            {
                carve = CanyonCarve(d);
                if (!riversActive) sum -= carve * canyonDepth;
            }

            return (sum + LandformHeight(d) + (coarse ? 0f : CraterHeight(d))) * heightMul;
        }

        // ---------------------------------------------------------------- provinces (#115)

        /// <summary>Province cells: jittered Fibonacci points, each assigned a definition by Share (own RNG).</summary>
        void BuildProvinces(PlanetShapeDef shape, int seed)
        {
            var defs = new List<PlanetShapeDef.ProvinceDef>();
            if (shape.Provinces != null) foreach (var p in shape.Provinces) if (p != null && p.Enabled && p.Share > 0f) defs.Add(p);
            if (defs.Count == 0) return;
            provinceDefs = defs.ToArray();
            var prng = new System.Random(seed * 31337 + 5);
            provinceNoise = new Noise3D(prng.Next());
            provinceWarpNoise = new Noise3D(prng.Next());
            provinceBlend = Mathf.Max(0.005f, shape.ProvinceBlend);
            provinceWarp = shape.ProvinceWarp;
            float total = 0f;
            foreach (var p in provinceDefs) total += p.Share;

            int count = Mathf.Clamp(shape.ProvinceCount, 4, MaxProvinces);
            provinces = new Province[count];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float spin = Range(prng, 0f, Mathf.PI * 2f);
            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i + 0.5f) / count * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float a = golden * i + spin;
                Vector3 c = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                // Jitter so the cells are not a regular pattern.
                c = (c + new Vector3(Range(prng, -1f, 1f), Range(prng, -1f, 1f), Range(prng, -1f, 1f)) * (0.9f / Mathf.Sqrt(count))).normalized;
                float pick = Range(prng, 0f, total);
                int def = 0;
                for (; def < provinceDefs.Length - 1; def++) { pick -= provinceDefs[def].Share; if (pick <= 0f) break; }
                provinces[i] = new Province
                {
                    Center = c, Def = def,
                    Offset = new Vector3(Range(prng, -300f, 300f), Range(prng, -300f, 300f), Range(prng, -300f, 300f)),
                };
            }
        }

        /// <summary>
        /// Soft Voronoi over the (warped) province cells. withRelief: returns the blended province height; else only the
        /// weights are needed (region / erosion). Cliff provinces sharpen their own elevation step so plateaus get walls.
        /// </summary>
        float ProvinceField(Vector3 d, float maxFreq, bool withRelief, out float erosionMul, out float region)
        {
            Vector3 q = d;
            if (provinceWarp > 0f)
            {
                const float wf = 3.3f;
                q.x += provinceWarpNoise.Eval(d.x * wf + 5.1f, d.y * wf, d.z * wf) * provinceWarp;
                q.y += provinceWarpNoise.Eval(d.x * wf, d.y * wf + 17.3f, d.z * wf) * provinceWarp;
                q.z += provinceWarpNoise.Eval(d.x * wf, d.y * wf, d.z * wf + 29.9f) * provinceWarp;
                q += new Vector3(provinceWarpNoise.Eval(d.x * 9.7f, d.y * 9.7f, d.z * 9.7f + 3f), 0f, provinceWarpNoise.Eval(d.x * 9.7f + 8f, d.y * 9.7f, d.z * 9.7f)) * (provinceWarp * 0.25f);
                q.Normalize();
            }
            int n = provinces.Length;
            System.Span<float> w = stackalloc float[MaxProvinces];
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = provinces[i].Center;
                float dx = q.x - c.x, dy = q.y - c.y, dz = q.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                w[i] = dist;
                if (dist < best) best = dist;
            }
            float sum = 0f, cut = provinceBlend * 7f;
            for (int i = 0; i < n; i++)
            {
                float x = w[i] - best;
                w[i] = x > cut ? 0f : System.MathF.Exp(-x / provinceBlend);
                sum += w[i];
            }
            float h = 0f, ero = 0f, reg = 0f;
            for (int i = 0; i < n; i++)
            {
                if (w[i] <= 0f) continue;
                float wi = w[i] / sum;
                var def = provinceDefs[provinces[i].Def];
                ero += def.Erosion * wi;
                reg += def.Region * wi;
                if (!withRelief || wi < 0.002f) continue;
                // Elevation step: cliffs sharpen the weight around its midpoint (and may terrace it).
                float we = wi;
                if (def.EdgeCliff > 0f)
                {
                    float half = 0.5f * (1f - def.EdgeCliff * 0.95f);
                    we = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f - half, 0.5f + half, wi));
                    if (def.Terraces > 0) we = Mathf.Clamp01(Terrace(we * def.Terraces) / def.Terraces);
                }
                h += def.Elevation * we + def.Relief * ProvinceRelief(def, q, provinces[i].Offset, maxFreq) * wi;
            }
            erosionMul = ero;
            region = reg;
            return h;
        }

        float ProvinceRelief(PlanetShapeDef.ProvinceDef def, Vector3 q, Vector3 off, float maxFreq)
        {
            float freq = def.Frequency, amp = 1f, sum = 0f, norm = 0f, weight = 1f;
            for (int o = 0; o < 4; o++)
            {
                if (freq > maxFreq) { for (; o < 4; o++) { sum += 0.45f * amp; norm += amp; amp *= 0.5f; } break; }
                float nv = provinceNoise.Eval(q.x * freq + off.x, q.y * freq + off.y, q.z * freq + off.z);
                float smooth = (nv + 1f) * 0.5f;
                float ridge = 1f - Mathf.Abs(nv);
                ridge *= ridge * weight;
                weight = Mathf.Clamp01(ridge * 1.6f);
                sum += Mathf.Lerp(smooth, ridge, def.Sharpness) * amp;
                norm += amp;
                amp *= 0.5f; freq *= 2.1f;
            }
            float r = sum / norm;
            if (def.Terraces > 0 && def.EdgeCliff <= 0f) r = Terrace(r * def.Terraces) / def.Terraces;
            return r;
        }

        // ---------------------------------------------------------------- erosion (#115)

        /// <summary>
        /// Gully erosion in the spirit of "eroded terrain noise": per octave a jittered 3D cell grid of plane waves whose
        /// crests run downhill (wave vector across the slope), so slopes get parallel gullies and sharp spurs instead of
        /// soft noise blobs. Each finer octave bends along the gullies of the coarser ones (branching). Only on slopes.
        /// </summary>
        /// <summary>
        /// Precomputes the slope of the smooth base terrain on a cube-face lattice (~0.3 x the gully wavelength), in
        /// parallel. Shared face edges use identical lattice directions, so the interpolated field is continuous.
        /// </summary>
        void BuildSlopeGrid()
        {
            float faceArc = Radius * Mathf.PI * 0.5f;
            slopeRes = Mathf.Clamp(Mathf.RoundToInt(faceArc / (erosionScale * 0.3f)), 24, 160);
            int side = slopeRes + 1;
            var grid = new Vector3[6 * side * side];
            float h = erosionScale * 0.3f / Radius, band = erosionScale * 0.3f;
            System.Threading.Tasks.Parallel.For(0, 6 * side, row =>
            {
                int f = row / side, j = row % side;
                float b = 2f * j / slopeRes - 1f;
                for (int i = 0; i < side; i++)
                {
                    Vector3 d = PlanetMesh.CubeDir(f, 2f * i / slopeRes - 1f, b);
                    Vector3 t1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 t2 = Vector3.Cross(d, t1);
                    // Central differences of a smooth proxy at the gully scale: small bumps must not twist the gullies.
                    float ea = BaseElevation((d + t1 * h).normalized, out _, band, true) - BaseElevation((d - t1 * h).normalized, out _, band, true);
                    float eb = BaseElevation((d + t2 * h).normalized, out _, band, true) - BaseElevation((d - t2 * h).normalized, out _, band, true);
                    grid[row * side + i] = (t1 * ea + t2 * eb) / (2f * h);
                }
            });
            slopeGrid = grid;
        }

        Vector3 SlopeAtGrid(Vector3 d)
        {
            PlanetMesh.DirToCube(d, out int f, out float a, out float b);
            float x = (a + 1f) * 0.5f * slopeRes, y = (b + 1f) * 0.5f * slopeRes;
            int x0 = Mathf.Clamp((int)x, 0, slopeRes - 1), y0 = Mathf.Clamp((int)y, 0, slopeRes - 1);
            float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
            int side = slopeRes + 1, i0 = (f * side + y0) * side + x0;
            Vector3 g = Vector3.Lerp(Vector3.Lerp(slopeGrid[i0], slopeGrid[i0 + 1], fx), Vector3.Lerp(slopeGrid[i0 + side], slopeGrid[i0 + side + 1], fx), fy);
            return g - d * Vector3.Dot(g, d); // back into this point's tangent plane
        }

        float Erosion(Vector3 d, float minWavelength)
        {
            if (slopeGrid == null) return 0f;
            Vector3 grad = SlopeAtGrid(d); // dimensionless slope (m per m)
            float slope = grad.magnitude;
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(erosionSlope.x, erosionSlope.y, slope));
            if (mask <= 0.001f) return 0f;
            Vector3 k = Vector3.Cross(d, grad) / slope; // unit, across the slope: wave crests run downhill

            float freq = Radius / erosionScale, amp = 1f, sum = 0f, wl = erosionScale;
            Vector3 bend = Vector3.zero;
            for (int o = 0; o < erosionOctaves; o++)
            {
                if (wl < minWavelength) break;
                Vector3 kk = k + Vector3.Cross(d, bend) * erosionBranching;
                float m = kk.magnitude;
                kk = m > 1e-5f ? kk / m : k;
                GullyOctave(d * freq + erosionOffsets[o], kk, (uint)o, out float v, out Vector3 dv);
                sum += v * amp;
                bend += dv * amp;
                amp *= 0.5f; freq *= 2f; wl *= 0.5f;
            }
            // Gullies cut down more than spurs rise: valleys read as carved, crests as sharp.
            sum = sum < 0f ? sum * 1.25f : sum * 0.75f;
            return sum * erosionDepth * mask;
        }

        void GullyOctave(Vector3 q, Vector3 k, uint octave, out float value, out Vector3 deriv)
        {
            int ix = Floor(q.x), iy = Floor(q.y), iz = Floor(q.z);
            float fx = q.x - ix, fy = q.y - iy, fz = q.z - iz;
            float va = 0f, wt = 0f;
            Vector3 dv = Vector3.zero;
            const float Tau = Mathf.PI * 2f;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    for (int l = -1; l <= 1; l++)
                    {
                        uint hsh = Hash(ix + i, iy + j, iz + l, erosionSeed + octave * 0x9E3779B9u);
                        // Feature point inside the cell (jittered); pp = sample relative to it.
                        float px = fx - i - 0.5f - ((hsh & 1023) / 1023f - 0.5f) * 0.8f;
                        float py = fy - j - 0.5f - (((hsh >> 10) & 1023) / 1023f - 0.5f) * 0.8f;
                        float pz = fz - l - 0.5f - (((hsh >> 20) & 1023) / 1023f - 0.5f) * 0.8f;
                        float d2 = px * px + py * py + pz * pz;
                        if (d2 > 2.6f) continue;
                        float w = System.MathF.Exp(-d2 * 2.2f);
                        float ph = (px * k.x + py * k.y + pz * k.z) * Tau;
                        va += System.MathF.Cos(ph) * w;
                        float sn = -System.MathF.Sin(ph) * w;
                        dv.x += sn * k.x; dv.y += sn * k.y; dv.z += sn * k.z;
                        wt += w;
                    }
            if (wt < 1e-6f) { value = 0f; deriv = Vector3.zero; return; }
            value = va / wt;
            deriv = dv / wt;
        }

        static int Floor(float v) { int i = (int)v; return v < i ? i - 1 : i; }

        static uint Hash(int x, int y, int z, uint seed)
        {
            uint h = seed ^ ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u) ^ ((uint)z * 0xCB1AB31Fu);
            h = (h ^ (h >> 15)) * 0x2C1B3C6Du;
            h = (h ^ (h >> 12)) * 0x297A2D39u;
            return h ^ (h >> 15);
        }

        // Monotonic, so coverage and coastlines are unchanged; a short shelf keeps beaches gentle.
        float Deepen(float depth) => depth * (1f + (oceanDeepen - 1f) * Mathf.SmoothStep(0f, 1f, depth / ShelfWidth));

        static float LayerValue(ref Layer layer, Vector3 p, float maxFreq)
        {
            var def = layer.Def;
            p = Vector3.Scale(p, def.Stretch);
            float freq = def.Frequency, amp = 1f, sum = 0f, norm = 0f, weight = 1f;
            for (int o = 0; o < def.Octaves; o++)
            {
                if (freq > maxFreq)
                {
                    // Band limit: the skipped octaves still count in the normalisation (they average out).
                    for (; o < def.Octaves; o++) { sum += (def.Kind == PlanetNoiseKind.Ridged ? 0.25f * weight : 0.5f) * amp; norm += amp; amp *= def.Persistence; }
                    break;
                }
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
            if (def.Terraces > 0) r = Terrace(r * def.Terraces) / def.Terraces;
            return r;
        }

        static float Terrace(float s)
        {
            float step = Mathf.Floor(s);
            return step + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, s - step));
        }

        // ---------------------------------------------------------------- canyons

        /// <summary>0 outside the network, 1 on a channel floor. Channels follow the zero isolines of warped noise.</summary>
        float CanyonCarve(Vector3 d)
        {
            float f = canyonFreq;
            Vector3 q = d * f + canyonOffset;
            float w = Shape.CanyonWarp;
            if (w > 0f)
            {
                q.x += canyonWarpNoise.Eval(d.x * 1.7f, d.y * 1.7f + 5.2f, d.z * 1.7f) * w * f * 0.6f;
                q.y += canyonWarpNoise.Eval(d.x * 1.7f + 8.8f, d.y * 1.7f, d.z * 1.7f) * w * f * 0.6f;
                q.z += canyonWarpNoise.Eval(d.x * 1.7f, d.y * 1.7f, d.z * 1.7f + 2.6f) * w * f * 0.6f;
            }
            float n = canyonNoise.Eval(q.x, q.y, q.z) + 0.3f * canyonNoise.Eval(q.x * 2.9f + 31f, q.y * 2.9f, q.z * 2.9f);
            float mn = canyonMaskNoise.Eval(d.x * 1.3f, d.y * 1.3f, d.z * 1.3f);
            float th = Mathf.Lerp(0.55f, -0.55f, canyonCoverage);
            float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(th, th + 0.3f, mn));
            float width = canyonWidth * m;
            float dist = Mathf.Abs(n);
            if (m <= 0f || dist >= width) return 0f;
            float t = dist / width;
            float floor = Shape.CanyonFloor;
            if (t <= floor) return Mathf.Sqrt(m);
            float u = (t - floor) / (1f - floor);
            // Small rim noise breaks up the perfectly smooth walls.
            u = Mathf.Clamp01(u + rimNoise.Eval(d.x * 40f, d.y * 40f, d.z * 40f) * 0.08f);
            float c = 1f - Mathf.Pow(u, Shape.CanyonSteepness);
            int terr = Shape.CanyonTerraces;
            if (terr > 0) c = Mathf.Clamp01(Terrace(c * terr) / terr);
            return c * Mathf.Sqrt(m);
        }

        // ---------------------------------------------------------------- landforms

        static Landform[] BuildLandforms(PlanetShapeDef s, System.Random rng, float v)
        {
            var result = new List<Landform>();
            if (s.Features == null) return result.ToArray();
            foreach (var set in s.Features)
            {
                if (set == null || !set.Enabled) continue;
                int count = rng.Next(set.Count.x, Mathf.Max(set.Count.x, set.Count.y) + 1);
                count = Mathf.Max(0, Mathf.RoundToInt(count * (1f + Range(rng, -v, v))));
                for (int i = 0; i < count; i++)
                {
                    float size = Range(rng, set.Size.x, set.Size.y);
                    Vector3 c = OnSphere(rng);
                    // Avoid stacking big landforms on top of each other.
                    for (int tries = 0; tries < 6; tries++)
                    {
                        bool clear = true;
                        foreach (var o in result)
                            if ((o.Center - c).magnitude < (o.Size + size) * 0.8f) { clear = false; break; }
                        if (clear) break;
                        c = OnSphere(rng);
                    }
                    result.Add(new Landform
                    {
                        Kind = set.Kind,
                        Center = c,
                        Size = size,
                        Height = size * Range(rng, set.Height.x, set.Height.y),
                        Sharp = set.Sharpness,
                        Rough = set.Roughness,
                        Caldera = set.Caldera * Range(rng, 0.7f, 1.3f),
                        Terraces = set.Terraces,
                        MaxChord = size * (set.Kind == PlanetFeatureKind.Basin ? 1.35f : 1.15f),
                    });
                }
            }
            return result.ToArray();
        }

        float LandformHeight(Vector3 d)
        {
            if (landformGrid == null) return 0f;
            var list = landformGrid[(Cell(d.x) * GridSize + Cell(d.y)) * GridSize + Cell(d.z)];
            if (list == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < list.Length; i++)
            {
                ref var f = ref landforms[list[i]];
                float dx = d.x - f.Center.x, dy = d.y - f.Center.y, dz = d.z - f.Center.z;
                float chordSqr = dx * dx + dy * dy + dz * dz;
                if (chordSqr >= f.MaxChord * f.MaxChord) continue;
                float x = Mathf.Sqrt(chordSqr) / f.Size;
                if (f.Rough > 0f) x *= 1f + rimNoise.Eval(d.x * 7f + f.Center.x * 13f, d.y * 7f, d.z * 7f) * f.Rough * 0.35f;
                sum += LandformProfile(ref f, x, d);
            }
            return sum;
        }

        float LandformProfile(ref Landform f, float x, Vector3 d)
        {
            float H = f.Height;
            switch (f.Kind)
            {
                case PlanetFeatureKind.Volcano:
                {
                    float p = Mathf.Lerp(1.4f, 2.6f, f.Sharp);
                    float c = Mathf.Clamp(f.Caldera, 0f, 0.6f);
                    float s = Mathf.Clamp01(1f - x);
                    float h = H * Mathf.Pow(s, p);
                    if (x < c && c > 0f)
                    {
                        float top = H * Mathf.Pow(1f - c, p);
                        float q = x / c;
                        h = top - H * 0.45f * (1f - q * q * q * q);
                    }
                    else if (f.Rough > 0f)
                    {
                        // Radial erosion gullies on the flanks.
                        float g = 1f - Mathf.Abs(gullyNoise.Eval(d.x * 90f, d.y * 90f, d.z * 90f));
                        h *= 1f - f.Rough * 0.22f * g * g * s * (1f - s) * 4f;
                    }
                    return h;
                }
                case PlanetFeatureKind.Mesa:
                {
                    float edge = Mathf.Lerp(0.5f, 0.05f, f.Sharp);
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 1f - edge, x));
                    if (f.Terraces > 0) t = Mathf.Clamp01(Terrace(t * f.Terraces) / f.Terraces);
                    return H * t;
                }
                case PlanetFeatureKind.Spire:
                {
                    float s = Mathf.Clamp01(1f - x);
                    float h = H * Mathf.Pow(s, Mathf.Lerp(1.2f, 3.5f, f.Sharp));
                    if (f.Rough > 0f)
                    {
                        float g = 1f - Mathf.Abs(gullyNoise.Eval(d.x * 60f, d.y * 60f, d.z * 60f));
                        h *= 1f - f.Rough * 0.3f * g * g;
                    }
                    return h;
                }
                case PlanetFeatureKind.Basin:
                {
                    float bowl = -H * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.35f, x));
                    float rimX = (x - 1f) / 0.18f;
                    return bowl + H * 0.18f * Mathf.Exp(-rimX * rimX);
                }
                default: // Dome
                {
                    float s = Mathf.Clamp01(1f - x * x);
                    return H * s * s;
                }
            }
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

        static int[][] BuildGrid(int count, System.Func<int, (Vector3 center, float chord)> get)
        {
            if (count == 0) return null;
            var cells = new List<int>[GridSize * GridSize * GridSize];
            for (int k = 0; k < count; k++)
            {
                var (c, r) = get(k);
                int x0 = Cell(c.x - r), x1 = Cell(c.x + r);
                int y0 = Cell(c.y - r), y1 = Cell(c.y + r);
                int z0 = Cell(c.z - r), z1 = Cell(c.z + r);
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

        // ---------------------------------------------------------------- sites

        float FlattenSites(Vector3 d, float e)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                Vector3 c = sites[i].Dir;
                float dx = d.x - c.x, dy = d.y - c.y, dz = d.z - c.z;
                float chordSqr = dx * dx + dy * dy + dz * dz;
                if (chordSqr >= siteChordSqr[i]) continue;
                float x = Mathf.Sqrt(chordSqr / siteChordSqr[i]);
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, x));
                e = Mathf.Lerp(e, sites[i].Elevation, w);
            }
            return e;
        }

        /// <summary>
        /// Picks well-spread, dry, gently sloped spots, preferring the feet of landforms and canyon rims so points
        /// of interest sit next to memorable geography. Each becomes a flattened clearing (natural landing site).
        /// </summary>
        void PickSites(System.Random rng)
        {
            var sr = Shape.SiteCount;
            int want = rng.Next(Mathf.Max(0, sr.x), Mathf.Max(sr.x, sr.y) + 1);
            if (want <= 0) return;
            float radiusM = Shape.SiteRadius;
            float minSep = Mathf.Clamp(2.2f / Mathf.Sqrt(want), 0.25f, 1.2f); // chord between sites
            var candidates = new List<(Vector3 dir, int lf)>();
            for (int i = 0; i < landforms.Length; i++)
            {
                var f = landforms[i];
                Vector3 t = Vector3.Cross(f.Center, Mathf.Abs(f.Center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                float ang = Range(rng, 0f, Mathf.PI * 2f);
                Vector3 off = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, f.Center) * t;
                float dist = f.Kind == PlanetFeatureKind.Mesa ? 0f : f.Kind == PlanetFeatureKind.Basin ? f.Size * 0.15f : f.Size * Range(rng, 1.05f, 1.3f);
                candidates.Add(((f.Center + off * dist).normalized, i));
            }
            for (int i = 0; i < want * 14; i++) candidates.Add((OnSphere(rng), -1));

            var picked = new List<Site>();
            float step = Mathf.Max(radiusM * 0.6f, 4f);
            for (int pass = 0; pass < 2 && picked.Count < want; pass++)
            {
                foreach (var (dir, lf) in candidates)
                {
                    if (picked.Count >= want) break;
                    bool clear = true;
                    foreach (var s in picked) if ((s.Dir - dir).magnitude < minSep * (pass == 0 ? 1f : 0.6f)) { clear = false; break; }
                    if (!clear) continue;
                    float e = ElevationFrac(dir);
                    if (HasOcean && e < seaElevation + 4f / Radius) continue;
                    if (SlopeAt(dir, step) > (pass == 0 ? 0.22f : 0.4f)) continue;
                    // Average a few ring samples for the clearing level so it doesn't sit on a lone bump.
                    float avg = e;
                    Vector3 t = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 o = Quaternion.AngleAxis(k * 90f, dir) * t;
                        avg += ElevationFrac((dir + o * (radiusM * 0.5f / Radius)).normalized);
                    }
                    avg /= 5f;
                    if (HasOcean) avg = Mathf.Max(avg, seaElevation + 2.5f / Radius);
                    picked.Add(new Site { Dir = dir, Elevation = avg, Landform = lf, RadiusMeters = radiusM });
                }
            }
            sites = picked.ToArray();
            siteChordSqr = new float[sites.Length];
            for (int i = 0; i < sites.Length; i++)
            {
                float c = sites[i].RadiusMeters / Radius;
                siteChordSqr[i] = c * c;
            }
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
            // Mesh vertices can land slightly outside the sampled extremes (narrow spires, canyon floors).
            minElevation = samples[0] - range * 0.05f;
            maxElevation = samples[CoverageSamples - 1] + range * 0.05f;
            foreach (var f in landforms)
                if (f.Kind != PlanetFeatureKind.Basin) maxElevation = Mathf.Max(maxElevation, samples[CoverageSamples / 2] + f.Height * heightMul);
            if (OceanCoverage > 0.001f)
            {
                HasOcean = true;
                int idx = Mathf.Clamp(Mathf.RoundToInt(OceanCoverage * (CoverageSamples - 1)), 0, CoverageSamples - 1);
                seaElevation = Mathf.Max(samples[idx], samples[0] + range * 0.04f);
                oceanDeepen = Mathf.Max(1f, Shape.OceanFloorDepth);
                minElevation = seaElevation - Deepen(seaElevation - samples[0]) - range * 0.05f;
                if (Shape.Canyons && Shape.CanyonRivers) minElevation = Mathf.Min(minElevation, seaElevation - canyonDepth * 0.3f);
            }
            else
            {
                HasOcean = false;
                seaElevation = minElevation;
            }
        }

        // ---------------------------------------------------------------- shading

        /// <summary>256x3 sRGB ramp: row 0 = ground (underwater | land), row 1 = cliff, row 2 = region ground. Caller owns the texture.</summary>
        public Texture2D CreateRampTexture()
        {
            const int w = 256, h = 3;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "PlanetRamp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            bool hasRegion = Shape.Region != null && Shape.Region.colorKeys.Length > 0 && Shape.RegionStrength > 0f;
            var px = new Color[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f);
                Color ground = u < 0.5f ? Shape.Underwater.Evaluate(u * 2f) : Shape.Land.Evaluate((u - 0.5f) * 2f);
                Color region = u < 0.5f || !hasRegion ? ground : Shape.Region.Evaluate((u - 0.5f) * 2f);
                px[x] = Shift(ground);
                px[w + x] = Shift(Shape.Cliff.Evaluate(u));
                px[2 * w + x] = Shift(region);
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        public Color Shift(Color c)
        {
            if (Mathf.Abs(HueShift) < 1e-4f) return c;
            Color.RGBToHSV(c, out float hh, out float s, out float v);
            hh = Mathf.Repeat(hh + HueShift, 1f);
            var r = Color.HSVToRGB(hh, s, v);
            r.a = c.a;
            return r;
        }
    }
}
