using UnityEditor;
using Voidward.Dev;

namespace Voidward.EditorTools
{
    /// <summary>Menu entries for the dev test harness (#65): one click from edit mode to a ready test situation.</summary>
    public static class DevMenu
    {
        [MenuItem("Voidward/Dev/Play: Space (pilot)", priority = 100)]
        static void PlaySpace() => DevTest.Play(DevScenario.Space);

        [MenuItem("Voidward/Dev/Play: Planet (on foot)", priority = 101)]
        static void PlayPlanet() => DevTest.Play(DevScenario.Planet);

        [MenuItem("Voidward/Dev/Play: Lab (test walls)", priority = 102)]
        static void PlayLab() => DevTest.Play(DevScenario.Lab);

        [MenuItem("Voidward/Dev/Play: Lab, wait for 2 players", priority = 103)]
        static void PlayLab2() => DevTest.Play(DevScenario.Lab, 2);

        [MenuItem("Voidward/Dev/Play: Space (pilot)", true)]
        [MenuItem("Voidward/Dev/Play: Planet (on foot)", true)]
        [MenuItem("Voidward/Dev/Play: Lab (test walls)", true)]
        [MenuItem("Voidward/Dev/Play: Lab, wait for 2 players", true)]
        static bool CanPlay() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
