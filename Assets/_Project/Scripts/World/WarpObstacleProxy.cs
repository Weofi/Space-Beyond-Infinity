using UnityEngine;
using Voidward.Core;

namespace Voidward.World
{
    /// <summary>
    /// Hit proxy of one corridor obstacle (#79), built by WarpCorridorView identically on every peer. Shots are
    /// forwarded to the server (WarpFlight tracks the damage and replicates destroyed ids), like AsteroidRock.
    /// </summary>
    public class WarpObstacleProxy : MonoBehaviour, IDamageable, ISurface
    {
        public int Id;
        public SurfaceKind Kind = SurfaceKind.Rock;

        public SurfaceKind Surface => Kind;
        public Team Team => Team.Neutral;
        public bool IsAlive => isActiveAndEnabled;

        public void ApplyDamage(DamageInfo info)
        {
            var f = WarpFlight.Instance;
            if (f == null || !f.IsSpawned) return;
            float amount = info.Type == DamageType.Mining ? info.Amount * 2f : info.Amount;
            if (f.IsServer) f.ServerDamageObstacle(Id, amount);
            else f.DamageObstacleServerRpc(Id, amount);
        }
    }
}
