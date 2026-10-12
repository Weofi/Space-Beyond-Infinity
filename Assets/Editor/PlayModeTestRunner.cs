using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Voidward.Networking;

namespace VoidwardDevTest
{
    [InitializeOnLoad]
    internal static class PlayModeTestRunner
    {
        const string StateKey = "PlayModeTest.State";
        const string ResultKey = "PlayModeTest.Result";
        const string ScriptPathKey = "PlayModeTest.ScriptPath";

        static PlayModeTestRunner()
        {
            switch (SessionState.GetString(StateKey, "Idle"))
            {
                case "WaitingForCompile":
                    EditorApplication.delayCall += () =>
                    {
                        SessionState.SetString(StateKey, "EnteringPlayMode");
                        EditorApplication.playModeStateChanged += OnStateChanged;
                        EditorApplication.isPlaying = true;
                    };
                    break;
                case "EnteringPlayMode":
                case "InPlayMode":
                    EditorApplication.playModeStateChanged += OnStateChanged;
                    if (EditorApplication.isPlaying) Begin();
                    break;
                case "Done":
                    EditorApplication.delayCall += SelfDestruct;
                    break;
            }
        }

        static void OnStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnStateChanged;
                Begin();
            }
        }

        static double startTime;
        static bool hostOk;
        static string transport = "?";

        static void Begin()
        {
            SessionState.SetString(StateKey, "InPlayMode");
            startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            var sm = SessionManager.Instance;
            if (sm == null) { Fail("SessionManager.Instance null"); return; }
            Debug.Log("[Test] Calling HostGame (UTP)...");
            sm.HostGame();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - startTime < 3.0) return;
            EditorApplication.update -= Tick;
            var nm = NetworkManager.Singleton;
            hostOk = nm != null && nm.IsHost;
            transport = SessionManager.Instance != null ? SessionManager.Instance.ActiveTransport.ToString() : "?";
            bool prefabMissing = nm == null || nm.NetworkConfig.PlayerPrefab == null;
            string msg = "isHost=" + hostOk + " transport=" + transport + " playerPrefabMissing=" + prefabMissing +
                         " clients=" + (nm != null ? nm.ConnectedClientsIds.Count : -1);
            Debug.Log("[Test] " + msg);
            SessionState.SetString(ResultKey, (hostOk ? "PASS " : "FAIL ") + msg);
            if (nm != null && (nm.IsHost || nm.IsClient)) nm.Shutdown();
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        static void Fail(string why)
        {
            EditorApplication.update -= Tick;
            SessionState.SetString(ResultKey, "FAIL " + why);
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        static void SelfDestruct()
        {
            string p = SessionState.GetString(ScriptPathKey, "");
            if (!string.IsNullOrEmpty(p) && AssetDatabase.AssetPathExists(p)) AssetDatabase.DeleteAsset(p);
            SessionState.EraseString(StateKey);
            SessionState.EraseString(ScriptPathKey);
        }
    }
}
