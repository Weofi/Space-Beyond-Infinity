using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>
    /// Deterministic sector layout shared by server (spawning) and clients (local visuals).
    /// The corridor runs along +X and is centered on the origin: every sector is rebuilt around
    /// (0,0,0), which is our floating-origin strategy (world rebasing on each jump keeps all
    /// coordinates within ~3 km, so float precision stays sub-millimetre in multiplayer).
    /// </summary>
    public class SectorLayout
    {
        public struct PlanetSpot { public Vector3 Pos; public float Radius; public int BiomeIndex; public int Seed; }
        public struct Field { public Vector3 Center; public Vector3 Extents; public int Count; public int Seed; }

        public int Seed;
        public SectorDef Def;
        public float Length;
        public float StartX;
        public float EndX;
        public Vector3 StartPoint;
        public Vector3 GatePos;
        public Vector3 StationPos;
        public Vector3 BossPos;
        public Vector3 ArkPos;
        public PlanetSpot[] Planets;
        public Field[] Fields;
        public Vector3[] Wrecks;

        public static SectorLayout Build(int seed, SectorDef def)
        {
            var L = new SectorLayout { Seed = seed, Def = def };
            var rng = new System.Random(seed);
            L.Length = def != null ? def.CorridorLength : 4500f;
            L.StartX = -L.Length * 0.5f;
            L.EndX = L.Length * 0.5f;
            L.StartPoint = new Vector3(L.StartX + 80f, 0f, 0f);
            L.GatePos = new Vector3(L.EndX, 0f, 0f);
            L.BossPos = new Vector3(L.EndX - 300f, 40f, 0f);
            L.StationPos = new Vector3(MathUtils.Range(rng, -300f, 300f), MathUtils.Range(rng, -60f, 60f), MathUtils.Range(rng, 180f, 260f) * Sign(rng));
            L.ArkPos = new Vector3(L.StartX + 700f, 25f, -140f);

            int nPlanets = def != null ? rng.Next(def.MinPlanets, def.MaxPlanets + 1) : 4;
            L.Planets = new PlanetSpot[nPlanets];
            var cat = ContentCatalog.Instance;
            float usable = L.Length - 1400f;
            for (int i = 0; i < nPlanets; i++)
            {
                float t = (i + 0.5f) / nPlanets;
                float x = L.StartX + 700f + usable * t + MathUtils.Range(rng, -120f, 120f);
                float r = MathUtils.Range(rng, 150f, 300f);
                float side = Sign(rng);
                float off = r * 2.2f + MathUtils.Range(rng, 60f, 220f);
                float ang = MathUtils.Range(rng, -0.6f, 0.6f);
                Vector3 p = new Vector3(x, Mathf.Sin(ang) * off, Mathf.Cos(ang) * off * side);
                // keep clear of the station
                if (Vector3.Distance(p, L.StationPos) < r * 2.5f + 120f) p.z += side * (r * 2.5f + 150f);
                int biomeIndex = 0;
                if (def != null && def.BiomePool != null && def.BiomePool.Length > 0 && cat != null)
                {
                    var b = def.BiomePool[(i + rng.Next(0, 100)) % def.BiomePool.Length];
                    biomeIndex = System.Array.IndexOf(cat.Biomes, b);
                    if (biomeIndex < 0) biomeIndex = 0;
                }
                L.Planets[i] = new PlanetSpot { Pos = p, Radius = r, BiomeIndex = biomeIndex, Seed = rng.Next() };
            }

            int nFields = def != null ? def.AsteroidBelts : 3;
            L.Fields = new Field[nFields];
            for (int i = 0; i < nFields; i++)
            {
                float x = L.StartX + 500f + (L.Length - 900f) * ((i + 0.5f) / nFields) + MathUtils.Range(rng, -200f, 200f);
                Vector3 c = new Vector3(x, MathUtils.Range(rng, -120f, 120f), MathUtils.Range(rng, -160f, 160f));
                L.Fields[i] = new Field
                {
                    Center = c,
                    Extents = new Vector3(MathUtils.Range(rng, 120f, 220f), MathUtils.Range(rng, 50f, 110f), MathUtils.Range(rng, 120f, 220f)),
                    Count = rng.Next(45, 80),
                    Seed = rng.Next()
                };
            }

            int nWrecks = def != null ? def.WreckCount : 2;
            L.Wrecks = new Vector3[nWrecks];
            for (int i = 0; i < nWrecks; i++)
                L.Wrecks[i] = new Vector3(MathUtils.Range(rng, L.StartX + 400f, L.EndX - 500f), MathUtils.Range(rng, -80f, 80f), MathUtils.Range(rng, 80f, 200f) * Sign(rng));
            return L;
        }

        static float Sign(System.Random r) => r.NextDouble() < 0.5 ? -1f : 1f;
    }
}
