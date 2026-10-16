using UnityEngine;

namespace Voidward.Core
{
    /// <summary>What a jump zone at the system edge leads to (#108).</summary>
    public enum JumpKind : byte { Hop, Rim, Skip, Home }

    /// <summary>Why a jump zone holds the ship back instead of letting it warp (#108).</summary>
    public enum JumpBlock : byte { Open, Crew, Fuel, Tank, Combat, Gravity }

    /// <summary>
    /// A jump zone at the edge of a system (#108): a disc of Radius around Center facing outward along Normal. A ship
    /// that flies out through it (the lead pilot, cruising or aligned for AlignSeconds) starts the warp to Target: a
    /// system of the galaxy (hop) or RunManager.WarpNextGalaxy / WarpSkipGalaxy (the Galactic Rim, the skip zone, the
    /// flight home). Deterministic from the system seed (SystemLayout), the same on every peer.
    /// </summary>
    public struct JumpZone
    {
        public int Target;
        public JumpKind Kind;
        public Vector3 Center;
        public Vector3 Normal;
        public float Radius;
        public string Label;

        /// <summary>Signed distance past the zone plane (m) and the lateral distance from its axis.</summary>
        public float Along(Vector3 p, out float lateral)
        {
            Vector3 d = p - Center;
            float along = Vector3.Dot(d, Normal);
            lateral = (d - Normal * along).magnitude;
            return along;
        }

        /// <summary>Inside the zone's volume: within the disc, from Depth before the plane outwards.</summary>
        public bool Contains(Vector3 p, out float along)
        {
            along = Along(p, out float lat);
            return lat < Radius && along > -JumpZones.Depth && along < JumpZones.Depth * 3f;
        }

        public Color Color => JumpZones.ColorOf(Kind);
    }

    /// <summary>
    /// Local view of the current system's jump zones (#108), shared across assemblies: SystemVisuals sets the zones of
    /// the system it shows, WarpFlight writes the server's block state and the entry/exit veil on every peer; ShipFlight
    /// holds blocked ships back at the boundary, the HUD and the camera read the rest.
    /// </summary>
    public static class JumpZones
    {
        /// <summary>Disc radius of a zone (m).</summary>
        public const float Radius = 650f;
        /// <summary>The zone volume starts this far before the plane (m): aligning, the boundary hold.</summary>
        public const float Depth = 450f;
        /// <summary>Seconds a ship in normal flight has to hold its nose outward inside the zone before it jumps.</summary>
        public const float AlignSeconds = 3f;
        /// <summary>Nose within this cosine of the zone normal counts as aligned.</summary>
        public const float AlignDot = 0.6f;

        static readonly JumpZone[] None = new JumpZone[0];

        /// <summary>The zones of the system this peer shows (empty in warp / outside a run).</summary>
        public static JumpZone[] Current = None;
        /// <summary>Server block state per zone index (WarpFlight.JumpBlocks).</summary>
        public static readonly JumpBlock[] Blocks = new JumpBlock[16];
        /// <summary>Living crew members not aboard a ship (seated or inside an interior).</summary>
        public static int CrewMissing;
        /// <summary>Entry spool-up 0..1 while the crew flies into a warp (FOV, speed lines), 0 otherwise.</summary>
        public static float Entering;
        /// <summary>The warp bubble's light veil 0..1 (hides the rebase at the start and the end of a warp).</summary>
        public static float Veil;
        public static Color VeilColor = new Color(0.75f, 0.9f, 1f);
        /// <summary>Direction the entering lead ship flies (streaks during the spool-up).</summary>
        public static Vector3 EntryDir = Vector3.right;
        /// <summary>Zone index the lead ship enters / the local pilot's ship is aligning in (-1 none), align progress 0..1.</summary>
        public static int EntryZone = -1, AlignZone = -1;
        public static float Align01;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Current = None;
            for (int i = 0; i < Blocks.Length; i++) Blocks[i] = JumpBlock.Open;
            CrewMissing = 0;
            Entering = Veil = Align01 = 0f;
            EntryDir = Vector3.right;
            EntryZone = AlignZone = -1;
        }

        public static void Clear() => Current = None;

        public static JumpBlock BlockOf(int zone) => zone >= 0 && zone < Blocks.Length ? Blocks[zone] : JumpBlock.Open;

        /// <summary>Zone whose volume contains p (-1 none).</summary>
        public static int At(Vector3 p, out float along)
        {
            along = 0f;
            var zs = Current;
            for (int i = 0; i < zs.Length; i++)
                if (zs[i].Contains(p, out along)) return i;
            return -1;
        }

        public static Color ColorOf(JumpKind k) =>
            k == JumpKind.Rim ? new Color(1f, 0.68f, 0.3f) :
            k == JumpKind.Skip ? new Color(0.78f, 0.48f, 1f) :
            k == JumpKind.Home ? new Color(1f, 0.86f, 0.55f) :
            new Color(0.35f, 0.82f, 1f);

        /// <summary>HUD text of a block (ASCII, upper case).</summary>
        public static string BlockText(JumpBlock b, int crewMissing) =>
            b == JumpBlock.Crew ? $"CREW NOT ABOARD ({crewMissing})" :
            b == JumpBlock.Fuel ? "NOT ENOUGH EXOTIC MATTER" :
            b == JumpBlock.Tank ? "EXOTIC TANK TOO SMALL" :
            b == JumpBlock.Combat ? "COMBAT LOCK" :
            b == JumpBlock.Gravity ? "GRAVITY LOCK" : "";
    }
}
