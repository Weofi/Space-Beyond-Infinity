using System;
using System.Collections;
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
using Voidward.World;

namespace Voidward.Dev
{
    /// <summary>Where QuickStart puts the local player.</summary>
    public enum DevScenario
    {
        /// <summary>Seated as pilot in the starting ship (normal run start).</summary>
        Space,
        /// <summary>On foot on a sunlit, dry spot of a planet, blaster in hand, god mode.</summary>
        Planet,
        /// <summary>Seated, plus a row of test walls (metal, rock, dirt, organic, shield) next to the trader station.</summary>
        Lab,
        /// <summary>Seated as pilot (like Space) with the galaxy map open (#46).</summary>
        Galaxy,
        /// <summary>Seated as lead pilot in a flown galaxy warp (#79): Mk II drive, fuel loaded, cruising at 60 %.</summary>
        Warp,
        /// <summary>Seated as lead pilot in the flight home from the LMC (#81): Home Drive, the Rift chasing, cruising at 80 %.</summary>
        Finale,
    }

    /// <summary>Camera views of <see cref="DevTest.PlanetSurvey"/> (one sheet column each).</summary>
    [Flags]
    public enum PlanetView
    {
        /// <summary>Whole planet, three-quarter lit, from 2.7 radii.</summary>
        Orbit = 1,
        /// <summary>Low orbit over the terminator, looking along the limb (atmosphere rim, sunset).</summary>
        Limb = 2,
        /// <summary>300 m above a dry spot, looking ahead.</summary>
        Flight = 4,
        /// <summary>Eye level on a dry, flat spot with the sun about 45 degrees high, side lit.</summary>
        Ground = 8,
        /// <summary>Eye level on a beach, looking out over the sea (falls back to Ground without an ocean).</summary>
        Shore = 16,
        All = Orbit | Limb | Flight | Ground | Shore,
    }

    /// <summary>
    /// Dev test harness (#65): makes typical test steps one-liners for agents (Unity_RunCommand) and humans.
    /// <code>
    /// DevTest.Play(DevScenario.Lab);              // edit mode: enter play mode, host, start run, skip intro, set up
    /// DevTest.QuickStart(DevScenario.Planet);     // same, when already in play mode
    /// DevTest.Sequence("muzzle", 1.5f, (f, t) => { if (f % 10 == 0) DevTest.Fire(...); }, new[] { 0.05f, 0.3f, 1.2f }, DevTest.LabCam);
    /// </code>
    /// Progress goes to the log as "[Dev] begin &lt;tag&gt;" (logged synchronously by the call) and "[Dev] end &lt;tag&gt; ...",
    /// so a caller waits for the marker (Tools/Dev/dev-wait.sh &lt;tag&gt;) instead of sleeping. Server-side helpers (Fire, SpawnEnemy) need the host.
    /// Only active in the editor and in development builds; nothing here runs in release builds.
    /// </summary>
    public static class DevTest
    {
        public const string PendingKey = "VW_DevPendingScenario";
        public const string PendingPlayersKey = "VW_DevPendingPlayers";

        public static bool Ready { get; private set; }
        public static DevScenario Scenario { get; private set; }

        public static bool Enabled => Application.isEditor || Debug.isDebugBuild;
        static NetworkManager Net => NetworkManager.Singleton;
        public static bool IsServer => Net != null && Net.IsServer;

        public static PlayerPawn Pawn => PlayerPawn.Local;
        /// <summary>The local player's own ship (null before it is spawned).</summary>
        public static ShipCore Ship => Net != null ? ShipCore.OwnedBy(Net.LocalClientId) : null;
        public static TraderStation Station => UnityEngine.Object.FindAnyObjectByType<TraderStation>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Ready = false; Scenario = DevScenario.Space; LabRoot = null; LabTargets = null; }

        public static void Log(string msg) => Debug.Log("[Dev] " + msg);

#if UNITY_EDITOR
        /// <summary>
        /// Edit mode: enter play mode and run QuickStart(scenario, players) right after the domain reload.
        /// players > 1: the host waits for that many connected players (MP test clients) before starting the run.
        /// </summary>
        public static void Play(DevScenario scenario = DevScenario.Space, int players = 1)
        {
            Log("begin quickstart " + scenario);
            UnityEditor.SessionState.SetInt(PendingPlayersKey, players);
            UnityEditor.SessionState.SetString(PendingKey, scenario.ToString());
            UnityEditor.EditorApplication.isPlaying = true;
        }
#endif

        // ------------------------------------------------------------------ quick start

        /// <summary>
        /// Play mode: host (unless already in a session), start the run, skip the intro, then set up the scenario.
        /// Clients just wait for the host's run. Logs "[Dev] end quickstart ready &lt;scenario&gt;" when done.
        /// </summary>
        public static void QuickStart(DevScenario scenario = DevScenario.Space, int players = 1)
        {
            Log("begin quickstart " + scenario);
            DevRunner.Get().Run(QuickStartCo(scenario, Mathf.Clamp(players, 1, 4)));
        }

        static IEnumerator QuickStartCo(DevScenario scenario, int players)
        {
            Ready = false;
            Scenario = scenario;
            float t0 = Time.realtimeSinceStartup;
            bool Timeout(float s) => Time.realtimeSinceStartup - t0 > s;

            // Command-line auto host / join start on their own a moment after launch.
            if (TransportConfig.AutoHost || !string.IsNullOrEmpty(TransportConfig.AutoJoinIp))
                while ((Net == null || !Net.IsListening) && !Timeout(15f)) yield return null;
            bool joining = !string.IsNullOrEmpty(TransportConfig.AutoJoinIp);
            if (joining) while ((Net == null || !Net.IsConnectedClient) && !Timeout(60f)) yield return null; // a joining client never hosts
            else if ((Net == null || !Net.IsListening) && SessionManager.Instance != null) SessionManager.Instance.HostGame();

            if (IsServer)
            {
                while ((RunManager.Instance == null || !RunManager.Instance.IsSpawned) && !Timeout(20f)) yield return null;
                if (RunManager.Instance == null) { Log("end quickstart FAILED: no RunManager"); yield break; }
                yield return null;
                if (players > 1) Log("waiting for " + players + " players");
                while (Net.ConnectedClientsIds.Count < players && !Timeout(90f)) yield return null;
                if (Net.ConnectedClientsIds.Count < players) { Log("end quickstart FAILED: only " + Net.ConnectedClientsIds.Count + "/" + players + " players"); yield break; }
                if (RunManager.Instance.Phase.Value == GamePhase.Lobby) SessionManager.Instance.StartRun();
            }

            while ((GameState.Phase != GamePhase.Playing || Pawn == null) && !Timeout(60f)) yield return null;
            if (Pawn == null) { Log("end quickstart FAILED: no local pawn / run not started"); yield break; }
            yield return SkipIntroCo();

            // The pilot seat is taken 0.6 s after spawn (RunManager.SeatLater).
            float seatWait = Time.realtimeSinceStartup + 4f;
            while (Pawn.CurrentSeat == null && Time.realtimeSinceStartup < seatWait) yield return null;

            switch (scenario)
            {
                case DevScenario.Planet: yield return PlanetCo(); break;
                case DevScenario.Lab: BuildLab(); break;
                case DevScenario.Galaxy: if (Voidward.UI.UIRoot.Instance != null) Voidward.UI.UIRoot.Instance.OpenGalaxyMap(); break;
                case DevScenario.Warp:
                    if (IsServer)
                    {
                        SetDrive(2);
                        StartWarp();
                        yield return null;
                        SetThrottle(0.6f);
                    }
                    else while (!WarpBubble.Active && !Timeout(30f)) yield return null;
                    break;
                case DevScenario.Finale:
                    if (IsServer)
                    {
                        yield return FinaleCo(DriveComponentDef.MaxTier);
                        SetThrottle(0.8f);
                    }
                    else while (!WarpBubble.Active && !Timeout(30f)) yield return null;
                    break;
            }
            Ready = true;
            Log("end quickstart ready " + scenario);
        }

        internal static IEnumerator SkipIntroCo()
        {
            float until = Time.realtimeSinceStartup + 5f;
            // the intro starts a frame or two after the run: keep skipping until it is gone
            int quiet = 0;
            while (Time.realtimeSinceStartup < until && quiet < 20)
            {
                if (GameState.Cinematic) { quiet = 0; if (Voidward.UI.UIRoot.Instance != null) Voidward.UI.UIRoot.Instance.SkipIntro(); }
                else quiet++;
                yield return null;
            }
        }

