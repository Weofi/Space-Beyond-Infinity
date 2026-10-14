using System;
using UnityEngine;

namespace Voidward.Data
{
    /// <summary>
    /// Story texts of the time loop (#86, gameplan #72): radio lines of the deja-vu start, radio echoes from earlier
    /// loops, crew logbook entries and the loop reset banners. Values come from ContentBuilder.BuildStory; code only
    /// picks and formats them. Placeholders: {loop}, {galaxy}, {eta}, {part}, {ship}, {crew}. TMP: ASCII only.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Story", fileName = "Story")]
    public class StoryDef : ScriptableObject
    {
        [Serializable]
        public class RadioLine
        {
            public string Speaker;
            [TextArea] public string Text;
        }

        /// <summary>A hint from an earlier loop, played when the crew enters galaxy `Galaxy` (6 = the flight home) again.</summary>
        [Serializable]
        public class Echo
        {
            public int Galaxy;
            [Tooltip("Earliest loop (1-based) this echo can play in.")]
            public int MinLoop = 2;
            public string Speaker;
            [TextArea] public string Text;
        }

        [Header("Deja-vu start (loop 2+)")]
        [Tooltip("Radio of the deja-vu start, one line per loop (the last one repeats). Distorted on screen.")]
        public RadioLine[] DejaVu = new RadioLine[0];

        [Header("Radio echoes")]
        public Echo[] Echoes = new Echo[0];

        [Header("Crew logbook")]
        public string LogLoopStart = "Loop {loop}. Woke at the wormhole exit in {galaxy}. Home ETA {eta}.";
        public string LogGalaxy = "Reached {galaxy}. Home ETA {eta}.";
        public string LogWarden = "Wardens of {galaxy} defeated.";
        public string LogDrive = "Installed {part}. Home ETA {eta}.";
        public string LogShipLost = "Lost the {ship}.";
        public string LogFlightHome = "Left {galaxy} for home. The Rift is waking.";
        public string LogCrewLost = "The whole crew was lost in {galaxy}.";
        public string LogRiftCaught = "The Rift caught us on the flight home.";
        public string LogHomecoming = "Home. Sol is outside the window.";

        [Header("Loop reset")]
        public string ResetCrewLost = "Crew lost";
        public string ResetRiftCaught = "Caught by the Rift";

        /// <summary>The deja-vu radio of a loop (null in loop 1).</summary>
        public RadioLine DejaVuFor(int loop)
        {
            if (loop < 2 || DejaVu.Length == 0) return null;
            return DejaVu[Mathf.Min(loop - 2, DejaVu.Length - 1)];
        }

        /// <summary>The echo for entering `galaxy` in `loop` (rotates through the matching ones per loop), or null.</summary>
        public Echo EchoFor(int galaxy, int loop)
        {
            int n = 0;
            foreach (var e in Echoes) if (e.Galaxy == galaxy && loop >= e.MinLoop) n++;
            if (n == 0) return null;
            int pick = loop % n;
            foreach (var e in Echoes)
                if (e.Galaxy == galaxy && loop >= e.MinLoop && pick-- == 0) return e;
            return null;
        }

        public static string Format(string template, int loop = 0, string galaxy = "", string eta = "", string part = "", string ship = "") =>
            (template ?? "").Replace("{loop}", loop.ToString()).Replace("{galaxy}", galaxy).Replace("{eta}", eta).Replace("{part}", part).Replace("{ship}", ship);
    }
}
