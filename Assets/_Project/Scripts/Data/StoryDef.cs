using UnityEngine;

namespace Voidward.Data
{
    /// <summary>
    /// Story texts (gameplan #72): the crew logbook templates. Values come from ContentBuilder.BuildStory; code only
    /// picks and formats them. Placeholders: {galaxy}, {eta}, {part}, {ship}. TMP: ASCII only.
    /// (Until #145 this also held the time loop's deja-vu radio, echoes and reset banners; the board computer's lines,
    /// #140, will live here.)
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Story", fileName = "Story")]
    public class StoryDef : ScriptableObject
    {
        [Header("Crew logbook")]
        public string LogJourneyStart = "Woke at the wormhole exit in {galaxy}. Home ETA {eta}.";
        public string LogGalaxy = "Reached {galaxy}. Home ETA {eta}.";
        public string LogWarden = "Wardens of {galaxy} defeated.";
        public string LogDrive = "Installed {part}. Home ETA {eta}.";
        public string LogShipLost = "Lost the {ship}.";
        public string LogHomeLost = "The {ship} was disabled in {galaxy}. No warp until it is repaired.";
        public string LogFlightHome = "Left {galaxy} for home.";
        public string LogLandingFailed = "The landing party was lost. The {ship} is a wreck on the ground.";
        public string LogHomecoming = "Home. Sol is outside the window.";

        public static string Format(string template, string galaxy = "", string eta = "", string part = "", string ship = "") =>
            (template ?? "").Replace("{galaxy}", galaxy).Replace("{eta}", eta).Replace("{part}", part).Replace("{ship}", ship);
    }
}
