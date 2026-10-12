using System;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Combat
{
    /// <summary>
    /// Server-authoritative health for everything damageable: players, ships, enemies, boss parts,
    /// resource nodes. Clients see HP via NetworkVariable; damage only applies on the server
    /// (clients call TakeDamageServerRpc).
    /// Death behaviour is owned by subscribers of Died (despawn, wreck swap, respawn, loot...).
    /// </summary>
    public class HealthComponent : NetworkBehaviour, IDamageable
    {
        [SerializeField] Team team = Team.Players;
        public float MaxHealth = 100f;
        [Tooltip("Damage multiplier when DamageInfo.IsWeakPoint is set (boss cores etc.).")]
        public float WeakPointMultiplier = 2f;
        [Tooltip("If true and CheatManager.GodMode is on, damage is ignored (player pawns).")]
        public bool GodModeEligible;
        [Tooltip("Invulnerable until this is false (e.g. boss hull until weak points destroyed).")]
        public bool Invulnerable;

        public NetworkVariable<float> Hp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Raised on every client when HP changes (newHp, maxHp).</summary>
        public event Action<float, float> HealthChanged;
        /// <summary>Raised on the server right before death handling (loot, stats).</summary>
        public event Action<DamageInfo> Died;
        /// <summary>Raised on every client when damage is applied (for hit feedback).</summary>
        public event Action<DamageInfo> DamagedClient;

        public Team Team => team;
        public bool IsAlive => Hp.Value > 0f;
        GameObject IDamageable.gameObject => gameObject;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Hp.Value = MaxHealth;
            Hp.OnValueChanged += (oldV, newV) => HealthChanged?.Invoke(newV, MaxHealth);
            HealthChanged?.Invoke(Hp.Value, MaxHealth);
        }

        /// <summary>Server-side damage entry (IDamageable). No-op on clients.</summary>
        public void ApplyDamage(DamageInfo info)
        {
            if (!IsServer || !IsAlive || Invulnerable) return;
            if (GodModeEligible && CheatManager.GodMode) return;

            float amount = info.Amount;
            if (info.IsWeakPoint) amount *= WeakPointMultiplier;

            Hp.Value = Mathf.Max(0f, Hp.Value - amount);
            info.Amount = amount;
            DamagedClientRpc(info);

            if (Hp.Value <= 0f)
            {
                Died?.Invoke(info);
                DiedClientRpc();
            }
        }

        /// <summary>Client-side damage request (hitscan weapons, melee). Server validates & applies.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void TakeDamageServerRpc(DamageInfo info)
        {
            ApplyDamage(info);
        }

        /// <summary>Server-side heal (repairs, medkits, stations).</summary>
        public void Heal(float amount)
        {
            if (!IsServer || !IsAlive) return;
            Hp.Value = Mathf.Min(MaxHealth, Hp.Value + amount);
        }

        [ClientRpc]
        void DamagedClientRpc(DamageInfo info)
        {
            DamagedClient?.Invoke(info);
        }

        [ClientRpc]
        void DiedClientRpc()
        {
            // clients get a chance for local feedback (the authoritative logic already ran on server)
        }

        /// <summary>Convenience: damage the IDamageable hit by a raycast hit, from a client.</summary>
        public static void DamageHit(RaycastHit hit, float amount, DamageType type, Vector3 direction, ulong sourceClientId, bool isWeakPoint = false)
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            var info = new DamageInfo
            {
                Amount = amount,
                Type = type,
                Point = hit.point,
                Direction = direction,
                SourceClientId = sourceClientId,
                IsWeakPoint = isWeakPoint
            };
            if (dmg is HealthComponent hc && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
                hc.TakeDamageServerRpc(info);
            else
                dmg.ApplyDamage(info);
        }
    }
}