        static IEnumerator PlanetCo()
        {
            God(true);
            var seat = Pawn.CurrentSeat as ShipSeat;
            if (seat != null)
            {
                seat.RequestLeave();
                float until = Time.realtimeSinceStartup + 3f;
                while (Pawn.CurrentSeat != null && Time.realtimeSinceStartup < until) yield return null;
            }
            if (PlanetBody.All.Count == 0) { Log("Planet scenario: no planets in this sector"); yield break; }

            // sunlit, dry spot on the first planet that has one
            var sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            PlanetBody planet = null;
            Vector3 ground = Vector3.zero;
            var rng = new System.Random(1234);
            foreach (var p in PlanetBody.All)
            {
                for (int i = 0; i < 60 && planet == null; i++)
                {
                    var jitter = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
                    Vector3 d = (toSun + jitter * 0.9f).normalized;
                    Vector3 g = p.SurfaceWorldPoint(p.transform.InverseTransformDirection(d));
                    if (!p.IsUnderwater(g)) { planet = p; ground = g; }
                }
                if (planet != null) break;
            }
            if (planet == null) { Log("Planet scenario: no dry spot found"); yield break; }
            planet.Visuals.EnsureColliders(ground, 80f);
            Vector3 up = (ground - planet.transform.position).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.Cross(up, Vector3.right), up).normalized;
            Teleport(ground + up * 1.5f, up, fwd);
            if (IsServer && Pawn.Gear != null) Pawn.Gear.HasRifle.Value = true;
            yield return null;
            Pawn.SelectTool(PlayerPawn.Tool.Blaster);
        }

        // ------------------------------------------------------------------ lab

        /// <summary>Lab walls, in order Metal, Rock, Dirt, Organic, Shield.</summary>
        public static readonly SurfaceKind[] LabKinds = { SurfaceKind.Metal, SurfaceKind.Rock, SurfaceKind.Dirt, SurfaceKind.Organic, SurfaceKind.Shield };
        public static Transform LabRoot { get; private set; }
        /// <summary>Centre of each wall's front face (index = LabKinds).</summary>
        public static Vector3[] LabTargets { get; private set; }
        /// <summary>Front normal of the walls (towards the camera and the light).</summary>
        public static Vector3 LabNormal { get; private set; }
        public static Vector3 LabRight { get; private set; }

        /// <summary>Overview of all walls.</summary>
        public static DevPose LabCam => LabTargets == null ? default : new DevPose(LabTargets[2] + LabNormal * 17f + Vector3.up * 3f, LabTargets[2], 50f);
        /// <summary>Close-up of one wall, slightly from the side.</summary>
        public static DevPose LabClose(int wall) => LabTargets == null ? default : new DevPose(LabTargets[wall] + LabNormal * 4.5f + LabRight * 1.5f + Vector3.up * 0.6f, LabTargets[wall], 45f);
        /// <summary>A muzzle position in front of a wall, inside the overview shot.</summary>
        public static Vector3 LabGun(int wall) => LabTargets[wall] + LabNormal * 9f - LabRight * 2.5f + Vector3.up * 1f;

        /// <summary>
        /// Build the test walls next to the trader station (deterministic from the station and the sun, so every
        /// peer builds the same lab). Local objects, cleared on sector change.
        /// </summary>
        public static void BuildLab()
        {
            if (LabRoot != null) UnityEngine.Object.Destroy(LabRoot.gameObject);
            var st = Station;
            Vector3 anchor = st != null ? st.transform.position + new Vector3(0f, 90f, -160f) : (Pawn != null ? Pawn.transform.position + Pawn.transform.forward * 60f : Vector3.zero);
            var sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.back;
            Vector3 n = Vector3.ProjectOnPlane(toSun, Vector3.up);
            if (n.sqrMagnitude < 0.01f) n = Vector3.back;
            n.Normalize();
            n = (n + Vector3.up * 0.15f).normalized; // a little up: the walls catch the light, the camera looks slightly down
            LabNormal = n;
            LabRight = Vector3.Cross(n, Vector3.up).normalized; // camera right when looking at the walls: index 0 is leftmost

            var root = new GameObject("_DevLab");
            LabRoot = root.transform;
            var cat = ContentCatalog.Instance;
            var mat = cat != null ? cat.ToonMaterial : null;
            Color[] colors = { new Color(0.82f, 0.84f, 0.88f), new Color(0.42f, 0.4f, 0.38f), new Color(0.62f, 0.5f, 0.36f), new Color(0.36f, 0.55f, 0.26f), new Color(0.32f, 0.5f, 0.9f) };
            LabTargets = new Vector3[LabKinds.Length];
            for (int i = 0; i < LabKinds.Length; i++)
            {
                Vector3 center = anchor + LabRight * ((i - 2) * 5f);
                var wall = new GameObject("Wall_" + LabKinds[i]);
                wall.transform.SetParent(root.transform, false);
                wall.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-n, Vector3.up));
                wall.transform.localScale = new Vector3(4f, 4f, 0.6f);
                wall.AddComponent<MeshFilter>().sharedMesh = ColoredCube(colors[i]);
                var mr = wall.AddComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
                wall.AddComponent<BoxCollider>();
                wall.AddComponent<DevSurface>().Kind = LabKinds[i];
                LabTargets[i] = center + n * 0.3f;
            }
            Log("lab built at " + anchor);
        }

        static Mesh ColoredCube(Color c)
        {
            var src = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var m = UnityEngine.Object.Instantiate(src);
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            m.colors = cols;
            m.hideFlags = HideFlags.DontSave;
            return m;
        }

        // ------------------------------------------------------------------ journey (#46)

        static RunManager Run => RunManager.Instance;

        /// <summary>One line about where the crew is: galaxy, system, kind, planets, visited mask, run time.</summary>
        public static string SystemInfo
        {
            get
            {
                var rm = Run;
                if (rm == null || rm.RunSeed.Value == 0) return "no run";
                var sys = rm.CurrentSystem;
                var plan = rm.Plan;
                return $"galaxy {rm.GalaxyIndex.Value} system {rm.SystemIndex.Value}/{plan.Systems.Length} {sys.Kind} '{sys.Name}' planets {PlanetBody.All.Count} visited {rm.VisitedMask.Value} t {rm.RunTime:0}s";
            }
        }

        /// <summary>The systems of the current galaxy as "index:Kind" (deterministic from the run seed).</summary>
        public static string GalaxySystems
        {
            get
            {
                var rm = Run;
                if (rm == null || rm.RunSeed.Value == 0) return "no run";
                var sb = new System.Text.StringBuilder();
                foreach (var s in rm.Plan.Systems) sb.Append(s.Index).Append(':').Append(s.Kind).Append(' ');
                return sb.ToString().Trim();
            }
        }

        /// <summary>Server: warp the crew to a system of the current galaxy now. Logs "[Dev] end goto ...".</summary>
        public static void GotoSystem(int system) => GotoGalaxy(Run != null ? Run.GalaxyIndex.Value : 1, system);

        /// <summary>Server: warp the crew straight to galaxy g (1..5), system s. Logs "[Dev] end goto ..." when entered.</summary>
        public static void GotoGalaxy(int galaxy, int system = 0)
        {
            Log("begin goto");
            if (!IsServer || Run == null) { Log("end goto FAILED: needs the host in a run"); return; }
            Run.ServerGoto(galaxy, system);
            DevRunner.Get().Run(GotoCo());
        }

        static IEnumerator GotoCo()
        {
            yield return null;
            yield return null; // the system visuals rebuild on the next Update
            Log("end goto " + SystemInfo);
        }

        /// <summary>Server: write the suspend save now (normally on galaxy entry and when the host leaves).</summary>
        public static void SaveRun() { if (IsServer && Run != null) { Run.ServerWriteSave(); Log("run saved"); } }
        public static bool HasRunSave => RunSave.Exists;

        // ------------------------------------------------------------------ warp drive (#78)

        /// <summary>Server: set all four drive components to one tier (1-6), or each one separately.</summary>
        public static void SetDrive(int tier) => SetDrive(tier, tier, tier, tier);
        public static void SetDrive(int core, int nav, int cooling, int tank)
        {
            if (!IsServer || WarpDrive.Instance == null) { Log("SetDrive FAILED: needs the host in a run"); return; }
            WarpDrive.Instance.ServerSetTiers(core, nav, cooling, tank);
            Log("drive " + DriveInfo);
        }

        /// <summary>Server: Exotic Matter in the drive's tank (clamped to its capacity).</summary>
        public static void SetFuel(float exotic)
        {
            var d = WarpDrive.Instance;
            if (!IsServer || d == null) { Log("SetFuel FAILED: needs the host in a run"); return; }
            d.Exotic.Value = Mathf.Clamp(exotic, 0f, d.Capacity);
            Log("drive " + DriveInfo);
        }

        /// <summary>"core 2 nav 2 cool 1 tank 1 | exotic 4/12 | next 10:00 (8) skip 40:00 (23) home 12 h 06 min | factor 1.08".</summary>
        public static string DriveInfo
        {
            get
            {
                var d = WarpDrive.Instance;
                if (d == null || Run == null) return "no drive";
                var s = d.Stats;
                int g = Run.GalaxyIndex.Value;
                return $"core {s.Core} nav {s.Nav} cool {s.Cooling} tank {s.Tank} | exotic {d.Exotic.Value:0}/{d.Capacity} | " +
                       $"next {DriveMath.Eta(d.EtaTo(g + 1))} ({WarpDrive.FuelTo(g + 1)}) skip {DriveMath.Eta(d.EtaTo(g + 2))} ({WarpDrive.FuelTo(g + 2)}) " +
                       $"home {DriveMath.Eta(d.EtaTo(DriveMath.Home))} | factor {s.Factor:0.00}";
            }
        }

        // ------------------------------------------------------------------ warp flight (#79)

        public static WarpFlight Flight => WarpFlight.Instance;

        /// <summary>
        /// Server: start a flown galaxy warp now (target = RunManager.WarpNextGalaxy / WarpSkipGalaxy), skipping the
        /// countdown; loads the Exotic Matter it needs. Logs "[Dev] end warp &lt;WarpInfo&gt;".
        /// </summary>
        public static void StartWarp(int target = RunManager.WarpNextGalaxy)
        {
            Log("begin warp");
            if (!IsServer || Run == null || Flight == null) { Log("end warp FAILED: needs the host in a run"); return; }
            int stop = Run.StopOf(target);
            var d = WarpDrive.Instance;
            if (stop < 0 || d == null) { Log("end warp FAILED: not a galaxy target"); return; }
            if (!d.TankCanHold(stop)) d.ServerSetTiers(d.TierOf(DriveSlot.Core), d.TierOf(DriveSlot.Navigation), d.TierOf(DriveSlot.Cooling), DriveComponentDef.MaxTier);
            if (!d.HasFuelFor(stop)) d.Exotic.Value = Mathf.Min(d.Capacity, WarpDrive.FuelTo(stop));
            Run.ServerWarp(target);
            DevRunner.Get().Run(WarpCo());
        }

        static IEnumerator WarpCo()
        {
            yield return null;
            yield return null;
            Log("end warp " + WarpInfo);
        }

        /// <summary>Server: jump the bubble to the start of the next zone of this kind (+50 m).</summary>
        public static void WarpSegment(WarpZone zone)
        {
            var f = Flight;
            var r = f != null ? f.Route : null;
            if (!IsServer || r == null) { Log("WarpSegment FAILED: no warp flight"); return; }
            float s = r.NextZone(zone, f.Progress.Value);
            if (s < 0f) { Log("WarpSegment FAILED: no " + zone + " ahead"); return; }
            f.ServerSetProgress(s + 50f);
            Log("warp segment " + WarpInfo);
        }

        /// <summary>Server: jump the bubble so the next obstacle of this kind is `ahead` metres in front of it.</summary>
        public static void WarpAhead(WarpHazard kind, float ahead = 500f)
        {
            var f = Flight;
            var r = f != null ? f.Route : null;
            if (!IsServer || r == null) { Log("WarpAhead FAILED: no warp flight"); return; }
            var list = new List<WarpObstacle>();
            float from = f.Progress.Value + ahead + 1f;
            for (int c = WarpRoute.ChunkOf(from); c * WarpRoute.ChunkLength < r.Length; c++)
            {
                foreach (var o in r.Chunk(c))
                {
                    if (o.Kind != kind || o.S < from || f.IsDestroyed(o.Id)) continue;
                    f.ServerSetProgress(o.S - ahead);
                    Log($"warp ahead {kind} id {o.Id} r {o.Radius:0} lat {o.Lat} | " + WarpInfo);
                    return;
                }
            }
            Log("WarpAhead FAILED: no " + kind + " ahead");
        }

        /// <summary>Server: jump the bubble next to the next signal (in drop-out range).</summary>
        public static void WarpSignalAhead(float ahead = 600f)
        {
            var f = Flight;
            var r = f != null ? f.Route : null;
            if (!IsServer || r == null) { Log("WarpSignalAhead FAILED: no warp flight"); return; }
            foreach (var s in r.Signals)
            {
                if (s.S < f.Progress.Value + ahead) continue;
                f.ServerSetProgress(s.S - ahead);
                Log($"warp signal {s.Label} id {s.Id} | " + WarpInfo);
                return;
            }
            Log("WarpSignalAhead FAILED: no signal ahead");
        }

        /// <summary>Server: jump the bubble to a fraction of the route (0..1).</summary>
        public static void WarpProgress(float fraction)
        {
            var f = Flight;
            if (!IsServer || f == null || !f.Active.Value) { Log("WarpProgress FAILED: no warp flight"); return; }
            f.ServerSetProgress(f.Length.Value * Mathf.Clamp01(fraction));
            Log("warp progress " + WarpInfo);
        }

        /// <summary>Server: warp throttle 0..1, held by the cruise control (so it stays without pilot input).</summary>
        public static void SetThrottle(float throttle, bool cruise = true)
        {
            var f = Flight;
            if (!IsServer || f == null || !f.Active.Value) { Log("SetThrottle FAILED: no warp flight"); return; }
            f.ServerSetThrottle(throttle, cruise);
        }

        /// <summary>Server: move the lead ship sideways to (y, z) in the corridor (it keeps that lane without pilot input).</summary>
        public static void WarpLeadTo(float y, float z)
        {
            var f = Flight;
            if (!IsServer || f == null || !f.Active.Value) { Log("WarpLeadTo FAILED: no warp flight"); return; }
            foreach (var s in ShipCore.All)
                if (s != null && s.NetworkObjectId == f.Lead.Value)
                    s.GetComponent<NetTeleport>().ServerTeleport(new Vector3(0f, y, z), Quaternion.LookRotation(WarpBubble.Axis));
        }

        /// <summary>Server: drop out at the signal in range, or start the resume countdown (as the lead pilot's [G] key).</summary>
        public static void WarpDropOut()
        {
            if (!IsServer || Flight == null || !Flight.Active.Value) { Log("WarpDropOut FAILED: no warp flight"); return; }
            Flight.ServerDevDropOut();
            Log("warp dropout " + WarpInfo);
        }

        /// <summary>Server: jump the bubble just before the next interceptor patrol (it spawns ~1.2 km ahead once the bubble moves).</summary>
        public static void WarpPatrolAhead()
        {
            var f = Flight;
            var r = f != null ? f.Route : null;
            if (!IsServer || r == null) { Log("WarpPatrolAhead FAILED: no warp flight"); return; }
            for (int c = WarpRoute.ChunkOf(f.Progress.Value + 1300f) + 1; c * WarpRoute.ChunkLength < r.Length; c++)
            {
                if (!r.PatrolIn(c, out _, out int galaxy)) continue;
                f.ServerSetProgress(c * WarpRoute.ChunkLength - 1250f);
                Log($"warp patrol chunk {c} galaxy {galaxy} | " + WarpInfo);
                return;
            }
            Log("WarpPatrolAhead FAILED: no patrol ahead");
        }

        /// <summary>"1 -> 2 | 34.2/225.0 km 15% GALAXY HALO | throttle 0.60 heat 0.12 cruise True stall False hot False | lead 12 hazard -1 | out False | exotic 7.2 | scan 655".</summary>
        public static string WarpInfo
        {
            get
            {
                var f = Flight;
                if (f == null || !f.Active.Value) return "no warp";
                var r = f.Route;
                var zone = r.ZoneAt(f.LocalProgress, out _);
                var d = WarpDrive.Instance;
                string route = f.IsHop ? $"hop {f.From.Value}:{(Run != null ? Run.SystemIndex.Value : -1)} -> system {f.TargetSystem.Value}" : $"{f.From.Value} -> {f.To.Value}";
                return $"{route} | {f.LocalProgress / 1000f:0.0}/{f.Length.Value / 1000f:0.0} km {f.Progress01 * 100f:0}% {r.ZoneName(zone)} | " +
                       $"throttle {f.Throttle.Value:0.00} heat {f.Heat.Value:0.00} cruise {f.Cruise.Value} stall {f.Stalled.Value} hot {f.Overheated.Value} | " +
                       $"lead {f.Lead.Value} hazard {f.Hazard.Value} | out {f.DroppedOut.Value} | exotic {(d != null ? d.Exotic.Value : 0f):0.0} | scan {f.ScanRange:0} | destroyed {f.Destroyed.Count}" +
                       (f.RiftStart.Value >= 0f ? " | " + RiftInfo : "");
            }
        }

        // ------------------------------------------------------------------ the flight home + the Rift (#81)

        /// <summary>
        /// Server: start the flight home from the LMC now (goes to galaxy 5 first if needed), with all drive parts at
        /// `tier` (6 = Home Drive, ~5 min); the Rift chases the last 5 planned minutes. Logs "[Dev] end finale &lt;WarpInfo&gt;".
        /// </summary>
        public static void StartFinale(int tier = 6)
        {
            Log("begin finale");
            if (!IsServer || Run == null || Flight == null) { Log("end finale FAILED: needs the host in a run"); return; }
            DevRunner.Get().Run(FinaleCo(tier));
        }

        static IEnumerator FinaleCo(int tier)
        {
            if (Flight.Active.Value) Flight.ServerAbort();
            if (Run.GalaxyIndex.Value != GalaxyDef.Count)
            {
                Run.ServerGoto(GalaxyDef.Count, 0);
                yield return null;
                yield return null;
            }
            SetDrive(tier);
            yield return null;
            StartWarp();
            yield return null;
            yield return null;
            Log("end finale " + WarpInfo);
        }

        /// <summary>Server: put the Rift `seconds` behind the bubble (wakes it; flight home only).</summary>
        public static void RiftAt(float seconds)
        {
            var f = Flight;
            if (!IsServer || f == null || !f.IsFinale) { Log("RiftAt FAILED: not in the flight home"); return; }
            f.ServerSetRiftSeconds(seconds);
            Log("rift at " + WarpInfo);
        }

        /// <summary>Server: the Wardens' last guard intercepts the bubble now.</summary>
        public static void WardenWave()
        {
            var f = Flight;
            if (!IsServer || f == null || !f.Active.Value) { Log("WardenWave FAILED: no warp flight"); return; }
            f.ServerDevWardenWave();
            Log("warden wave " + WarpInfo);
        }

        /// <summary>"rift 31.2 s 36.5 km @1170 m/s close 0.00 | from 0.0 km", "rift asleep (from 120.0 km)" or "no rift".</summary>
        public static string RiftInfo
        {
            get
            {
                var f = Flight;
                if (f == null || !f.Active.Value || f.RiftStart.Value < 0f) return "no rift";
                if (!f.RiftAwake) return $"rift asleep (from {f.RiftStart.Value / 1000f:0.0} km)";
                return $"rift {f.RiftSeconds:0.0} s {f.LocalRiftGap / 1000f:0.0} km @{f.RiftSpeed.Value:0} m/s close {f.RiftCloseness:0.00} | from {f.RiftStart.Value / 1000f:0.0} km";
            }
        }

        // ------------------------------------------------------------------ time loop (#86)

        /// <summary>
        /// Server: the whole crew is lost (the real crew-wipe path); waits for the collapse and the logbook screen.
        /// Records the run in the real meta save: BackupMeta() first. Logs "[Dev] end killcrew &lt;LoopInfo&gt;".
        /// </summary>
        public static void KillCrew()
        {
            Log("begin killcrew");
            if (!IsServer || Run == null || Run.Phase.Value != GamePhase.Playing) { Log("end killcrew FAILED: needs the host in a run"); return; }
            DevRunner.Get().Run(KillCrewCo());
        }

        static IEnumerator KillCrewCo()
        {
            foreach (var p in PlayerPawn.All) if (p != null && p.Active.Value) p.IsLost.Value = true;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && (UI.UIRoot.Instance == null || UI.UIRoot.Instance.Current != UI.Screen.Results)) yield return null;
            Log("end killcrew " + LoopInfo);
        }

        /// <summary>Host: start the next loop from the logbook (as the "New Loop" button). Logs "[Dev] end newloop &lt;LoopInfo&gt;".</summary>
        public static void NewLoop()
        {
            Log("begin newloop");
            if (!IsServer || Run == null || Run.Phase.Value == GamePhase.Playing) { Log("end newloop FAILED: needs the host on the logbook"); return; }
            DevRunner.Get().Run(NewLoopCo());
        }

        static IEnumerator NewLoopCo()
        {
            Run.ServerNewLoop();
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until && Run.Phase.Value != GamePhase.Playing) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            Log("end newloop " + LoopInfo);
        }

        /// <summary>"loop 2 Playing seed 123 | meta loops 1 best galaxy 1 best eta 35 h 12 min | log 2 | screen None | time 1.00".</summary>
        public static string LoopInfo
        {
            get
            {
                if (Run == null) return "no run";
                var ui = UI.UIRoot.Instance;
                string eta = Meta.Data.BestEtaMinutes > 0f ? DriveMath.Eta(Meta.Data.BestEtaMinutes) : "--";
                return $"loop {Run.Loop.Value} {Run.Phase.Value} seed {Run.RunSeed.Value} | meta loops {Meta.Loops} best galaxy {Meta.Data.BestSector} best eta {eta} | " +
                    $"log {(IsServer ? Run.Logbook.Count : -1)} | screen {(ui != null ? ui.Current.ToString() : "-")} | time {Time.timeScale:0.00}";
            }
        }

        /// <summary>Server: the crew logbook of this loop, one entry per line.</summary>
        public static string Logbook => IsServer && Run != null ? string.Join("\n", Run.Logbook) : "";

        // ------------------------------------------------------------------ system cruise + hops (#80)

        /// <summary>Server: start the flown hop to another system of this galaxy now (skips countdown and locks). Logs "[Dev] end hop &lt;WarpInfo&gt;".</summary>
        public static void StartHop(int system)
        {
            Log("begin hop");
            if (!IsServer || Run == null || Flight == null) { Log("end hop FAILED: needs the host in a run"); return; }
            Run.ServerWarp(system);
            DevRunner.Get().Run(HopCo());
        }

        static IEnumerator HopCo()
        {
            yield return null;
            yield return null;
            Log("end hop " + WarpInfo);
        }

        /// <summary>The ship the local player sits in (any seat), else the own ship.</summary>
        public static ShipCore SeatShip
        {
            get
            {
                var seat = Pawn != null ? Pawn.CurrentShipSeat : null;
                return seat != null && seat.Ship != null ? seat.Ship : Ship;
            }
        }

        /// <summary>Local pilot: cruise on at once (no spool-up) / off. force = ignore gravity/combat lock.</summary>
        public static void Cruise(bool on, bool force = false)
        {
            var s = SeatShip;
            bool ok = s != null && s.Flight != null && s.Flight.DevCruise(on, force);
            Log($"cruise {(on ? "on" : "off")} {(ok ? "ok" : "FAILED: " + (s == null ? "no ship" : s.Flight.IsSimulating ? s.Flight.CruiseBlock() : "not the pilot"))} | " + CruiseInfo);
        }

        /// <summary>"cruise Cruising 2380/3000 m/s | clear 2311 m Planet_x | block - | spool 1.00".</summary>
        public static string CruiseInfo
        {
            get
            {
                var s = SeatShip;
                if (s == null || s.Flight == null) return "no ship";
                var f = s.Flight;
                float clear = GravitySource.CruiseClearance(s.transform.position, out var near);
                return $"cruise {f.Cruise} {f.Speed:0}/{ShipFlight.CruiseTopSpeed:0} m/s | clear {(float.IsInfinity(clear) ? "inf" : clear.ToString("0"))} m {(near != null ? near.name : "-")} | block {f.CruiseBlock() ?? "-"} | spool {f.CruiseSpool01:0.00}";
            }
        }

        /// <summary>Cruise state of every ship as this peer sees it: "79:Cruising:993 80:Off:0".</summary>
        public static string ShipsCruise
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var s in ShipCore.All)
                    if (s != null && s.Flight != null) sb.Append($"{s.NetworkObjectId}:{s.Flight.Cruise}:{s.Flight.Speed:0} ");
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Crew bodies as this peer sees them (#95): "owner:mode/tool/used speed state" per pawn, e.g.
        /// "0:Ground/1/0 4.2 Move 1:seat Pilot Sit" (seated pawns show their seat body's state).
        /// </summary>
        public static string CrewInfo
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in PlayerPawn.All)
                {
                    if (p == null) continue;
                    var seat = ShipSeat.SeatOf(p.OwnerClientId);
                    if (seat != null)
                    {
                        var pose = seat.GetComponentInChildren<Voidward.Ship.CrewSeatPose>(true);
                        bool on = pose != null && pose.gameObject.activeInHierarchy;
                        sb.Append($"{p.OwnerClientId}:seat {seat.SeatRole} {(on ? "Sit" : "hidden")} ");
                        continue;
                    }
                    PlayerPawn.UnpackPose(p.Pose.Value, out var mode, out int tool, out bool used);
                    var a = p.GetComponentInChildren<Animator>();
                    string state = "-";
                    float speed = 0f;
                    if (a != null && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
                    {
                        speed = a.GetFloat("Speed");
                        var st = a.GetCurrentAnimatorStateInfo(0);
                        state = st.IsName("Move") ? "Move" : st.IsName("Air") ? "Air" : st.IsName("Land") ? "Land" : st.IsName("Eva") ? "Eva" : "?";
                    }
                    sb.Append($"{p.OwnerClientId}:{mode}/{tool}/{(used ? 1 : 0)} {speed:0.0} {state} ");
                }
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>Server: put the local player's ship `clearance` metres outside planet i's gravity lock (cruise boundary), nose on the planet.</summary>
        public static void FacePlanet(int planet = 0, float clearance = 3000f)
        {
            var s = SeatShip;
            var all = GravitySource.All;
            if (!IsServer || s == null || planet < 0 || planet >= all.Count) { Log("FacePlanet FAILED: ship or planet missing"); return; }
            var g = all[planet];
            Vector3 dir = (Vector3.right * -1f + Vector3.up * 0.2f).normalized; // approach from the corridor's start side
            Vector3 pos = g.Center + dir * (g.CruiseRadius + clearance);
            s.GetComponent<NetTeleport>().ServerTeleport(pos, Quaternion.LookRotation(g.Center - pos));
            Log($"face planet {planet} {g.name} r {g.SurfaceRadius:0} lock {g.CruiseRadius:0} at {clearance:0} m");
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Server: fire an authoritative projectile from 'from' at 'to' (dmg 0 = cosmetic test shot).</summary>
        public static void Fire(ProjectileKind kind, Vector3 from, Vector3 to, float dmg = 0f, Team team = Team.Pirates)
        {
            if (!IsServer) { Log("Fire needs the host"); return; }
            CombatNet.ServerFire(kind, from, (to - from).normalized, dmg, team);
        }

        /// <summary>Hitscan shot like the rifle (tracer, muzzle flash, impact + decal) seen by every peer. No damage.</summary>
        public static void Hitscan(Vector3 from, Vector3 to)
        {
            if (CombatNet.Instance == null) return;
            var color = new Color(3f, 2.2f, 0.8f);
            Vector3 d = (to - from).normalized;
            float len = Vector3.Distance(from, to) + 2f;
            if (Physics.Raycast(from, d, out var hit, len, ~0, QueryTriggerInteraction.Ignore))
                CombatNet.Instance.Tracer(from, hit.point, color, hit.normal, FeedbackFx.SurfaceOf(hit.collider));
            else CombatNet.Instance.Tracer(from, to, color);
        }

        /// <summary>The local pawn's muzzle (same offset as the held tool).</summary>
        public static Vector3 PawnMuzzle
        {
            get
            {
                var c = Pawn != null ? Pawn.CameraPoint : null;
                return c == null ? Vector3.zero : c.position + c.forward * 0.78f + c.right * 0.26f - c.up * 0.25f;
            }
        }

        /// <summary>
        /// Shoot from the local pawn's hand at a world point, like the player would: blaster projectile (server)
        /// or rifle hitscan, plus the local recoil. No damage.
        /// </summary>
        public static void PawnShoot(Vector3 target, bool rifle = false)
        {
            if (Pawn == null || CombatNet.Instance == null) return;
            Vector3 m = PawnMuzzle;
            if (rifle) { Hitscan(m, target); FeedbackFx.LocalRecoil(0.3f); }
            else { CombatNet.Instance.RequestFireServerRpc(ProjectileKind.Blaster, m, (target - m).normalized, 0f); FeedbackFx.LocalRecoil(0.55f); }
        }

        /// <summary>Ground point 'dist' metres in front of the local pawn (offset 'side' metres to the right).</summary>
        public static Vector3 GroundAhead(float dist, float side = 0f)
        {
            var p = Pawn;
            if (p == null) return Vector3.zero;
            Vector3 up = p.transform.up;
            Vector3 probe = p.transform.position + p.transform.forward * dist + p.transform.right * side + up * 20f;
            // nearest hit that is not the pawn itself (its capsule would lift it 1.8 m per call)
            var hits = Physics.RaycastAll(probe, -up, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = probe - up * 20f;
            foreach (var h in hits)
                if (!h.collider.transform.IsChildOf(p.transform) && h.distance < best) { best = h.distance; point = h.point; }
            return point;
        }

        /// <summary>Server: spawn an enemy (tier 1).</summary>
        public static EnemyBrain SpawnEnemy(EnemyKind kind, Vector3 pos, Vector3 lookAt)
        {
            if (!IsServer) { Log("SpawnEnemy needs the host"); return null; }
            var dir = lookAt - pos;
            return EnemyFactory.Spawn(kind, pos, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward), 1f);
        }

        /// <summary>
        /// Server (#98): wake the Warden `ahead` m in front of the local ship (or pawn) in any system, the real enclave spawn
        /// path (announcement, boss music, hoard on defeat). Logs "[Dev] end warden &lt;position&gt;".
        /// </summary>
        public static BossBrain SpawnWarden(float ahead = 260f)
        {
            Log("begin warden");
            var rm = RunManager.Instance;
            Transform from = Ship != null ? Ship.transform : Pawn != null ? Pawn.transform : null;
            if (!IsServer || rm == null || rm.Director == null || from == null) { Log("end warden FAILED: needs the host in a run"); return null; }
            rm.Director.SpawnWarden(from.position + from.forward * ahead);
            var boss = BossBrain.Instance;
            Log(boss != null ? $"end warden {boss.transform.position}" : "end warden FAILED: not spawned (one is alive already?)");
            return boss;
        }

        /// <summary>Move the local pawn (owner authority) to a position, standing on 'up', facing 'forward'.</summary>
        public static void Teleport(Vector3 pos, Vector3 up, Vector3 forward)
        {
            var p = Pawn;
            if (p == null) return;
            var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, up).normalized, up);
            p.transform.SetPositionAndRotation(pos, rot);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = pos; rb.rotation = rot; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
        }

        /// <summary>
        /// Server (#97): the local player boards a ship of a class in a seat of a role (index-th seat of that role).
        /// Takes an existing ship of the class (a working one before a derelict), else spawns one 45 m to the right of
        /// the player's ship. claim = restore a derelict first. Logs "[Dev] end board Ark Pilot ...".
        /// </summary>
        public static ShipCore Board(ShipClass cls, StationRole role = StationRole.Pilot, bool claim = false, int index = 0)
        {
            Log($"begin board {cls} {role}");
            if (!IsServer || Pawn == null) { Log("end board FAILED: needs the host + a pawn"); return null; }
            ShipCore ship = null;
            foreach (var s in ShipCore.All)
                if (s != null && s.IsSpawned && s.Class == cls && (ship == null || (ship.Derelict.Value && !s.Derelict.Value))) ship = s;
            if (ship == null)
            {
                var prefab = ContentCatalog.GetShipPrefab(cls);
                if (prefab == null) { Log("end board FAILED: no prefab for " + cls); return null; }
                var near = SeatShip != null ? SeatShip.transform : Pawn.transform;
                var go = UnityEngine.Object.Instantiate(prefab, near.position + near.right * 45f, near.rotation);
                go.GetComponent<NetworkObject>().Spawn(true);
                ship = go.GetComponent<ShipCore>();
            }
            if (claim && ship.Derelict.Value) ship.Derelict.Value = false;
            ulong me = Net.LocalClientId;
            int k = 0;
            foreach (var seat in ShipSeat.All)
            {
                if (seat == null || seat.Ship != ship || seat.Role != role || k++ < index) continue;
                var prev = ShipSeat.SeatOf(me);
                if (prev != null && prev != seat) prev.Vacate();
                seat.Occupant.Value = me;
                if (role == StationRole.Pilot) ship.NetworkObject.ChangeOwnership(me);
                Log($"end board {cls} {role} {seat.name} | ship {ship.NetworkObjectId} derelict {ship.Derelict.Value}");
                return ship;
            }
            Log($"end board FAILED: {cls} has no {role} seat #{index}");
            return ship;
        }

        /// <summary>Server (#97): switch a ship of a class (the first found) to derelict or back (ShipDerelictLook).</summary>
        public static void SetDerelict(ShipClass cls, bool on)
        {
            if (!IsServer) return;
            foreach (var s in ShipCore.All)
                if (s != null && s.IsSpawned && s.Class == cls) { s.Derelict.Value = on; Log($"derelict {cls} {on}"); return; }
        }

        /// <summary>Server: god mode on/off (no damage, no oxygen loss).</summary>
        public static void God(bool on) { if (IsServer) CheatManager.GodMode = on; }

        /// <summary>
        /// Server: hit the local pawn for 'amount' from an attacker in world direction 'from' (relative to the pawn),
        /// bypassing god mode for this one hit. Drives the damage-taken HUD (arc, flash, low-health pulse).
        /// </summary>
        public static void HurtPawn(float amount, Vector3 from)
        {
            var p = Pawn;
            if (!IsServer || p == null) { Log("HurtPawn needs the host + a pawn"); return; }
            HurtServer(p.Health, p.transform.position + p.transform.up * 1f, from, amount);
        }

        /// <summary>Server: hit the local player's ship (shield absorbs first, like a real shot) from world direction 'from'.</summary>
        public static void HurtShip(float amount, Vector3 from)
        {
            var s = Ship;
            if (!IsServer || s == null) { Log("HurtShip needs the host + a ship"); return; }
            HurtServer(s.Hull, s.transform.position + from.normalized * 2f, from, amount);
        }

        static void HurtServer(HealthComponent h, Vector3 point, Vector3 from, float amount)
        {
            bool god = CheatManager.GodMode;
            CheatManager.GodMode = false;
            h.ApplyDamage(new DamageInfo { Amount = amount, Type = DamageType.Kinetic, Point = point, Direction = -from.normalized });
            CheatManager.GodMode = god;
        }

        /// <summary>Server: set the local pawn's health (0..1 of max) and, if any, the ship shield (0..1 of max).</summary>
        public static void SetVitals(float health, float shield = -1f)
        {
            if (!IsServer) return;
            var p = Pawn;
            if (p != null) p.Health.Hp.Value = Mathf.Max(1f, p.Health.MaxHp.Value * Mathf.Clamp01(health));
            var s = Ship;
            if (s != null && shield >= 0f) s.Shield.Value = s.Stats.MaxShield * Mathf.Clamp01(shield);
        }

        public static void ClearDecals() { if (G.Get<IVfxService>() is VfxManager v) v.ClearDecals(); }

        public static int DecalCount => G.Get<IVfxService>() is VfxManager v ? v.DecalCount : 0;

        // ------------------------------------------------------------------ loot & music (#43 part 4)

        /// <summary>One item per rarity: Common, Uncommon, Rare, Epic.</summary>
        public static readonly string[] RarityItems = { "ore", "exotic", "gear_suit_mk2", "gear_suit_mk3" };

        /// <summary>Server: drop an item that pops out of 'from' (like an enemy / crate drop) and lands nearby.</summary>
        public static WorldPickup DropLoot(string itemId, Vector3 from, int count = 1)
        {
            if (!IsServer) { Log("DropLoot needs the host"); return null; }
            return WorldPickup.SpawnServer(itemId, count, from);
        }

        /// <summary>Server: drop one item of every rarity from 'from' (beam / colour / landing check).</summary>
        public static void DropRarityRow(Vector3 from)
        {
            foreach (var id in RarityItems) DropLoot(id, from);
        }

        /// <summary>Live drops, and how many of them the local magnet is pulling in.</summary>
        public static int LootCount => WorldPickup.All.Count;
        public static int LootMagnetized { get { int n = 0; foreach (var p in WorldPickup.All) if (p != null && p.Magnetized) n++; return n; } }

        /// <summary>Server: remove every drop.</summary>
        public static void ClearLoot()
        {
            if (!IsServer) return;
            foreach (var p in WorldPickup.All.ToArray()) if (p != null && p.IsSpawned) p.NetworkObject.Despawn(true);
        }

        /// <summary>Client: force the music threat level (0..1) and the boss layer for some seconds (level &lt; 0 clears).</summary>
        public static void SetThreat(float level, bool boss = false, float seconds = 30f) => Threat.Override(level, boss, seconds);

        /// <summary>"threat 0.80 boss False | combat 0.95 bossMix 0.00" (also logged as [Dev] music &lt;tag&gt; ...).</summary>
        public static string MusicState => $"threat {Threat.Level:0.00} boss {Threat.BossNear} | combat {AudioManager.CombatMix:0.00} bossMix {AudioManager.BossMix:0.00}";
        public static void LogMusic(string tag) => Log("music " + tag + " " + MusicState);

        /// <summary>Local meta save: add Stardust (perk unlock tests in the Hangar). Call BackupMeta first: this is the real save.</summary>
        public static void AddStardust(int amount) { Meta.Data.Stardust += amount; Meta.Save(); }

        static string metaBackup;
        /// <summary>Snapshot the local meta save (Stardust, perks) before a test that changes it; RestoreMeta puts it back.</summary>
        public static void BackupMeta() => metaBackup = JsonUtility.ToJson(Meta.Data);
        public static void RestoreMeta()
        {
            if (string.IsNullOrEmpty(metaBackup)) { Log("RestoreMeta: no backup"); return; }
            JsonUtility.FromJsonOverwrite(metaBackup, Meta.Data);
            Meta.Save();
            Log("meta restored (stardust " + Meta.Data.Stardust + ")");
        }

        // ------------------------------------------------------------------ planet survey (#70)

        /// <summary>Distinct planet shapes of all biomes in the content catalog.</summary>
        public static PlanetShapeDef[] CatalogShapes()
        {
            var list = new List<PlanetShapeDef>();
            var cat = ContentCatalog.Instance;
            if (cat != null) foreach (var b in cat.Biomes) if (b != null && b.Shape != null && !list.Contains(b.Shape)) list.Add(b.Shape);
            return list.ToArray();
        }

        /// <summary>
        /// Look comparison for planet shading: builds each shape as a temporary planet far outside the sector, drives the
        /// main camera through the requested views (so LOD, decor and PlanetAmbience react like in game), and writes ONE
        /// sheet: one row per shape, one column per view, no HUD. Needs play mode (any phase). Logs "[Dev] end name -> path".
        /// </summary>
        public static void PlanetSurvey(string name, PlanetShapeDef[] shapes, PlanetView views = PlanetView.All, float radius = 1500f, int seed = 4242, float settle = 4f)
        {
            Log("begin " + name);
            DevRunner.Get().Run(PlanetSurveyCo(name, shapes, views, radius, seed, settle));
        }

        static IEnumerator PlanetSurveyCo(string name, PlanetShapeDef[] shapes, PlanetView views, float radius, int seed, float settle)
        {
            var cam = Camera.main;
            if (cam == null || shapes == null || shapes.Length == 0) { Log("end " + name + " FAILED: no main camera or shapes"); yield break; }
            var brain = cam.GetComponent("CinemachineBrain") as Behaviour;
            bool brainWas = brain != null && brain.enabled;
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            float camFov = cam.fieldOfView;
            var sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            var tiles = new List<Texture2D>();
            int cols = 0;
            foreach (PlanetView v in Enum.GetValues(typeof(PlanetView))) if (v != PlanetView.All && (views & v) != 0) cols++;

            for (int k = 0; k < shapes.Length; k++)
            {
                if (shapes[k] == null) continue;
                var go = new GameObject("_DevPlanet_" + shapes[k].name);
                go.transform.position = new Vector3(0f, 40000f, 80000f);
                var vis = go.AddComponent<PlanetVisuals>();
                vis.Build(shapes[k], seed + k, radius, false);
                foreach (var pose in SurveyPoses(vis, views, toSun))
                {
                    float until = Time.realtimeSinceStartup + (pose.Fov < 0f ? 1.5f : settle);
                    var p = new DevPose(pose.Pos, pose.LookAt, Mathf.Abs(pose.Fov));
                    while (Time.realtimeSinceStartup < until)
                    {
                        if (brain != null) brain.enabled = false;
                        cam.transform.SetPositionAndRotation(p.Pos, Quaternion.LookRotation(p.LookAt - p.Pos, (p.Pos - go.transform.position).normalized));
                        yield return null;
                    }
                    tiles.Add(DevShots.Render(p, TileW, TileH, (p.Pos - go.transform.position).normalized));
                }
                UnityEngine.Object.Destroy(go);
                yield return null;
            }

            if (brain != null) brain.enabled = brainWas;
            cam.transform.SetPositionAndRotation(camPos, camRot);
            cam.fieldOfView = camFov;
            string path = "";
            if (tiles.Count > 0)
            {
                var sheet = DevShots.Sheet(tiles, cols);
                path = DevShots.Save(sheet, name);
                DevShots.Kill(sheet);
            }
            foreach (var t in tiles) DevShots.Kill(t);
            Log("end " + name + " -> " + path + " (rows " + shapes.Length + ", cols " + cols + ")");
        }

        /// <summary>Survey poses for one planet; a negative Fov marks quick (orbit) views.</summary>
        static List<DevPose> SurveyPoses(PlanetVisuals vis, PlanetView views, Vector3 toSun)
        {
            var poses = new List<DevPose>();
            var T = vis.Terrain;
            Vector3 c = vis.transform.position;
            float R = T.Radius;
            Vector3 side = Vector3.Cross(toSun, Mathf.Abs(toSun.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

            if ((views & PlanetView.Orbit) != 0)
                poses.Add(new DevPose(c + (toSun * 0.75f + side * 0.65f).normalized * R * 2.7f, c, -50f));
            if ((views & PlanetView.Limb) != 0)
            {
                // Over the terminator, looking along the limb toward the day side.
                Vector3 d = (side + toSun * 0.15f).normalized;
                Vector3 pos = c + d * (T.AtmosphereRadius + R * 0.25f);
                Vector3 fwd = Vector3.ProjectOnPlane(toSun, d).normalized;
                poses.Add(new DevPose(pos, c + fwd * R * 1.1f, -55f));
            }

            Vector3 dry = FindSpot(T, toSun, side, false);
            Vector3 look = Vector3.ProjectOnPlane(side, dry).normalized;
            Vector3 g = c + dry * T.GroundRadius(dry);
            if ((views & PlanetView.Flight) != 0)
                poses.Add(new DevPose(g + dry * 300f - look * 250f, g + look * 600f, 60f));
            if ((views & PlanetView.Ground) != 0)
                poses.Add(new DevPose(g + dry * 1.8f, g + dry * 1.0f + look * 100f, 60f));
            if ((views & PlanetView.Shore) != 0)
            {
                Vector3 beach = T.HasOcean ? FindSpot(T, toSun, side, true) : dry;
                Vector3 b = c + beach * T.GroundRadius(beach);
                Vector3 seaward = look;
                if (T.HasOcean)
                {
                    // Step outward in 8 directions and face the first one that reaches the water.
                    Vector3 t1 = Vector3.Cross(beach, side).normalized;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 o = Quaternion.AngleAxis(i * 45f, beach) * t1;
                        if (T.IsUnderwater((beach + o * (40f / R)).normalized)) { seaward = o; break; }
                    }
                }
                poses.Add(new DevPose(b + beach * 2.2f - seaward * 6f, b + beach * 1.0f + seaward * 80f, 60f));
            }
            return poses;
        }

        /// <summary>Dry spot with the sun ~45 degrees high; shore = dry land within 40 m of the sea.</summary>
        static Vector3 FindSpot(PlanetTerrain T, Vector3 toSun, Vector3 side, bool shore)
        {
            Vector3 baseDir = (toSun + side).normalized;
            Vector3 fallback = baseDir;
            var rng = new System.Random(77);
            float step = 40f / T.Radius;
            for (int i = 0; i < 4000; i++)
            {
                float spread = Mathf.Lerp(0.2f, 1.6f, i / 4000f);
                var j = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * spread;
                Vector3 d = (baseDir + j).normalized;
                if (Vector3.Dot(d, toSun) < 0.35f || T.IsUnderwater(d)) continue;
                if (T.SlopeAt(d, 4f) > 0.15f) continue;
                if (!shore) return d;
                Vector3 t1 = Vector3.Cross(d, side).normalized;
                for (int a = 0; a < 8; a++)
                    if (T.IsUnderwater((d + Quaternion.AngleAxis(a * 45f, d) * t1 * step).normalized)) return d;
                fallback = d;
            }
            return fallback;
        }

        // ------------------------------------------------------------------ shots

        /// <summary>Render a free camera pose now (works in edit mode too). Returns the file path.</summary>
        public static string Shot(string name, DevPose pose) => DevShots.Shot(name, pose);

        /// <summary>Grab the player's screen (incl. HUD) at the end of this frame, downscaled. Logs "[Dev] shot path".</summary>
        public static void Screen(string name) => DevRunner.Get().Run(ScreenCo(name));

        /// <summary>
        /// The player's current view (main camera pose and FOV, no HUD) rendered at full 1920x1080 (#94): judge
        /// cockpit display readability at game resolution, not in the small editor game view.
        /// </summary>
        public static string PilotView(string name)
        {
            var cam = Camera.main;
            if (cam == null) { Log("PilotView needs a main camera"); return null; }
            var t = cam.transform;
            var tex = DevShots.Render(new DevPose(t.position, t.position + t.forward * 10f, cam.fieldOfView), 1920, 1080, t.up);
            string path = DevShots.Save(tex, name);
            DevShots.Kill(tex);
            return path;
        }

        /// <summary>
        /// Look around inside the seated ship's cockpit (#94): one sheet with four views from the pilot's eye
        /// (ship-local, eye at (0, 0, eyeZ); without eyeZ the occupied seat's view point, #97): back over the seat,
        /// left shoulder, up through the canopy, right console.
        /// </summary>
        public static string CockpitViews(string name, float eyeZ = float.NaN)
        {
            var s = SeatShip;
            if (s == null) { Log("CockpitViews needs a ship"); return null; }
            var t = s.transform;
            var eye = SeatEye(s, eyeZ);
            var looks = new[] { new Vector3(0f, -0.65f, -2.1f), new Vector3(-1.5f, -0.3f, -1.1f), new Vector3(-1.2f, 1.2f, 0.5f), new Vector3(1.6f, -0.2f, 0.9f) };
            var tiles = new System.Collections.Generic.List<Texture2D>();
            foreach (var l in looks)
            {
                var from = eye + (l.z < -1.5f ? new Vector3(0f, 0.05f, 0.25f) : Vector3.zero);
                tiles.Add(DevShots.Render(new DevPose(t.TransformPoint(from), t.TransformPoint(eye + l), 90f), 640, 360, t.up));
            }
            var sheet = DevShots.Sheet(tiles, 2);
            string path = DevShots.Save(sheet, name);
            foreach (var x in tiles) DevShots.Kill(x);
            DevShots.Kill(sheet);
            return path;
        }

        /// <summary>
        /// The seated crew body as the other players see it (#95): one sheet of four views around the local pawn's seat
        /// (side through the canopy, front over the dashboard, right shoulder onto the stick hand, left onto the throttle),
        /// with the head shown (as on a remote peer) for the shots. Ship-local, eye at (0, 0, eyeZ) or the seat's view point.
        /// </summary>
        public static string SeatBodyViews(string name, float eyeZ = float.NaN)
        {
            var s = SeatShip;
            var seat = Pawn != null ? Pawn.CurrentShipSeat : null;
            if (s == null || seat == null) { Log("SeatBodyViews needs a seated pawn"); return null; }
            var pose = seat.GetComponentInChildren<Voidward.Ship.CrewSeatPose>(true);
            bool local = pose != null && pose.Local;
            if (pose != null) pose.Local = false;
            var t = s.transform;
            var eye = SeatEye(s, eyeZ);
            var views = new[]
            {
                (new Vector3(1.7f, 0.5f, -0.45f), new Vector3(0f, -0.5f, 0.25f), 55f),
                (new Vector3(0.3f, 0.3f, 1.95f), new Vector3(0f, -0.5f, -0.05f), 60f),
                (new Vector3(0.75f, 0.15f, -0.25f), new Vector3(0.4f, -0.6f, 0.3f), 60f),
                (new Vector3(-0.75f, 0.15f, -0.25f), new Vector3(-0.4f, -0.65f, 0.3f), 60f),
            };
            var tiles = new System.Collections.Generic.List<Texture2D>();
            foreach (var (from, to, fov) in views)
                tiles.Add(DevShots.Render(new DevPose(t.TransformPoint(eye + from), t.TransformPoint(eye + to), fov), 640, 360, t.up));
            if (pose != null) pose.Local = local;
            var sheet = DevShots.Sheet(tiles, 2);
            string path = DevShots.Save(sheet, name);
            foreach (var x in tiles) DevShots.Kill(x);
            DevShots.Kill(sheet);
            return path;
        }

        /// <summary>Ship-local eye for the cockpit views: (0, 0, eyeZ), or the occupied seat's view point when eyeZ is NaN.</summary>
        static Vector3 SeatEye(ShipCore ship, float eyeZ)
        {
            if (!float.IsNaN(eyeZ)) return new Vector3(0f, 0f, eyeZ);
            var seat = Pawn != null ? Pawn.CurrentShipSeat : null;
            var vp = seat != null ? seat.transform.Find("ViewPoint") : null;
            return vp != null ? ship.transform.InverseTransformPoint(vp.position) : new Vector3(0f, 0f, 3.67f);
        }

        /// <summary>
        /// The local pawn's on-foot body as the other players see it (#95): one sheet of four views (front three-quarter,
        /// side, back, close on the tool hand). The owner's body normally only casts shadows; it is shown for the shots.
        /// </summary>
        public static string BodyViews(string name)
        {
            var p = Pawn;
            if (p == null) { Log("BodyViews needs a pawn"); return null; }
            var t = p.transform;
            var rs = p.GetComponentsInChildren<Renderer>(true);
            var modes = new UnityEngine.Rendering.ShadowCastingMode[rs.Length];
            for (int i = 0; i < rs.Length; i++)
            {
                modes[i] = rs[i].shadowCastingMode;
                if (rs[i].transform.IsChildOf(p.CameraPoint)) continue;   // the viewmodels stay as they are
                rs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var views = new[]
            {
                (new Vector3(1.4f, 1.5f, 2.4f), new Vector3(0f, 1f, 0f), 45f),
                (new Vector3(3f, 1.1f, 0f), new Vector3(0f, 0.95f, 0f), 45f),
                (new Vector3(-1.2f, 1.7f, -2.6f), new Vector3(0f, 1f, 0f), 45f),
                (new Vector3(0.9f, 1.3f, 0.9f), new Vector3(0.25f, 1.05f, 0.3f), 40f),
            };
            var tiles = new System.Collections.Generic.List<Texture2D>();
            foreach (var (from, to, fov) in views)
                tiles.Add(DevShots.Render(new DevPose(t.TransformPoint(from), t.TransformPoint(to), fov), 640, 360, t.up));
            for (int i = 0; i < rs.Length; i++) rs[i].shadowCastingMode = modes[i];
            var sheet = DevShots.Sheet(tiles, 2);
            string path = DevShots.Save(sheet, name);
            foreach (var x in tiles) DevShots.Kill(x);
            DevShots.Kill(sheet);
            return path;
        }

        /// <summary>
        /// Light/sky look test (#96): one sheet of four views around the seated ship (front three-quarter, rear onto the
        /// engines, wide with the sky behind, close along the hull for reflections). No HUD.
        /// </summary>
        public static string ShipViews(string name)
        {
            var s = SeatShip;
            if (s == null) { Log("ShipViews needs a ship"); return null; }
            var t = s.transform;
            var views = new[]
            {
                (new Vector3(14f, 5f, 22f), new Vector3(0f, 0f, 1f), 45f),
                (new Vector3(-9f, 4f, -24f), new Vector3(0f, 0.5f, -2f), 45f),
                (new Vector3(30f, 12f, -55f), new Vector3(-20f, 25f, 80f), 70f),
                (new Vector3(4.5f, 2.2f, 7f), new Vector3(0f, 0.3f, -3f), 40f),
            };
            var tiles = new System.Collections.Generic.List<Texture2D>();
            foreach (var (from, to, fov) in views)
                tiles.Add(DevShots.Render(new DevPose(t.TransformPoint(from), t.TransformPoint(to), fov), 640, 360, t.up));
            var sheet = DevShots.Sheet(tiles, 2);
            string path = DevShots.Save(sheet, name);
            foreach (var x in tiles) DevShots.Kill(x);
            DevShots.Kill(sheet);
            return path;
        }

        /// <summary>
        /// VFX look test (#96): in front of the seated ship (or the camera) an explosion, a muzzle flash burst, a metal and a
        /// rock impact, a laser impact and a shield hit/break fire at t = 0; one sheet of six tiles over 2.5 s from a fixed
        /// camera (0.05 / 0.15 / 0.35 / 0.7 / 1.3 / 2.5 s). Wait with dev-wait.sh name.
        /// </summary>
        public static void VfxViews(string name, float distance = 40f)
        {
            var s = SeatShip;
            Transform t = s != null ? s.transform : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) { Log("VfxViews needs a ship or a camera"); return; }
            Vector3 c = t.position + t.forward * (distance + 20f), r = t.right, u = t.up;
            var cam = new DevPose(t.position + t.forward * 20f + u * 2f, c, 50f);
            Sequence(name, 2.6f, (f, time) =>
            {
                if (f == 0)
                {
                    Vfx.Explosion(c - r * 11f + u * 2f, 4f);
                    Vfx.Impact(c + u * 7f, -t.forward, SurfaceKind.Metal, new Color(1f, 0.7f, 0.3f), 1.5f);
                    Vfx.Impact(c + r * 4f + u * 7f, -t.forward, SurfaceKind.Rock, new Color(1f, 0.7f, 0.3f), 1.5f);
                    Vfx.LaserImpact(c - u * 6f);
                    Vfx.ShieldHit(c + r * 12f + u * 3f);
                    Vfx.ShieldBreak(c + r * 12f - u * 5f, 3f);
                }
                if (f % 4 == 0 && time < 0.8f) Vfx.MuzzleFlash(c - r * 2f, -r, new Color(2f, 1.8f, 1.2f), 2f);
            }, new[] { 0.05f, 0.15f, 0.35f, 0.7f, 1.3f, 2.5f }, cam);
        }

        /// <summary>
        /// Projectile look test (#96, server): ship laser, enemy laser, blaster, missile, flak and boss orb fly side by side
        /// across the view in front of the seated ship (or camera); one sheet of four tiles (0.04 / 0.08 / 0.2 / 0.4 s)
        /// from a camera looking across the lanes. Wait with dev-wait.sh name.
        /// </summary>
        public static void ProjectileViews(string name)
        {
            var s = SeatShip;
            Transform t = s != null ? s.transform : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) { Log("ProjectileViews needs a ship or a camera"); return; }
            Vector3 c = t.position + t.forward * 60f + t.up * 6f, r = t.right, u = t.up;
            var kinds = new[] { ProjectileKind.LaserBolt, ProjectileKind.EnemyLaser, ProjectileKind.Blaster, ProjectileKind.Missile, ProjectileKind.Flak, ProjectileKind.BossOrb };
            var cam = new DevPose(c - r * 40f - t.forward * 28f, c - r * 40f, 60f);
            Sequence(name, 0.9f, (f, time) =>
            {
                if (f != 0) return;
                for (int i = 0; i < kinds.Length; i++)
                {
                    Vector3 lane = c + u * ((i - 2.5f) * 3.5f) - r * 70f;
                    Fire(kinds[i], lane, lane + r * 200f);
                }
            }, new[] { 0.04f, 0.08f, 0.2f, 0.4f }, cam);
        }

        /// <summary>
        /// Locomotion look test (#95): the local pawn is carried along the ground ahead (walk 1.6, jog 6, sprint 9.5 m/s,
        /// one second each) with its body shown, a side camera follows; ONE sheet with a tile per gait (+ a backwards
        /// walk). Wait with dev-wait.sh name. The pawn must stand on a planet (scenario Planet).
        /// </summary>
        public static void WalkViews(string name)
        {
            var p = Pawn;
            if (p == null) { Log("WalkViews needs a pawn"); return; }
            var rb = p.GetComponent<Rigidbody>();
            var rs = p.GetComponentsInChildren<Renderer>(true);
            Sequence(name, 4.2f, (f, t) =>
            {
                foreach (var r in rs)
                    if (!r.transform.IsChildOf(p.CameraPoint)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                float v = t < 1.1f ? 1.6f : t < 2.1f ? 6f : t < 3.1f ? 9.5f : -1.6f;
                var g = GroundAhead(v * Time.deltaTime);
                rb.position = g;
                p.transform.position = g;
                rb.linearVelocity = Vector3.zero;
            }, new[] { 0.9f, 1.9f, 2.9f, 4f },
            () => new DevPose(p.transform.position + p.transform.right * 3.2f + p.transform.up * 1.1f + p.transform.forward * 0.4f,
                p.transform.position + p.transform.up * 0.95f, 40f));
        }

        /// <summary>The local pawn's interaction prompt ("[F] Board ...", "" when nothing is in reach).</summary>
        public static string InteractPrompt => Pawn != null ? Pawn.Prompt : "";

        static IEnumerator ScreenCo(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = DevShots.GrabScreen();
            DevShots.Save(tex, name);
            DevShots.Kill(tex);
        }

        /// <summary>
        /// Run a timed test and capture a contact sheet: step(frame, seconds) is called every frame for 'duration'
        /// seconds; at each of shotTimes (seconds) one tile is captured, from 'pose' or (null) the player's screen.
        /// All tiles end up in ONE image (tiles 640x360, a single shot 960x540). Logs "[Dev] end name -> path".
        /// </summary>
        public static void Sequence(string name, float duration, Action<int, float> step, float[] shotTimes, DevPose? pose = null)
            => Sequence(name, duration, step, shotTimes, pose.HasValue ? () => pose.Value : (Func<DevPose>)null);

        /// <summary>Like Sequence, with a camera pose that is re-evaluated per shot (follow moving things).</summary>
        public static void Sequence(string name, float duration, Action<int, float> step, float[] shotTimes, Func<DevPose> pose)
        {
            Log("begin " + name);
            DevRunner.Get().Run(SequenceCo(name, duration, step, shotTimes, pose));
        }

        const int TileW = 640, TileH = 360;

        static IEnumerator SequenceCo(string name, float duration, Action<int, float> step, float[] shotTimes, Func<DevPose> pose)
        {
            var times = shotTimes != null ? (float[])shotTimes.Clone() : new float[0];
            Array.Sort(times);
            var tiles = new List<Texture2D>();
            float t0 = Time.time;
            int frame = 0, next = 0;
            while (true)
            {
                float t = Time.time - t0;
                try { step?.Invoke(frame, t); }
                catch (Exception e) { Log("end " + name + " FAILED: " + e.Message); foreach (var x in tiles) DevShots.Kill(x); yield break; }
                if (next < times.Length && t >= times[next])
                {
                    next++;
                    bool single = times.Length == 1;
                    int w = single ? DevShots.W : TileW, h = single ? DevShots.H : TileH;
                    if (pose != null) tiles.Add(DevShots.Render(pose(), w, h));
                    else { yield return new WaitForEndOfFrame(); tiles.Add(DevShots.GrabScreen(w, h)); }
                }
                if (t >= duration && next >= times.Length) break;
                frame++;
                yield return null;
            }
            string path = "";
            if (tiles.Count > 0)
            {
                var sheet = tiles.Count == 1 ? tiles[0] : DevShots.Sheet(tiles);
                path = DevShots.Save(sheet, name);
                if (sheet != tiles[0]) DevShots.Kill(sheet);
            }
            foreach (var t in tiles) DevShots.Kill(t);
            Log("end " + name + " -> " + path + " (tiles " + tiles.Count + ", decals " + DecalCount + ")");
        }
    }
}
