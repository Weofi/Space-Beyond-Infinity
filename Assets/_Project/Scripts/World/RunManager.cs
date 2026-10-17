using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Networking;
using Voidward.Player;
using Voidward.Progression;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// The run director of the journey home (lives on the GameDirector network object, spawned by the host).
    /// Server: run lifecycle (start, continue from the suspend save, end), galaxy/system flow and warps, ships,
    /// respawns, stats, the run timer and the suspend save. What happens inside a system (content, spawns, events,
    /// the Warden enclave) is the SystemDirector's job. Clients: mirror galaxy/system into GameState and build the
    /// local system visuals; the galaxy plan is regenerated locally from the synced run seed (#46).
    /// </summary>
    public class RunManager : NetworkBehaviour
    {
        public static RunManager Instance { get; private set; }

        /// <summary>WarpTarget value for the warp to the next galaxy (or home from the last one).</summary>
        public const int WarpNextGalaxy = -1;
        /// <summary>WarpTarget value for skipping a galaxy (#78): straight to the one after the next, for more Exotic Matter.</summary>
        public const int WarpSkipGalaxy = -2;

        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        public NetworkVariable<int> RunSeed = new NetworkVariable<int>(0);
        public NetworkVariable<int> GalaxyIndex = new NetworkVariable<int>(1);
        public NetworkVariable<int> SystemIndex = new NetworkVariable<int>(0);
        /// <summary>Bit i = system i of the current galaxy was visited.</summary>
        public NetworkVariable<int> VisitedMask = new NetworkVariable<int>(0);
        public NetworkVariable<float> RunClockBase = new NetworkVariable<float>(0f);
        public NetworkVariable<double> RunClockStart = new NetworkVariable<double>(0);
        public NetworkVariable<bool> BossActive = new NetworkVariable<bool>(false);
        /// <summary>Asteroid ids (SystemVisuals.RockId) destroyed in the current system.</summary>
        public NetworkList<int> DestroyedAsteroids;

        // ---- server state ----
        readonly Dictionary<ulong, RunLoadout> loadouts = new Dictionary<ulong, RunLoadout>();
        readonly Dictionary<ulong, float> shipRespawnTimers = new Dictionary<ulong, float>();
        readonly Dictionary<int, float> asteroidDamage = new Dictionary<int, float>();
        SystemDirector director;
        RunSummary stats = new RunSummary();
        /// <summary>Crew logbook of this journey (#86): "[h:mm:ss] text" entries.</summary>
        readonly List<string> log = new List<string>();
        float distanceDone;
        float maxXThisSystem;
        float autoStartTimer = 3f;
        int clearedEnclaves;
        /// <summary>The home ship Nostos (#49); at 0 health it is disabled until repaired (#145).</summary>
        ShipCore home;

        // ---- client state ----
        SystemVisuals visuals;
        int shownKey = int.MinValue;
        GalaxyPlan cachedPlan;
        int cachedPlanKey = int.MinValue;

        /// <summary>Server: the director of the current system (null before the first run).</summary>
        public SystemDirector Director => director;

        /// <summary>Run time in seconds (paused between sessions, continues after loading the suspend save).</summary>
        public float RunTime
        {
            get
            {
                if (Phase.Value != GamePhase.Playing || NetworkManager == null) return RunClockBase.Value;
                return RunClockBase.Value + (float)(NetworkManager.ServerTime.Time - RunClockStart.Value);
            }
        }

        /// <summary>The current galaxy with its systems (regenerated locally from the run seed, cached).</summary>
        public GalaxyPlan Plan
        {
            get
            {
                int key = GalaxyGenerator.GalaxySeed(RunSeed.Value, GalaxyIndex.Value);
                if (cachedPlan == null || key != cachedPlanKey) { cachedPlan = GalaxyGenerator.Generate(RunSeed.Value, GalaxyIndex.Value); cachedPlanKey = key; }
                return cachedPlan;
            }
        }

        public StarSystem CurrentSystem
        {
            get
            {
                var p = Plan;
                return p != null && p.Systems.Length > 0 ? p.Systems[Mathf.Clamp(SystemIndex.Value, 0, p.Systems.Length - 1)] : null;
            }
        }

        public bool IsVisited(int system) => (VisitedMask.Value & (1 << system)) != 0;

        // ================================================================ lifecycle

        void Awake() => DestroyedAsteroids = new NetworkList<int>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            DestroyedAsteroids.OnListChanged += OnAsteroidsChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            visuals = new GameObject("_SystemVisuals").AddComponent<SystemVisuals>();
            OnPhaseChanged(GamePhase.Lobby, Phase.Value);

            if (IsServer)
            {
                director = new SystemDirector(this);
                CombatEvents.Died += OnSomethingDied;
                LootStats.Collected += OnLootCollected;
                ShipCore.ServerDestroyed += OnShipDestroyed;
                BossBrain.ServerDefeated += OnBossDefeated;
                if (SessionManager.Instance != null)
                {
                    SessionManager.Instance.RunStartRequested += ServerStartRun;
                    SessionManager.Instance.RunContinueRequested += ServerContinueRun;
                    SessionManager.Instance.BeforeLeave += ServerSuspend;
                }
            }
            RegisterCheats();
            SubmitLoadoutServerRpc(Meta.CurrentLoadout());
            Meta.Changed += OnMetaChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            Phase.OnValueChanged -= OnPhaseChanged;
            DestroyedAsteroids.OnListChanged -= OnAsteroidsChanged;
            Meta.Changed -= OnMetaChanged;
            if (IsServer)
            {
                CombatEvents.Died -= OnSomethingDied;
                LootStats.Collected -= OnLootCollected;
                ShipCore.ServerDestroyed -= OnShipDestroyed;
                BossBrain.ServerDefeated -= OnBossDefeated;
                if (SessionManager.Instance != null)
                {
                    SessionManager.Instance.RunStartRequested -= ServerStartRun;
                    SessionManager.Instance.RunContinueRequested -= ServerContinueRun;
                    SessionManager.Instance.BeforeLeave -= ServerSuspend;
                }
            }
            if (visuals != null) Destroy(visuals.gameObject);
            UnregisterCheats();
        }

        void OnMetaChanged() { if (IsSpawned) SubmitLoadoutServerRpc(Meta.CurrentLoadout()); }

        void OnPhaseChanged(GamePhase a, GamePhase b)
        {
            GameState.Phase = b;
            EventBus.RaisePhaseChanged(b);
            if (b == GamePhase.Lobby) Sfx.Music(0);
            if (a != b) Debug.Log($"[Run] Phase {a} -> {b} (galaxy {GalaxyIndex.Value}, system {SystemIndex.Value}, players {PlayerPawn.All.Count})");
        }

        /// <summary>
        /// Client (and host): follow the synced galaxy/system. Polled instead of value callbacks so it never builds
        /// with a half-updated set of variables.
        /// </summary>
        void UpdateShownSystem()
        {
            bool playing = Phase.Value == GamePhase.Playing && RunSeed.Value != 0;
            bool warping = playing && WarpFlight.Instance != null && WarpFlight.Instance.Active.Value;
            int key = !playing ? int.MinValue : warping ? int.MinValue + 1 : GalaxyGenerator.GalaxySeed(RunSeed.Value, GalaxyIndex.Value) ^ (SystemIndex.Value * 7919 + 1);
            if (key == shownKey) return;
            shownKey = key;
            if (!playing) { if (visuals != null) visuals.Clear(); Sfx.Music(0); return; }
            if (warping) { if (visuals != null) visuals.Clear(); Debug.Log("[Run] Warp flight"); return; } // the corridor is WarpCorridorView's
            GameState.GalaxyIndex = GalaxyIndex.Value;
            GameState.SystemIndex = SystemIndex.Value;
            if (visuals != null)
            {
                visuals.Build(RunSeed.Value, GalaxyIndex.Value, SystemIndex.Value);
                foreach (int id in DestroyedAsteroids) visuals.DestroyRock(id, false);
            }
            var sys = CurrentSystem;
            EventBus.RaiseSystemEntered(GalaxyIndex.Value, SystemIndex.Value);
            Debug.Log($"[Run] Entered galaxy {GalaxyIndex.Value} system {SystemIndex.Value} ({(sys != null ? sys.Kind.ToString() : "?")})");
        }

        // ================================================================ asteroids

        void OnAsteroidsChanged(NetworkListEvent<int> e)
        {
            if (e.Type == NetworkListEvent<int>.EventType.Add && visuals != null) visuals.DestroyRock(e.Value, true);
        }

        /// <summary>Client hit on an asteroid (hitscan tools); projectiles hit on the server directly.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DamageAsteroidServerRpc(int id, float amount, Vector3 point, RpcParams rp = default)
        {
            ServerDamageAsteroid(id, new DamageInfo
            {
                Amount = Mathf.Clamp(amount, 0f, 200f), Point = point, Type = DamageType.Kinetic,
                SourceClientId = rp.Receive.SenderClientId, ByPlayer = true
            });
        }

        /// <summary>Server: damage an asteroid; destroyed rocks are replicated and pay out ore to the shooter.</summary>
        public void ServerDamageAsteroid(int id, DamageInfo info)
        {
            if (!IsServer || visuals == null || Phase.Value != GamePhase.Playing || DestroyedAsteroids.Contains(id)) return;
            float radius = visuals.RockRadius(id);
            if (radius <= 0f || info.Amount <= 0f) return;
            float maxHp = 25f + radius * 10f;
            float amount = info.Type == DamageType.Mining ? info.Amount * 2f : info.Amount;
            asteroidDamage.TryGetValue(id, out float dealt);
            dealt += amount;
            asteroidDamage[id] = dealt;
            if (dealt < maxHp) return;

            asteroidDamage.Remove(id);
            DestroyedAsteroids.Add(id);
            if (!info.ByPlayer) return;
            var inv = NetInventory.ForClient(info.SourceClientId);
            if (inv == null) return;
            int ore = 1 + Mathf.RoundToInt(radius / 5f);
            int got = ore - inv.Add("ore", ore);
            if (got > 0)
            {
                LootStats.Collected?.Invoke(info.SourceClientId, got);
                ToastRpc($"Asteroid destroyed  +{got} Ore", RpcTarget.Single(info.SourceClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ToastRpc(string msg, RpcParams rp = default) => EventBus.RaiseNotification(msg);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SubmitLoadoutServerRpc(RunLoadout l, RpcParams rp = default) => loadouts[rp.Receive.SenderClientId] = l;

        // ================================================================ per-frame

        void Update()
        {
            UpdateShownSystem();
            if (!IsServer) return;

            if (Phase.Value == GamePhase.Lobby) { TickAutoStart(); return; }
            if (Phase.Value != GamePhase.Playing) return;
            float dt = Time.deltaTime;

            TickPlayers(dt);
            TickHazards(dt);
            if (WarpFlight.Instance == null || !WarpFlight.Instance.Active.Value) director.Tick(dt);
        }

        void TickAutoStart()
        {
            if (!TransportConfig.AutoStart) return;
            if (NetworkManager.ConnectedClientsIds.Count < TransportConfig.AutoStartPlayers) return;
            autoStartTimer -= Time.deltaTime;
            if (autoStartTimer <= 0f) { TransportConfig.SetRequested(TransportConfig.RequestedTransport); ServerStartRun(); }
        }

        // ================================================================ run lifecycle (server)

        /// <summary>Host: a new journey - fresh seed, the crew wakes up at the wormhole exit in M33.</summary>
        public void ServerStartRun()
        {
            if (!IsServer || Phase.Value == GamePhase.Playing) return;
            ResetRunState(Random.Range(1, int.MaxValue), 0f);
            foreach (var pawn in PlayerPawn.All) if (pawn != null) pawn.Gear.ResetServer();
            if (WarpDrive.Instance != null) WarpDrive.Instance.ServerReset(0);
            EnterSystem(1, 0, false);
            SpawnHomeShip(null);
            int slot = 0;
            foreach (var pawn in PlayerPawn.All)
                if (pawn != null) SetupPlayer(pawn, slot++, true, null);
            StartClock();
            var def = director.Def;
            AnnounceClientRpc($"{GalaxyTitle(def)}\n{(def != null ? def.Intro : "")}", "jump_gate");
            ServerLog(Story != null ? Story.LogJourneyStart : "", def);
            ServerWriteSave();
        }

        /// <summary>Host: resume the suspend-saved run at the arrival point of its saved system.</summary>
        public void ServerContinueRun()
        {
            if (!IsServer || Phase.Value == GamePhase.Playing) return;
            var save = RunSave.Read();
            if (save == null) { AnnounceClientRpc("No saved journey to continue", "ui_back"); return; }
            ResetRunState(save.RunSeed, save.RunTime);
            if (save.Log != null) log.AddRange(save.Log);
            clearedEnclaves = save.ClearedEnclaves;
            distanceDone = save.Distance;
            stats.Kills = save.Kills;
            stats.LootCollected = save.Loot;
            var drive = WarpDrive.Instance;
            if (drive != null)
            {
                drive.ServerReset(0);
                if (save.DriveTiers != null && save.DriveTiers.Length == 4)
                {
                    drive.ServerSetTiers(save.DriveTiers[0], save.DriveTiers[1], save.DriveTiers[2], save.DriveTiers[3]);
                    drive.Exotic.Value = Mathf.Min(save.Exotic, drive.Capacity);
                }
            }
            // The saved system is entered fresh: the save does not know which of its crates were already opened.
            EnterSystem(save.Galaxy, save.System, false);
            VisitedMask.Value = save.VisitedMask | (1 << save.System);

            // The home ship first (the players' ships dock at it), then the players by name; each saved entry is
            // handed out once (two players may share a name). An old save without a working home ship gets a new one.
            var homeSave = save.Ships.Find(s => s.Class == ShipClass.HomeShip);
            SpawnHomeShip(homeSave);
            var players = new List<PlayerSave>(save.Players);
            var ships = save.Ships.FindAll(s => s.Class != ShipClass.HomeShip);
            int slot = 0;
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null) continue;
                string name = NameOf(pawn.OwnerClientId);
                var ps = players.Find(p => p.Name == name);
                var ss = ships.Find(s => s.Owner == name);
                players.Remove(ps);
                ships.Remove(ss);
                SetupPlayer(pawn, slot++, true, ps, ss);
            }
            StartClock();
            AnnounceClientRpc($"JOURNEY RESUMED - {GalaxyTitle(director.Def)}\n{director.System.Name}", "jump_gate");
        }

        void ResetRunState(int seed, float runTime)
        {
            stats = new RunSummary();
            log.Clear();
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerAbort();
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerResetJumps();
            distanceDone = 0f;
            clearedEnclaves = 0;
            home = null;
            ClearShips();
            RunSeed.Value = seed;
            VisitedMask.Value = 0;
            RunClockBase.Value = runTime;
        }

        void StartClock()
        {
            RunClockStart.Value = NetworkManager.ServerTime.Time;
            Phase.Value = GamePhase.Playing;
        }

        /// <summary>Host: back to the lobby after results.</summary>
        public void ServerReturnToLobby()
        {
            if (!IsServer) return;
            ClearRun();
            Phase.Value = GamePhase.Lobby;
        }

        void ClearRun()
        {
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerAbort();
            director.Clear();
            ClearShips();
            foreach (var p in PlayerPawn.All) if (p != null) { p.SetActiveServer(false); p.Inventory.ClearAll(); p.Inventory.Salvage.Value = 0; }
            RunSeed.Value = 0;
            BossActive.Value = false;
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerResetJumps();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToLobbyServerRpc(RpcParams rp = default)
        {
            if (rp.Receive.SenderClientId == NetworkManager.ServerClientId) ServerReturnToLobby();
        }

        // ================================================================ crew logbook (#86)

        static StoryDef Story => ContentCatalog.Instance != null ? ContentCatalog.Instance.Story : null;

        /// <summary>Server: the crew logbook of this journey so far.</summary>
        public IReadOnlyList<string> Logbook => log;

        /// <summary>
        /// Server: add an entry to the crew logbook of this journey (StoryDef template, {galaxy}/{eta} filled in here)
        /// and show it to everyone. The logbook ends up on the homecoming screen and in the save.
        /// </summary>
        public void ServerLog(string template, GalaxyDef galaxy = null, string part = "", string ship = "")
        {
            if (!IsServer || string.IsNullOrEmpty(template)) return;
            if (galaxy == null) galaxy = director != null ? director.Def : null;
            string eta = WarpDrive.Instance != null ? DriveMath.Eta(WarpDrive.Instance.EtaTo(DriveMath.Home)) : "--";
            string text = StoryDef.Format(template, galaxy != null ? galaxy.DisplayName : "", eta, part, ship);
            log.Add($"[{LogClock(RunTime)}] {text}");
            LogbookClientRpc(text);
        }

        static string LogClock(float seconds)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}";
        }

        [ClientRpc]
        void LogbookClientRpc(string text) => EventBus.RaiseLogbookEntry(text);

        /// <summary>
        /// Server: the journey ends with the homecoming (#74): the closing screen for everyone, the save is deleted.
        /// There is no game over any more (#145): failing costs loot or a disabled Nostos, never the journey.
        /// </summary>
        void ServerHomecoming(string reason, string logTemplate)
        {
            if (Phase.Value != GamePhase.Playing) return;
            ServerLog(logTemplate);
            var drive = WarpDrive.Instance;
            stats.DriveTier = drive != null ? drive.TierOf(DriveSlot.Core) : 1;
            stats.HomeEtaMinutes = 0f;
            stats.Log = string.Join("\n", log);
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerAbort();
            stats.Victory = true;
            stats.EndReason = reason;
            stats.GalaxyReached = GalaxyIndex.Value;
            stats.DistanceTraveled = distanceDone + Mathf.Max(0f, maxXThisSystem - (director.Layout != null ? director.Layout.StartX : 0f));
            stats.RunTimeSeconds = RunTime;
            stats.MemoriesEarned = stats.ComputeMemories();
            RunClockBase.Value = RunTime;
            Phase.Value = GamePhase.DemoComplete;
            RunSave.Delete(); // nothing left to resume after the homecoming
            foreach (var p in PlayerPawn.All) if (p != null) { var s = ShipSeat.SeatOf(p.OwnerClientId); if (s) s.Vacate(); }
            RunEndedClientRpc(stats.Victory, stats.EndReason, stats.GalaxyReached, stats.DistanceTraveled, stats.Kills, stats.LootCollected, stats.MemoriesEarned, stats.RunTimeSeconds,
                stats.DriveTier, stats.HomeEtaMinutes, stats.Log);
        }

        [ClientRpc]
        void RunEndedClientRpc(bool victory, string reason, int galaxy, float dist, int kills, int loot, int dust, float time, int driveTier, float eta, string logText)
        {
            var s = new RunSummary
            {
                Victory = victory, EndReason = reason, GalaxyReached = galaxy, DistanceTraveled = dist,
                Kills = kills, LootCollected = loot, MemoriesEarned = dust, RunTimeSeconds = time,
                DriveTier = driveTier, HomeEtaMinutes = eta, Log = logText
            };
            Meta.RecordRun(s);
            Sfx.Engine(false, 0f);
            EventBus.RaiseRunEnded(s);
        }

        // ================================================================ galaxies & systems

        void EnterSystem(int galaxy, int system, bool revisit)
        {
            DestroyedAsteroids.Clear();
            asteroidDamage.Clear();
            var plan = GalaxyGenerator.Generate(RunSeed.Value, galaxy);
            system = Mathf.Clamp(system, 0, plan.Systems.Length - 1);
            director.Enter(plan, system, revisit, (clearedEnclaves & (1 << (galaxy - 1))) != 0);
            maxXThisSystem = director.Layout.StartX;
            GalaxyIndex.Value = galaxy;
            SystemIndex.Value = system;
            VisitedMask.Value |= 1 << system;
            var sys = director.System;
            GameState.Objective = sys.Kind == SystemKind.Enclave && !director.EnclaveCleared ? "Warden enclave" : "Explore";
        }

        public static string GalaxyTitle(GalaxyDef def) =>
            def == null ? "" : $"GALAXY {def.Index}/{GalaxyDef.Count} - {def.DisplayName.ToUpperInvariant()} ({def.Designation})";

        // ---------------------------------------------------------------- warp (galaxy warps flown since #79, system warps since #80)

        // Since #108 nobody presses a warp button: the crew flies out through a jump zone at the system edge
        // (WarpFlight detects it and calls ServerJump). ServerWarp stays for cheats and DevTest.

        /// <summary>Route stop a galaxy warp target leads to (DriveMath.Home after the last galaxy), -1 for a system warp.</summary>
        public int StopOf(int target) =>
            target == WarpNextGalaxy ? GalaxyIndex.Value + 1 : target == WarpSkipGalaxy ? GalaxyIndex.Value + 2 : -1;

        /// <summary>Enough Exotic Matter in the warp drive for this target (system hops are free).</summary>
        bool FuelOk(int target, out string why)
        {
            why = "";
            var block = FuelBlock(target);
            if (block == JumpBlock.Open) return true;
            int stop = StopOf(target), need = WarpDrive.FuelTo(stop);
            var drive = WarpDrive.Instance;
            why = block == JumpBlock.Tank
                ? $"Exotic tank too small: this warp needs {need}, the tank holds {drive.Capacity}"
                : $"Not enough Exotic Matter: need {need}, tank {Mathf.FloorToInt(drive.Exotic.Value)}\nLoad it in the Warp Drive tab";
            return false;
        }

        JumpBlock FuelBlock(int target)
        {
            int stop = StopOf(target);
            var drive = WarpDrive.Instance;
            if (stop < 0 || drive == null || CheatManager.GodMode) return JumpBlock.Open;
            if (drive.Capacity < WarpDrive.FuelTo(stop)) return JumpBlock.Tank;
            return drive.HasFuelFor(stop) ? JumpBlock.Open : JumpBlock.Fuel;
        }

        /// <summary>
        /// Server (#108): what holds a jump zone shut for a warp to `target` - not enough Exotic Matter for a galaxy
        /// warp, or the gravity/combat lock (#80) around the zone or one of the ships inside it (god mode skips both).
        /// The crew rule is checked by WarpFlight.
        /// </summary>
        public JumpBlock JumpBlockFor(int target, Vector3 zoneCenter, List<Vector3> shipsInZone)
        {
            if (!ValidWarpTarget(target)) return JumpBlock.Tank;
            var fuel = FuelBlock(target);
            if (fuel != JumpBlock.Open || CheatManager.GodMode) return fuel;
            if (GravitySource.CruiseClearance(zoneCenter, out _) <= 0f) return JumpBlock.Gravity;
            foreach (var p in shipsInZone) if (GravitySource.CruiseClearance(p, out _) <= 0f) return JumpBlock.Gravity;
            float r2 = ShipFlight.CombatLockRange * ShipFlight.CombatLockRange;
            foreach (var e in EnemyFactory.Alive)
            {
                if (e == null || !e.IsSpawned || ((IThreatSource)e).Engagement <= 0.5f) continue;
                Vector3 ep = e.transform.position;
                if ((ep - zoneCenter).sqrMagnitude < r2) return JumpBlock.Combat;
                foreach (var p in shipsInZone) if ((ep - p).sqrMagnitude < r2) return JumpBlock.Combat;
            }
            return JumpBlock.Open;
        }

        bool ValidWarpTarget(int target)
        {
            if (target == WarpNextGalaxy) return true;
            if (target == WarpSkipGalaxy) return GalaxyIndex.Value + 2 <= GalaxyDef.Count;
            var p = Plan;
            return p != null && target >= 0 && target < p.Systems.Length && target != SystemIndex.Value;
        }

        public string WarpLabel(int target)
        {
            int stop = StopOf(target);
            if (stop >= 0)
            {
                if (stop > GalaxyDef.Count) return "HOME - the Milky Way";
                var next = ContentCatalog.GetGalaxy(stop);
                return next != null ? $"{next.DisplayName} ({next.Designation})" : "next galaxy";
            }
            var p = Plan;
            return p != null && target >= 0 && target < p.Systems.Length ? p.Systems[target].Name : "?";
        }

        /// <summary>
        /// Server (#108): the lead ship flew out through the jump zone to `target` and the entry spool-up is over: the
        /// crew is in the warp without a cut (WarpFlight rebases every ship around the lead under the bubble's veil).
        /// False when the warp is not possible any more (fuel was taken out meanwhile).
        /// </summary>
        public bool ServerJump(int target, ShipCore lead)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing || WarpFlight.Instance == null || WarpFlight.Instance.Active.Value) return false;
            if (!ValidWarpTarget(target)) return false;
            int stop = StopOf(target);
            if (stop >= 0 && (!FuelOk(target, out string why) || WarpDrive.Instance == null)) { AnnounceClientRpc("Warp aborted - " + why, "ui_back"); return false; }
            BeginWarpFlight(stop, stop >= 0 ? -1 : target, lead, true);
            return true;
        }

        /// <summary>Server: warp the whole crew now (cheats and DevTest; the crew itself flies out through a jump zone, #108).</summary>
        public void ServerWarp(int target)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            if (!ValidWarpTarget(target)) return;
            if (WarpFlight.Instance != null) WarpFlight.Instance.ServerResetJumps();
            int stop = StopOf(target);
            if (stop >= 0)
            {
                var flight = WarpFlight.Instance;
                if (flight != null && flight.Active.Value) return;
                if (!FuelOk(target, out string why) || WarpDrive.Instance == null)
                {
                    AnnounceClientRpc("Warp aborted - " + why, "ui_back");
                    return;
                }
                if (flight == null)
                {
                    // No flight controller (should not happen): the old instant warp.
                    WarpDrive.Instance.ServerConsume(WarpDrive.FuelTo(stop));
                    ServerArrive(stop);
                    return;
                }
                BeginWarpFlight(stop, -1, null, false);
            }
            else if (WarpFlight.Instance == null || WarpFlight.Instance.Active.Value) WarpTo(GalaxyIndex.Value, target);
            else BeginWarpFlight(-1, target, null, false);
        }

        /// <summary>
        /// Server: leave the system for the flown galaxy warp to `stop` (#79, the fuel burns along the route) or the hop
        /// to `system` (#80). Seamless (#108): out of a jump zone, the crew keeps its formation around the lead.
        /// </summary>
        void BeginWarpFlight(int stop, int system, ShipCore lead, bool seamless)
        {
            distanceDone += Mathf.Max(0f, maxXThisSystem - director.Layout.StartX);
            director.Clear();
            DestroyedAsteroids.Clear();
            asteroidDamage.Clear();
            if (lead != null && !lead.IsSpawned) lead = null;
            string label = system >= 0 ? WarpLabel(system) : WarpLabel(stop == GalaxyIndex.Value + 1 ? WarpNextGalaxy : WarpSkipGalaxy);
            if (system >= 0) WarpFlight.Instance.ServerBeginHop(GalaxyIndex.Value, system, lead, seamless);
            else WarpFlight.Instance.ServerBegin(GalaxyIndex.Value, stop, lead, seamless);
            if (stop > GalaxyDef.Count) ServerLog(Story != null ? Story.LogFlightHome : "");
            GameState.Objective = "Warp flight";
            WarpFlightStartedClientRpc(label, system >= 0, seamless);
        }

        [ClientRpc]
        void WarpFlightStartedClientRpc(string destination, bool hop, bool seamless)
        {
            // Out of a jump zone the bubble's veil is the transition (#108); the dev warps keep the flash.
            if (!seamless) Vfx.JumpFlash(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Sfx.PlayUI("jump_gate");
            Vfx.Shake(seamless ? 0.35f : 0.6f);
            EventBus.RaiseNotification(hop
                ? $"WARP - {destination}\nLead pilot: W/S throttle, steer through the fields, [C] cruise control"
                : $"WARP - {destination}\nLead pilot: W/S throttle, steer through the gaps, [C] cruise control, [G] drop out at signals");
        }

        /// <summary>
        /// Server: the flown warp reached its stop (WarpFlight): enter the galaxy, or the homecoming after the last one.
        /// With the lead ship (#108) the crew comes out of the bubble in formation, cruising into the system.
        /// </summary>
        public void ServerArrive(int stop, ShipCore lead = null)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            // The flight home ends in the Solar System. Placeholder until the Earth/Moon approach and landing of #74:
            // the homecoming result.
            if (stop > GalaxyDef.Count) { ServerHomecoming("Homecoming - the crew made it back to Sol!", Story != null ? Story.LogHomecoming : ""); return; }
            WarpTo(stop, 0, lead, -1);
        }

        /// <summary>Server: a hop (#80) reached its system: the crew comes out of the jump zone that leads back (#108).</summary>
        public void ServerArriveSystem(int system, ShipCore lead = null)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            WarpTo(GalaxyIndex.Value, system, lead, SystemIndex.Value);
        }

        /// <summary>Server, dev/test: warp the crew straight to any galaxy and system.</summary>
        public void ServerGoto(int galaxy, int system)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            if (WarpFlight.Instance != null) { WarpFlight.Instance.ServerAbort(); WarpFlight.Instance.ServerResetJumps(); }
            WarpTo(Mathf.Clamp(galaxy, 1, GalaxyDef.Count), Mathf.Max(0, system));
        }

        void WarpTo(int galaxy, int system, ShipCore lead = null, int fromSystem = -1)
        {
            bool newGalaxy = galaxy != GalaxyIndex.Value;
            distanceDone += Mathf.Max(0f, maxXThisSystem - director.Layout.StartX);


            if (newGalaxy)
            {
                VisitedMask.Value = 0;
            }
            EnterSystem(galaxy, system, !newGalaxy && IsVisited(system));
            bool seamless = lead != null && lead.IsSpawned;
            if (seamless) RegroupOutOfWarp(lead, newGalaxy ? -1 : fromSystem);
            else RegroupAtArrival();
            var def = director.Def;
            WarpedClientRpc(newGalaxy, newGalaxy ? GalaxyTitle(def) : director.System.Name,
                newGalaxy ? (def != null ? def.Intro : "") : GalaxyGenerator.KindLabel(director.System.Kind), seamless);
            if (newGalaxy) { ServerLog(Story != null ? Story.LogGalaxy : "", def); ServerWriteSave(); }
        }

        /// <summary>
        /// Out of the bubble (#108): the formation around the lead ship is carried over rigidly to the arrival point
        /// (the jump zone leading back, or the galaxy's arrival point), noses inward, and every ship glides in - the
        /// piloted ones in cruise. Pawns on foot ride along at the same offset.
        /// </summary>
        void RegroupOutOfWarp(ShipCore lead, int fromSystem)
        {
            director.Layout.Arrival(fromSystem, out Vector3 anchor, out Vector3 inward);
            Vector3 origin = lead.transform.position;
            Quaternion turn = Quaternion.FromToRotation(WarpBubble.Axis, inward);
            foreach (var s in ShipCore.All.ToArray())
            {
                if (s == null || !s.IsSpawned || s.Docked) continue; // docked ships ride on their clamps
                Vector3 p = anchor + turn * (s.transform.position - origin);
                s.GetComponent<NetTeleport>().ServerTeleport(p, turn * s.transform.rotation);
                s.RestoreServer(false, false);
                if (s.Flight != null) s.Flight.ArriveFromWarpRpc();
            }
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value || pawn.IsDown.Value || ShipSeat.SeatOf(pawn.OwnerClientId) != null) continue;
                if (GravityVolume.At(pawn.transform.position) != null) continue; // carried by the interior it stands in
                pawn.GetComponent<NetTeleport>().ServerTeleport(anchor + turn * (pawn.transform.position - origin), turn * pawn.transform.rotation);
            }
            Debug.Log($"[Warp] out of the bubble at {anchor} heading {inward} (from system {fromSystem})");
        }

        /// <summary>Ships into formation at the arrival point; pawns on foot follow their ship or the formation.</summary>
        void RegroupAtArrival()
        {
            var layout = director.Layout;
            int slot = 0;
            var shipPositions = new Dictionary<ShipCore, Vector3>();
            foreach (var s in ShipCore.All.ToArray())
            {
                if (s == null || !s.IsSpawned || s.Docked) continue; // docked ships ride on their clamps
                Vector3 p = layout.StartPoint + new Vector3(-(slot / 3) * 40f, (slot % 3) * 30f - 30f, (slot % 2 == 0 ? 1 : -1) * (20f + slot * 12f));
                if (s.IsHome) p = HomeShipPoint(layout);
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
                if (own != null && own.Docked && home != null) own = home;
                Vector3 p = own != null && shipPositions.TryGetValue(own, out var sp) ? sp + Vector3.up * 8f : layout.StartPoint + Random.onUnitSphere * 10f;
                if (pawn.IsDown.Value) continue;
                pawn.GetComponent<NetTeleport>().ServerTeleport(p, Quaternion.LookRotation(Vector3.right));
            }
        }

        [ClientRpc]
        void WarpedClientRpc(bool newGalaxy, string title, string subtitle, bool seamless)
        {
            if (!seamless) Vfx.JumpFlash(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Sfx.PlayUI("jump_gate");
            Vfx.Shake(seamless ? 0.3f : newGalaxy ? 0.8f : 0.5f);
            EventBus.RaiseWarpCompleted(GalaxyIndex.Value);
            EventBus.RaiseNotification($"{title}\n{subtitle}");
        }

        // ================================================================ suspend save (server)

        void ServerSuspend()
        {
            if (IsServer && Phase.Value == GamePhase.Playing) ServerWriteSave();
        }

        /// <summary>Write the suspend save from the live run (galaxy entry, host leaving, DevTest).</summary>
        public void ServerWriteSave()
        {
            if (!IsServer || RunSeed.Value == 0) return;
            var s = new RunState
            {
                RunSeed = RunSeed.Value, Galaxy = GalaxyIndex.Value, System = SystemIndex.Value, VisitedMask = VisitedMask.Value,
                ClearedEnclaves = clearedEnclaves, RunTime = RunTime,
                Distance = distanceDone, Kills = stats.Kills, Loot = stats.LootCollected, Log = new List<string>(log)
            };
            var drive = WarpDrive.Instance;
            if (drive != null)
            {
                s.DriveTiers = new[] { drive.TierOf(DriveSlot.Core), drive.TierOf(DriveSlot.Navigation), drive.TierOf(DriveSlot.Cooling), drive.TierOf(DriveSlot.Tank) };
                // Saved mid-flight: the crew continues before the warp, with the Exotic Matter it has burnt so far.
                s.Exotic = drive.Exotic.Value + (WarpFlight.Instance != null ? WarpFlight.Instance.FuelBurned : 0f);
            }
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value) continue;
                var ps = new PlayerSave
                {
                    Name = NameOf(pawn.OwnerClientId), Salvage = pawn.Inventory.Salvage.Value, Items = SaveItems(pawn.Inventory),
                    Suit = pawn.Gear.SuitTier.Value, Oxygen = pawn.Gear.OxygenTier.Value, Jetpack = pawn.Gear.JetpackTier.Value,
                    Drill = pawn.Gear.DrillTier.Value, Rifle = pawn.Gear.HasRifle.Value, BonusHp = pawn.Gear.BonusHp.Value, BonusOxygen = pawn.Gear.BonusOxygen.Value
                };
                s.Players.Add(ps);
            }
            foreach (var ship in ShipCore.All)
            {
                if (ship == null || !ship.IsSpawned || (!ship.Hull.IsAlive && !ship.IsHome)) continue; // a disabled Nostos is saved too (#145)
                bool crewShip = ship.IsHome;
                if (!crewShip && !NetworkManager.ConnectedClients.ContainsKey(ship.OwnerPlayer.Value)) continue;
                var ss = new ShipSave
                {
                    Owner = crewShip ? "" : NameOf(ship.OwnerPlayer.Value), Class = ship.Class, Name = ship.ShipName.Value.ToString(),
                    Derelict = ship.Derelict.Value, Fuel = ship.Fuel.Value,
                    Hull01 = ship.Hull.MaxHp.Value > 0f ? ship.Hull.Hp.Value / ship.Hull.MaxHp.Value : 1f,
                    Cargo = SaveItems(ship.Cargo)
                };
                foreach (var m in ship.Modules) ss.Modules.Add(m.ToString());
                s.Ships.Add(ss);
            }
            RunSave.Write(s);
        }

        static List<ItemSave> SaveItems(NetInventory inv)
        {
            var list = new List<ItemSave>();
            if (inv == null) return list;
            foreach (var st in inv.Slots) if (!st.IsEmpty) list.Add(new ItemSave { Id = st.IdString, Count = st.Count });
            return list;
        }

        static void LoadItems(NetInventory inv, List<ItemSave> items)
        {
            if (inv == null || items == null) return;
            foreach (var it in items) if (!string.IsNullOrEmpty(it.Id) && it.Count > 0) inv.Add(it.Id, it.Count);
        }

        static void ApplyShipSave(ShipCore ship, ShipSave save)
        {
            if (ship == null || save == null) return;
            for (int i = 0; i < save.Modules.Count && i < ship.Modules.Count; i++) ship.Modules[i] = save.Modules[i];
            ship.Cargo.ClearAll();
            LoadItems(ship.Cargo, save.Cargo);
            ship.RestoreServer(false, false);
            ship.Fuel.Value = save.Fuel;
            ship.Hull.Hp.Value = Mathf.Clamp01(save.Hull01) * ship.Hull.MaxHp.Value;
            if (ship.IsHome) ship.Derelict.Value = save.Derelict; // a disabled Nostos stays disabled (#145)
        }

        // ================================================================ players & ships

        void SetupPlayer(PlayerPawn pawn, int slot, bool runStart, PlayerSave save, ShipSave shipSave = null)
        {
            ulong c = pawn.OwnerClientId;
            if (!loadouts.TryGetValue(c, out var lo)) lo = new RunLoadout { Ship = ShipClass.Dart };
            var layout = director.Layout;
            Vector3 basePos = layout.StartPoint + new Vector3(0f, (slot % 2) * 25f - 12f, (slot / 2) * 30f - 15f + slot * 8f);
            if (!runStart) basePos = TeamCenter() + Random.onUnitSphere * 30f;

            pawn.Inventory.ClearAll();
            pawn.Gear.ResetServer();
            if (save != null)
            {
                pawn.Inventory.Salvage.Value = save.Salvage;
                LoadItems(pawn.Inventory, save.Items);
                pawn.Gear.SuitTier.Value = save.Suit; pawn.Gear.OxygenTier.Value = save.Oxygen;
                pawn.Gear.JetpackTier.Value = save.Jetpack; pawn.Gear.DrillTier.Value = save.Drill;
                pawn.Gear.HasRifle.Value = save.Rifle; pawn.Gear.BonusHp.Value = save.BonusHp; pawn.Gear.BonusOxygen.Value = save.BonusOxygen;
            }
            else
            {
                pawn.Inventory.Salvage.Value = 80;
                if (lo.Has("reinforced_suit")) pawn.Gear.BonusHp.Value = 30f;
                if (lo.Has("deep_lungs")) pawn.Gear.BonusOxygen.Value = 0.4f;
                if (lo.Has("head_start")) pawn.Inventory.Salvage.Value += 150;
                if (lo.Has("spare_cells")) pawn.Inventory.Add(WarpDrive.ExoticId, 2);
                if (lo.Has("salvager")) { pawn.Inventory.Add("scrap", 10); pawn.Inventory.Add("fuel", 5); }
                if (lo.Has("rifle_start")) pawn.Gear.HasRifle.Value = true;
                pawn.Inventory.Add("medkit", 2);
            }

            var ship = SpawnShip(shipSave != null ? shipSave.Class : lo.Ship, c, basePos, Quaternion.LookRotation(Vector3.right));
            if (ship != null && home != null && home.IsSpawned)
            {
                // the crew wakes aboard their docked ships at the Nostos (#49): hold thrust to launch
                int clamp = home.FreeClamp(runStart ? slot : -1);
                if (clamp >= 0) ship.ServerDock(home, clamp);
            }
            if (ship != null)
            {
                if (shipSave != null) ApplyShipSave(ship, shipSave);
                else
                {
                    if (lo.Has("laser_mk2")) ship.InstallServer("module_laser_mk2", false);
                    if (lo.Has("shield_mk2")) ship.InstallServer("module_shield_mk2", false);
                    if (lo.Has("engine_mk2")) ship.InstallServer("module_engine_mk2", false);
                    ship.RestoreServer(true, true);
                }
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

        /// <summary>Where the home ship waits at a galaxy's arrival point (beside the crew's formation).</summary>
        static Vector3 HomeShipPoint(SystemLayout layout) => layout.StartPoint + new Vector3(-60f, 0f, -90f);

        /// <summary>
        /// Server (#49): the home ship Nostos at the arrival point, from the suspend save or new. Crewed by everyone
        /// (no owner); losing it ends the loop.
        /// </summary>
        void SpawnHomeShip(ShipSave save)
        {
            var cat = ContentCatalog.Instance;
            if (cat == null || cat.ShipHome == null) return;
            var go = Instantiate(cat.ShipHome, HomeShipPoint(director.Layout), Quaternion.LookRotation(Vector3.right));
            go.GetComponent<NetworkObject>().Spawn(true);
            home = go.GetComponent<ShipCore>();
            home.Derelict.Value = false;
            var def = home.Def;
            home.ShipName.Value = save != null && !string.IsNullOrEmpty(save.Name) ? save.Name : def != null ? def.DisplayName : "Nostos";
            if (save != null) ApplyShipSave(home, save);
        }

        public Vector3 TeamCenter()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in PlayerPawn.All)
                if (p != null && p.Active.Value && !p.IsDown.Value) { sum += p.transform.position; n++; }
            return n > 0 ? sum / n : (director != null && director.Layout != null ? director.Layout.StartPoint : Vector3.zero);
        }

        void TickPlayers(float dt)
        {
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null) continue;
                // Late joiner: inactive pawn while playing.
                if (!pawn.Active.Value) { SetupPlayer(pawn, 0, false, null); AnnounceClientRpc(NameOf(pawn.OwnerClientId) + " joined the expedition", "spawn"); continue; }
                if (pawn.transform.position.x > maxXThisSystem && !pawn.IsDown.Value) maxXThisSystem = pawn.transform.position.x;

                if (pawn.IsDown.Value && !pawn.IsLost.Value)
                {
                    if (pawn.RespawnAt.Value <= 0f) pawn.RespawnAt.Value = Time.time + 8f;
                    else if (Time.time >= pawn.RespawnAt.Value && TryRespawnPoint(pawn.OwnerClientId, out var pos, out var rot, out int pod))
                    {
                        pawn.RespawnAt.Value = 0f;
                        pawn.RespawnServer(pos, rot);
                        if (pod >= 0 && home != null) home.ServerCryoWake(pod);
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
                        var dart = SpawnShip(ShipClass.Dart, c, p, Quaternion.LookRotation(Vector3.right));
                        // the Nostos carries a spare (#49): it waits at a free clamp
                        int clamp = home != null && home.IsSpawned && dart != null ? home.FreeClamp(WarpFlight.PlayerClamp(dart)) : -1;
                        if (clamp >= 0 && dart.ServerDock(home, clamp)) AnnounceClientRpc($"{NameOf(c)}: a replacement Dart waits at clamp {clamp + 1} of the {home.ShipName.Value}", "spawn");
                        else AnnounceClientRpc(NameOf(c) + " received a replacement Dart", "spawn");
                    }
                }
            }
        }

        /// <summary>
        /// Where a fallen crew member wakes up: the cryo pod of their player slot in the Nostos (#49, `pod` >= 0),
        /// else at their own ship, any ship, or beside a living crew member.
        /// </summary>
        bool TryRespawnPoint(ulong client, out Vector3 pos, out Quaternion rot, out int pod)
        {
            pod = -1;
            if (home != null && home.IsSpawned && !home.Derelict.Value && home.CryoPods.Length > 0)
            {
                int slot = 0;
                foreach (var p in PlayerPawn.All) { if (p == null) continue; if (p.OwnerClientId == client) break; slot++; }
                pod = slot % home.CryoPods.Length;
                var cp = home.CryoPods[pod];
                var at = cp.Exit != null ? cp.Exit : cp.transform;
                pos = at.position;
                rot = at.rotation;
                return true;
            }
            var ship = ShipCore.OwnedBy(client);
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

        void ClearShips()
        {
            foreach (var s in ShipCore.All.ToArray())
                if (s != null && s.IsSpawned) { ShipSeat.VacateAllOn(s); s.NetworkObject.Despawn(true); }
            shipRespawnTimers.Clear();
        }

        /// <summary>Solar flare radiation for players in the open.</summary>
        float hazardTimer;
        void TickHazards(float dt)
        {
            hazardTimer -= dt;
            if (hazardTimer > 0f) return;
            hazardTimer = 0.5f;
            if (!director.FlareActive) return;
            foreach (var p in PlayerPawn.All)
                if (p != null && p.Active.Value && !p.IsDown.Value && !p.ServerSheltered)
                    p.Health.ApplyDamage(new DamageInfo { Amount = 2.5f, Type = DamageType.Environmental, Point = p.transform.position });
        }

        // ================================================================ announcements

        public void Announce(string msg, string sfx) => AnnounceClientRpc(msg, sfx);
        public void PingAll(Vector3 pos, string label) => PingAllClientRpc(pos, label);

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
            if (!s.IsHome) AnnounceClientRpc(s.ShipName.Value + " was destroyed!", "alarm");
            if (s != home || !s.IsHome) return;
            // The home ship is disabled, not gone (#145): no warp, no workbench, no cryo respawn until the crew repairs it.
            var st = Story;
            if (Phase.Value != GamePhase.Playing) return;
            ServerLog(st != null ? st.LogHomeLost : "", null, "", s.ShipName.Value.ToString());
            AnnounceClientRpc($"{s.ShipName.Value} IS DISABLED\nRepair it at its terminal ({ShipCore.HomeRepairSalvage} Salvage): no warp, workbench or cryo until then", "alarm");
            ServerWriteSave();
        }

        void OnBossDefeated()
        {
            stats.Kills += 10;
            var boss = BossBrain.Instance;
            Vector3 at = boss != null ? boss.transform.position : (director.Layout != null ? director.Layout.BossPos : TeamCenter());
            if (director.System != null && director.System.Kind == SystemKind.Enclave)
            {
                clearedEnclaves |= 1 << (GalaxyIndex.Value - 1);
                ServerLog(Story != null ? Story.LogWarden : "");
            }
            director.OnWardenDefeated(at);
            GameState.Objective = "Explore";
        }

        static string NameOf(ulong client)
        {
            var np = NetworkPlayer.GetByClient(client);
            return np != null ? np.PlayerName.Value.ToString() : "A player";
        }

        // ================================================================ cheats (F1)

        static readonly string[] CheatNames =
        {
            "God Mode (server) on/off", "Give resources + salvage", "Warp to next system", "Spawn enemy near me",
            "Warp to next galaxy", "Wake a Warden near me", "Trigger random event", "Refill my ship"
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
            if (Phase.Value != GamePhase.Playing && id != 0) return;
            switch (id)
            {
                case 0:
                    CheatManager.GodMode = !CheatManager.GodMode;
                    AnnounceClientRpc("God mode " + (CheatManager.GodMode ? "ON" : "OFF"), "ui_click");
                    break;
                case 1:
                    if (pawn == null) return;
                    foreach (var r in new[] { "ore", "crystal", "scrap", "fuel" }) pawn.Inventory.Add(r, 20);
                    pawn.Inventory.Add(WarpDrive.ExoticId, 20);
                    pawn.Inventory.Salvage.Value += 500;
                    break;
                case 2:
                    ServerWarp((SystemIndex.Value + 1) % Plan.Systems.Length);
                    break;
                case 3:
                    if (pawn == null) return;
                    var src = GravitySource.Dominant(pawn.transform.position);
                    bool surface = src != null && src.SurfaceDistance(pawn.transform.position) < 120f;
                    Vector3 fwd = pawn.CameraPoint.forward;
                    director.SpawnEnemyNear(pawn.transform.position + fwd * (surface ? 20f : 120f) + pawn.transform.up * 2f, -fwd, surface);
                    break;
                case 4:
                    ServerWarp(WarpNextGalaxy);
                    break;
                case 5:
                    if (pawn != null) director.SpawnWarden(pawn.transform.position + pawn.CameraPoint.forward * 900f);
                    break;
                case 6:
                    director.TriggerEvent((SectorEventKind)Random.Range(0, 3));
                    break;
                case 7:
                    var ship = ShipCore.OwnedBy(c);
                    if (ship != null) ship.RestoreServer(true, true);
                    break;
            }
        }
    }
}
