using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Networking;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Progression;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// The run director (lives on the GameDirector network object, spawned by the host).
    /// Server: run lifecycle, sector generation, ships, respawns, the Collapse, jump gates, events,
    /// enemy spawning, stats and results. Clients: mirror phase/sector/collapse into GameState and
    /// build local sector visuals.
    /// </summary>
    public class RunManager : NetworkBehaviour
    {
        public static RunManager Instance { get; private set; }

        public const int StartingRespawns = 4;

        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        public NetworkVariable<int> SectorIndex = new NetworkVariable<int>(1);
        public NetworkVariable<int> SectorSeed = new NetworkVariable<int>(0);
        public NetworkVariable<float> CollapseBaseX = new NetworkVariable<float>(-99999f);
        public NetworkVariable<double> CollapseBaseTime = new NetworkVariable<double>(0);
        public NetworkVariable<float> CollapseSpeed = new NetworkVariable<float>(0f);
        public NetworkVariable<int> RespawnsLeft = new NetworkVariable<int>(StartingRespawns);
        public NetworkVariable<float> JumpCountdown = new NetworkVariable<float>(-1f);
        public NetworkVariable<bool> BossActive = new NetworkVariable<bool>(false);

        // ---- server state ----
        readonly Dictionary<ulong, RunLoadout> loadouts = new Dictionary<ulong, RunLoadout>();
        readonly List<NetworkObject> sectorObjects = new List<NetworkObject>();
        readonly Dictionary<ulong, float> shipRespawnTimers = new Dictionary<ulong, float>();
        SectorLayout layout;
        RunSummary stats = new RunSummary();
        float runStart;
        float distanceDone;
        float maxXThisSector;
        float spawnTimer;
        float eventTimer;
        int eventsLeft;
        float flareUntil;
        float meteorUntil;
        float meteorTimer;
        float collapseTickTimer;
        float autoStartTimer = 3f;
        int bossSpawned;
        ShipCore ark;

        // ---- client state ----
        SectorVisuals visuals;

        public float CurrentCollapseX
        {
            get
            {
                if (CollapseSpeed.Value <= 0f && CollapseBaseX.Value < -90000f) return -99999f;
                double now = NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
                return CollapseBaseX.Value + CollapseSpeed.Value * (float)(now - CollapseBaseTime.Value);
            }
        }

        // ================================================================ lifecycle

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += OnPhaseChanged;
            SectorSeed.OnValueChanged += (a, b) => RebuildVisuals();
            SectorIndex.OnValueChanged += (a, b) => { GameState.SectorIndex = b; RebuildVisuals(); EventBus.RaiseSectorStarted(b); Debug.Log($"[Run] Entered sector {b}"); };
            visuals = new GameObject("_SectorVisuals").AddComponent<SectorVisuals>();
            GameState.SectorIndex = SectorIndex.Value;
            OnPhaseChanged(GamePhase.Lobby, Phase.Value);
            RebuildVisuals();

            if (IsServer)
            {
                CombatEvents.Died += OnSomethingDied;
                LootStats.Collected += OnLootCollected;
                ShipCore.ServerDestroyed += OnShipDestroyed;
                BossBrain.ServerDefeated += OnBossDefeated;
                if (SessionManager.Instance != null) SessionManager.Instance.RunStartRequested += ServerStartRun;
            }
            RegisterCheats();
            SubmitLoadoutServerRpc(Meta.CurrentLoadout());
            Meta.Changed += OnMetaChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            Phase.OnValueChanged -= OnPhaseChanged;
            Meta.Changed -= OnMetaChanged;
            if (IsServer)
            {
                CombatEvents.Died -= OnSomethingDied;
                LootStats.Collected -= OnLootCollected;
                ShipCore.ServerDestroyed -= OnShipDestroyed;
                BossBrain.ServerDefeated -= OnBossDefeated;
                if (SessionManager.Instance != null) SessionManager.Instance.RunStartRequested -= ServerStartRun;
            }
            if (visuals != null) Destroy(visuals.gameObject);
            GameState.CollapseX = -99999f;
            GameState.HasGate = false;
            UnregisterCheats();
        }

        void OnMetaChanged() { if (IsSpawned) SubmitLoadoutServerRpc(Meta.CurrentLoadout()); }

        void OnPhaseChanged(GamePhase a, GamePhase b)
        {
            GameState.Phase = b;
            EventBus.RaisePhaseChanged(b);
            if (b == GamePhase.Lobby) Sfx.Music(0);
            // The seed is set while still in Lobby (RebuildVisuals skips then), so build once Playing.
            if (b == GamePhase.Playing) RebuildVisuals();
            if (a != b) Debug.Log($"[Run] Phase {a} -> {b} (sector {SectorIndex.Value}, players {PlayerPawn.All.Count})");
        }

        void RebuildVisuals()
        {
            if (visuals == null) return;
            if (Phase.Value == GamePhase.Lobby || SectorSeed.Value == 0) { visuals.Clear(); Sfx.Music(0); return; }
            visuals.Build(SectorSeed.Value, SectorIndex.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SubmitLoadoutServerRpc(RunLoadout l, RpcParams rp = default) => loadouts[rp.Receive.SenderClientId] = l;

        // ================================================================ per-frame

        void Update()
        {
            GameState.CollapseX = CurrentCollapseX;
            if (!IsServer) return;

            if (Phase.Value == GamePhase.Lobby) { TickAutoStart(); return; }
            if (Phase.Value != GamePhase.Playing) return;
            float dt = Time.deltaTime;

            TickPlayers(dt);
            TickCollapse(dt);
            TickGate(dt);
            TickSpawns(dt);
            TickEvents(dt);
            TickBoss();
            CheckRunOver();
        }

        void TickAutoStart()
        {
            if (!TransportConfig.AutoStart) return;
            if (NetworkManager.ConnectedClientsIds.Count < TransportConfig.AutoStartPlayers) return;
            autoStartTimer -= Time.deltaTime;
            if (autoStartTimer <= 0f) { TransportConfig.SetRequested(TransportConfig.RequestedTransport); ServerStartRun(); }
        }

        // ================================================================ run lifecycle (server)

        public void ServerStartRun()
        {
            if (!IsServer || Phase.Value == GamePhase.Playing) return;
            stats = new RunSummary();
            runStart = Time.time;
            distanceDone = 0f;
            RespawnsLeft.Value = StartingRespawns;
            ark = null;
            ClearShips();
            foreach (var pawn in PlayerPawn.All) if (pawn != null) pawn.Gear.ResetServer();
            GenerateSector(1, Random.Range(1, int.MaxValue));
            int slot = 0;
            foreach (var pawn in PlayerPawn.All)
                if (pawn != null) SetupPlayer(pawn, slot++, true);
            SpawnDerelictArk();
            Phase.Value = GamePhase.Playing;
            AnnounceClientRpc("SECTOR 1 - " + (layout.Def != null ? layout.Def.DisplayName : "") + "\nOutrun the Collapse. Power the Jump Gate.", "jump_gate");
        }

        /// <summary>Host: back to the lobby after results.</summary>
        public void ServerReturnToLobby()
        {
            if (!IsServer) return;
            ClearSector();
            ClearShips();
            foreach (var p in PlayerPawn.All) if (p != null) { p.SetActiveServer(false); p.Inventory.ClearAll(); p.Inventory.Credits.Value = 0; }
            CollapseBaseX.Value = -99999f;
            CollapseSpeed.Value = 0f;
            SectorSeed.Value = 0;
            BossActive.Value = false;
            Phase.Value = GamePhase.Lobby;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToLobbyServerRpc(RpcParams rp = default)
        {
            if (rp.Receive.SenderClientId == NetworkManager.ServerClientId) ServerReturnToLobby();
        }

        void EndRun(bool victory, string reason)
        {
            if (Phase.Value != GamePhase.Playing) return;
            stats.Victory = victory;
            stats.EndReason = reason;
            stats.SectorReached = SectorIndex.Value;
            stats.DistanceTraveled = distanceDone + Mathf.Max(0f, maxXThisSector - (layout != null ? layout.StartX : 0f));
            stats.RunTimeSeconds = Time.time - runStart;
            stats.StardustEarned = stats.ComputeStardust();
            Phase.Value = victory ? GamePhase.DemoComplete : GamePhase.GameOver;
            foreach (var p in PlayerPawn.All) if (p != null) { var s = ShipSeat.SeatOf(p.OwnerClientId); if (s) s.Vacate(); }
            RunEndedClientRpc(stats.Victory, stats.EndReason, stats.SectorReached, stats.DistanceTraveled, stats.Kills, stats.LootCollected, stats.StardustEarned, stats.RunTimeSeconds);
        }

        [ClientRpc]
        void RunEndedClientRpc(bool victory, string reason, int sector, float dist, int kills, int loot, int dust, float time)
        {
            var s = new RunSummary
            {
                Victory = victory, EndReason = reason, SectorReached = sector, DistanceTraveled = dist,
                Kills = kills, LootCollected = loot, StardustEarned = dust, RunTimeSeconds = time
            };
            Meta.RecordRun(s);
            Sfx.Engine(false, 0f);
            EventBus.RaiseRunEnded(s);
        }

        // ================================================================ sectors

        void GenerateSector(int index, int runSeed)
        {
            ClearSector();
            var def = ContentCatalog.GetSector(index);
            int seed = runSeed != 0 ? runSeed + index * 7919 : Random.Range(1, int.MaxValue);
            layout = SectorLayout.Build(seed, def);
            maxXThisSector = layout.StartX;
            var cat = ContentCatalog.Instance;

            // Planets + surface POIs.
            for (int i = 0; i < layout.Planets.Length; i++)
            {
                var ps = layout.Planets[i];
                var biome = cat.Biomes[Mathf.Clamp(ps.BiomeIndex, 0, cat.Biomes.Length - 1)];
                var planet = PlanetBody.SpawnServer(ps.Seed, ps.Radius, biome, ps.Pos);
                if (planet == null) continue;
                Track(planet.NetworkObject);
                PopulatePlanet(planet, biome, ps.Seed, index);
            }

            // Station
            if (def == null || def.HasStation)
            {
                var st = SpawnPrefab(cat.TraderStation, layout.StationPos, Quaternion.Euler(0f, 90f, 0f));
                if (st != null)
                {
                    var ts = st.GetComponent<TraderStation>();
                    ts.Seed.Value = seed;
                    ts.Sector.Value = index;
                }
            }

            // Wrecks with salvage
            var rng = new System.Random(seed ^ 0x5f3759df);
            foreach (var w in layout.Wrecks) SpawnWreckSite(w, rng, index);

            // Gate or boss arena
            if (def != null && def.IsBossSector)
            {
                bossSpawned = 0;
            }
            else
            {
                var gate = SpawnPrefab(cat.JumpGate, layout.GatePos, Quaternion.Euler(0f, 90f, 0f));
                if (gate != null) gate.GetComponent<JumpGate>().CellsRequired.Value = def != null ? def.GateCellCost : 2;
            }

            // Collapse
            float offset = def != null ? def.CollapseStartOffset : 1200f;
            CollapseBaseX.Value = layout.StartX - offset;
            CollapseBaseTime.Value = NetworkManager.ServerTime.Time;
            CollapseSpeed.Value = CollapseSpeedFor(def);

            eventsLeft = def != null ? def.EventCount : 2;
            eventTimer = Random.Range(70f, 140f);
            spawnTimer = 40f;
            SectorIndex.Value = index;
            SectorSeed.Value = seed;
            GameState.Objective = def != null && def.IsBossSector ? "Defeat the Warden" : "Power the Jump Gate";
        }

        static float CollapseSpeedFor(SectorDef def) => def != null ? def.CollapseSpeed : 6f;

        /// <summary>
        /// Points of interest cluster at the planet's sites (flattened clearings next to landforms), so each landing
        /// zone holds a worthwhile activity: ruins, crash sites, hives, resource fields or supply caches.
        /// </summary>
        void PopulatePlanet(PlanetBody planet, BiomeDef biome, int seed, int sector)
        {
            var terrain = planet.Terrain;
            if (terrain == null) return;
            var rng = new System.Random(seed);
            var cat = ContentCatalog.Instance;
            int loot = Mathf.Clamp(sector, 1, 3);
            int Amount() => rng.Next(6, 12) + sector * 2;
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
                        Track(LootCrate.SpawnServer(c + up * 0.5f + rot * Vector3.forward * 2f, rot, Mathf.Clamp(sector + 1, 1, 3))?.NetworkObject);
                        Ring(planet, c, 12f, 24f, 2, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.CrashSite:
                        SpawnPrefab(cat.CrashedShipPrefab, c, rot);
                        Track(LootCrate.SpawnServer(c + up * 0.5f + rot * Vector3.right * 6f, rot, loot)?.NetworkObject);
                        Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, c + rot * Vector3.left * 7f, rot, 10)?.NetworkObject);
                        Ring(planet, c, 14f, 26f, 1, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.FuelNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.Hive:
                        hives++;
                        var nest = SpawnPrefab(cat.NestPrefab, c, rot);
                        if (nest != null) nest.GetComponent<AlienNest>()?.Configure(TierFor(sector));
                        Ring(planet, c, 10f, 22f, 2, rng, (p, r) => Track(LootCrate.SpawnServer(p + r * Vector3.up * 0.5f, r, Mathf.Clamp(sector + 1, 1, 3))?.NetworkObject));
                        Ring(planet, c, 12f, 26f, 2, rng, (p, r) => Track(ResourceNode.SpawnServer(ScatterKind.CrystalNode, p, r, Amount())?.NetworkObject));
                        break;
                    case SiteKind.Cache:
                        Ring(planet, c, 2f, 10f, 3, rng, (p, r) => Track(LootCrate.SpawnServer(p + r * Vector3.up * 0.5f, r, Mathf.Clamp(sector + 1, 1, 3))?.NetworkObject));
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
                        Track(LootCrate.SpawnServer(c + up * 0.5f, rot, loot)?.NetworkObject);
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
                    if (nest != null) nest.GetComponent<AlienNest>()?.Configure(TierFor(sector));
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

        void SpawnWreckSite(Vector3 pos, System.Random rng, int sector)
        {
            var cat = ContentCatalog.Instance;
            var rot = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
            SpawnPrefab(cat.Wreck, pos, rot);
            for (int i = 0; i < 2; i++)
                Track(LootCrate.SpawnServer(pos + rot * new Vector3(i == 0 ? 7f : -6f, 3f, 2f), rot, Mathf.Clamp(sector, 1, 3))?.NetworkObject);
            Track(ResourceNode.SpawnServer(ScatterKind.ScrapNode, pos + rot * new Vector3(0f, -4f, 8f), rot, 12)?.NetworkObject);
            Track(ResourceNode.SpawnServer(ScatterKind.FuelNode, pos + rot * new Vector3(0f, 4f, -9f), rot, 6)?.NetworkObject);
        }

        GameObject SpawnPrefab(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            Track(no);
            return go;
        }

        void Track(NetworkObject no) { if (no != null) sectorObjects.Add(no); }

        void ClearSector()
        {
            foreach (var no in sectorObjects) if (no != null && no.IsSpawned) no.Despawn(true);
            sectorObjects.Clear();
            foreach (var e in EnemyFactory.Alive.ToArray()) if (e != null && e.IsSpawned) e.NetworkObject.Despawn(true);
            EnemyFactory.Alive.Clear();
            foreach (var p in WorldPickup.All.ToArray()) if (p != null && p.IsSpawned) p.NetworkObject.Despawn(true);
            if (BossBrain.Instance != null && BossBrain.Instance.IsSpawned) BossBrain.Instance.NetworkObject.Despawn(true);
            BossActive.Value = false;
        }

        void ClearShips()
        {
            foreach (var s in ShipCore.All.ToArray())
                if (s != null && s.IsSpawned) { ShipSeat.VacateAllOn(s); s.NetworkObject.Despawn(true); }
            shipRespawnTimers.Clear();
        }

        // ================================================================ players & ships

        void SetupPlayer(PlayerPawn pawn, int slot, bool runStart)
        {
            ulong c = pawn.OwnerClientId;
            if (!loadouts.TryGetValue(c, out var lo)) lo = new RunLoadout { Ship = ShipClass.Dart };
            Vector3 basePos = layout.StartPoint + new Vector3(0f, (slot % 2) * 25f - 12f, (slot / 2) * 30f - 15f + slot * 8f);
            if (!runStart) basePos = TeamCenter() + Random.onUnitSphere * 30f;

            pawn.Inventory.ClearAll();
            pawn.Inventory.Credits.Value = 80;
            pawn.Gear.ResetServer();
            if (lo.Has("reinforced_suit")) pawn.Gear.BonusHp.Value = 30f;
            if (lo.Has("deep_lungs")) pawn.Gear.BonusOxygen.Value = 0.4f;
            if (lo.Has("head_start")) pawn.Inventory.Credits.Value += 150;
            if (lo.Has("spare_cells")) pawn.Inventory.Add("cell", 1);
            if (lo.Has("salvager")) { pawn.Inventory.Add("scrap", 10); pawn.Inventory.Add("fuel", 5); }
            if (lo.Has("rifle_start")) pawn.Gear.HasRifle.Value = true;
            pawn.Inventory.Add("medkit", 2);

            var ship = SpawnShip(lo.Ship, c, basePos, Quaternion.LookRotation(Vector3.right));
            if (ship != null)
            {
                if (lo.Has("laser_mk2")) ship.InstallServer("module_laser_mk2", false);
                if (lo.Has("shield_mk2")) ship.InstallServer("module_shield_mk2", false);
                if (lo.Has("engine_mk2")) ship.InstallServer("module_engine_mk2", false);
                ship.RestoreServer(true, true);
            }

            pawn.SetActiveServer(true);
            pawn.RespawnServer(ship != null && ship.RespawnPoint != null ? ship.RespawnPoint.position : basePos, Quaternion.LookRotation(Vector3.right));
            if (ship != null)
            {
                // Transferring ownership in the spawn frame makes the remote client publish its
                // not-yet-positioned transform; seat (and hand over the ship) a moment later.
                if (c == NetworkManager.ServerClientId) SeatServer(ship, StationRole.Pilot, c);
                else StartCoroutine(SeatLater(ship, c, 0.6f));
            }
        }

        System.Collections.IEnumerator SeatLater(ShipCore ship, ulong client, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ship != null && ship.IsSpawned && NetworkManager.ConnectedClients.ContainsKey(client))
                SeatServer(ship, StationRole.Pilot, client);
        }

        ShipCore SpawnShip(ShipClass cls, ulong owner, Vector3 pos, Quaternion rot)
        {
            var prefab = ContentCatalog.GetShipPrefab(cls);
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, rot);
            var ship = go.GetComponent<ShipCore>();
            go.GetComponent<NetworkObject>().Spawn(true);
            ship.OwnerPlayer.Value = owner;
            var np = NetworkPlayer.GetByClient(owner);
            string name = np != null ? np.PlayerName.Value.ToString() : "Pilot";
            ship.ShipName.Value = $"{name}'s {cls}";
            if (np != null) np.SetShipServer(ship.NetworkObjectId);
            return ship;
        }

        static void SeatServer(ShipCore ship, StationRole role, ulong client)
        {
            foreach (var seat in ShipSeat.All)
                if (seat != null && seat.Ship == ship && seat.Role == role && !seat.IsOccupied)
                {
                    seat.Occupant.Value = client;
                    if (role == StationRole.Pilot) ship.NetworkObject.ChangeOwnership(client);
                    return;
                }
        }

        void SpawnDerelictArk()
        {
            var cat = ContentCatalog.Instance;
            if (cat.ShipArk == null) return;
            var go = Instantiate(cat.ShipArk, layout.ArkPos, Quaternion.Euler(8f, 70f, 14f));
            go.GetComponent<NetworkObject>().Spawn(true);
            ark = go.GetComponent<ShipCore>();
            ark.Derelict.Value = true;
            ark.ShipName.Value = "Derelict Ark";
            ark.Hull.Hp.Value = ark.Hull.MaxHp.Value * 0.35f;
            ark.Fuel.Value = 0f;
            ark.Shield.Value = 0f;
            ark.Cargo.Add("cell", 1);
            ark.Cargo.Add("scrap", 4);
        }

        Vector3 TeamCenter()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in PlayerPawn.All)
                if (p != null && p.Active.Value && !p.IsDown.Value) { sum += p.transform.position; n++; }
            return n > 0 ? sum / n : (layout != null ? layout.StartPoint : Vector3.zero);
        }

        void TickPlayers(float dt)
        {
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null) continue;
                // Late joiner: inactive pawn while playing.
                if (!pawn.Active.Value) { SetupPlayer(pawn, 0, false); AnnounceClientRpc(NameOf(pawn.OwnerClientId) + " joined the expedition", "spawn"); continue; }
                if (pawn.transform.position.x > maxXThisSector && !pawn.IsDown.Value) maxXThisSector = pawn.transform.position.x;

                if (pawn.IsDown.Value && !pawn.IsLost.Value)
                {
                    if (pawn.RespawnAt.Value <= 0f)
                    {
                        if (RespawnsLeft.Value <= 0) { pawn.IsLost.Value = true; continue; }
                        RespawnsLeft.Value--;
                        pawn.RespawnAt.Value = Time.time + 8f;
                    }
                    else if (Time.time >= pawn.RespawnAt.Value && TryRespawnPoint(pawn.OwnerClientId, out var pos, out var rot))
                    {
                        pawn.RespawnAt.Value = 0f;
                        pawn.RespawnServer(pos, rot);
                    }
                }

                // Replacement ship for players who lost theirs.
                ulong c = pawn.OwnerClientId;
                if (!pawn.IsLost.Value && ShipCore.OwnedBy(c) == null)
                {
                    if (!shipRespawnTimers.TryGetValue(c, out var t)) shipRespawnTimers[c] = t = Time.time + 20f;
                    if (Time.time >= t)
                    {
                        shipRespawnTimers.Remove(c);
                        Vector3 p = (pawn.IsDown.Value ? TeamCenter() : pawn.transform.position) + Random.onUnitSphere * 25f;
                        var src = GravitySource.Dominant(p);
                        if (src != null) p = src.Center + src.UpAt(p) * (src.GroundRadius(p) + 70f);
                        SpawnShip(ShipClass.Dart, c, p, Quaternion.LookRotation(Vector3.right));
                        AnnounceClientRpc(NameOf(c) + " received a replacement Dart", "spawn");
                    }
                }
            }
        }

        bool TryRespawnPoint(ulong client, out Vector3 pos, out Quaternion rot)
        {
            var ship = ShipCore.OwnedBy(client);
            if (ship == null && ark != null && ark.IsSpawned && !ark.Derelict.Value) ship = ark;
            if (ship == null) foreach (var s in ShipCore.All) if (s != null && !s.Derelict.Value) { ship = s; break; }
            if (ship != null)
            {
                var rp = ship.RespawnPoint != null ? ship.RespawnPoint : ship.transform;
                pos = rp.position;
                rot = rp.rotation;
                return true;
            }
            foreach (var p in PlayerPawn.All)
                if (p != null && p.OwnerClientId != client && p.Active.Value && !p.IsDown.Value)
                {
                    pos = p.transform.position + p.transform.right * 2f + p.transform.up;
                    rot = p.transform.rotation;
                    return true;
                }
            pos = default; rot = Quaternion.identity;
            return false;
        }

        void CheckRunOver()
        {
            int active = 0, gone = 0;
            foreach (var p in PlayerPawn.All)
            {
                if (p == null || !p.Active.Value) continue;
                active++;
                if (p.IsLost.Value) gone++;
            }
            if (active > 0 && gone == active)
                EndRun(false, CurrentCollapseX > maxXThisSector - 50f ? "Consumed by the Collapse" : "The whole crew was lost");
        }

        // ================================================================ collapse

        void TickCollapse(float dt)
        {
            collapseTickTimer -= dt;
            if (collapseTickTimer > 0f) return;
            collapseTickTimer = 0.5f;
            float cx = CurrentCollapseX;
            foreach (var h in HealthComponent.All.ToArray())
            {
                if (h == null || !h.IsAlive || h.transform.position.x > cx) continue;
                bool player = h.Team == Team.Players;
                h.ApplyDamage(new DamageInfo
                {
                    Amount = player ? (h.GetComponent<ShipCore>() != null ? 30f : 14f) : 200f,
                    Type = DamageType.Collapse, Point = h.transform.position, Direction = Vector3.right
                });
            }
            // Unmanned derelict left behind is consumed too.
            if (ark != null && ark.IsSpawned && ark.Derelict.Value && ark.transform.position.x < cx) { ark.NetworkObject.Despawn(true); ark = null; }

            // Flare radiation for players in the open.
            if (Time.time < flareUntil)
                foreach (var p in PlayerPawn.All)
                    if (p != null && p.Active.Value && !p.IsDown.Value && !p.ServerSheltered)
                        p.Health.ApplyDamage(new DamageInfo { Amount = 2.5f, Type = DamageType.Environmental, Point = p.transform.position });
        }

        // ================================================================ gate / jump

        void TickGate(float dt)
        {
            var gate = JumpGate.Instance;
            if (gate == null || !gate.Powered) { JumpCountdown.Value = -1f; return; }
            bool allNear = true;
            int alive = 0;
            foreach (var p in PlayerPawn.All)
            {
                if (p == null || !p.Active.Value || p.IsLost.Value) continue;
                if (p.IsDown.Value) continue;
                alive++;
                if (Vector3.Distance(p.transform.position, gate.transform.position) > JumpGate.GatherRange) allNear = false;
            }
            if (!allNear || alive == 0)
            {
                if (JumpCountdown.Value >= 0f) AnnounceClientRpc("Jump aborted - everyone must be near the gate", "ui_back");
                JumpCountdown.Value = -1f;
                gate.Charge.Value = 0f;
                return;
            }
            if (JumpCountdown.Value < 0f)
            {
                JumpCountdown.Value = 6f;
                AnnounceClientRpc("All aboard - jump in 6 seconds!", "jump_charge");
            }
            JumpCountdown.Value -= dt;
            gate.Charge.Value = 1f - Mathf.Clamp01(JumpCountdown.Value / 6f);
            if (JumpCountdown.Value <= 0f) ServerJump();
        }

        public void ServerJump()
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            int next = SectorIndex.Value + 1;
            if (ContentCatalog.GetSector(next) == null) { EndRun(true, "Escaped the Collapse"); return; }
            JumpCountdown.Value = -1f;

            // Remember who is on foot inside which ship interior (they ride along via platform carry).
            Vector3 oldGate = layout.GatePos;
            distanceDone += layout.Length;
            stats.SectorReached = next;
            RespawnsLeft.Value += 1;

            // Abandon unclaimed derelicts.
            if (ark != null && ark.IsSpawned && ark.Derelict.Value) { ark.NetworkObject.Despawn(true); ark = null; }

            GenerateSector(next, SectorSeed.Value - SectorIndex.Value * 7919);

            // Ships into formation at the new start; pawns on foot follow their ship or the formation.
            int slot = 0;
            var shipPositions = new Dictionary<ShipCore, Vector3>();
            foreach (var s in ShipCore.All.ToArray())
            {
                if (s == null || !s.IsSpawned) continue;
                Vector3 p = layout.StartPoint + new Vector3(-(slot / 3) * 40f, (slot % 3) * 30f - 30f, (slot % 2 == 0 ? 1 : -1) * (20f + slot * 12f));
                if (s.Class == ShipClass.Ark) p = layout.StartPoint + new Vector3(-60f, 0f, -90f);
                slot++;
                s.GetComponent<NetTeleport>().ServerTeleport(p, Quaternion.LookRotation(Vector3.right));
                s.RestoreServer(false, false);
                shipPositions[s] = p;
            }
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value || ShipSeat.SeatOf(pawn.OwnerClientId) != null) continue;
                var vol = GravityVolume.At(pawn.transform.position);
                if (vol != null) continue; // carried by the interior it stands in
                var own = ShipCore.OwnedBy(pawn.OwnerClientId);
                Vector3 p = own != null && shipPositions.TryGetValue(own, out var sp) ? sp + Vector3.up * 8f : layout.StartPoint + Random.onUnitSphere * 10f;
                if (pawn.IsDown.Value) continue;
                pawn.GetComponent<NetTeleport>().ServerTeleport(p, Quaternion.LookRotation(Vector3.right));
            }
            var def = layout.Def;
            JumpedClientRpc(next, def != null ? def.DisplayName : "", def != null ? def.Intro : "");
        }

        [ClientRpc]
        void JumpedClientRpc(int sector, string name, string intro)
        {
            Vfx.JumpFlash(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Sfx.PlayUI("jump_gate");
            Vfx.Shake(0.8f);
            EventBus.RaiseSectorJumpCompleted(sector - 1);
            EventBus.RaiseNotification($"SECTOR {sector} - {name}\n{intro}");
        }

        // ================================================================ boss

        void TickBoss()
        {
            var def = layout != null ? layout.Def : null;
            if (def == null || !def.IsBossSector || bossSpawned > 0) return;
            foreach (var p in PlayerPawn.All)
            {
                if (p == null || !p.Active.Value || p.IsDown.Value) continue;
                if (p.transform.position.x > layout.BossPos.x - 1300f) { SpawnBoss(); return; }
            }
        }

        void SpawnBoss()
        {
            bossSpawned = 1;
            var cat = ContentCatalog.Instance;
            if (cat.BossMothership == null) return;
            var go = Instantiate(cat.BossMothership, layout.BossPos, Quaternion.LookRotation(Vector3.left));
            int players = 0;
            foreach (var p in PlayerPawn.All) if (p != null && p.Active.Value && !p.IsLost.Value) players++;
            go.GetComponent<BossBrain>().Configure(players, 1f);
            go.GetComponent<NetworkObject>().Spawn(true);
            BossActive.Value = true;
            // Freeze the Collapse a little behind the arena so the fight is about the boss.
            float cx = CurrentCollapseX;
            CollapseBaseX.Value = Mathf.Max(cx, layout.BossPos.x - 2600f);
            CollapseBaseTime.Value = NetworkManager.ServerTime.Time;
            CollapseSpeed.Value = 1.5f;
            AnnounceClientRpc("WARNING: The Warden approaches!\nDestroy its 3 glowing core pods, then the hull.", "boss_roar");
            GameState.Objective = "Defeat the Warden";
        }

        void OnBossDefeated()
        {
            BossActive.Value = false;
            stats.Kills += 10;
            EndRun(true, "The Warden is destroyed - you escaped the Collapse!");
        }

        // ================================================================ enemies & events

        static float TierFor(int sector) => 1f + (sector - 1) * 0.45f;

        void TickSpawns(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = 9f;
            var def = layout.Def;
            if (def == null) return;
            float tier = TierFor(SectorIndex.Value);

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
                    if (near >= 3 + SectorIndex.Value || def.SurfaceEnemies.Length == 0 || Random.value < 0.35f) continue;
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
                    if (space >= def.SpaceEnemyCap || def.SpaceEnemies.Length == 0 || Random.value < 0.55f) continue;
                    var kind = def.SpaceEnemies[Random.Range(0, def.SpaceEnemies.Length)];
                    int group = kind == EnemyKind.DroneSwarm ? 3 : kind == EnemyKind.PirateGunship ? 1 : Random.Range(1, 3);
                    Vector3 basePos = p.transform.position + new Vector3(Random.Range(250f, 500f), Random.Range(-150f, 150f), Random.Range(-350f, 350f));
                    for (int i = 0; i < group; i++)
                        EnemyFactory.Spawn(kind, basePos + Random.insideUnitSphere * 30f, Quaternion.LookRotation(p.transform.position - basePos), tier);
                }
            }
        }

        float NearestPlayerDist(Vector3 pos)
        {
            float best = float.MaxValue;
            foreach (var p in PlayerPawn.All)
                if (p != null && p.Active.Value) best = Mathf.Min(best, Vector3.Distance(p.transform.position, pos));
            return best;
        }

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

            if (eventsLeft <= 0 || BossActive.Value) return;
            eventTimer -= dt;
            if (eventTimer > 0f) return;
            eventsLeft--;
            eventTimer = Random.Range(110f, 200f);
            var kinds = new[] { SectorEventKind.MeteorShower, SectorEventKind.DistressSignal, SectorEventKind.SolarFlare };
            TriggerEvent(kinds[Random.Range(0, kinds.Length)]);
        }

        public void TriggerEvent(SectorEventKind k)
        {
            switch (k)
            {
                case SectorEventKind.MeteorShower:
                    meteorUntil = Time.time + 25f;
                    AnnounceClientRpc("METEOR SHOWER! Take cover or keep moving.", "alarm");
                    break;
                case SectorEventKind.SolarFlare:
                    flareUntil = Time.time + 18f;
                    foreach (var s in ShipCore.All) if (s != null) s.ShieldsOffline.Value = true;
                    AnnounceClientRpc("SOLAR FLARE! Shields offline. Get inside a ship or under an atmosphere.", "alarm");
                    break;
                case SectorEventKind.DistressSignal:
                    Vector3 c = TeamCenter() + new Vector3(Random.Range(350f, 600f), Random.Range(-80f, 80f), Random.Range(-250f, 250f));
                    var rng = new System.Random(Random.Range(1, 99999));
                    SpawnWreckSite(c, rng, SectorIndex.Value + 1);
                    var def = layout.Def;
                    if (def != null && def.SpaceEnemies.Length > 0)
                        for (int i = 0; i < 2 + SectorIndex.Value; i++)
                            EnemyFactory.Spawn(def.SpaceEnemies[0], c + Random.onUnitSphere * 60f, Quaternion.identity, TierFor(SectorIndex.Value));
                    PingAllClientRpc(c, "Distress signal");
                    AnnounceClientRpc("DISTRESS SIGNAL detected - salvage awaits (beware of pirates).", "ping");
                    break;
            }
        }

        [ClientRpc]
        void PingAllClientRpc(Vector3 pos, string label) => EventBus.RaisePingCreated(pos, PingKind.Loot, label);

        [ClientRpc]
        void AnnounceClientRpc(string msg, string sfx)
        {
            EventBus.RaiseNotification(msg);
            if (!string.IsNullOrEmpty(sfx)) Sfx.PlayUI(sfx);
        }

        // ================================================================ stats

        void OnSomethingDied(HealthComponent h, DamageInfo info)
        {
            if (h.Team == Team.Players || !info.ByPlayer) return;
            if (h.GetComponent<EnemyBrain>() == null && h.GetComponent<AlienNest>() == null) return;
            stats.Kills++;
            var np = NetworkPlayer.GetByClient(info.SourceClientId);
            if (np != null) np.Kills.Value++;
        }

        void OnLootCollected(ulong client, int amount) => stats.LootCollected += amount;

        void OnShipDestroyed(ShipCore s)
        {
            if (s == ark) ark = null;
            AnnounceClientRpc(s.ShipName.Value + " was destroyed!", "alarm");
        }

        static string NameOf(ulong client)
        {
            var np = NetworkPlayer.GetByClient(client);
            return np != null ? np.PlayerName.Value.ToString() : "A player";
        }

        // ================================================================ cheats (F1)

        static readonly string[] CheatNames =
        {
            "God Mode (server) on/off", "Give resources + credits", "Skip sector (jump now)", "Spawn enemy near me",
            "Push Collapse back 1500 m", "Spawn boss now", "Trigger random event", "Refill my ship"
        };

        void RegisterCheats()
        {
            for (int i = 0; i < CheatNames.Length; i++)
            {
                int id = i;
                CheatManager.Register(CheatNames[i], () => { if (IsSpawned) CheatServerRpc(id); });
            }
        }

        void UnregisterCheats()
        {
            foreach (var n in CheatNames) CheatManager.Unregister(n);
        }

        /// <summary>Run a debug cheat by index (same as the F1 buttons).</summary>
        public void ExecuteCheat(int id) { if (IsSpawned) CheatServerRpc(id); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void CheatServerRpc(int id, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var pawn = PlayerPawn.ForClient(c);
            switch (id)
            {
                case 0:
                    CheatManager.GodMode = !CheatManager.GodMode;
                    AnnounceClientRpc("God mode " + (CheatManager.GodMode ? "ON" : "OFF"), "ui_click");
                    break;
                case 1:
                    if (pawn == null) return;
                    foreach (var r in new[] { "ore", "crystal", "scrap", "fuel" }) pawn.Inventory.Add(r, 20);
                    pawn.Inventory.Add("cell", 3);
                    pawn.Inventory.Credits.Value += 500;
                    break;
                case 2:
                    if (Phase.Value == GamePhase.Playing) ServerJump();
                    break;
                case 3:
                    if (pawn == null || layout == null || layout.Def == null) return;
                    var src = GravitySource.Dominant(pawn.transform.position);
                    bool surface = src != null && src.SurfaceDistance(pawn.transform.position) < 120f;
                    var list = surface ? layout.Def.SurfaceEnemies : layout.Def.SpaceEnemies;
                    if (list.Length == 0) return;
                    Vector3 fwd = pawn.CameraPoint.forward;
                    Vector3 pos = pawn.transform.position + fwd * (surface ? 20f : 120f) + pawn.transform.up * 2f;
                    EnemyFactory.Spawn(list[Random.Range(0, list.Length)], pos, Quaternion.LookRotation(-fwd, pawn.transform.up), TierFor(SectorIndex.Value));
                    break;
                case 4:
                    CollapseBaseX.Value = CurrentCollapseX - 1500f;
                    CollapseBaseTime.Value = NetworkManager.ServerTime.Time;
                    break;
                case 5:
                    if (layout != null && bossSpawned == 0 && layout.Def != null && layout.Def.IsBossSector) SpawnBoss();
                    else AnnounceClientRpc("Boss only spawns in sector 3 (use Skip sector)", "ui_back");
                    break;
                case 6:
                    TriggerEvent((SectorEventKind)Random.Range(0, 3));
                    break;
                case 7:
                    var ship = ShipCore.OwnedBy(c);
                    if (ship != null) ship.RestoreServer(true, true);
                    break;
            }
        }
    }
}
