using System;

namespace Voidward.Core
{
    /// <summary>Stats collected during the journey, shown on the homecoming screen and converted to Memory fragments.</summary>
    [Serializable]
    public class RunSummary
    {
        public bool Victory;
        public string EndReason = "";
        public int GalaxyReached = 1;
        public float DistanceTraveled;
        public int Kills;
        public int LootCollected;
        public int MemoriesEarned;
        public float RunTimeSeconds;
        /// <summary>Warp core tier at the end (1-6).</summary>
        public int DriveTier = 1;
        /// <summary>Home ETA in flight minutes from where the run ended (0 after the homecoming).</summary>
        public float HomeEtaMinutes;
        /// <summary>Crew logbook of this journey, one "[h:mm:ss] text" entry per line.</summary>
        public string Log = "";

        /// <summary>Memory fragments: the meta currency earned by this run.</summary>
        public int ComputeMemories()
        {
            float dust = DistanceTraveled * 0.005f + Kills * 2f + (GalaxyReached - 1) * 40f + (Victory ? 150f : 0f);
            return UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(dust));
        }
    }
}
