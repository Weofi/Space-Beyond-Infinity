using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// Server-only director of the system the crew is in (#46, split out of RunManager): spawns the system's content
    /// (planets with their points of interest, wrecks, the nomad station, the Warden enclave), runs enemy spawning
    /// and random events, and clears everything again when the crew warps on. Difficulty and loot come from the
    /// galaxy; what the system holds comes from its kind. Plain class owned by RunManager (no network object).
    /// </summary>
    public class SystemDirector
    {
        readonly RunManager rm;
        readonly List<NetworkObject> objects = new List<NetworkObject>();

        public GalaxyPlan Plan { get; private set; }
        public StarSystem System { get; private set; }
        public SystemLayout Layout { get; private set; }
        public GalaxyDef Def => Plan != null ? Plan.Def : null;
        public int Galaxy => Plan != null ? Plan.Index : 1;
        /// <summary>The Warden of this system's enclave is (or was) awake.</summary>
        public bool BossSpawned { get; private set; }
        public bool FlareActive => Time.time < flareUntil;

        float spawnTimer;
        float startTime;
        float eventTimer;
        int eventsLeft;
        float flareUntil;
        float meteorUntil;
        float meteorTimer;
        bool enclaveCleared;

        public SystemDirector(RunManager rm) { this.rm = rm; }

        // ================================================================ enter / clear

        /// <summary>
        /// Spawn the content of a system. A revisited system keeps its worlds, nests and resource nodes, but its
        /// loot crates were already handed out; a defeated enclave stays empty.
        /// </summary>
        public void Enter(GalaxyPlan plan, int systemIndex, bool revisit, bool enclaveDefeated)
        {
            Clear();
            Plan = plan;
            System = plan.Systems[Mathf.Clamp(systemIndex, 0, plan.Systems.Length - 1)];
            Layout = SystemLayout.Build(System.Seed, plan.Def, System.Kind, System.HasStation);
            Layout.AddJumpZones(plan, System.Index);
            enclaveCleared = enclaveDefeated;
            BossSpawned = enclaveDefeated;
            var cat = ContentCatalog.Instance;
            int loot = revisit ? 0 : LootTier;

            for (int i = 0; i < Layout.Planets.Length; i++)
            {
                var ps = Layout.Planets[i];
                var biome = cat.Biomes[Mathf.Clamp(ps.BiomeIndex, 0, cat.Biomes.Length - 1)];
                var planet = PlanetBody.SpawnServer(ps.Seed, ps.Radius, biome, ps.Pos);
                if (planet == null) continue;
                Track(planet.NetworkObject);
                PopulatePlanet(planet, biome, ps.Seed, loot);
            }

            if (Layout.HasStation)
            {
                var st = SpawnPrefab(cat.TraderStation, Layout.StationPos, Quaternion.Euler(0f, 90f, 0f));
                if (st != null)
                {
                    var ts = st.GetComponent<TraderStation>();
                    ts.Seed.Value = System.Seed;
                    ts.Sector.Value = Mathf.Clamp(Galaxy, 1, 3);
                    ts.Galaxy.Value = Galaxy;
                }
            }

            var rng = new System.Random(System.Seed ^ 0x5f3759df);
            foreach (var w in Layout.Wrecks) SpawnWreckSite(w, rng, loot);
            if (System.Kind == SystemKind.Anomaly && !revisit) SpawnExoticClusters(rng);

            var def = Def;
            eventsLeft = (def != null ? def.EventCount : 2) + (System.Kind == SystemKind.Anomaly ? 2 : 0);
            eventTimer = System.Kind == SystemKind.Anomaly ? Random.Range(40f, 70f) : System.Kind == SystemKind.Void ? Random.Range(25f, 45f) : Random.Range(70f, 140f);
            spawnTimer = 40f;
            startTime = Time.time;
            meteorUntil = flareUntil = 0f;
        }

        public void Clear()
        {
            foreach (var no in objects) if (no != null && no.IsSpawned) no.Despawn(true);
            objects.Clear();
            foreach (var e in EnemyFactory.Alive.ToArray()) if (e != null && e.IsSpawned) e.NetworkObject.Despawn(true);
            EnemyFactory.Alive.Clear();
            foreach (var p in WorldPickup.All.ToArray()) if (p != null && p.IsSpawned) p.NetworkObject.Despawn(true);
            if (BossBrain.Instance != null && BossBrain.Instance.IsSpawned) BossBrain.Instance.NetworkObject.Despawn(true);
            rm.BossActive.Value = false;
            foreach (var s in ShipCore.All) if (s != null && s.ShieldsOffline.Value) s.ShieldsOffline.Value = false;
        }

        int LootTier => Mathf.Clamp(Def != null ? Def.LootTier : 1, 1, 3);
        float EnemyTier => Def != null ? Def.EnemyTier : 1f;

        // ================================================================ content

        /// <summary>
        /// Points of interest cluster at the planet's sites (flattened clearings next to landforms), so each landing
        /// zone holds a worthwhile activity: ruins, crash sites, hives, resource fields or supply caches.
        /// loot = crate tier (0 = no crates, the system was already looted).
        /// </summary>
        void PopulatePlanet(PlanetBody planet, BiomeDef biome, int seed, int loot)
        {
            var terrain = planet.Terrain;
            if (terrain == null) return;
            var rng = new System.Random(seed);
            var cat = ContentCatalog.Instance;
            int g = Galaxy;
            int richLoot = loot > 0 ? Mathf.Clamp(loot + 1, 1, 3) : 0;
            int Amount() => rng.Next(6, 12) + Mathf.Min(g, 3) * 2;
            void Crate(Vector3 p, Quaternion r, int tier) { if (tier > 0) Track(LootCrate.SpawnServer(p, r, tier)?.NetworkObject); }
            int sites = terrain.Sites.Count;
            int hives = 0;
            for (int i = 0; i < sites; i++)
            {
                Vector3 c = planet.SiteWorldPoint(i);
                Vector3 up = (c - planet.transform.position).normalized;
                Vector3 fwd = Vector3.ProjectOnPlane(MathUtils.OnSphere(rng), up).normalized;
                Quaternion rot = Quaternion.LookRotation(fwd, up);
                switch (planet.SiteKinds[i])
                {
                    case SiteKind.Ruins:
                        SpawnPrefab(cat.RuinPrefab, c, rot);
                        // the relic (16 m, #99): its ring plate reaches ~4.5 m, the arms run along the axes; the cache sits between two arms
                        Crate(c + up * 0.5f + rot * new Vector3(4.2f, 0f, 4.2f), rot, richLoot);
                        Ring(planet, c, 12f, 24f, 2, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.CrashSite:
                        SpawnPrefab(cat.CrashedShipPrefab, c, rot);
                        // crashed Dart (#99): hull x -4..8 / z -8..6, the torn wing lies at x -14..-5 / z -4..0, debris in front
                        Crate(c + up * 0.5f + rot * new Vector3(-5f, 0f, -8f), rot, loot);
                        Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, c + rot * new Vector3(12f, 0f, 2f), rot, 10)?.NetworkObject);
                        Ring(planet, c, 14f, 26f, 1, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.FuelNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.Hive:
                        hives++;
                        var nest = SpawnPrefab(cat.NestPrefab, c, rot);
                        if (nest != null) nest.GetComponent<AlienNest>()?.Configure(EnemyTier);
                        Ring(planet, c, 10f, 22f, 2, rng, (p, r) => Crate(p + r * Vector3.up * 0.5f, r, richLoot));
                        Ring(planet, c, 12f, 26f, 2, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.CrystalNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.Cache:
                        Ring(planet, c, 2f, 10f, 3, rng, (p, r) => Crate(p + r * Vector3.up * 0.5f, r, richLoot));
                        break;
                    default:
                        var kind = planet.SiteKinds[i] switch
                        {
                            SiteKind.CrystalGrove => ScatterKind.CrystalNode,
                            SiteKind.FuelVent => ScatterKind.FuelNode,
                            SiteKind.Salvage => ScatterKind.ScrapNode,
                            _ => ScatterKind.OreNode,
                        };
                        Ring(planet, c, 4f, 26f, rng.Next(4, 7), rng, (p, r) => Track(ResourceNode.SpawnServer(kind, p, r, Amount())?.NetworkObject));
                        Crate(c + up * 0.5f, rot, loot);
                        break;
                }
            }

            // Extra nests from the biome table guard the approaches between sites.
            int nests = 0;
            foreach (var e in biome.Scatter) if (e.Kind == ScatterKind.Nest) nests += e.Count;
            for (int k = hives; k < nests && sites > 0; k++)
            {
                Vector3 c = planet.SiteWorldPoint(rng.Next(sites));
                Ring(planet, c, 70f, 140f, 1, rng, (p, r) =>
                {
                    var nest = SpawnPrefab(cat.NestPrefab, p, r);
                    if (nest != null) nest.GetComponent<AlienNest>()?.Configure(EnemyTier);
                });
            }
        }

        /// <summary>Spawns `count` things on dry, gentle ground at minDist..maxDist around a surface point.</summary>
        static void Ring(PlanetBody planet, Vector3 center, float minDist, float maxDist, int count, System.Random rng, System.Action<Vector3, Quaternion> spawn)
        {
            Vector3 c = planet.transform.position;
            Vector3 up = (center - c).normalized;
            for (int i = 0; i < count; i++)
            {
                for (int tries = 0; tries < 8; tries++)
                {
                    Vector3 t = Vector3.ProjectOnPlane(MathUtils.OnSphere(rng), up).normalized;
                    float d = MathUtils.Range(rng, minDist, maxDist);
                    Vector3 dir = (center + t * d - c).normalized;
                    if (!SurfacePoint(planet, dir, out var pos, out var n)) continue;
                    spawn(pos, Quaternion.LookRotation(Vector3.ProjectOnPlane(t, n).normalized, n));
                    break;
                }
            }
        }

        static bool SurfacePoint(PlanetBody planet, Vector3 dir, out Vector3 pos, out Vector3 up)
        {
            pos = planet.SurfaceWorldPoint(dir);
            up = planet.SurfaceNormal(dir, 1.5f);
            if (planet.IsUnderwater(pos)) return false;
            if (Vector3.Dot(up, dir) < 0.8f) return false; // skip steep slopes
            up = Vector3.Slerp(dir, up, 0.5f).normalized;
            return true;
        }

        /// <summary>Anomalies (#78): unstable space condenses Exotic Matter - a few drifting clusters in each field.</summary>
        void SpawnExoticClusters(System.Random rng)
        {
            foreach (var f in Layout.Fields)
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = f.Center + Vector3.Scale(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), f.Extents);
                    var pk = WorldPickup.SpawnServer(WarpDrive.ExoticId, 2 + rng.Next(0, 2), p);
                    if (pk != null) Track(pk.NetworkObject);
                }
        }

        /// <summary>A derelict hull in space with salvage; crates only when loot &gt; 0.</summary>
        public void SpawnWreckSite(Vector3 pos, System.Random rng, int loot)
        {
            var cat = ContentCatalog.Instance;
            var rot = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
            SpawnPrefab(cat.Wreck, pos, rot);
            if (loot > 0)
                for (int i = 0; i < 2; i++)
                    Track(LootCrate.SpawnServer(pos + rot * new Vector3(i == 0 ? 12f : -12f, i == 0 ? 2f : 0f, i == 0 ? 6f : -8f), rot, Mathf.Clamp(loot, 1, 3))?.NetworkObject);
            // the destroyer section (#99) is ~16 x 17 x 47 m around the origin: everything floats just outside its hull
            Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, pos + rot * new Vector3(0f, -11f, 14f), rot, 12)?.NetworkObject);
            Track(ResourceNode.SpawnServer(ScatterKind.FuelNode, pos + rot * new Vector3(0f, 12f, -12f), rot, 6)?.NetworkObject);
            // Wrecks carry what was left of their own drive: Exotic Matter, sometimes a warp core one tier below the enclave's.
            if (loot > 0)
            {
                Track(WorldPickup.SpawnServer(WarpDrive.ExoticId, 1 + rng.Next(0, 3), pos + rot * new Vector3(0f, 12f, 4f))?.NetworkObject);
                if (rng.NextDouble() < 0.2)
                    Track(WorldPickup.SpawnServer(DriveComponentDef.ItemId(DriveSlot.Core, Mathf.Max(1, DriveTier - 1)), 1, pos + rot * new Vector3(3f, 12f, 4f))?.NetworkObject);
            }
        }

        /// <summary>Warp core tier guarded by this galaxy's enclave (2 = Mk II ... 6 = Home Drive).</summary>
        int DriveTier => Def != null ? Def.DriveTier : Galaxy + 1;

        public GameObject SpawnPrefab(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            Track(no);
            return go;
        }

        public void Track(NetworkObject no) { if (no != null) objects.Add(no); }

        /// <summary>Number of tracked objects (mark before spawning a warp signal site, then TakeTracked).</summary>
        public int TrackedCount => objects.Count;

        /// <summary>Hand the objects tracked since `mark` over to the caller (they are no longer cleared with the system).</summary>
        public List<NetworkObject> TakeTracked(int mark)
        {
            mark = Mathf.Clamp(mark, 0, objects.Count);
            var taken = objects.GetRange(mark, objects.Count - mark);
            objects.RemoveRange(mark, objects.Count - mark);
            return taken;
        }

        /// <summary>A sunless rogue planet met in the void between galaxies (#79): frozen or crystal, with its points of interest.</summary>
        public PlanetBody SpawnRoguePlanet(int seed, float radius, Vector3 pos, int loot)
        {
            var cat = ContentCatalog.Instance;
            if (cat == null || cat.Biomes.Length == 0) return null;
            var biome = cat.Biomes[Mathf.Abs(seed) % 2 == 0 ? 0 : Mathf.Min(3, cat.Biomes.Length - 1)];
            var planet = PlanetBody.SpawnServer(seed, radius, biome, pos);
            if (planet == null) return null;
            Track(planet.NetworkObject);
            PopulatePlanet(planet, biome, seed, loot);
            return planet;
        }

        // ================================================================ per frame

        public void Tick(float dt)
        {
            if (Layout == null) return;
            TickSpawns(dt);
            TickEvents(dt);
            TickEnclave();
        }

        // ---------------------------------------------------------------- Warden enclave

        void TickEnclave()
        {
            if (System.Kind != SystemKind.Enclave || BossSpawned) return;
            foreach (var p in PlayerPawn.All)
            {
                if (p == null || !p.Active.Value || p.IsDown.Value) continue;
                if (p.transform.position.x > Layout.BossPos.x - 1300f) { SpawnWarden(Layout.BossPos); return; }
            }
        }

        /// <summary>Wake the enclave's Warden (also the F1 cheat / DevTest, anywhere).</summary>
        public void SpawnWarden(Vector3 pos)
        {
            BossSpawned = true;
            var cat = ContentCatalog.Instance;
            if (cat.BossMothership == null || BossBrain.Instance != null) return;
            var go = Object.Instantiate(cat.BossMothership, pos, Quaternion.LookRotation(Vector3.left));
            int players = 0;
            foreach (var p in PlayerPawn.All) if (p != null && p.Active.Value && !p.IsLost.Value) players++;
            // Tuned for the old sector 3 (tier 2): the first enclaves are gentler, the LMC one harder.
            go.GetComponent<BossBrain>().Configure(players, Mathf.Clamp(EnemyTier / 2f, 0.5f, 1.4f));
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            Track(no);
            rm.BossActive.Value = true;
            rm.Announce("WARNING: A Warden wakes up!\nDestroy its 3 glowing core pods, then the hull.", "boss_roar");
        }

        /// <summary>The Warden fell: its hoard spills out - the galaxy's best warp core, a matching drive part and crates.</summary>
        public void OnWardenDefeated(Vector3 at)
        {
            enclaveCleared = true;
            rm.BossActive.Value = false;
            var rot = Quaternion.identity;
            for (int i = 0; i < 4; i++)
                Track(LootCrate.SpawnServer(at + Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(14f, 4f, 0f), rot, 3)?.NetworkObject);
            int tier = DriveTier;
            var core = DriveComponentDef.ItemId(DriveSlot.Core, tier);
            var part = DriveComponentDef.ItemId((DriveSlot)Random.Range(1, 4), tier);
            Track(WorldPickup.SpawnServer(core, 1, at)?.NetworkObject);
            Track(WorldPickup.SpawnServer(part, 1, at)?.NetworkObject);
            Track(WorldPickup.SpawnServer(WarpDrive.ExoticId, 6, at)?.NetworkObject);
            rm.PingAll(at, "Warden hoard");
            rm.Announce($"The Warden is destroyed! Its hoard is yours:\n{DriveComponentDef.FullName(DriveSlot.Core, tier)}", "unlock");
        }

        public bool EnclaveCleared => enclaveCleared;

        // ---------------------------------------------------------------- enemies

        /// <summary>
        /// Space enemy cap right now: 0 during the system's grace period, then ramping from 1 up to
        /// SpaceEnemyCap so the crew gets time to look around before hostiles show up.
        /// </summary>
        int SpaceEnemyCapNow()
        {
            var def = Def;
            if (def == null) return 0;
            float t = Time.time - startTime - def.SpaceEnemyGrace;
            if (t < 0f) return 0;
            int cap = Mathf.Max(1, def.SpaceEnemyCap + (System.Kind == SystemKind.Nomads ? -2 : 0));
            return Mathf.Clamp(1 + Mathf.FloorToInt((cap - 1) * t / Mathf.Max(1f, def.SpaceEnemyRamp)), 1, cap);
        }

        void TickSpawns(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = 9f;
            var def = Def;
            if (def == null) return;
            float tier = EnemyTier;

            // Despawn stragglers far from everyone.
            foreach (var e in EnemyFactory.Alive.ToArray())
            {
                if (e == null || !e.IsSpawned) continue;
                if (NearestPlayerDist(e.transform.position) > 1600f) e.NetworkObject.Despawn(true);
            }

            foreach (var p in PlayerPawn.All)
            {
                if (p == null || !p.Active.Value || p.IsDown.Value) continue;
                var src = GravitySource.Dominant(p.transform.position);
                bool onPlanet = src != null && src.SurfaceDistance(p.transform.position) < 120f;
                if (onPlanet)
                {
                    int near = EnemyFactory.CountWhere(e => !e.IsSpaceEnemy && Vector3.Distance(e.transform.position, p.transform.position) < 150f);
                    if (near >= 3 + Galaxy || def.SurfaceEnemies.Length == 0 || Random.value < 0.35f) continue;
                    var kind = def.SurfaceEnemies[Random.Range(0, def.SurfaceEnemies.Length)];
                    Vector3 up = src.UpAt(p.transform.position);
                    Vector3 tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up).normalized;
                    Vector3 dir = (p.transform.position + tangent * Random.Range(60f, 90f) - src.Center).normalized;
                    Vector3 pos = src.Center + dir * (src.GroundRadius(src.Center + dir) + 1f);
                    EnemyFactory.Spawn(kind, pos, Quaternion.LookRotation(tangent, dir), tier);
                }
                else
                {
                    int space = EnemyFactory.CountWhere(e => e.IsSpaceEnemy);
                    if (space >= SpaceEnemyCapNow() || def.SpaceEnemies.Length == 0 || Random.value < 0.55f) continue;
                    var kind = def.SpaceEnemies[Random.Range(0, def.SpaceEnemies.Length)];
                    int group = kind == EnemyKind.DroneSwarm ? 3 : kind == EnemyKind.PirateGunship ? 1 : Random.Range(1, 3);
                    Vector3 basePos = p.transform.position + new Vector3(Random.Range(250f, 500f), Random.Range(-150f, 150f), Random.Range(-350f, 350f));
                    for (int i = 0; i < group; i++)
                        EnemyFactory.Spawn(kind, basePos + Random.insideUnitSphere * 30f, Quaternion.LookRotation(p.transform.position - basePos), tier);
                }
            }
        }

        /// <summary>Spawn one enemy of this galaxy's mix (surface or space list) for cheats and tests.</summary>
        public void SpawnEnemyNear(Vector3 pos, Vector3 facing, bool surface)
        {
            var def = Def;
            if (def == null) return;
            var list = surface ? def.SurfaceEnemies : def.SpaceEnemies;
            if (list.Length == 0) return;
            EnemyFactory.Spawn(list[Random.Range(0, list.Length)], pos, Quaternion.LookRotation(facing), EnemyTier);
        }

        static float NearestPlayerDist(Vector3 pos)
        {
            float best = float.MaxValue;
            foreach (var p in PlayerPawn.All)
                if (p != null && p.Active.Value) best = Mathf.Min(best, Vector3.Distance(p.transform.position, pos));
            return best;
        }

        // ---------------------------------------------------------------- events

        void TickEvents(float dt)
        {
            if (Time.time < meteorUntil)
            {
                meteorTimer -= dt;
                if (meteorTimer <= 0f)
                {
                    meteorTimer = 0.45f;
                    var targets = PlayerPawn.All.FindAll(p => p != null && p.Active.Value && !p.IsDown.Value);
                    if (targets.Count > 0)
                    {
                        var t = targets[Random.Range(0, targets.Count)];
                        Vector3 aim = t.transform.position + Random.insideUnitSphere * 120f;
                        Vector3 from = aim + new Vector3(Random.Range(-200f, 200f), Random.Range(300f, 450f), Random.Range(-200f, 200f));
                        CombatNet.ServerFire(ProjectileKind.Meteor, from, aim - from, 40f, Team.Neutral);
                    }
                }
            }
            if (Time.time < flareUntil) return;
            foreach (var s in ShipCore.All) if (s != null && s.ShieldsOffline.Value) s.ShieldsOffline.Value = false;

            if (eventsLeft <= 0 || rm.BossActive.Value) return;
            eventTimer -= dt;
            if (eventTimer > 0f) return;
            bool first = eventsLeft == (Def != null ? Def.EventCount : 2) + (System.Kind == SystemKind.Anomaly ? 2 : 0);
            eventsLeft--;
            eventTimer = System.Kind == SystemKind.Anomaly ? Random.Range(60f, 110f) : Random.Range(110f, 200f);
            SectorEventKind kind;
            if (System.Kind == SystemKind.Void && first) kind = SectorEventKind.DistressSignal;
            else if (System.Kind == SystemKind.Anomaly) kind = Random.value < 0.5f ? SectorEventKind.SolarFlare : SectorEventKind.MeteorShower;
            else kind = (SectorEventKind)Random.Range(0, 3);
            TriggerEvent(kind);
        }

        public void TriggerEvent(SectorEventKind k)
        {
            switch (k)
            {
                case SectorEventKind.MeteorShower:
                    meteorUntil = Time.time + 25f;
                    rm.Announce("METEOR SHOWER! Take cover or keep moving.", "alarm");
                    break;
                case SectorEventKind.SolarFlare:
                    flareUntil = Time.time + 18f;
                    foreach (var s in ShipCore.All) if (s != null) s.ShieldsOffline.Value = true;
                    rm.Announce("SOLAR FLARE! Shields offline. Get inside a ship or under an atmosphere.", "alarm");
                    break;
                case SectorEventKind.DistressSignal:
                    Vector3 c = rm.TeamCenter() + new Vector3(Random.Range(350f, 600f), Random.Range(-80f, 80f), Random.Range(-250f, 250f));
                    var rng = new System.Random(Random.Range(1, 99999));
                    SpawnWreckSite(c, rng, Mathf.Clamp(LootTier + 1, 1, 3));
                    var def = Def;
                    if (def != null && def.SpaceEnemies.Length > 0 && SpaceEnemyCapNow() > 0)
                        for (int i = 0; i < 1 + Galaxy; i++)
                            EnemyFactory.Spawn(def.SpaceEnemies[0], c + Random.onUnitSphere * 60f, Quaternion.identity, EnemyTier);
                    rm.PingAll(c, "Distress signal");
                    rm.Announce("DISTRESS SIGNAL detected - salvage awaits (beware of hostiles).", "ping");
                    break;
            }
        }
    }
}
