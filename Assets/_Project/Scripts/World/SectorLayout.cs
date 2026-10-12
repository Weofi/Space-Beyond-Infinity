using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>
    /// Deterministic sector layout shared by server (spawning) and clients (local visuals).
    /// The corridor runs along +X and is centered on the origin: every sector is rebuilt around
    /// (0,0,0), which is our floating-origin strategy (world rebasing on each jump keeps all
    /// coordinates within ~12 km incl. the kilometre-scale planets, so float precision stays around a
    /// millimetre in multiplayer). Distances are deliberately large (#22): crossing a sector is a trip.
    /// </summary>
    public class SectorLayout
    {
        public struct PlanetSpot { public Vector3 Pos; public float Radius; public int BiomeIndex; public int Seed; }
        public struct Field { public Vector3 Center; public Vector3 Extents; public int Count; public int Seed; }

        public const float MinPlanetRadius = 1000f;
        public const float MaxPlanetRadius = 1900f;

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
            L.Length = def != null ? def.CorridorLength : 9000f;
            L.StartX = -L.Length * 0.5f;
            L.EndX = L.Length * 0.5f;
            L.StartPoint = new Vector3(L.StartX + 80f, 0f, 0f);
            L.GatePos = new Vector3(L.EndX, 0f, 0f);
            L.BossPos = new Vector3(L.EndX - 300f, 40f, 0f);
            L.StationPos = new Vector3(MathUtils.Range(rng, -L.Length * 0.15f, L.Length * 0.15f), MathUtils.Range(rng, -120f, 120f), MathUtils.Range(rng, 350f, 600f) * Sign(rng));
            L.ArkPos = new Vector3(L.StartX + 1500f, 40f, -320f);

            int nPlanets = def != null ? rng.Next(def.MinPlanets, def.MaxPlanets + 1) : 3;
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
                    bool clear = Vector3.Distance(p, L.StationPos) > r * 1.4f + 400f;
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

            int nFields = def != null ? def.AsteroidBelts : 3;
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

            int nWrecks = def != null ? def.WreckCount : 2;
            L.Wrecks = new Vector3[nWrecks];
            for (int i = 0; i < nWrecks; i++)
                L.Wrecks[i] = new Vector3(MathUtils.Range(rng, L.StartX + 800f, L.EndX - 900f), MathUtils.Range(rng, -150f, 150f), MathUtils.Range(rng, 150f, 450f) * Sign(rng));
            return L;
        }

        static float Sign(System.Random r) => r.NextDouble() < 0.5 ? -1f : 1f;
    }
}
