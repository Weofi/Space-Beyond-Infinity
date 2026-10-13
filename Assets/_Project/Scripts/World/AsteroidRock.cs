using UnityEngine;
using Voidward.Core;

namespace Voidward.World
{
    /// <summary>
    /// Collider proxy of one asteroid inside a client-local field (built identically on every peer by
    /// SectorVisuals). Hits are forwarded to the server, which tracks rock HP in RunManager and
    /// replicates destroyed rock ids; every peer then removes the rock from its batched chunk.
    /// </summary>
    public class AsteroidRock : MonoBehaviour, IDamageable, ISurface
    {
        public SurfaceKind Surface => SurfaceKind.Rock;

        public int Id;
        public float Radius;

        public Team Team => Team.Neutral;
        public bool IsAlive => isActiveAndEnabled;

        public void ApplyDamage(DamageInfo info)
        {
            var rm = RunManager.Instance;
            if (rm == null || !rm.IsSpawned) return;
            if (rm.IsServer) rm.ServerDamageAsteroid(Id, info);
            else rm.DamageAsteroidServerRpc(Id, Mathf.Clamp(info.Amount, 0f, 200f), info.Point);
        }
    }
}
