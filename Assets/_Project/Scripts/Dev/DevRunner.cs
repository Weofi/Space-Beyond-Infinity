using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Dev
{
    /// <summary>
    /// Hidden helper object of the dev test harness (#65): runs DevTest coroutines and the dev command-line flags.
    /// Created automatically in the editor and in development builds, never in release builds.
    /// <list type="bullet">
    /// <item>-skipintro: skip the run intro as soon as it starts (MP test clients get stuck in it otherwise).</item>
    /// <item>-devscenario=space|planet|lab: DevTest.QuickStart(scenario) on launch (host with -autohost and -players=N,
    /// client with -autojoin).</item>
    /// <item>-devshots=N: grab the screen every N seconds while playing (%TEMP%/VoidwardDev/client&lt;id&gt;_&lt;t&gt;.jpg).</item>
    /// </list>
    /// In the editor, DevTest.Play(scenario) survives the play-mode domain reload through SessionState.
    /// </summary>
    public class DevRunner : MonoBehaviour
    {
        static DevRunner instance;

        public static bool SkipIntro { get; private set; }
        public static float AutoShotInterval { get; private set; }
        static string cliScenario;
        static int cliPlayers = 1;
        float nextAutoShot;

        public static DevRunner Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("_DevRunner") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevRunner>();
            return instance;
        }

        public void Run(IEnumerator routine) => StartCoroutine(routine);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!DevTest.Enabled) return;
            ParseArgs();
            string pending = null;
            int players = cliPlayers;
#if UNITY_EDITOR
            pending = UnityEditor.SessionState.GetString(DevTest.PendingKey, "");
            UnityEditor.SessionState.EraseString(DevTest.PendingKey);
            if (!string.IsNullOrEmpty(pending)) players = UnityEditor.SessionState.GetInt(DevTest.PendingPlayersKey, 1);
#endif
            if (string.IsNullOrEmpty(pending)) pending = cliScenario;
            if (!string.IsNullOrEmpty(pending) || SkipIntro || AutoShotInterval > 0f) Get();
            if (!string.IsNullOrEmpty(pending) && Enum.TryParse(pending, true, out DevScenario s))
                instance.StartCoroutine(StartLater(s, players));
        }

        static IEnumerator StartLater(DevScenario s, int players)
        {
            yield return null; // let the scene's services (SessionManager, UI) finish Awake/Start
            yield return null;
            DevTest.QuickStart(s, players);
        }

        static void ParseArgs()
        {
            SkipIntro = false; AutoShotInterval = 0f; cliScenario = null; cliPlayers = 1;
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a.Equals("-skipintro", StringComparison.OrdinalIgnoreCase)) SkipIntro = true;
                else if (a.StartsWith("-devscenario=", StringComparison.OrdinalIgnoreCase)) cliScenario = a.Substring("-devscenario=".Length);
                else if (a.StartsWith("-players=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring("-players=".Length), out int pl)) cliPlayers = pl;
                else if (a.StartsWith("-devshots=", StringComparison.OrdinalIgnoreCase) &&
                         float.TryParse(a.Substring("-devshots=".Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n))
                    AutoShotInterval = Mathf.Max(0.5f, n);
            }
        }

        void OnEnable() { EventBus.SystemEntered += OnSystem; }
        void OnDisable() { EventBus.SystemEntered -= OnSystem; }

        void OnSystem(int galaxy, int system)
        {
            if (DevTest.LabRoot != null) Destroy(DevTest.LabRoot.gameObject);
        }

        void Update()
        {
            if (SkipIntro && GameState.Cinematic && Voidward.UI.UIRoot.Instance != null) Voidward.UI.UIRoot.Instance.SkipIntro();
            if (AutoShotInterval > 0f && GameState.Phase == GamePhase.Playing && Time.unscaledTime >= nextAutoShot)
            {
                nextAutoShot = Time.unscaledTime + AutoShotInterval;
                var nm = NetworkManager.Singleton;
                ulong id = nm != null ? nm.LocalClientId : 0;
                string name = "client" + id + "_" + Mathf.RoundToInt(Time.unscaledTime);
                // With a lab, shoot the lab (that is what MP effect tests look at), otherwise the player's screen.
                if (DevTest.LabRoot != null) { DevShots.Shot(name, DevTest.LabCam); DevTest.Log(name + " decals " + DevTest.DecalCount); }
                else DevTest.Screen(name);
                // Replicated run state for host/client comparisons (#78: the crew warp drive).
                if (Voidward.World.RunManager.Instance != null) DevTest.Log($"{name} galaxy {GameState.GalaxyIndex} drive {DevTest.DriveInfo}");
            }
        }
    }
}
