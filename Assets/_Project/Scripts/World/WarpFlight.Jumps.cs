using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Player;
using Voidward.Ship;

namespace Voidward.World
{
    /// <summary>
    /// Seamless warps out of jump zones (#108). Every system has jump zones at its edge (SystemLayout.Jumps): hops to
    /// the neighbouring systems, and in the galaxy's outermost system the Galactic Rim (next galaxy / the flight home)
    /// and the skip zone. There is no warp button: when the lead pilot flies out through an open zone - cruising, or in
    /// normal flight after holding the nose outward for JumpZones.AlignSeconds - the entry spool-up starts (FOV, speed
    /// lines, the bubble's veil building up), and at its peak the crew is rebased into the warp corridor around the
    /// lead ship with the formation kept. A warp ends the same way: the veil rises, the crew comes out of the zone that
    /// leads back (hops) or at the arrival point (galaxies), cruising into the system. Blocked zones (crew not aboard,
    /// Exotic Matter, combat/gravity lock) hold the ship back at the boundary (ShipFlight) with a HUD message.
    /// </summary>
    public partial class WarpFlight
    {
        /// <summary>Seconds of entry spool-up between flying out through the zone and the rebase into the corridor.</summary>
        public const float EnterSeconds = 2.6f;
        /// <summary>Seconds the bubble's veil rises at the end of a warp before the crew comes out.</summary>
        const float ExitSeconds = 0.8f;
        /// <summary>Lateral distance (m) up to which a ship keeps its offset to the lead when the bubble closes; farther ones get a formation slot.</summary>
        const float CarryRange = 450f;

        /// <summary>Block state per zone of the current system, 4 bits each (JumpBlock).</summary>
        public NetworkVariable<ulong> JumpBlocks = new NetworkVariable<ulong>(0);
        /// <summary>Living crew members not aboard a ship (the crew rule of the jump zones).</summary>
        public NetworkVariable<int> JumpCrewMissing = new NetworkVariable<int>(0);
        /// <summary>Server time the crew is rebased into the warp after flying out through a zone; -1 = no entry running.</summary>
        public NetworkVariable<double> EntryAt = new NetworkVariable<double>(-1);
        public NetworkVariable<ulong> EntryLead = new NetworkVariable<ulong>(ulong.MaxValue);
        public NetworkVariable<int> EntryZone = new NetworkVariable<int>(-1);
        /// <summary>Server time of the last/next rebase under the bubble's veil (entry or exit); -1 = none.</summary>
        public NetworkVariable<double> RebaseAt = new NetworkVariable<double>(-1);
        public NetworkVariable<byte> VeilKind = new NetworkVariable<byte>(0);

        // ---- server ----
        readonly Dictionary<ulong, float> alignTimers = new Dictionary<ulong, float>();
        readonly List<Vector3> zoneShips = new List<Vector3>();
        float blockTimer;
        int entryTarget;
        bool exiting;
        double exitAt;
        // ---- local pilot (HUD only) ----
        float localAlign;

        double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        /// <summary>The bubble's veil over time around a rebase: rises, holds while the world is swapped (with slack for latency), fades.</summary>
        static float VeilAt(float t)
        {
            if (t < -0.6f || t > 1.05f) return 0f;
            if (t < -0.05f) return Mathf.SmoothStep(0f, 1f, (t + 0.6f) / 0.55f);
            if (t < 0.3f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.75f);
        }

        // ================================================================ every peer

        void TickJumps(float dt)
        {
            var rm = RunManager.Instance;
            bool playing = rm != null && rm.Phase.Value == GamePhase.Playing;
            ulong bits = JumpBlocks.Value;
            for (int i = 0; i < JumpZones.Blocks.Length; i++) JumpZones.Blocks[i] = (JumpBlock)((bits >> (4 * i)) & 0xF);
            JumpZones.CrewMissing = JumpCrewMissing.Value;

            double now = ServerNow;
            JumpZones.Veil = playing && RebaseAt.Value > 0 ? VeilAt((float)(now - RebaseAt.Value)) : 0f;
            JumpZones.VeilColor = Color.Lerp(new Color(0.92f, 0.97f, 1f), JumpZones.ColorOf((JumpKind)VeilKind.Value), 0.45f);
            bool entering = playing && EntryAt.Value > 0 && !Active.Value;
            JumpZones.Entering = entering ? Mathf.Clamp01(1f - (float)(EntryAt.Value - now) / EnterSeconds) : 0f;
            JumpZones.EntryZone = entering ? EntryZone.Value : -1;
            if (entering)
            {
                var lead = ShipById(EntryLead.Value);
                if (lead != null) JumpZones.EntryDir = lead.transform.forward;
            }
            TickLocalAlign(dt);
            if (IsServer && playing) ServerTickJumps(dt);
        }

