using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>
    /// Deterministic star system layout shared by server (spawning) and clients (local visuals).
    /// The flight corridor runs along +X and is centered on the origin: every system is rebuilt around
    /// (0,0,0), which is our floating-origin strategy (world rebasing on each warp keeps all
    /// coordinates within ~12 km incl. the kilometre-scale planets, so float precision stays around a
    /// millimetre in multiplayer). Distances are deliberately large (#22): crossing a system is a trip.
    /// What a system contains depends on its kind (#46): planetary systems have 2-3 worlds, asteroid fields
    /// many rocks and wrecks, enclaves a Warden arena at the far end, empty space next to nothing.
    /// </summary>
    public class SystemLayout
    {
        public struct PlanetSpot { public Vector3 Pos; public float Radius; public int BiomeIndex; public int Seed; }
        public struct Field { public Vector3 Center; public Vector3 Extents; public int Count; public int Seed; }

        public const float MinPlanetRadius = 1000f;
        public const float MaxPlanetRadius = 1900f;

        public int Seed;
        public GalaxyDef Def;
        public SystemKind Kind;
        public bool HasStation;
        public float Length;
        public float StartX;
        public float EndX;
        public Vector3 StartPoint;
        public Vector3 StationPos;
        public Vector3 BossPos;
        public PlanetSpot[] Planets;
        public Field[] Fields;
        public Vector3[] Wrecks;

        public static SystemLayout Build(int seed, GalaxyDef def, SystemKind kind, bool hasStation)
        {
            var L = new SystemLayout { Seed = seed, Def = def, Kind = kind, HasStation = hasStation };
            var rng = new System.Random(seed);
            L.Length = def != null ? def.SystemLength : 9000f;
            L.StartX = -L.Length * 0.5f;
            L.EndX = L.Length * 0.5f;
            L.StartPoint = new Vector3(L.StartX + 80f, 0f, 0f);
            L.BossPos = new Vector3(L.EndX - 300f, 40f, 0f);
            L.StationPos = new Vector3(MathUtils.Range(rng, -L.Length * 0.15f, L.Length * 0.15f), MathUtils.Range(rng, -120f, 120f), MathUtils.Range(rng, 350f, 600f) * Sign(rng));

            int nPlanets, nFields, nWrecks;
            switch (kind)
            {
                case SystemKind.Planetary: nPlanets = rng.Next(2, 4); nFields = 3; nWrecks = 2; break;
                case SystemKind.AsteroidField: nPlanets = rng.Next(0, 2); nFields = 6; nWrecks = 4; break;
                case SystemKind.Anomaly: nPlanets = 1; nFields = 3; nWrecks = 1; break;
                case SystemKind.Enclave: nPlanets = 1; nFields = 2; nWrecks = 1; break;
                case SystemKind.Nomads: nPlanets = rng.Next(1, 3); nFields = 2; nWrecks = 1; break;
                default: nPlanets = 0; nFields = 1; nWrecks = 1; break;
            }
            if (def != null && def.Index == 4) nFields = Mathf.Max(1, nFields - 1); // Leo I: star-poor and empty

            L.Planets = new PlanetSpot[nPlanets];
            var cat = ContentCatalog.Instance;
            float usable = L.Length - 2600f;
            float firstSide = Sign(rng);
            for (int i = 0; i < nPlanets; i++)
            {
                // Kilometre-scale worlds alternate sides of the corridor, their atmospheres just clear of the lane.
                float t = (i + 0.5f) / nPlanets;
                float x = L.StartX + 1300f + usable * t + MathUtils.Range(rng, -350f, 350f);
                float r = MathUtils.Range(rng, MinPlanetRadius, MaxPlanetRadius);
                float side = (i % 2 == 0 ? 1f : -1f) * firstSide;
                float off = r * 1.3f + MathUtils.Range(rng, 600f, 1300f);
                float ang = MathUtils.Range(rng, -0.45f, 0.45f);
                Vector3 p = new Vector3(x, Mathf.Sin(ang) * off, Mathf.Cos(ang) * off * side);
                Vector3 outward = new Vector3(0f, p.y, p.z).normalized;
                for (int k = 0; k < 8; k++)
                {
                    bool clear = !hasStation || Vector3.Distance(p, L.StationPos) > r * 1.4f + 400f;
                    for (int j = 0; j < i && clear; j++)
                        if (Vector3.Distance(p, L.Planets[j].Pos) < (r + L.Planets[j].Radius) * 1.35f + 600f) clear = false;
                    if (clear) break;
                    p += outward * 400f;
                }
                int biomeIndex = 0;
                if (def != null && def.BiomePool != null && def.BiomePool.Length > 0 && cat != null)
                {
                    var b = def.BiomePool[(i + rng.Next(0, 100)) % def.BiomePool.Length];
                    biomeIndex = System.Array.IndexOf(cat.Biomes, b);
                    if (biomeIndex < 0) biomeIndex = 0;
                }
                L.Planets[i] = new PlanetSpot { Pos = p, Radius = r, BiomeIndex = biomeIndex, Seed = rng.Next() };
            }

            L.Fields = new Field[nFields];
            for (int i = 0; i < nFields; i++)
            {
                float x = L.StartX + 900f + (L.Length - 1700f) * ((i + 0.5f) / nFields) + MathUtils.Range(rng, -350f, 350f);
                Vector3 c = new Vector3(x, MathUtils.Range(rng, -120f, 120f), MathUtils.Range(rng, -220f, 220f));
                L.Fields[i] = new Field
                {
                    Center = c,
                    Extents = new Vector3(MathUtils.Range(rng, 160f, 300f), MathUtils.Range(rng, 60f, 130f), MathUtils.Range(rng, 160f, 300f)),
                    Count = rng.Next(55, 95),
                    Seed = rng.Next()
                };
            }

            L.Wrecks = new Vector3[nWrecks];
            for (int i = 0; i < nWrecks; i++)
                L.Wrecks[i] = new Vector3(MathUtils.Range(rng, L.StartX + 800f, L.EndX - 900f), MathUtils.Range(rng, -150f, 150f), MathUtils.Range(rng, 150f, 450f) * Sign(rng));
            return L;
        }

        // ---------------------------------------------------------------- jump zones (#108)

        /// <summary>The jump zones at the system edge (empty until AddJumpZones).</summary>
        public JumpZone[] Jumps = new JumpZone[0];

        /// <summary>
        /// The jump zones of system `system` of `plan` (#108): a hop zone towards each neighbouring system of the galaxy
        /// (the plan's order, aimed like the galaxy map), and in the galaxy's outermost system the Galactic Rim towards
        /// the next galaxy on the sky (from the LMC: the Milky Way, the flight home) plus the skip zone beside it, further
        /// out. Zones sit on an ellipsoid around the corridor, well outside the planets' gravity fields.
        /// </summary>
        public void AddJumpZones(GalaxyPlan plan, int system)
        {
            var list = new System.Collections.Generic.List<JumpZone>();
            if (plan == null || plan.Systems == null || plan.Systems.Length == 0) { Jumps = list.ToArray(); return; }
            var rng = new System.Random(Seed ^ 0x5A17);
            int n = plan.Systems.Length;
            var here = plan.Systems[Mathf.Clamp(system, 0, n - 1)];
            for (int k = -1; k <= 1; k += 2)
            {
                int j = system + k;
                if (j < 0 || j >= n) continue;
                Vector2 m = plan.Systems[j].MapPos - here.MapPos;
                var dir = new Vector3(m.x, 0f, m.y);
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : new Vector3(k, 0f, 0f);
                dir.x = k > 0 ? Mathf.Max(dir.x, 0.6f) : Mathf.Min(dir.x, -0.6f);
                dir.y = MathUtils.Range(rng, -0.12f, 0.12f);
                list.Add(Zone(j, JumpKind.Hop, dir.normalized, 0f, plan.Systems[j].Name));
            }
            if (system == n - 1)
            {
                int g = plan.Index;
                bool last = g >= GalaxyDef.Count;
                var next = ContentCatalog.GetGalaxy(g + 1);
                list.Add(last
                    ? Zone(RunManager.WarpNextGalaxy, JumpKind.Home, SystemVisuals.MilkyWayDir, 0f, "HOME - THE MILKY WAY")
                    : Zone(RunManager.WarpNextGalaxy, JumpKind.Rim, SystemVisuals.DestinationDir, 0f, "GALACTIC RIM - " + (next != null ? next.DisplayName : "next galaxy")));
                if (g + 2 <= GalaxyDef.Count)
                {
                    var skip = ContentCatalog.GetGalaxy(g + 2);
                    Vector3 sd = (Quaternion.AngleAxis(-38f, Vector3.up) * SystemVisuals.DestinationDir).normalized;
                    var sz = Zone(RunManager.WarpSkipGalaxy, JumpKind.Skip, sd, 1400f, "SKIP - " + (skip != null ? skip.DisplayName : "a galaxy"));
                    // further out than the rim it sits beside
                    float rim = list[list.Count - 1].Center.magnitude + 1400f;
                    if (sz.Center.magnitude < rim) sz.Center = sd * rim;
                    list.Add(sz);
                }
            }
            Jumps = list.ToArray();
        }

        JumpZone Zone(int target, JumpKind kind, Vector3 dir, float extra, string label)
        {
            // Point on an ellipsoid around the corridor: past its ends along X, wide to the sides, flatter vertically.
            float a = Length * 0.5f + 900f, b = 2600f, c = 5200f;
            float t = 1f / Mathf.Sqrt(dir.x * dir.x / (a * a) + dir.y * dir.y / (b * b) + dir.z * dir.z / (c * c)) + extra;
            // Out along the normal until the zone and the arrival point inside it are well clear of every gravity field
            // (the zone must be a place to cruise through, and the crew arrives there cruising).
            for (int k = 0; k < 24 && !ClearOfPlanets(dir * t, dir); k++) t += 500f;
            return new JumpZone { Target = target, Kind = kind, Center = dir * t, Normal = dir, Radius = JumpZones.Radius, Label = label };
        }

        bool ClearOfPlanets(Vector3 center, Vector3 normal)
        {
            Vector3 arrive = center - normal * (JumpZones.Depth + 350f);
            foreach (var p in Planets)
            {
                // gravity/cruise lock reaches ~1 km past the atmosphere: keep 2 km more plus the disc
                float keep = p.Radius * 1.35f + 3000f + JumpZones.Radius;
                if (Vector3.Distance(center, p.Pos) < keep || Vector3.Distance(arrive, p.Pos) < keep) return false;
            }
            return true;
        }

        /// <summary>
        /// Where a warp into this system ends (#108): a hop from `fromSystem` comes out of the zone that leads back
        /// there (inside it, nose inward); a galaxy warp (fromSystem &lt; 0) at the arrival point at the corridor's start.
        /// </summary>
        public void Arrival(int fromSystem, out Vector3 pos, out Vector3 inward)
        {
            foreach (var z in Jumps)
                if (z.Kind == JumpKind.Hop && z.Target == fromSystem && fromSystem >= 0)
                {
                    inward = -z.Normal;
                    pos = z.Center + inward * (JumpZones.Depth + 350f);
                    return;
                }
            pos = StartPoint;
            inward = Vector3.right;
        }

        static float Sign(System.Random r) => r.NextDouble() < 0.5 ? -1f : 1f;
    }
}
