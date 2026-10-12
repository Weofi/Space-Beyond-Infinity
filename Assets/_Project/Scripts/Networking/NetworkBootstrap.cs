using UnityEngine;
using Voidward.Core;

namespace Voidward.Networking
{
    /// <summary>
    /// Creates the always-alive networking objects before the first scene loads:
    /// SessionManager, the temporary dev net menu, and the cheat overlay host.
    /// Idempotent — safe when a scene also contains these objects.
    /// </summary>
    public static class NetworkBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureExists()
        {
            VwInput.Init();

            if (SessionManager.Instance == null)
            {
                var go = new GameObject("_Networking");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<SessionManager>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                go.AddComponent<DevNetMenu>();
#endif
            }

            if (Object.FindAnyObjectByType<CheatManager>() == null)
            {
                var cheats = new GameObject("_Cheats");
                cheats.AddComponent<CheatManager>();
            }
        }
    }
}
