using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>One star system of a galaxy (generated, never stored as an asset).</summary>
    public class StarSystem
    {
        public int Index;
        public SystemKind Kind;
        public string Name;
        public int Seed;
        /// <summary>Position on the galaxy map, 0..1 in both axes (arrival on the left).</summary>
        public Vector2 MapPos;
        /// <summary>1..5 for the galaxy map.</summary>
        public int Threat;
        /// <summary>A nomad trader station sits in this system.</summary>
        public bool HasStation;
    }

    /// <summary>A galaxy of the current loop: its definition plus its 4-6 generated systems.</summary>
    public class GalaxyPlan
    {
        public int Index;
        public int Seed;
        public GalaxyDef Def;
        public StarSystem[] Systems;
    }

    /// <summary>
    /// Deterministic galaxy generation (#46): the same run seed gives the same systems on every peer and after
    /// loading a suspend save. Pure data, no scene access. System 0 is the arrival system (always planetary);
    /// every galaxy has exactly one Warden enclave, placed farthest from the arrival.
    /// </summary>
    public static class GalaxyGenerator
    {
        static readonly string[] Syllables = { "ka", "ve", "lor", "thi", "ma", "ser", "on", "qua", "dra", "ny", "el", "zu", "ri", "tor", "phe", "al", "os", "ix", "ren", "sa" };

        public static int GalaxySeed(int runSeed, int galaxy) => (int)(((uint)runSeed * 2654435761u) ^ ((uint)galaxy * 40503u + 0x9E3779B9u)) & int.MaxValue | 1;

        public static GalaxyPlan Generate(int runSeed, int galaxy)
        {
            var def = ContentCatalog.GetGalaxy(galaxy);
            int seed = GalaxySeed(runSeed, galaxy);
            var rng = new System.Random(seed);
            int min = def != null ? def.MinSystems : 4, max = def != null ? def.MaxSystems : 6;
            int n = Mathf.Clamp(rng.Next(min, max + 1), 4, 6);

            var kinds = new SystemKind[n];
            var pool = new[] { SystemKind.Planetary, SystemKind.Planetary, SystemKind.Planetary, SystemKind.AsteroidField, SystemKind.AsteroidField, SystemKind.Anomaly, SystemKind.Void };
            for (int i = 1; i < n; i++) kinds[i] = pool[rng.Next(pool.Length)];
            kinds[0] = SystemKind.Planetary;
            int enclave = n - 1;
            kinds[enclave] = SystemKind.Enclave;
            if (rng.NextDouble() < 0.6) kinds[1 + rng.Next(n - 2)] = SystemKind.Nomads;
            // At least two planetary systems: planets are where drive parts and exotic matter are.
            int planetary = 0;
            foreach (var k in kinds) if (k == SystemKind.Planetary) planetary++;
            for (int i = 1; i < enclave && planetary < 2; i++)
                if (kinds[i] != SystemKind.Nomads && kinds[i] != SystemKind.Planetary) { kinds[i] = SystemKind.Planetary; planetary++; }

            var plan = new GalaxyPlan { Index = galaxy, Seed = seed, Def = def, Systems = new StarSystem[n] };
            var pos = MapPositions(n, rng);
            for (int i = 0; i < n; i++)
            {
                var s = new StarSystem { Index = i, Kind = kinds[i], Seed = rng.Next(1, int.MaxValue), MapPos = pos[i] };
                s.Name = galaxy == 1 && i == 0 ? "Wormhole Exit" : SystemName(rng);
                s.HasStation = s.Kind == SystemKind.Nomads || (galaxy == 1 && i == 0);
                int bonus = s.Kind == SystemKind.Enclave ? 2 : s.Kind == SystemKind.Anomaly ? 1 : s.Kind == SystemKind.Nomads || i == 0 ? -1 : 0;
                s.Threat = Mathf.Clamp(Mathf.RoundToInt(galaxy * 0.7f + 0.3f) + bonus, 1, 5);
                plan.Systems[i] = s;
            }
            return plan;
        }

        /// <summary>Arrival on the left, enclave (last index) on the far right, the rest spread out in between.</summary>
        static Vector2[] MapPositions(int n, System.Random rng)
        {
            var p = new Vector2[n];
            p[0] = new Vector2(0.08f, MathUtils.Range(rng, 0.35f, 0.65f));
            p[n - 1] = new Vector2(0.92f, MathUtils.Range(rng, 0.3f, 0.7f));
            for (int i = 1; i < n - 1; i++)
            {
                Vector2 best = default;
                float bestD = -1f;
                for (int t = 0; t < 24; t++)
                {
                    // Columns from left to right keep the map readable: the index roughly follows the distance.
                    float x = Mathf.Lerp(0.22f, 0.78f, (i - 1 + (float)rng.NextDouble()) / Mathf.Max(1, n - 2));
                    var c = new Vector2(x, MathUtils.Range(rng, 0.12f, 0.88f));
                    float d = float.MaxValue;
                    for (int j = 0; j < i; j++) d = Mathf.Min(d, Vector2.Distance(c, p[j]));
                    d = Mathf.Min(d, Vector2.Distance(c, p[n - 1]));
                    if (d > bestD) { bestD = d; best = c; }
                }
                p[i] = best;
            }
            return p;
        }

        static string SystemName(System.Random rng)
        {
            string a = Syllables[rng.Next(Syllables.Length)], b = Syllables[rng.Next(Syllables.Length)];
            string name = char.ToUpperInvariant(a[0]) + a.Substring(1) + b;
            if (rng.NextDouble() < 0.35) name += " " + (rng.Next(2, 9)).ToString();
            return name;
        }

        /// <summary>Layout of one system of a galaxy (shared by server spawning and client visuals).</summary>
        public static SystemLayout Layout(int runSeed, int galaxy, int system)
        {
            var plan = Generate(runSeed, galaxy);
            var s = plan.Systems[Mathf.Clamp(system, 0, plan.Systems.Length - 1)];
            return SystemLayout.Build(s.Seed, plan.Def, s.Kind, s.HasStation);
        }

        public static string KindLabel(SystemKind k)
        {
            switch (k)
            {
                case SystemKind.Planetary: return "Planetary system";
                case SystemKind.AsteroidField: return "Asteroid & wreck field";
                case SystemKind.Anomaly: return "Anomaly";
                case SystemKind.Enclave: return "Warden enclave";
                case SystemKind.Nomads: return "Nomad meeting point";
                default: return "Empty space";
            }
        }

        public static string KindHint(SystemKind k)
        {
            switch (k)
            {
                case SystemKind.Planetary: return "Worlds to explore: resources, ruins, wrecks - and what lives there.";
                case SystemKind.AsteroidField: return "Dense rock and old wrecks full of salvage. Few planets.";
                case SystemKind.Anomaly: return "Unstable space: flares and meteor storms come often.";
                case SystemKind.Enclave: return "A Warden guards a warp core here. The hardest fight in the galaxy.";
                case SystemKind.Nomads: return "Neutral alien nomads trade here.";
                default: return "Nothing on the scans - but signals have been picked up.";
            }
        }
    }
}