        /// <summary>The local pilot's align progress in a zone (HUD; the server keeps its own timer).</summary>
        void TickLocalAlign(float dt)
        {
            JumpZones.AlignZone = -1;
            JumpZones.Align01 = 0f;
            var nm = NetworkManager;
            var seat = nm != null ? ShipSeat.SeatOf(nm.LocalClientId) : null;
            if (seat == null || seat.Role != StationRole.Pilot || seat.Ship == null || Active.Value) { localAlign = 0f; return; }
            var t = seat.Ship.transform;
            int zi = JumpZones.At(t.position, out _);
            if (zi < 0 || JumpZones.BlockOf(zi) != JumpBlock.Open) { localAlign = 0f; return; }
            localAlign = Vector3.Dot(t.forward, JumpZones.Current[zi].Normal) > JumpZones.AlignDot ? localAlign + dt : 0f;
            JumpZones.AlignZone = zi;
            bool cruising = seat.Ship.Flight != null && seat.Ship.Flight.Cruise == CruiseMode.Cruising;
            JumpZones.Align01 = cruising ? 1f : Mathf.Clamp01(localAlign / JumpZones.AlignSeconds);
        }

        // ================================================================ server

        /// <summary>Server: forget a running entry and the veil (new run, dev warps, run end).</summary>
        public void ServerResetJumps()
        {
            if (!IsServer) return;
            EntryAt.Value = -1;
            EntryZone.Value = -1;
            EntryLead.Value = ulong.MaxValue;
            RebaseAt.Value = -1;
            alignTimers.Clear();
            exiting = false;
        }

        void ServerTickJumps(float dt)
        {
            if (Active.Value) return;
            var rm = RunManager.Instance;
            var layout = rm.Director != null ? rm.Director.Layout : null;
            if (layout == null) return;
            var zones = layout.Jumps;

            if (EntryAt.Value > 0)
            {
                if (ServerNow >= EntryAt.Value) ServerEnter(rm);
                return;
            }

            blockTimer -= dt;
            if (blockTimer <= 0f) { blockTimer = 0.25f; ServerBlocks(rm, zones); }

            foreach (var seat in ShipSeat.All)
            {
                if (seat == null || seat.Role != StationRole.Pilot || !seat.IsOccupied || seat.Ship == null || !seat.Ship.IsSpawned || seat.Ship.Derelict.Value) continue;
                var ship = seat.Ship;
                ulong id = ship.NetworkObjectId;
                Vector3 pos = ship.transform.position;
                int zi = -1;
                float along = 0f;
                for (int i = 0; i < zones.Length && zi < 0; i++) if (zones[i].Contains(pos, out along)) zi = i;
                if (zi < 0 || JumpZones.BlockOf(zi) != JumpBlock.Open) { alignTimers.Remove(id); continue; }
                float dot = Vector3.Dot(ship.transform.forward, zones[zi].Normal);
                alignTimers.TryGetValue(id, out float t);
                t = dot > JumpZones.AlignDot ? t + dt : 0f;
                alignTimers[id] = t;
                bool cruising = ship.Flight != null && ship.Flight.Cruise == CruiseMode.Cruising;
                if (along >= 0f && dot > 0.3f && (cruising || t >= JumpZones.AlignSeconds))
                {
                    ServerBeginEntry(rm, ship, zi, zones[zi]);
                    return;
                }
            }
        }

