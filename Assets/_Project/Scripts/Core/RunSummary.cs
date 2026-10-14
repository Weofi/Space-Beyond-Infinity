using System;

namespace Voidward.Core
{
    /// <summary>Stats collected during a run, shown on the loop reset / homecoming screen and converted to Stardust.</summary>
    [Serializable]
    public class RunSummary
    {
        public bool Victory;
        public string EndReason = "";
        public int GalaxyReached = 1;
        public float DistanceTraveled;
        public int Kills;
        public int LootCollected;
        public int StardustEarned;
        public float RunTimeSeconds;
        /// <summary>The loop this run was (1-based, #86).</summary>
        public int Loop = 1;
        /// <summary>Warp core tier at the end (1-6).</summary>
        public int DriveTier = 1;
        /// <summary>Home ETA in flight minutes from where the run ended (0 after the homecoming).</summary>
        public float HomeEtaMinutes;
        /// <summary>Crew logbook of this loop, one "[h:mm:ss] text" entry per line.</summary>
        public string Log = "";

        /// <summary>Stardust conversion: the meta currency earned by this run.</summary>
        public int ComputeStardust()
        {
            float dust = DistanceTraveled * 0.005f + Kills * 2f + (GalaxyReached - 1) * 40f + (Victory ? 150f : 0f);
            return UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(dust));
        }
    }
}
