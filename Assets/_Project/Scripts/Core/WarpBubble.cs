using System;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Local view of the crew's warp flight between galaxies (#79), shared across assemblies: World (WarpFlight)
    /// writes it on every peer, Ship (flight, pilot seat) and Player (camera) read it. The flight happens in a
    /// corridor that streams past the origin along +X: ships stay near x = 0 and only move sideways, the lead ship
    /// steers, every other ship holds its slot in the warp bubble.
    /// </summary>
    public static class WarpBubble
    {
        /// <summary>Direction of travel in world space (the corridor flows towards -X).</summary>
        public static readonly Vector3 Axis = Vector3.right;
        /// <summary>Half width of the corridor the lead ship can steer in (m, around the axis).</summary>
        public const float HalfWidth = 320f;

        /// <summary>A galaxy warp is in progress (also while dropped out at a signal).</summary>
        public static bool Active;
        /// <summary>Flying in the bubble: active and not dropped out (normal flight is off).</summary>
        public static bool Flying;
        /// <summary>NetworkObjectId of the ship that steers the bubble.</summary>
        public static ulong LeadShip = ulong.MaxValue;
        /// <summary>Actual warp throttle 0..1.</summary>
        public static float Throttle;
        /// <summary>Corridor speed now (m/s): throttle x the core's visual speed.</summary>
        public static float Speed;
        /// <summary>Visual intensity 0..1 (throttle x drive tier) for streaks and FOV.</summary>
        public static float Intensity;
        /// <summary>Extra camera FOV (degrees) while warping fast.</summary>
        public static float FovBoost;
        /// <summary>Lateral acceleration of corridor fields (gravity wells) at a world position, set by the corridor view.</summary>
        public static Func<Vector3, Vector3> Pull;
        /// <summary>Core tier of the crew's warp drive (WarpDrive writes it on every peer): system cruise speed (#80).</summary>
        public static int CoreTier = 1;

        // ---- lead pilot input (set by the pilot seat, read and sent by WarpFlight on the same client) ----
        /// <summary>W/S axis of the lead pilot this frame (-1..1).</summary>
        public static float PilotThrottleAxis;
        public static bool PilotBrake;
        public static bool PilotCruiseToggle;
        public static bool PilotDropOut;
        /// <summary>Unscaled time of the last lead pilot input.</summary>
        public static float PilotInputTime = -99f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Active = Flying = false;
            LeadShip = ulong.MaxValue;
            Throttle = Speed = Intensity = FovBoost = 0f;
            Pull = null;
            CoreTier = 1;
            PilotThrottleAxis = 0f;
            PilotBrake = PilotCruiseToggle = PilotDropOut = false;
            PilotInputTime = -99f;
        }

        /// <summary>Slot offset of a formation ship relative to the lead (slot 1, 2, ... alternate sides, rows fall back).</summary>
        public static Vector3 FormationOffset(int slot, bool big)
        {
            if (big) return new Vector3(-150f, -35f, 0f);
            int row = (slot + 1) / 2;
            float side = slot % 2 == 1 ? 1f : -1f;
            return new Vector3(-26f * row, 5f * row, side * 30f * row);
        }

        /// <summary>Lateral distance (y/z plane) between two points.</summary>
        public static float Lateral(Vector3 a, Vector3 b) => new Vector2(a.y - b.y, a.z - b.z).magnitude;
    }
}
