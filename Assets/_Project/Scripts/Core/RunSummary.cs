using System;

namespace Voidward.Core
{
    /// <summary>Stats collected during a run, shown on Game Over / Demo Complete and converted to Stardust.</summary>
    [Serializable]
    public class RunSummary
    {
        public bool Victory;
        public string EndReason = "";
        public int SectorReached = 1;
        public float DistanceTraveled;
        public int Kills;
        public int LootCollected;
        public int StardustEarned;
        public float RunTimeSeconds;

        /// <summary>Stardust conversion: the meta currency earned by this run.</summary>
        public int ComputeStardust()
        {
            float dust = DistanceTraveled * 0.005f + Kills * 2f + (SectorReached - 1) * 50f + (Victory ? 150f : 0f);
            return UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(dust));
        }
    }
}
