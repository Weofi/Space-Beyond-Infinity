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

        public const int StartingRespawns = 4;
        /// <summary>WarpTarget value for the one-way warp to the next galaxy (or home from the last one).</summary>
        public const int WarpNextGalaxy = -1;
        public const float WarpDelay = 5f;

        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        public NetworkVariable<int> RunSeed = new NetworkVariable<int>(0);
        public NetworkVariable<int> GalaxyIndex = new NetworkVariable<int>(1);
        public NetworkVariable<int> SystemIndex = new NetworkVariable<int>(0);
        /// <summary>Bit i = system i of the current galaxy was visited.</summary>
        public NetworkVariable<int> VisitedMask = new NetworkVariable<int>(0);
        public NetworkVariable<float> RunClockBase = new NetworkVariable<float>(0f);
        public NetworkVariable<double> RunClockStart = new NetworkVariable<double>(0);
        public NetworkVariable<int> RespawnsLeft = new NetworkVariable<int>(StartingRespawns);
        public NetworkVariable<float> WarpCountdown = new NetworkVariable<float>(-1f);
        public NetworkVariable<int> WarpTarget = new NetworkVariable<int>(0);
        public NetworkVariable<bool> BossActive = new NetworkVariable<bool>(false);
        /// <summary>Asteroid ids (SystemVisuals.RockId) destroyed in the current system.</summary>
        public NetworkList<int> DestroyedAsteroids;

        // ---- server state ----
        readonly Dictionary<ulong, RunLoadout> loadouts = new Dictionary<ulong, RunLoadout>();
        readonly Dictionary<ulong, float> shipRespawnTimers = new Dictionary<ulong, float>();
        readonly Dictionary<int, float> asteroidDamage = new Dictionary<int, float>();
        SystemDirector director;
        RunSummary stats = new RunSummary();
        float distanceDone;
        float maxXThisSystem;
        float autoStartTimer = 3f;
        int clearedEnclaves;
        ShipCore ark;

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
            int key = playing ? GalaxyGenerator.GalaxySeed(RunSeed.Value, GalaxyIndex.Value) ^ (SystemIndex.Value * 7919 + 1) : int.MinValue;
            if (key == shownKey) return;
            shownKey = key;
            if (!playing) { if (visuals != null) visuals.Clear(); Sfx.Music(0); return; }
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
            TickWarp(dt);
            director.Tick(dt);
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

        /// <summary>Host: a new loop - fresh seed, the crew wakes up at the wormhole exit in M33.</summary>
        public void ServerStartRun()
        {
            if (!IsServer || Phase.Value == GamePhase.Playing) return;
            ResetRunState(Random.Range(1, int.MaxValue), StartingRespawns, 0f);
            foreach (var pawn in PlayerPawn.All) if (pawn != null) pawn.Gear.ResetServer();
            EnterSystem(1, 0, false);
            int slot = 0;
            foreach (var pawn in PlayerPawn.All)
                if (pawn != null) SetupPlayer(pawn, slot++, true, null);
            SpawnDerelictArk();
            StartClock();
            var def = director.Def;
            AnnounceClientRpc($"{GalaxyTitle(def)}\n{(def != null ? def.Intro : "")}", "jump_gate");
            ServerWriteSave();
        }

        /// <summary>Host: resume the suspend-saved run at the arrival point of its saved system.</summary>
        public void ServerContinueRun()
        {
            if (!IsServer || Phase.Value == GamePhase.Playing) return;
            var save = RunSave.Read();
            if (save == null) { AnnounceClientRpc("No saved run to continue", "ui_back"); return; }
            ResetRunState(save.RunSeed, save.RespawnsLeft, save.RunTime);
            clearedEnclaves = save.ClearedEnclaves;
            distanceDone = save.Distance;
            stats.Kills = save.Kills;
            stats.LootCollected = save.Loot;
            // The saved system is entered fresh: the save does not know which of its crates were already opened.
            EnterSystem(save.Galaxy, save.System, false);
            VisitedMask.Value = save.VisitedMask | (1 << save.System);

            int slot = 0;
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null) continue;
                string name = NameOf(pawn.OwnerClientId);
                SetupPlayer(pawn, slot++, true, save.Players.Find(p => p.Name == name), save.Ships.Find(s => s.Owner == name && s.Class != ShipClass.Ark));
            }
            var arkSave = save.Ships.Find(s => s.Class == ShipClass.Ark);
            if (arkSave != null && !arkSave.Derelict) RestoreArk(arkSave);
            else if (save.Galaxy == 1 && save.System == 0) SpawnDerelictArk();
            StartClock();
            AnnounceClientRpc($"RUN RESUMED - {GalaxyTitle(director.Def)}\n{director.System.Name}", "jump_gate");
        }

        void ResetRunState(int seed, int respawns, float runTime)
        {
            stats = new RunSummary();
            distanceDone = 0f;
            clearedEnclaves = 0;
            RespawnsLeft.Value = respawns;
            WarpCountdown.Value = -1f;
            ark = null;
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
            director.Clear();
            ClearShips();
            foreach (var p in PlayerPawn.All) if (p != null) { p.SetActiveServer(false); p.Inventory.ClearAll(); p.Inventory.Credits.Value = 0; }
            RunSeed.Value = 0;
            BossActive.Value = false;
            WarpCountdown.Value = -1f;
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
            stats.GalaxyReached = GalaxyIndex.Value;
            stats.DistanceTraveled = distanceDone + Mathf.Max(0f, maxXThisSystem - (director.Layout != null ? director.Layout.StartX : 0f));
            stats.RunTimeSeconds = RunTime;
            stats.StardustEarned = stats.ComputeStardust();
            RunClockBase.Value = RunTime;
            WarpCountdown.Value = -1f;
            Phase.Value = victory ? GamePhase.DemoComplete : GamePhase.GameOver;
            RunSave.Delete(); // one slot, no reloading after death (and nothing left to resume after the homecoming)
            foreach (var p in PlayerPawn.All) if (p != null) { var s = ShipSeat.SeatOf(p.OwnerClientId); if (s) s.Vacate(); }
            RunEndedClientRpc(stats.Victory, stats.EndReason, stats.GalaxyReached, stats.DistanceTraveled, stats.Kills, stats.LootCollected, stats.StardustEarned, stats.RunTimeSeconds);
        }

        [ClientRpc]
        void RunEndedClientRpc(bool victory, string reason, int galaxy, float dist, int kills, int loot, int dust, float time)
        {
            var s = new RunSummary
            {
                Victory = victory, EndReason = reason, GalaxyReached = galaxy, DistanceTraveled = dist,
                Kills = kills, LootCollected = loot, StardustEarned = dust, RunTimeSeconds = time
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

        // ---------------------------------------------------------------- warp (placeholder until the flown warp of #73)

        /// <summary>
        /// A pilot sets the warp course: another system of this galaxy, or (WarpNextGalaxy) the one-way warp to the
        /// next galaxy - from the last galaxy that is the flight home. Starts a short countdown, then everyone warps.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestWarpServerRpc(int target, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (Phase.Value != GamePhase.Playing || WarpCountdown.Value >= 0f) return;
            var seat = ShipSeat.SeatOf(c);
            if (seat == null || seat.Role != StationRole.Pilot) { ToastRpc("Only a pilot can set the warp course", RpcTarget.Single(c, RpcTargetUse.Temp)); return; }
            if (!ValidWarpTarget(target)) return;
            WarpTarget.Value = target;
            WarpCountdown.Value = WarpDelay;
            AnnounceClientRpc($"{NameOf(c)} set course: {WarpLabel(target)}\nWarp in {WarpDelay:0} s - everyone aboard!", "jump_charge");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CancelWarpServerRpc(RpcParams rp = default)
        {
            if (WarpCountdown.Value < 0f) return;
            WarpCountdown.Value = -1f;
            AnnounceClientRpc("Warp cancelled by " + NameOf(rp.Receive.SenderClientId), "ui_back");
        }

        bool ValidWarpTarget(int target)
        {
            if (target == WarpNextGalaxy) return true;
            var p = Plan;
            return p != null && target >= 0 && target < p.Systems.Length && target != SystemIndex.Value;
        }

        public string WarpLabel(int target)
        {
            if (target == WarpNextGalaxy)
            {
                if (GalaxyIndex.Value >= GalaxyDef.Count) return "HOME - the Milky Way";
                var next = ContentCatalog.GetGalaxy(GalaxyIndex.Value + 1);
                return next != null ? $"{next.DisplayName} ({next.Designation})" : "next galaxy";
            }
            var p = Plan;
            return p != null && target >= 0 && target < p.Systems.Length ? p.Systems[target].Name : "?";
        }

        void TickWarp(float dt)
        {
            if (WarpCountdown.Value < 0f) return;
            WarpCountdown.Value -= dt;
            if (WarpCountdown.Value <= 0f) ServerWarp(WarpTarget.Value);
        }

        /// <summary>Server: warp the whole crew now (also cheats and DevTest).</summary>
        public void ServerWarp(int target)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            WarpCountdown.Value = -1f;
            if (!ValidWarpTarget(target)) return;
            bool newGalaxy = target == WarpNextGalaxy;
            if (newGalaxy && GalaxyIndex.Value >= GalaxyDef.Count)
            {
                // Placeholder for the flown home flight and landing (#73, #74).
                EndRun(true, "Homecoming - the crew made it back to the Milky Way!");
                return;
            }
            if (newGalaxy) WarpTo(GalaxyIndex.Value + 1, 0);
            else WarpTo(GalaxyIndex.Value, target);
        }

        /// <summary>Server, dev/test: warp the crew straight to any galaxy and system.</summary>
        public void ServerGoto(int galaxy, int system)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            WarpCountdown.Value = -1f;
            WarpTo(Mathf.Clamp(galaxy, 1, GalaxyDef.Count), Mathf.Max(0, system));
        }

        void WarpTo(int galaxy, int system)
        {
            bool newGalaxy = galaxy != GalaxyIndex.Value;
            distanceDone += Mathf.Max(0f, maxXThisSystem - director.Layout.StartX);

            // Abandon unclaimed derelicts.
            if (ark != null && ark.IsSpawned && ark.Derelict.Value) { ark.NetworkObject.Despawn(true); ark = null; }

            if (newGalaxy)
            {
                VisitedMask.Value = 0;
                RespawnsLeft.Value += Mathf.Max(0, galaxy - GalaxyIndex.Value);
            }
            EnterSystem(galaxy, system, !newGalaxy && IsVisited(system));
            RegroupAtArrival();
            var def = director.Def;
            WarpedClientRpc(newGalaxy, newGalaxy ? GalaxyTitle(def) : director.System.Name,
                newGalaxy ? (def != null ? def.Intro : "") : GalaxyGenerator.KindLabel(director.System.Kind));
            if (newGalaxy) ServerWriteSave();
        }

        /// <summary>Ships into formation at the arrival point; pawns on foot follow their ship or the formation.</summary>
        void RegroupAtArrival()
        {
            var layout = director.Layout;
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
        }

        [ClientRpc]
        void WarpedClientRpc(bool newGalaxy, string title, string subtitle)
        {
            Vfx.JumpFlash(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Sfx.PlayUI("jump_gate");
            Vfx.Shake(newGalaxy ? 0.8f : 0.5f);
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
                ClearedEnclaves = clearedEnclaves, RunTime = RunTime, RespawnsLeft = RespawnsLeft.Value,
                Distance = distanceDone, Kills = stats.Kills, Loot = stats.LootCollected
            };
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value) continue;
                var ps = new PlayerSave
                {
                    Name = NameOf(pawn.OwnerClientId), Credits = pawn.Inventory.Credits.Value, Items = SaveItems(pawn.Inventory),
                    Suit = pawn.Gear.SuitTier.Value, Oxygen = pawn.Gear.OxygenTier.Value, Jetpack = pawn.Gear.JetpackTier.Value,
                    Drill = pawn.Gear.DrillTier.Value, Rifle = pawn.Gear.HasRifle.Value, BonusHp = pawn.Gear.BonusHp.Value, BonusOxygen = pawn.Gear.BonusOxygen.Value
                };
                s.Players.Add(ps);
            }
            foreach (var ship in ShipCore.All)
            {
                if (ship == null || !ship.IsSpawned || !ship.Hull.IsAlive) continue;
                bool crewShip = ship.Class == ShipClass.Ark;
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
                pawn.Inventory.Credits.Value = save.Credits;
                LoadItems(pawn.Inventory, save.Items);
                pawn.Gear.SuitTier.Value = save.Suit; pawn.Gear.OxygenTier.Value = save.Oxygen;
                pawn.Gear.JetpackTier.Value = save.Jetpack; pawn.Gear.DrillTier.Value = save.Drill;
                pawn.Gear.HasRifle.Value = save.Rifle; pawn.Gear.BonusHp.Value = save.BonusHp; pawn.Gear.BonusOxygen.Value = save.BonusOxygen;
            }
            else
            {
                pawn.Inventory.Credits.Value = 80;
                if (lo.Has("reinforced_suit")) pawn.Gear.BonusHp.Value = 30f;
                if (lo.Has("deep_lungs")) pawn.Gear.BonusOxygen.Value = 0.4f;
                if (lo.Has("head_start")) pawn.Inventory.Credits.Value += 150;
                if (lo.Has("spare_cells")) pawn.Inventory.Add("cell", 1);
                if (lo.Has("salvager")) { pawn.Inventory.Add("scrap", 10); pawn.Inventory.Add("fuel", 5); }
                if (lo.Has("rifle_start")) pawn.Gear.HasRifle.Value = true;
                pawn.Inventory.Add("medkit", 2);
            }

            var ship = SpawnShip(shipSave != null ? shipSave.Class : lo.Ship, c, basePos, Quaternion.LookRotation(Vector3.right));
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

        void SpawnDerelictArk()
        {
            var cat = ContentCatalog.Instance;
            if (cat.ShipArk == null) return;
            var go = Instantiate(cat.ShipArk, director.Layout.ArkPos, Quaternion.Euler(8f, 70f, 14f));
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

        void RestoreArk(ShipSave save)
        {
            var cat = ContentCatalog.Instance;
            if (cat.ShipArk == null) return;
            var go = Instantiate(cat.ShipArk, director.Layout.StartPoint + new Vector3(-60f, 0f, -90f), Quaternion.LookRotation(Vector3.right));
            go.GetComponent<NetworkObject>().Spawn(true);
            ark = go.GetComponent<ShipCore>();
            ark.Derelict.Value = false;
            ark.ShipName.Value = string.IsNullOrEmpty(save.Name) ? "Ark" : save.Name;
            ApplyShipSave(ark, save);
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

        void ClearShips()
        {
            foreach (var s in ShipCore.All.ToArray())
                if (s != null && s.IsSpawned) { ShipSeat.VacateAllOn(s); s.NetworkObject.Despawn(true); }
            shipRespawnTimers.Clear();
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
            if (active > 0 && gone == active) EndRun(false, "The whole crew was lost - the loop begins again");
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
            if (s == ark) ark = null;
            AnnounceClientRpc(s.ShipName.Value + " was destroyed!", "alarm");
        }

        void OnBossDefeated()
        {
            stats.Kills += 10;
            var boss = BossBrain.Instance;
            Vector3 at = boss != null ? boss.transform.position : (director.Layout != null ? director.Layout.BossPos : TeamCenter());
            if (director.System != null && director.System.Kind == SystemKind.Enclave) clearedEnclaves |= 1 << (GalaxyIndex.Value - 1);
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
            "God Mode (server) on/off", "Give resources + credits", "Warp to next system", "Spawn enemy near me",
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
                    pawn.Inventory.Add("cell", 3);
                    pawn.Inventory.Credits.Value += 500;
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
