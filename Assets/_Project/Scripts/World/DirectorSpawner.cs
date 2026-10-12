using Unity.Netcode;
using UnityEngine;
using Voidward.Data;
using Voidward.Networking;

namespace Voidward.World
{
    /// <summary>Spawns the GameDirector network object (RunManager, CombatNet) when a host starts.</summary>
    public static class DirectorSpawner
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var sm = SessionManager.Instance;
            if (sm == null) return;
            sm.ServerStarted -= SpawnDirector;
            sm.ServerStarted += SpawnDirector;
        }

        static void SpawnDirector()
        {
            var nm = NetworkManager.Singleton;
            var cat = ContentCatalog.Instance;
            if (nm == null || !nm.IsServer || cat == null || cat.GameDirector == null) return;
            if (RunManager.Instance != null && RunManager.Instance.IsSpawned) return;
            var go = Object.Instantiate(cat.GameDirector);
            go.GetComponent<NetworkObject>().Spawn(true);
        }
    }
}
