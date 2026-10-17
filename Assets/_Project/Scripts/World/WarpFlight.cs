using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.AI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Player;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// The flown warp between galaxies (#79, part 2 of #73). Lives on the GameDirector next to RunManager and
    /// WarpDrive. Instead of an instant jump the crew flies a corridor of WarpRoute: the ships stay near the origin
    /// (floating origin) while obstacles stream past along -X, deterministic from the flight seed on every peer.
    /// Server state: route, progress, throttle, heat, cruise control, emergency stops, destroyed obstacles, dropping
    /// out at signals. The pilot of the lead ship throttles (W/S) and steers sideways; every other ship holds its
    /// slot in the warp bubble (ShipFlight) and its crew shoots rocks and interceptors. Each ship's transform
    /// authority detects its own impacts (WarpCorridorView) and reports them; the server applies damage and stops.
    /// The flight home from the last galaxy (#81) is the longest flight: its last FinaleSeconds of planned flight
    /// (the whole flight with the Home Drive) are the densest stretch (WarpRoute.FinaleStart), without signals.
    /// </summary>
    public partial class WarpFlight : NetworkBehaviour
    {
        public static WarpFlight Instance { get; private set; }

        /// <summary>Corridor speed at full throttle per core tier (m/s): what the crew sees, not the light-years.</summary>
        static readonly float[] VisualSpeed = { 320f, 420f, 560f, 720f, 900f, 1200f };
        /// <summary>Seconds of warning the nav computer gives at the speed it is rated for (scanner range = this x safe speed).</summary>
        const float ScanSeconds = 2.6f;
        /// <summary>How close (route metres) a signal must be to drop out next to it.</summary>
        public const float DropRange = 1500f;
        const float ResumeDelay = 5f;
        const float HeatGain = 0.12f, HeatCool = 0.045f;
        /// <summary>Heat starts above this share of the cooling's sustainable throttle (so even a matched build has to ease off now and then).</summary>
        const float HeatThreshold = 0.92f;
        const float CollisionStall = 4f;
        /// <summary>Engaged enemies this close to the lead ship (m) pull the bubble out of warp (#80).</summary>
        public const float InterdictRange = 300f;

        // ---- the flight home (#81) ----
        /// <summary>Planned seconds at the end of the flight home that form the dense finale stretch (the whole flight with the Home Drive, ~5 min).</summary>
        public const float FinaleSeconds = 300f;

        public NetworkVariable<bool> Active = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> DroppedOut = new NetworkVariable<bool>(false);
        public NetworkVariable<int> Seed = new NetworkVariable<int>(0);
        public NetworkVariable<int> From = new NetworkVariable<int>(1);
        public NetworkVariable<int> To = new NetworkVariable<int>(2);
        /// <summary>System of galaxy From a hop (#80) leads to; -1 = a galaxy warp.</summary>
        public NetworkVariable<int> TargetSystem = new NetworkVariable<int>(-1);
        public NetworkVariable<float> Length = new NetworkVariable<float>(1f);
        public NetworkVariable<int> CoreTier = new NetworkVariable<int>(1);
        public NetworkVariable<float> Progress = new NetworkVariable<float>(0f);
        public NetworkVariable<float> Throttle = new NetworkVariable<float>(0f);
        public NetworkVariable<float> Heat = new NetworkVariable<float>(0f);
        public NetworkVariable<bool> Cruise = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> Stalled = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> Overheated = new NetworkVariable<bool>(false);
        public NetworkVariable<ulong> Lead = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>Obstacle id in the lead ship's lane within the scanner range, -1 = clear.</summary>
        public NetworkVariable<int> Hazard = new NetworkVariable<int>(-1);
        public NetworkVariable<int> DropSignal = new NetworkVariable<int>(-1);
        public NetworkVariable<float> ResumeCountdown = new NetworkVariable<float>(-1f);
        /// <summary>Bit per signal id the crew already dropped out at.</summary>
        public NetworkVariable<int> VisitedSignals = new NetworkVariable<int>(0);
        public NetworkList<int> Destroyed;
        /// <summary>Route position where the dense finale stretch of the flight home begins (#81); -1 = no finale.</summary>
        public NetworkVariable<float> FinaleStart = new NetworkVariable<float>(-1f);

        // ---- shared (every peer) ----
        WarpRoute route;
        int routeKey = int.MinValue;
        WarpCorridorView view;
        float localProgress;
        float localProgressPrev;

        // ---- server ----
        float commanded;
        float lastPilotInput = -99f;
        float stallTimer;
        float hazardTimer;
        float fuelNeed, fuelBurned;
        int lastPatrolChunk = -1;
        readonly Dictionary<int, float> obstacleDamage = new Dictionary<int, float>();
        readonly HashSet<long> impacts = new HashSet<long>();
        readonly List<NetworkObject> siteObjects = new List<NetworkObject>();
        readonly List<WarpObstacle> query = new List<WarpObstacle>();
        Combat.HealthComponent watchedHull;
        float lastImpactAt = -99f;

        // ---- lead pilot client ----
        float pilotCommand;
        bool wasPiloting;
        int pilotSeed;
        float pilotSyncUntil;
        float sendTimer;

        public static bool Flying => Instance != null && Instance.Active.Value && !Instance.DroppedOut.Value;

        /// <summary>This flight is a hop between two systems of one galaxy (#80).</summary>
        public bool IsHop => TargetSystem.Value >= 0;

        /// <summary>The route of the current flight (rebuilt from the synced seed), null when not warping.</summary>
        public WarpRoute Route
        {
            get
            {
                if (!Active.Value) return null;
                int key = Seed.Value ^ From.Value * 7919 ^ To.Value * 104729 ^ Mathf.RoundToInt(Length.Value) ^ (TargetSystem.Value + 1) * 15485863
                          ^ Mathf.RoundToInt(FinaleStart.Value) * 31;
                if (route == null || key != routeKey) { route = new WarpRoute(Seed.Value, From.Value, To.Value, Length.Value, IsHop, FinaleStart.Value); routeKey = key; }
                return route;
            }
        }

        /// <summary>Corridor speed at full throttle (m/s) of this flight.</summary>
        public float FullSpeed => VisualSpeed[Mathf.Clamp(CoreTier.Value, 1, VisualSpeed.Length) - 1];

        static DriveStats DriveStatsNow => WarpDrive.Instance != null ? WarpDrive.Instance.Stats : DriveMath.Compute(WarpDrive.Def, 1, 1, 1, 1);

        /// <summary>Forward scanner range of the crew's nav computer (m): obstacles appear and warn inside it.</summary>
        public float ScanRange => Mathf.Max(250f, DriveStatsNow.NavFraction * FullSpeed * ScanSeconds);

        /// <summary>Route position this peer renders (server: exact; clients: extrapolated and eased to the server value).</summary>
        public float LocalProgress => localProgress;
        public float LocalProgressPrev => localProgressPrev;
        public float Progress01 => Length.Value > 0f ? Mathf.Clamp01(localProgress / Length.Value) : 0f;

        /// <summary>Seconds left at the current throttle (Infinity when stopped).</summary>
        public float SecondsLeft(float throttle) =>
            throttle > 0.01f ? Mathf.Max(0f, Length.Value - localProgress) / (throttle * FullSpeed) : float.PositiveInfinity;

        /// <summary>Average throttle of the ETA model (fields at the nav's safe speed, free space at the cooling's).</summary>
        static float PlannedPace
        {
            get
            {
                var s = DriveStatsNow;
                var d = WarpDrive.Def;
                float field = d != null ? d.FieldShare : 0.3f;
                return 1f / (field / Mathf.Max(0.05f, s.NavFraction) + (1f - field) / Mathf.Max(0.05f, s.Sustain));
            }
        }

        /// <summary>Seconds left flying the rest like the ETA model.</summary>
        public float PlannedSecondsLeft => Mathf.Max(0f, Length.Value - localProgress) / (PlannedPace * FullSpeed);

        /// <summary>This flight is the flight home, the run's finale (#81).</summary>
        public bool IsFinale => Active.Value && !IsHop && To.Value > GalaxyDef.Count;

        public bool IsDestroyed(int id) => Destroyed.Contains(id);

        void Awake() => Destroyed = new NetworkList<int>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            view = new GameObject("_WarpCorridor").AddComponent<WarpCorridorView>();
            view.Flight = this;
            Destroyed.OnListChanged += OnDestroyedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (watchedHull != null) watchedHull.DamagedClient -= OnLeadHit;
            watchedHull = null;
            Destroyed.OnListChanged -= OnDestroyedChanged;
            if (view != null) Destroy(view.gameObject);
            WarpBubble.Active = WarpBubble.Flying = false;
        }

        void OnDestroyedChanged(NetworkListEvent<int> e)
        {
            if (e.Type == NetworkListEvent<int>.EventType.Add && view != null) view.DestroyObstacle(e.Value, true);
        }

        // ================================================================ every peer

        void Update()
        {
            bool active = Active.Value && RunManager.Instance != null && RunManager.Instance.Phase.Value == GamePhase.Playing;
            TickJumps(Time.deltaTime);
            if (!active)
            {
                WarpBubble.Active = WarpBubble.Flying = false;
                WarpBubble.Throttle = WarpBubble.Speed = WarpBubble.Intensity = 0f;
                // The entry spool-up into a warp out of a jump zone (#108) widens the view before the bubble closes.
                WarpBubble.FovBoost = Mathf.Lerp(WarpBubble.FovBoost, JumpZones.Entering * JumpZones.Entering * 16f, 1f - Mathf.Exp(-4f * Time.deltaTime));
                WarpBubble.Heat = WarpBubble.Progress01 = 0f;
                WarpBubble.Overheated = false;
                wasPiloting = false;
                if (IsServer && Active.Value && RunManager.Instance != null && RunManager.Instance.Phase.Value != GamePhase.Playing) ServerAbort();
                return;
            }
            float dt = Time.deltaTime;
            if (IsServer && !ServerTickExit()) ServerTick(dt);

            // Local route position: the server's, extrapolated between network ticks and eased (no visible snapping).
            localProgressPrev = localProgress;
            if (IsServer) localProgress = Progress.Value;
            else
            {
                localProgress += Throttle.Value * FullSpeed * dt;
                float err = Progress.Value - localProgress;
                localProgress = Mathf.Abs(err) > 600f ? Progress.Value : localProgress + err * Mathf.Clamp01(3f * dt);
            }
            if (localProgress < localProgressPrev - 600f) localProgressPrev = localProgress; // jumped back (new flight)

            WarpBubble.Active = true;
            WarpBubble.Flying = !DroppedOut.Value;
            WarpBubble.LeadShip = Lead.Value;
            WarpBubble.Throttle = Throttle.Value;
            WarpBubble.Speed = DroppedOut.Value ? 0f : Throttle.Value * FullSpeed;
            WarpBubble.Intensity = Mathf.Clamp01(WarpBubble.Speed / 900f);
            WarpBubble.FovBoost = Mathf.Lerp(WarpBubble.FovBoost, WarpBubble.Intensity * 14f, 1f - Mathf.Exp(-3f * dt));
            WarpBubble.Heat = Heat.Value;
            WarpBubble.Overheated = Overheated.Value;
            WarpBubble.Progress01 = Progress01;
            TickLocalPilot(dt);
        }

        /// <summary>The local player pilots the lead ship.</summary>
        public bool LocalIsLeadPilot
        {
            get
            {
                var nm = NetworkManager;
                if (nm == null) return false;
                var seat = ShipSeat.SeatOf(nm.LocalClientId);
                return seat != null && seat.Role == StationRole.Pilot && seat.Ship != null && seat.Ship.NetworkObjectId == Lead.Value;
            }
        }

        /// <summary>Lead pilot client: turn the seat's input into a commanded throttle and send it (10 Hz, toggles at once).</summary>
        void TickLocalPilot(float dt)
        {
            bool piloting = LocalIsLeadPilot && Time.unscaledTime - WarpBubble.PilotInputTime < 0.3f;
            if (!piloting) { wasPiloting = false; ConsumeToggles(); return; }
            // A new flight (also the seamless one out of a jump zone, #108) starts at the server's throttle.
            if (!wasPiloting || pilotSeed != Seed.Value) { pilotCommand = Throttle.Value; wasPiloting = true; pilotSeed = Seed.Value; pilotSyncUntil = Time.unscaledTime + 1f; }
            if (Time.unscaledTime < pilotSyncUntil) pilotCommand = Mathf.Max(pilotCommand, Throttle.Value);
            if (Stalled.Value || Overheated.Value || DroppedOut.Value) pilotCommand = 0f;
            else
            {
                pilotCommand = Mathf.Clamp01(pilotCommand + WarpBubble.PilotThrottleAxis * 0.45f * dt);
                if (WarpBubble.PilotBrake) pilotCommand = Mathf.MoveTowards(pilotCommand, 0f, 1.5f * dt);
            }
            bool cruise = WarpBubble.PilotCruiseToggle, drop = WarpBubble.PilotDropOut;
            ConsumeToggles();
            sendTimer -= dt;
            if (sendTimer > 0f && !cruise && !drop) return;
            sendTimer = 0.1f;
            PilotInputServerRpc(pilotCommand, cruise, drop);
        }

        static void ConsumeToggles() { WarpBubble.PilotCruiseToggle = false; WarpBubble.PilotDropOut = false; }

        // ================================================================ server: begin / end

        /// <summary>Server: start the flown warp from galaxy `from` to route stop `to` (fuel was checked by RunManager).</summary>
        public void ServerBegin(int from, int to, ShipCore lead, bool seamless = false)
        {
            if (!IsServer) return;
            float minutes = DriveMath.Minutes(DriveStatsNow, from, to);
            if (float.IsInfinity(minutes) || minutes <= 0f) minutes = 10f;
            ServerStart(from, to, -1, minutes, WarpDrive.FuelTo(to), lead, seamless);
        }

        /// <summary>Server: start a hop to another system of galaxy `galaxy` (#80): short, free of Exotic Matter, thinner fields.</summary>
        public void ServerBeginHop(int galaxy, int system, ShipCore lead, bool seamless = false)
        {
            if (!IsServer) return;
            float minutes = DriveMath.HopMinutes(WarpDrive.Def, DriveStatsNow);
            if (float.IsInfinity(minutes) || minutes <= 0f) minutes = 1.5f;
            ServerStart(galaxy, galaxy, system, minutes, 0, lead, seamless);
        }

        void ServerStart(int from, int to, int system, float minutes, int fuel, ShipCore lead, bool seamless)
        {
            var stats = DriveStatsNow;
            var d = WarpDrive.Def;
            float balanced = d != null ? d.BalancedFactor : 1.2f;
            CoreTier.Value = Mathf.Clamp(stats.Core, 1, VisualSpeed.Length);
            // Full-throttle seconds of the route = model minutes without the build penalty and without the field slow-down.
            float fullSeconds = minutes / Mathf.Max(0.01f, stats.Factor) / balanced * 60f;
            Length.Value = Mathf.Max(WarpRoute.ChunkLength * 4f, fullSeconds * FullSpeed);
            Seed.Value = Random.Range(1, int.MaxValue);
            From.Value = from;
            To.Value = to;
            TargetSystem.Value = system;
            Progress.Value = 0f;
            localProgress = localProgressPrev = 0f;
            pilotCommand = 0f;
            Throttle.Value = 0f;
            Heat.Value = 0f;
            Cruise.Value = false;
            Stalled.Value = Overheated.Value = false;
            Hazard.Value = -1;
            DropSignal.Value = -1;
            ResumeCountdown.Value = -1f;
            Destroyed.Clear();
            obstacleDamage.Clear();
            impacts.Clear();
            commanded = 0f;
            stallTimer = 0f;
            lastPatrolChunk = -1;
            VisitedSignals.Value = 0;
            fuelNeed = fuel;
            fuelBurned = 0f;
            // The flight home (#81): the last FinaleSeconds of planned flight are the dense finale stretch.
            bool finale = system < 0 && to > GalaxyDef.Count;
            FinaleStart.Value = finale ? FinaleStartOf(new WarpRoute(Seed.Value, from, to, Length.Value)) : -1f;
            Lead.Value = PickLead(lead);
            exiting = false;
            if (seamless && ShipById(Lead.Value) != null)
            {
                // Out of a jump zone (#108): the crew keeps flying, the bubble holds a cruising throttle until the
                // first obstacle warning or the lead pilot's own input.
                ServerRebaseIntoBubble(ShipById(Lead.Value));
                commanded = Throttle.Value = Mathf.Clamp(Mathf.Min(stats.NavFraction, stats.Sustain) * 0.8f, 0.25f, 0.7f);
                Cruise.Value = true;
            }
            else ServerFormation();
            Active.Value = true;
            DroppedOut.Value = false;
            Debug.Log($"[Warp] begin {from} -> {to}{(system >= 0 ? " system " + system : "")}: {Length.Value / 1000f:0.0} km at {FullSpeed:0} m/s, planned {minutes:0.0} min, fuel {fuelNeed}" +
                      (finale ? $", finale from {FinaleStart.Value / 1000f:0.0} km" : ""));
        }

        /// <summary>Server: the flight is over (arrived, run ended, dev goto). Clears signal sites; RunManager enters the destination.</summary>
        public void ServerAbort()
        {
            if (!IsServer) return;
            exiting = false;
            DespawnSite();
            Active.Value = false;
            DroppedOut.Value = false;
            Throttle.Value = 0f;
            Cruise.Value = false;
            ResumeCountdown.Value = -1f;
        }

        /// <summary>Exotic Matter burnt so far in this flight (refunded by a suspend save taken mid-flight).</summary>
        public float FuelBurned => Active.Value ? fuelBurned : 0f;

        ulong PickLead(ShipCore preferred)
        {
            // The home ship carries the warp drive (#49): it leads every warp while it exists.
            var home = ShipCore.Home;
            if (home != null) return home.NetworkObjectId;
            if (preferred != null && preferred.IsSpawned) return preferred.NetworkObjectId;
            foreach (var seat in ShipSeat.All)
                if (seat != null && seat.Role == StationRole.Pilot && seat.IsOccupied && seat.Ship != null && !seat.Ship.Derelict.Value) return seat.Ship.NetworkObjectId;
            foreach (var s in ShipCore.All) if (s != null && s.IsSpawned && !s.Derelict.Value) return s.NetworkObjectId;
            return ulong.MaxValue;
        }

        static ShipCore ShipById(ulong id)
        {
            foreach (var s in ShipCore.All) if (s != null && s.IsSpawned && s.NetworkObjectId == id) return s;
            return null;
        }

        /// <summary>Server: every ship into its bubble slot around the origin, pawns on foot next to their ship.</summary>
        void ServerFormation()
        {
            var lead = ShipById(Lead.Value);
            var rot = Quaternion.LookRotation(WarpBubble.Axis);
            var positions = new Dictionary<ShipCore, Vector3>();
            var others = new List<ShipCore>();
            ServerDockForWarp(lead);
            foreach (var s in ShipCore.All) if (s != null && s.IsSpawned && s != lead && !s.Docked) others.Add(s);
            others.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
            if (lead != null) positions[lead] = Vector3.zero;
            for (int i = 0; i < others.Count; i++) positions[others[i]] = WarpBubble.FormationOffset(i + 1, others[i].IsHome);
            foreach (var kv in positions)
            {
                kv.Key.GetComponent<NetTeleport>().ServerTeleport(kv.Value, rot);
                kv.Key.RestoreServer(false, false);
            }
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value || pawn.IsDown.Value || ShipSeat.SeatOf(pawn.OwnerClientId) != null) continue;
                if (GravityVolume.At(pawn.transform.position) != null) continue; // carried by the interior it stands in
                var own = ShipCore.OwnedBy(pawn.OwnerClientId);
                if (own != null && own.Docked) own = lead;
                Vector3 p = own != null && positions.TryGetValue(own, out var sp) ? sp + Vector3.up * 9f : new Vector3(-20f, 12f, 0f) + Random.insideUnitSphere * 6f;
                pawn.GetComponent<NetTeleport>().ServerTeleport(p, rot);
            }
        }

        // ================================================================ server: per frame

        void ServerTick(float dt)
        {
            var rm = RunManager.Instance;
            var r = Route;
            if (r == null || rm == null) return;
            var stats = DriveStatsNow;

            // Lead lost (destroyed): hand the bubble to another ship.
            if (ShipById(Lead.Value) == null) Lead.Value = PickLead(null);

            if (ResumeCountdown.Value >= 0f)
            {
                ResumeCountdown.Value -= dt;
                if (ResumeCountdown.Value < 0f) ServerResume();
            }
            if (DroppedOut.Value) { Throttle.Value = 0f; return; }

            // Emergency stops: after a collision for a few seconds, after overheating until cooled down.
            if (Stalled.Value) { stallTimer -= dt; if (stallTimer <= 0f) Stalled.Value = false; }
            bool pilot = Time.time - lastPilotInput < 0.6f && LeadPilotSeated();
            float t = Throttle.Value;
            if (Stalled.Value || Overheated.Value) { commanded = 0f; t = Mathf.MoveTowards(t, 0f, 2.5f * dt); }
            else if (pilot || Cruise.Value) t = Mathf.MoveTowards(t, commanded, 0.9f * dt);
            else { commanded = 0f; t = Mathf.MoveTowards(t, 0f, 0.35f * dt); }
            Throttle.Value = t;

            // Heat: above the cooling's sustainable throttle it builds up, below it bleeds off (faster with an engineer).
            float limit = stats.Sustain * HeatThreshold;
            float heat = Heat.Value;
            if (t > limit) heat += (t - limit) * HeatGain * dt;
            else heat -= HeatCool * (1f + (limit - t) * 2f) * (EngineerAboard() ? 1.6f : 1f) * dt;
            heat = Mathf.Clamp01(heat);
            if (heat >= 1f && !Overheated.Value)
            {
                Overheated.Value = true;
                Cruise.Value = false;
                rm.Announce("OVERHEAT - emergency stop!\nThe drive cools down before it can warp again.", "alarm");
            }
            if (Overheated.Value && heat < 0.35f)
            {
                Overheated.Value = false;
                rm.Announce("Drive cooled down - warp ready", "jump_charge");
            }
            Heat.Value = heat;

            // Progress and fuel.
            float before = Progress.Value;
            float p = Mathf.Min(r.Length, before + t * FullSpeed * dt);
            Progress.Value = p;
            if (!CheatManager.GodMode && WarpDrive.Instance != null && p > before)
            {
                float burn = fuelNeed * (p - before) / r.Length;
                burn = Mathf.Min(burn, Mathf.Max(0f, fuelNeed - fuelBurned));
                fuelBurned += burn;
                WarpDrive.Instance.Exotic.Value = Mathf.Max(0f, WarpDrive.Instance.Exotic.Value - burn);
            }

            ServerHazardScan(r);
            ServerPatrols(r, rm);
            ServerInterdiction(rm);

            if (p >= r.Length - 0.5f) { ServerBeginExit(); return; }
        }

        // ================================================================ server: the finale stretch (#81)

        /// <summary>Planned speed (m/s) at route position s: fields at the nav's safe speed, the void at the cooling's.</summary>
        float PlannedSpeedAt(WarpRoute r, float s)
        {
            var st = DriveStatsNow;
            return (r.ZoneAt(s, out _) == WarpZone.Void ? st.Sustain : st.NavFraction) * FullSpeed;
        }

        /// <summary>Route position FinaleSeconds of planned flight before the end (0 if the flight is shorter).</summary>
        float FinaleStartOf(WarpRoute r)
        {
            const float step = 250f;
            float s = r.Length, t = 0f;
            while (s > 0f && t < FinaleSeconds)
            {
                t += step / Mathf.Max(1f, PlannedSpeedAt(r, s - step * 0.5f));
                s -= step;
            }
            return Mathf.Max(0f, s);
        }

        bool LeadPilotSeated()
        {
            foreach (var seat in ShipSeat.All)
                if (seat != null && seat.Role == StationRole.Pilot && seat.IsOccupied && seat.Ship != null && seat.Ship.NetworkObjectId == Lead.Value) return true;
            return false;
        }

        static bool EngineerAboard()
        {
            foreach (var seat in ShipSeat.All) if (seat != null && seat.Role == StationRole.Engineer && seat.IsOccupied) return true;
            return false;
        }

        /// <summary>Server: the nav computer's warning for the lead ship's lane; it also switches the cruise control off.</summary>
        void ServerHazardScan(WarpRoute r)
        {
            hazardTimer -= Time.deltaTime;
            if (hazardTimer > 0f) return;
            hazardTimer = 0.15f;
            var lead = ShipById(Lead.Value);
            int found = -1;
            if (lead != null && Throttle.Value > 0.02f)
            {
                Vector3 lp = lead.transform.position;
                float p = Progress.Value;
                r.Query(p + lp.x, p + lp.x + ScanRange * 0.8f, query);
                foreach (var o in query)
                {
                    if (Destroyed.Contains(o.Id) || o.Kind == WarpHazard.Debris) continue;
                    float ahead = o.S - p;
                    float lat = (o.LatAt(ahead) - new Vector2(lp.y, lp.z)).magnitude;
                    bool inLane = lat < (o.Kind == WarpHazard.GravityWell ? o.Radius * 1.4f : o.Radius) + 40f;
                    if (inLane) { found = o.Id; break; }
                }
            }
            if (Hazard.Value != found) Hazard.Value = found;
            if (found >= 0 && Cruise.Value)
            {
                Cruise.Value = false;
                RunManager.Instance.Announce("Cruise control off - obstacle ahead!", "alarm");
            }
        }

        /// <summary>Server (#80): engaged enemies close to the lead ship pull the bubble out of warp (an emergency stop until they are gone).</summary>
        void ServerInterdiction(RunManager rm)
        {
            var lead = ShipById(Lead.Value);
            WatchLeadHull(lead);
            if (lead == null || Stalled.Value || Overheated.Value || Throttle.Value < 0.2f) return;
            Vector3 lp = lead.transform.position;
            foreach (var e in EnemyFactory.Alive)
            {
                if (e == null || !e.IsSpawned || ((IThreatSource)e).Engagement < 0.5f) continue;
                if ((e.transform.position - lp).sqrMagnitude < InterdictRange * InterdictRange) { Interdict(rm, "interceptors on the bubble"); return; }
            }
        }

        /// <summary>Server: the lead ship's hull, so enemy hits on it interdict the warp.</summary>
        void WatchLeadHull(ShipCore lead)
        {
            var hull = lead != null ? lead.Hull : null;
            if (hull == watchedHull) return;
            if (watchedHull != null) watchedHull.DamagedClient -= OnLeadHit;
            watchedHull = hull;
            if (hull != null) hull.DamagedClient += OnLeadHit;
        }

        void OnLeadHit(DamageInfo info)
        {
            // Corridor impacts stop the bubble on their own (ReportImpactServerRpc).
            if (!IsServer || !Active.Value || DroppedOut.Value || Stalled.Value || Time.time - lastImpactAt < 0.3f || Throttle.Value < 0.2f) return;
            Interdict(RunManager.Instance, "the lead ship is under fire");
        }

        void Interdict(RunManager rm, string why)
        {
            Stalled.Value = true;
            stallTimer = CollisionStall;
            Cruise.Value = false;
            commanded = 0f;
            Throttle.Value = 0f;
            Debug.Log("[Warp] interdicted: " + why);
            if (rm != null) rm.Announce($"INTERDICTED - {why}!\nFight them off, then throttle up again", "alarm");
        }

        /// <summary>Server: interceptor patrols of the halo's galaxy meet the bubble.</summary>
        void ServerPatrols(WarpRoute r, RunManager rm)
        {
            int chunk = WarpRoute.ChunkOf(Progress.Value + 1200f);
            if (chunk <= lastPatrolChunk) return;
            for (int c = lastPatrolChunk + 1; c <= chunk; c++)
            {
                if (!r.PatrolIn(c, out _, out int galaxy)) continue;
                var def = ContentCatalog.GetGalaxy(galaxy);
                if (def == null || def.SpaceEnemies.Length == 0 || EnemyFactory.Alive.Count >= 8) continue;
                var kind = def.SpaceEnemies[Random.Range(0, def.SpaceEnemies.Length)];
                int n = kind == EnemyKind.DroneSwarm ? 3 : 1 + Random.Range(0, 2);
                Vector3 at = new Vector3(Random.Range(500f, 700f), Random.Range(-120f, 120f), Random.Range(-200f, 200f));
                for (int i = 0; i < n; i++)
                    EnemyFactory.Spawn(kind, at + Random.insideUnitSphere * 30f, Quaternion.LookRotation(-WarpBubble.Axis), def.EnemyTier);
                rm.Announce($"Interceptors of {def.DisplayName} ahead!", "alarm");
            }
            lastPatrolChunk = chunk;
        }

        void ServerArrive(RunManager rm)
        {
            int to = To.Value;
            if (WarpDrive.Instance != null && !CheatManager.GodMode)
                WarpDrive.Instance.Exotic.Value = Mathf.Max(0f, WarpDrive.Instance.Exotic.Value - Mathf.Max(0f, fuelNeed - fuelBurned));
            fuelBurned = fuelNeed;
            int system = TargetSystem.Value;
            Debug.Log(system >= 0 ? $"[Warp] arrived at system {system}" : $"[Warp] arrived at stop {to}");
            var lead = ShipById(Lead.Value);
            ServerAbort();
            if (system >= 0) rm.ServerArriveSystem(system, lead);
            else rm.ServerArrive(to, lead);
        }

        // ================================================================ server: pilot, impacts, shots

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PilotInputServerRpc(float command, bool cruiseToggle, bool dropOut, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            var seat = ShipSeat.SeatOf(c);
            if (!Active.Value || seat == null || seat.Role != StationRole.Pilot || seat.Ship == null || seat.Ship.NetworkObjectId != Lead.Value) return;
            lastPilotInput = Time.time;
            if (!Stalled.Value && !Overheated.Value && !DroppedOut.Value) commanded = Mathf.Clamp01(command);
            if (cruiseToggle && !DroppedOut.Value)
            {
                Cruise.Value = !Cruise.Value && commanded > 0.05f;
                FeedbackRpc(Cruise.Value ? $"Cruise control ON - holding {Mathf.RoundToInt(commanded * 100f)}%\nIt switches off at the next obstacle warning" : "Cruise control off", "ui_click", RpcTarget.Single(c, RpcTargetUse.Temp));
            }
            if (dropOut) ServerDropOut(c);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void FeedbackRpc(string msg, string sfx, RpcParams rp = default)
        {
            EventBus.RaiseNotification(msg);
            if (!string.IsNullOrEmpty(sfx)) Sfx.PlayUI(sfx);
        }

        /// <summary>Authority of a ship (its pilot, or the server): one of its impacts in the corridor.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReportImpactServerRpc(int obstacleId, ulong shipId, bool slingshot)
        {
            var r = Route;
            if (r == null || DroppedOut.Value || !r.TryGet(obstacleId, out var o) || Destroyed.Contains(obstacleId)) return;
            if (Mathf.Abs(o.S - Progress.Value) > 2000f + FullSpeed) return; // stale or bogus
            long key = (long)obstacleId << 20 ^ (long)shipId << 1 ^ (slingshot ? 1 : 0);
            if (!impacts.Add(key)) return;
            var ship = ShipById(shipId);
            if (ship == null) return;
            var rm = RunManager.Instance;
            bool lead = shipId == Lead.Value;
            float t = Throttle.Value;

            if (slingshot)
            {
                if (!lead || o.Kind != WarpHazard.GravityWell || t < 0.2f) return;
                float bonus = Mathf.Min(r.Length * 0.03f, 20000f);
                Progress.Value = Mathf.Min(r.Length - 1f, Progress.Value + bonus);
                rm.Announce($"SLINGSHOT! +{bonus / 1000f:0.0} km", "jump_gate");
                Debug.Log($"[Warp] slingshot {o.Id} +{bonus:0} m");
                ImpactClientRpc(ship.transform.position, (int)o.Kind, true);
                return;
            }

            float dmg = o.ImpactDamage * (0.25f + 0.75f * t);
            lastImpactAt = Time.time;
            Debug.Log($"[Warp] impact {o.Kind} {o.Id} ship {shipId}{(lead ? " (lead)" : "")} throttle {t:0.00} dmg {dmg:0}");
            ship.Hull.ApplyDamage(new DamageInfo { Amount = dmg, Type = DamageType.Kinetic, Point = ship.transform.position + WarpBubble.Axis * 4f, Direction = -WarpBubble.Axis });
            ImpactClientRpc(ship.transform.position + WarpBubble.Axis * 6f, (int)o.Kind, false);
            if (o.Kind == WarpHazard.Debris || (o.Kind == WarpHazard.Rock && o.Radius < 14f)) Destroyed.Add(o.Id);

            if (o.Kind == WarpHazard.Debris) { commanded = Mathf.Max(0f, commanded - 0.25f); Throttle.Value = Mathf.Max(0f, t - 0.25f); return; }
            if (lead && t > 0.3f)
            {
                Stalled.Value = true;
                stallTimer = CollisionStall;
                Cruise.Value = false;
                commanded = 0f;
                Throttle.Value = 0f;
                string what = o.Kind == WarpHazard.GravityWell ? "the anomaly core" : o.Kind == WarpHazard.Comet ? "a comet" : "an asteroid";
                rm.Announce($"COLLISION with {what} - emergency stop!", "alarm");
            }
        }

        [ClientRpc]
        void ImpactClientRpc(Vector3 at, int kind, bool slingshot)
        {
            if (slingshot) { Vfx.ScreenFlash(new Color(1f, 0.75f, 0.35f, 0.35f), 0.4f); Vfx.Shake(0.4f); return; }
            Vfx.Explosion(at, kind == (int)WarpHazard.Debris ? 2.5f : 7f, new Color(1f, 0.65f, 0.4f));
            Sfx.Play(kind == (int)WarpHazard.Debris ? "explosion" : "explosion_big", at);
            Vfx.Shake(kind == (int)WarpHazard.Debris ? 0.3f : 0.9f);
        }

        /// <summary>Client hitscan on an obstacle (projectiles hit on the server directly).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DamageObstacleServerRpc(int id, float amount, RpcParams rp = default) =>
            ServerDamageObstacle(id, Mathf.Clamp(amount, 0f, 200f));

        /// <summary>Server: shoot at an obstacle; small ones break apart (replicated to every peer).</summary>
        public void ServerDamageObstacle(int id, float amount)
        {
            if (!IsServer || !Active.Value || Destroyed.Contains(id) || amount <= 0f) return;
            var r = Route;
            if (r == null || !r.TryGet(id, out var o) || o.Hp <= 0f) return;
            obstacleDamage.TryGetValue(id, out float dealt);
            dealt += amount;
            if (dealt < o.Hp) { obstacleDamage[id] = dealt; return; }
            obstacleDamage.Remove(id);
            Destroyed.Add(id);
        }

        // ================================================================ server: signals

        /// <summary>Signal within drop-out range of the bubble (not visited yet), or -1.</summary>
        public int SignalInRange(float progress)
        {
            var r = Route;
            if (r == null) return -1;
            foreach (var s in r.Signals)
                if ((VisitedSignals.Value & (1 << s.Id)) == 0 && Mathf.Abs(s.S - progress) < DropRange) return s.Id;
            return -1;
        }

        /// <summary>A signal this peer can see on the scanner's edge: the closest one ahead within 3x scanner range (or in drop range).</summary>
        public bool NextSignal(out WarpSignal signal)
        {
            signal = default;
            var r = Route;
            if (r == null) return false;
            float best = float.MaxValue;
            foreach (var s in r.Signals)
            {
                float ahead = s.S - localProgress;
                if ((VisitedSignals.Value & (1 << s.Id)) != 0) continue;
                if (ahead < -DropRange || ahead > ScanRange * 3f + DropRange || ahead >= best) continue;
                best = ahead;
                signal = s;
            }
            return best < float.MaxValue;
        }

        void ServerDropOut(ulong client)
        {
            var rm = RunManager.Instance;
            if (DroppedOut.Value)
            {
                if (ResumeCountdown.Value >= 0f) return;
                ResumeCountdown.Value = ResumeDelay;
                rm.Announce($"Warp resumes in {ResumeDelay:0} s - everyone back to the ships!", "jump_charge");
                return;
            }
            int id = SignalInRange(Progress.Value);
            if (id < 0) { FeedbackRpc("No signal in range to drop out at", "ui_back", RpcTarget.Single(client, RpcTargetUse.Temp)); return; }
            WarpSignal sig = default;
            foreach (var s in Route.Signals) if (s.Id == id) sig = s;
            VisitedSignals.Value |= 1 << id;
            DroppedOut.Value = true;
            DropSignal.Value = id;
            Throttle.Value = 0f;
            commanded = 0f;
            Cruise.Value = false;
            ServerSpawnSite(sig);
            rm.Announce($"Dropped out of warp: {sig.Label}\nPilot of the lead ship: [G] resumes the warp", "jump_gate");
        }

        /// <summary>Where a signal's site sits in the world while dropped out next to it.</summary>
        public static Vector3 SitePosition(WarpSignal s)
        {
            Vector2 lat = Vector2.ClampMagnitude(s.Lat, 700f);
            float ahead = s.Kind == WarpSignalKind.RoguePlanet ? 3200f : 650f;
            return new Vector3(ahead, lat.x, lat.y);
        }

        void ServerSpawnSite(WarpSignal s)
        {
            DespawnSite();
            var rm = RunManager.Instance;
            var director = rm != null ? rm.Director : null;
            if (director == null) return;
            var r = Route;
            r.ZoneAt(s.S, out int galaxy);
            var def = ContentCatalog.GetGalaxy(galaxy);
            int loot = Mathf.Clamp(def != null ? def.LootTier : 1, 1, 3);
            var rng = new System.Random(s.Seed);
            Vector3 pos = SitePosition(s);
            int mark = director.TrackedCount;
            switch (s.Kind)
            {
                case WarpSignalKind.Wreck:
                    director.SpawnWreckSite(pos, rng, loot);
                    if (def != null && def.SpaceEnemies.Length > 0 && rng.NextDouble() < 0.5)
                        for (int i = 0; i < 2; i++)
                            EnemyFactory.Spawn(def.SpaceEnemies[0], pos + Random.onUnitSphere * 80f, Quaternion.identity, def.EnemyTier);
                    break;
                case WarpSignalKind.GasCloud:
                    for (int i = 0; i < 9; i++)
                    {
                        Vector3 p = pos + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 140f;
                        director.Track(WorldPickup.SpawnServer(WarpDrive.ExoticId, 2 + rng.Next(0, 2), p)?.NetworkObject);
                    }
                    break;
                case WarpSignalKind.RoguePlanet:
                    director.SpawnRoguePlanet(s.Seed, 900f, pos, loot);
                    break;
            }
            siteObjects.AddRange(director.TakeTracked(mark));
            rm.PingAll(pos, s.Label);
        }

        void DespawnSite()
        {
            foreach (var no in siteObjects) if (no != null && no.IsSpawned) no.Despawn(true);
            siteObjects.Clear();
        }

        void ServerResume()
        {
            ResumeCountdown.Value = -1f;
            DespawnSite();
            // Loot left out at the site stays behind with it.
            foreach (var p in WorldPickup.All.ToArray())
                if (p != null && p.IsSpawned && p.transform.position.magnitude > 400f) p.NetworkObject.Despawn(true);
            DroppedOut.Value = false;
            DropSignal.Value = -1;
            Stalled.Value = false;
            ServerFormation();
            RunManager.Instance.Announce("Warp resumed", "jump_gate");
        }

        // ================================================================ dev

        /// <summary>Server, dev: jump the bubble to a route position (m).</summary>
        public void ServerSetProgress(float p)
        {
            if (!IsServer || !Active.Value) return;
            Progress.Value = Mathf.Clamp(p, 0f, Length.Value - 1f);
            localProgress = localProgressPrev = Progress.Value;
            lastPatrolChunk = WarpRoute.ChunkOf(Progress.Value + 1200f);
        }

        /// <summary>Server, dev: drop out at the signal in range, or start the resume countdown (as the lead pilot's [G]).</summary>
        public void ServerDevDropOut()
        {
            if (IsServer && Active.Value) ServerDropOut(NetworkManager.ServerClientId);
        }

        /// <summary>Server, dev: set the throttle directly (and hold it with cruise control when asked).</summary>
        public void ServerSetThrottle(float t, bool cruise)
        {
            if (!IsServer || !Active.Value) return;
            commanded = Mathf.Clamp01(t);
            pilotCommand = commanded; // a host pilot keeps it instead of sending its old command
            Throttle.Value = commanded;
            Cruise.Value = cruise && commanded > 0.05f;
            Stalled.Value = Overheated.Value = false;
            Heat.Value = 0f;
        }
    }
}
