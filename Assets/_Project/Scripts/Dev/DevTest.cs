using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Networking;
using Voidward.Planet;
using Voidward.Player;
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
            return Physics.Raycast(probe, -up, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : probe - up * 20f;
        }

        /// <summary>Server: spawn an enemy (tier 1).</summary>
        public static EnemyBrain SpawnEnemy(EnemyKind kind, Vector3 pos, Vector3 lookAt)
        {
            if (!IsServer) { Log("SpawnEnemy needs the host"); return null; }
            var dir = lookAt - pos;
            return EnemyFactory.Spawn(kind, pos, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward), 1f);
        }

        /// <summary>Move the local pawn (owner authority) to a position, standing on 'up', facing 'forward'.</summary>
        public static void Teleport(Vector3 pos, Vector3 up, Vector3 forward)
        {
            var p = Pawn;
            if (p == null) return;
            var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, up).normalized, up);
            p.transform.SetPositionAndRotation(pos, rot);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = pos; rb.rotation = rot; rb.linearVelocity = Vector3.zero; }
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

        // ------------------------------------------------------------------ shots

        /// <summary>Render a free camera pose now (works in edit mode too). Returns the file path.</summary>
        public static string Shot(string name, DevPose pose) => DevShots.Shot(name, pose);

        /// <summary>Grab the player's screen (incl. HUD) at the end of this frame, downscaled. Logs "[Dev] shot path".</summary>
        public static void Screen(string name) => DevRunner.Get().Run(ScreenCo(name));

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