        /// <summary>Server: crew rule, fuel and locks per zone, packed for every peer.</summary>
        void ServerBlocks(RunManager rm, JumpZone[] zones)
        {
            int missing = 0;
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value || pawn.IsDown.Value || pawn.IsLost.Value) continue;
                if (ShipSeat.SeatOf(pawn.OwnerClientId) != null || GravityVolume.At(pawn.transform.position) != null) continue;
                missing++;
            }
            ulong bits = 0;
            for (int i = 0; i < zones.Length && i < 16; i++)
            {
                JumpBlock b;
                if (missing > 0) b = JumpBlock.Crew;
                else
                {
                    zoneShips.Clear();
                    foreach (var s in ShipCore.All)
                        if (s != null && s.IsSpawned && zones[i].Contains(s.transform.position, out _)) zoneShips.Add(s.transform.position);
                    b = rm.JumpBlockFor(zones[i].Target, zones[i].Center, zoneShips);
                }
                bits |= (ulong)b << (4 * i);
            }
            if (JumpBlocks.Value != bits) JumpBlocks.Value = bits;
            if (JumpCrewMissing.Value != missing) JumpCrewMissing.Value = missing;
            // The server reads its own state right away (no wait for the next tick).
            for (int i = 0; i < JumpZones.Blocks.Length; i++) JumpZones.Blocks[i] = (JumpBlock)((bits >> (4 * i)) & 0xF);
        }

        void ServerBeginEntry(RunManager rm, ShipCore lead, int zone, JumpZone z)
        {
            entryTarget = z.Target;
            EntryLead.Value = lead.NetworkObjectId;
            EntryZone.Value = zone;
            VeilKind.Value = (byte)z.Kind;
            EntryAt.Value = ServerNow + EnterSeconds;
            RebaseAt.Value = EntryAt.Value;
            alignTimers.Clear();
            Debug.Log($"[Jump] entry: {lead.ShipName.Value} through zone {zone} ({z.Kind} -> {z.Target}) {z.Label}");
            rm.Announce($"WARP ENTRY - {z.Label}", "jump_charge");
        }

        void ServerEnter(RunManager rm)
        {
            var lead = ShipById(EntryLead.Value);
            int target = entryTarget;
            EntryAt.Value = -1;
            EntryZone.Value = -1;
            if (lead == null || !rm.ServerJump(target, lead))
            {
                RebaseAt.Value = -1;
                Debug.Log("[Jump] entry aborted");
            }
        }

        /// <summary>
        /// Server: the bubble closes around the lead ship. Every ship keeps its offset and attitude relative to the lead,
        /// turned so the lead's nose points along the corridor (WarpBubble.Axis); ships far from the lead take a
        /// formation slot instead. Under the veil nobody sees the world being swapped.
        /// </summary>
        void ServerRebaseIntoBubble(ShipCore lead)
        {
            Vector3 origin = lead.transform.position;
            Quaternion turn = Quaternion.FromToRotation(lead.transform.forward, WarpBubble.Axis);
            var others = new List<ShipCore>();
            foreach (var s in ShipCore.All) if (s != null && s.IsSpawned && s != lead) others.Add(s);
            others.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
            var axisRot = Quaternion.LookRotation(WarpBubble.Axis);
            Place(lead, Vector3.zero, turn * lead.transform.rotation);
            for (int i = 0; i < others.Count; i++)
            {
                var s = others[i];
                Vector3 rel = s.transform.position - origin;
                if (rel.magnitude < CarryRange) Place(s, turn * rel, turn * s.transform.rotation);
                else Place(s, WarpBubble.FormationOffset(i + 1, s.Class == ShipClass.Ark), axisRot);
            }
            foreach (var pawn in PlayerPawn.All)
            {
                if (pawn == null || !pawn.Active.Value || pawn.IsDown.Value || ShipSeat.SeatOf(pawn.OwnerClientId) != null) continue;
                if (GravityVolume.At(pawn.transform.position) != null) continue; // carried by the interior it stands in
                Vector3 rel = pawn.transform.position - origin;
                Vector3 p = rel.magnitude < CarryRange ? turn * rel : new Vector3(-20f, 12f, 0f) + Random.insideUnitSphere * 6f;
                pawn.GetComponent<NetTeleport>().ServerTeleport(p, turn * pawn.transform.rotation);
            }
            Debug.Log($"[Jump] rebased into the bubble around {lead.ShipName.Value} ({others.Count} more ships)");
        }

        static void Place(ShipCore s, Vector3 pos, Quaternion rot)
        {
            s.GetComponent<NetTeleport>().ServerTeleport(pos, rot);
            s.RestoreServer(false, false);
        }

        /// <summary>Server: the route is flown - the veil rises for ExitSeconds, then the crew comes out (ServerArrive).</summary>
        void ServerBeginExit()
        {
            if (exiting) return;
            exiting = true;
            exitAt = ServerNow + ExitSeconds;
            RebaseAt.Value = exitAt;
            Cruise.Value = false;
        }

        /// <summary>Server: true while the exit veil rises (the flight is frozen at the end of the route).</summary>
        bool ServerTickExit()
        {
            if (!exiting) return false;
            if (ServerNow >= exitAt) { exiting = false; ServerArrive(RunManager.Instance); }
            return true;
        }

        // ================================================================ dev

        /// <summary>Server, dev: the local pilot's ship (or the first piloted one) just inside zone `zone`, nose outward, `distance` m before the plane.</summary>
        public bool ServerDevFaceZone(int zone, float distance, out ShipCore ship)
        {
            ship = null;
            var rm = RunManager.Instance;
            var layout = rm != null && rm.Director != null ? rm.Director.Layout : null;
            if (!IsServer || layout == null || zone < 0 || zone >= layout.Jumps.Length) return false;
            var seat = ShipSeat.SeatOf(NetworkManager.LocalClientId);
            ship = seat != null && seat.Ship != null ? seat.Ship : null;
            if (ship == null)
                foreach (var s in ShipSeat.All)
                    if (s != null && s.Role == StationRole.Pilot && s.IsOccupied && s.Ship != null) { ship = s.Ship; break; }
            if (ship == null) return false;
            var z = layout.Jumps[zone];
            ship.GetComponent<NetTeleport>().ServerTeleport(z.Center - z.Normal * distance, Quaternion.LookRotation(z.Normal));
            return true;
        }

        /// <summary>One line for tests: entry/veil state and every zone with its block.</summary>
        public string JumpInfo()
        {
            var zs = JumpZones.Current;
            var sb = new System.Text.StringBuilder();
            sb.Append($"entry {(EntryAt.Value > 0 ? $"{JumpZones.Entering:0.00} zone {EntryZone.Value}" : "-")} veil {JumpZones.Veil:0.00} crew-missing {JumpZones.CrewMissing} align {JumpZones.AlignZone}:{JumpZones.Align01:0.00} |");
            for (int i = 0; i < zs.Length; i++)
                sb.Append($" {i}:{zs[i].Kind}>{zs[i].Target} {JumpZones.BlockOf(i)} @{zs[i].Center.magnitude / 1000f:0.0}km");
            return sb.ToString();
        }
    }
}
