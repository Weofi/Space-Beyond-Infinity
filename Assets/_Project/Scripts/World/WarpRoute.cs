using System.Collections.Generic;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.World
{
    /// <summary>Zones of a galaxy-to-galaxy leg (#79): dense halos at both galaxy rims, deep void in between.</summary>
    public enum WarpZone { ExitHalo, Void, ArrivalHalo }

    /// <summary>What can block the corridor (#79).</summary>
    public enum WarpHazard
    {
        /// <summary>Halo asteroid (small ones can be shot apart).</summary>
        Rock,
        /// <summary>Wreckage of a debris cloud: small, shootable, slows the bubble on impact.</summary>
        Debris,
        /// <summary>Icy body drifting across the corridor with a glowing tail.</summary>
        Comet,
        /// <summary>Space-time rift: a wall across the corridor with one gap to fly through.</summary>
        Rift,
        /// <summary>Gravity anomaly: pulls ships sideways; a close pass slingshots the bubble ahead, the core is deadly.</summary>
        GravityWell,
        /// <summary>A lone, huge asteroid in the void.</summary>
        StrayRock,
    }

    public enum WarpSignalKind { Wreck, GasCloud, RoguePlanet }

    /// <summary>One obstacle of the corridor. S = distance along the route (m), Lat = (y, z) offset from the axis when it passes the bubble.</summary>
    public struct WarpObstacle
    {
        public int Id;
        public WarpHazard Kind;
        public float S;
        public Vector2 Lat;
        /// <summary>Body radius; the gap radius for a rift.</summary>
        public float Radius;
        /// <summary>Lateral metres per metre of approach (comets cross the corridor).</summary>
        public Vector2 Drift;
        public int Seed;

        /// <summary>Lateral position while it is `ahead` metres in front of the bubble.</summary>
        public Vector2 LatAt(float ahead) => Lat + Drift * ahead;

        /// <summary>Hit points for shooting it apart; 0 = indestructible.</summary>
        public float Hp => Kind == WarpHazard.Rock ? 20f + Radius * 8f : Kind == WarpHazard.Debris ? 25f : Kind == WarpHazard.Comet ? Radius * 30f : 0f;

        /// <summary>Influence radius of a gravity well (m).</summary>
        public float Reach => Kind == WarpHazard.GravityWell ? 680f : Radius;

        /// <summary>Collision damage at full throttle (scaled down when slower).</summary>
        public float ImpactDamage
        {
            get
            {
                switch (Kind)
                {
                    case WarpHazard.Rock: return 35f + Radius * 3f;
                    case WarpHazard.Debris: return 10f;
                    case WarpHazard.Comet: return 150f;
                    case WarpHazard.Rift: return 90f;
                    case WarpHazard.GravityWell: return 260f;
                    default: return 220f;
                }
            }
        }
    }

    /// <summary>Something worth a detour off the route: drop out of warp next to it.</summary>
    public struct WarpSignal
    {
        public int Id;
        public WarpSignalKind Kind;
        public float S;
        public Vector2 Lat;
        public int Seed;

        public string Label => Kind == WarpSignalKind.Wreck ? "Derelict wreck" : Kind == WarpSignalKind.GasCloud ? "Exotic gas cloud" : "Rogue planet";
    }

    /// <summary>One galaxy-to-galaxy leg of the route (a skip has two).</summary>
    public struct WarpLeg
    {
        public int From, To;
        public float Start, End;
        /// <summary>Share of the leg at each rim (halo / system outskirts).</summary>
        public float Share;
        public float Length => End - Start;
        public float HaloEnd => Start + Length * Share;
        public float HaloStart => End - Length * Share;
    }

    /// <summary>
    /// Deterministic profile of one warp flight (#79), identical on every peer: legs, zones, obstacles and signals
    /// along a corridor of `Length` metres. Obstacles are generated per 1 km chunk on demand (a slow drive flies
    /// thousands of kilometres), ids are chunk * 1024 + index. A hop (#80) is the short warp between two systems of
    /// one galaxy: one leg, the rims are the systems' outskirts (thinner fields), no signals and no welcome party.
    /// </summary>
    public class WarpRoute
    {
        public const float ChunkLength = 1000f;
        /// <summary>Share of every leg at each galaxy rim (two halos = 24 %, rifts and wells add the rest of WarpDriveDef.FieldShare).</summary>
        public const float HaloShare = 0.12f;
        /// <summary>Share of a hop at each end (the systems' outskirts).</summary>
        public const float HopShare = 0.2f;
        /// <summary>No obstacles right after the start of the flight.</summary>
        const float SafeStart = 1500f;
        const int MaxPerChunk = 1024;

        public readonly int Seed;
        public readonly float Length;
        public readonly int From, To;
        /// <summary>A warp between two systems of galaxy From (#80).</summary>
        public readonly bool Hop;
        public readonly WarpLeg[] Legs;
        public readonly WarpSignal[] Signals;

        readonly Dictionary<int, List<WarpObstacle>> chunks = new Dictionary<int, List<WarpObstacle>>();
        readonly Queue<int> chunkOrder = new Queue<int>();

        public WarpRoute(int seed, int from, int to, float length, bool hop = false)
        {
            Seed = seed;
            From = from;
            To = to;
            Hop = hop;
            Length = Mathf.Max(ChunkLength * 4f, length);
            if (hop)
            {
                Legs = new[] { new WarpLeg { From = from, To = from, Start = 0f, End = Length, Share = HopShare } };
                Signals = new WarpSignal[0];
                return;
            }

            // Legs in proportion to the galaxies' leg lengths.
            int n = Mathf.Max(1, Mathf.Min(to, DriveMath.Home) - Mathf.Max(1, from));
            var weights = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                var def = ContentCatalog.GetGalaxy(from + i);
                weights[i] = def != null ? Mathf.Max(1f, def.LegLength) : 1f;
                sum += weights[i];
            }
            Legs = new WarpLeg[n];
            float s = 0f;
            for (int i = 0; i < n; i++)
            {
                float len = Length * weights[i] / sum;
                Legs[i] = new WarpLeg { From = from + i, To = from + i + 1, Start = s, End = i == n - 1 ? Length : s + len, Share = HaloShare };
                s += len;
            }

            // Three signals per leg, scattered over its void, off the corridor.
            var rng = new System.Random(seed ^ 0x51a7);
            var sig = new List<WarpSignal>();
            for (int i = 0; i < n; i++)
            {
                var leg = Legs[i];
                for (int k = 0; k < 3; k++)
                {
                    float t = (k + 0.2f + 0.6f * (float)rng.NextDouble()) / 3f;
                    float at = Mathf.Lerp(leg.HaloEnd + leg.Length * 0.05f, leg.HaloStart - leg.Length * 0.05f, t);
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = 700f + 800f * (float)rng.NextDouble();
                    sig.Add(new WarpSignal
                    {
                        Id = i * 8 + k, Kind = (WarpSignalKind)rng.Next(3), S = at,
                        Lat = new Vector2(Mathf.Sin(ang) * 0.45f, Mathf.Cos(ang)) * dist, Seed = rng.Next()
                    });
                }
            }
            Signals = sig.ToArray();
        }

        public static int ChunkOf(float s) => Mathf.FloorToInt(s / ChunkLength);
        public static int ChunkOfId(int id) => id / MaxPerChunk;

        /// <summary>Zone at route position s; galaxy = the galaxy whose rim it is (the leg's start galaxy in the void).</summary>
        public WarpZone ZoneAt(float s, out int galaxy)
        {
            var leg = LegAt(s);
            if (s < leg.HaloEnd) { galaxy = leg.From; return WarpZone.ExitHalo; }
            if (s >= leg.HaloStart) { galaxy = Mathf.Clamp(leg.To, 1, GalaxyDef.Count); return WarpZone.ArrivalHalo; }
            galaxy = leg.From;
            return WarpZone.Void;
        }

        public WarpLeg LegAt(float s)
        {
            for (int i = 0; i < Legs.Length; i++) if (s < Legs[i].End) return Legs[i];
            return Legs[Legs.Length - 1];
        }

        /// <summary>Start of the next zone of this kind at or after s (-1 if there is none).</summary>
        public float NextZone(WarpZone zone, float s)
        {
            foreach (var leg in Legs)
            {
                float start = zone == WarpZone.ExitHalo ? leg.Start : zone == WarpZone.Void ? leg.HaloEnd : leg.HaloStart;
                if (start >= s - 1f) return start;
            }
            return -1f;
        }

        public static string ZoneLabel(WarpZone z) => z == WarpZone.ExitHalo ? "GALAXY HALO" : z == WarpZone.Void ? "DEEP VOID" : "ARRIVAL HALO";

        /// <summary>HUD name of a zone of this route (a hop crosses system outskirts and interstellar space).</summary>
        public string ZoneName(WarpZone z) =>
            !Hop ? ZoneLabel(z) : z == WarpZone.ExitHalo ? "SYSTEM OUTSKIRTS" : z == WarpZone.Void ? "INTERSTELLAR" : "APPROACH";

        /// <summary>Obstacles with from &lt;= S &lt; to, in route order.</summary>
        public void Query(float from, float to, List<WarpObstacle> into)
        {
            into.Clear();
            int c0 = Mathf.Max(0, ChunkOf(from)), c1 = ChunkOf(Mathf.Min(to, Length));
            for (int c = c0; c <= c1; c++)
                foreach (var o in Chunk(c))
                    if (o.S >= from && o.S < to) into.Add(o);
        }

        public bool TryGet(int id, out WarpObstacle o)
        {
            foreach (var x in Chunk(ChunkOfId(id)))
                if (x.Id == id) { o = x; return true; }
            o = default;
            return false;
        }

        /// <summary>Interceptor patrol of the halo galaxy in this chunk (at route position s)?</summary>
        public bool PatrolIn(int chunk, out float s, out int galaxy)
        {
            s = (chunk + 0.5f) * ChunkLength;
            var zone = ZoneAt(s, out galaxy);
            if (zone == WarpZone.Void || s < SafeStart * 2f) return false;
            var leg = LegAt(s);
            // Every arrival halo of a galaxy has a welcome party a little way in; elsewhere patrols are random.
            if (!Hop && zone == WarpZone.ArrivalHalo && chunk == ChunkOf(leg.HaloStart) + 3) return true;
            var rng = new System.Random(Hash(chunk) ^ 0x7a7);
            return rng.NextDouble() < (zone == WarpZone.ArrivalHalo ? 0.05 : 0.025);
        }

        int Hash(int chunk) => unchecked(Seed * 73856093 ^ (chunk + 1) * 19349663);

        public List<WarpObstacle> Chunk(int index)
        {
            if (chunks.TryGetValue(index, out var list)) return list;
            list = Generate(index);
            chunks[index] = list;
            chunkOrder.Enqueue(index);
            while (chunkOrder.Count > 48) chunks.Remove(chunkOrder.Dequeue());
            return list;
        }

        List<WarpObstacle> Generate(int index)
        {
            var list = new List<WarpObstacle>();
            float c0 = index * ChunkLength;
            if (index < 0 || c0 >= Length) return list;
            var rng = new System.Random(Hash(index));
            float W = WarpBubble.HalfWidth + 80f;
            int id = index * MaxPerChunk;

            void Add(WarpHazard kind, float s, Vector2 lat, float r, Vector2 drift = default)
            {
                if (s < SafeStart || s >= Length - 200f || id >= (index + 1) * MaxPerChunk) return;
                list.Add(new WarpObstacle { Id = id++, Kind = kind, S = s, Lat = lat, Radius = r, Drift = drift, Seed = rng.Next() });
            }
            float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            Vector2 Lat(float half) => new Vector2(R(-half, half), R(-half, half));

            var zone = ZoneAt(c0 + ChunkLength * 0.5f, out _);
            if (zone != WarpZone.Void)
            {
                // Halo: an asteroid field over the whole corridor (~1 rock per 4 m of route; system outskirts are thinner) ...
                int rocks = 230 + rng.Next(40);
                if (Hop) rocks = rocks * 2 / 5;
                for (int i = 0; i < rocks; i++)
                    Add(WarpHazard.Rock, c0 + R(0f, ChunkLength), Lat(W), Mathf.Lerp(5f, 28f, Mathf.Pow((float)rng.NextDouble(), 2.2f)));
                // ... now and then a debris cloud ...
                if (rng.NextDouble() < 0.3)
                {
                    float cs = c0 + R(100f, ChunkLength - 100f);
                    Vector2 cl = Lat(WarpBubble.HalfWidth * 0.7f);
                    int n = 24 + rng.Next(18);
                    for (int i = 0; i < n; i++)
                        Add(WarpHazard.Debris, cs + R(-90f, 90f), cl + Random2(rng) * 90f, R(1.5f, 4f));
                }
                // ... and comets crossing.
                if (rng.NextDouble() < 0.25) AddComet();
            }
            else
            {
                // Deep void: mostly empty, with rare but dangerous things.
                if (rng.NextDouble() < 0.12) Add(WarpHazard.StrayRock, c0 + R(0f, ChunkLength), Lat(WarpBubble.HalfWidth * 0.9f), R(35f, 70f));
                if (rng.NextDouble() < 0.025) Add(WarpHazard.Rift, c0 + R(0f, ChunkLength), Lat(WarpBubble.HalfWidth - 120f), R(70f, 110f));
                if (rng.NextDouble() < 0.02) Add(WarpHazard.GravityWell, c0 + R(0f, ChunkLength), Lat(WarpBubble.HalfWidth * 0.7f), R(45f, 70f));
                if (rng.NextDouble() < 0.02) AddComet();
            }
            list.Sort((a, b) => a.S.CompareTo(b.S));
            return list;

            void AddComet()
            {
                Vector2 dir = Random2(rng).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector2.right;
                Add(WarpHazard.Comet, c0 + R(0f, ChunkLength), Lat(WarpBubble.HalfWidth * 0.8f), R(22f, 40f), dir * R(0.15f, 0.3f));
            }
        }

        static Vector2 Random2(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Sqrt((float)rng.NextDouble());
        }
    }
}
