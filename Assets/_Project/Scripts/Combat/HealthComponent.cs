using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Combat
{
    /// <summary>
    /// Server-authoritative health for everything damageable: players, ships, enemies, boss parts,
    /// nests. Clients see HP via NetworkVariable; damage only applies on the server
    /// (clients call TakeDamageServerRpc). Death behaviour is owned by subscribers of Died.
    /// </summary>
    public class HealthComponent : NetworkBehaviour, IDamageable
    {
        [SerializeField] Team team = Team.Players;
        public float MaxHealth = 100f;
        [Tooltip("Damage multiplier when DamageInfo.IsWeakPoint is set (boss cores etc.).")]
        public float WeakPointMultiplier = 2f;
        [Tooltip("If true and CheatManager.GodMode is on, damage is ignored (player pawns, player ships).")]
        public bool GodModeEligible;
        [Tooltip("Invulnerable until this is false (e.g. boss hull until weak points destroyed).")]
        [UnityEngine.Serialization.FormerlySerializedAs("Invulnerable")]
        [SerializeField] bool invulnerable;
        [Tooltip("Hint shown to a player whose shot hits this while invulnerable (empty = none).")]
        public string ImmuneHint = "";
        [Tooltip("Incoming damage multiplier (armor). 1 = no reduction.")]
        public float DamageTakenMult = 1f;
        [Tooltip("Mark this part as a weak point (always counts as IsWeakPoint).")]
        public bool IsWeakPointPart;

        public NetworkVariable<float> Hp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> MaxHp = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<bool> invulnerableNet = new NetworkVariable<bool>(false);

        /// <summary>Damage is ignored while set. Server writes, every peer reads (aim assist, HUD).</summary>
        public bool Invulnerable
        {
            get => IsSpawned ? invulnerableNet.Value : invulnerable;
            set { invulnerable = value; if (IsSpawned && IsServer) invulnerableNet.Value = value; }
        }

        /// <summary>Alive and currently damageable: what aim assist and homing should pick.</summary>
        public bool IsTargetable => IsAlive && !Invulnerable;

        /// <summary>Server-side hook (shields): returns the damage amount that reaches the hull.</summary>
        public Func<DamageInfo, float> DamageFilter;

        /// <summary>Raised on every client when HP changes (newHp, maxHp).</summary>
        public event Action<float, float> HealthChanged;
        /// <summary>Raised on the server right before death handling (loot, stats).</summary>
        public event Action<DamageInfo> Died;
        /// <summary>Raised on every client when damage is applied (for hit feedback).</summary>
        public event Action<DamageInfo> DamagedClient;

        public Team Team => team;
        public bool IsAlive => Hp.Value > 0f;
        public float Fraction => MaxHp.Value > 0f ? Hp.Value / MaxHp.Value : 0f;
        GameObject IDamageable.gameObject => gameObject;
        float nextImmuneFx;
        static float nextImmuneHint;

        /// <summary>Every client: any component took damage (HUD hit markers).</summary>
        public static event Action<HealthComponent, DamageInfo> AnyDamagedClient;
        /// <summary>Every client: a shot was absorbed by an invulnerable component (point, shooter).</summary>
        public static event Action<HealthComponent, Vector3, ulong> AnyImmuneClient;

        /// <summary>All spawned health components (every client) — used for targeting.</summary>
        public static readonly List<HealthComponent> All = new List<HealthComponent>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); AnyDamagedClient = null; AnyImmuneClient = null; }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                MaxHp.Value = MaxHealth;
                Hp.Value = MaxHealth;
                invulnerableNet.Value = invulnerable;
            }
            Hp.OnValueChanged += OnHpChanged;
            HealthChanged?.Invoke(Hp.Value, MaxHp.Value);
            All.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            Hp.OnValueChanged -= OnHpChanged;
            All.Remove(this);
        }

        void OnHpChanged(float oldV, float newV) => HealthChanged?.Invoke(newV, MaxHp.Value);

        /// <summary>Server: change max HP (modules, suit upgrades). Keeps the current fraction when raising.</summary>
        public void SetMaxHealthServer(float max, bool fill)
        {
            if (!IsServer) return;
            float frac = Fraction;
            MaxHealth = max;
            MaxHp.Value = max;
            Hp.Value = fill ? max : Mathf.Clamp(max * frac, 1f, max);
        }

        /// <summary>Server-side damage entry (IDamageable). No-op on clients.</summary>
        public void ApplyDamage(DamageInfo info)
        {
            if (!IsServer || !IsSpawned || !IsAlive) return;
            if (Invulnerable)
            {
                // Tell the shooter why nothing happens (throttled): shield flash + hint.
                if (info.ByPlayer && Time.time >= nextImmuneFx) { nextImmuneFx = Time.time + 0.2f; ImmuneClientRpc(info.Point, info.SourceClientId); }
                return;
            }
            if (GodModeEligible && CheatManager.GodMode) return;

            float amount = info.Amount * DamageTakenMult;
            if (info.IsWeakPoint || IsWeakPointPart) { amount *= WeakPointMultiplier; info.IsWeakPoint = true; }
            if (DamageFilter != null) amount = DamageFilter(new DamageInfo
            {
                Amount = amount, Type = info.Type, Point = info.Point, Direction = info.Direction,
                SourceClientId = info.SourceClientId, IsWeakPoint = info.IsWeakPoint, ByPlayer = info.ByPlayer
            });
            info.Amount = amount;
            if (amount > 0f)
                Hp.Value = Mathf.Max(0f, Hp.Value - amount);
            DamagedClientRpc(info);

            if (Hp.Value <= 0f)
            {
                Died?.Invoke(info);
                CombatEvents.RaiseDied(this, info);
            }
        }

        /// <summary>Client-side damage request (hitscan weapons, melee). Server validates & applies.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeDamageServerRpc(DamageInfo info, RpcParams p = default)
        {
            info.SourceClientId = p.Receive.SenderClientId;
            info.ByPlayer = true;
            info.Amount = Mathf.Clamp(info.Amount, 0f, 200f);
            ApplyDamage(info);
        }

        /// <summary>Server-side heal (repairs, medkits, stations).</summary>
        public void Heal(float amount)
        {
            if (!IsServer || !IsAlive) return;
            Hp.Value = Mathf.Min(MaxHp.Value, Hp.Value + amount);
        }

        /// <summary>Server: bring back to full health (respawn).</summary>
        public void ReviveServer()
        {
            if (!IsServer) return;
            Hp.Value = MaxHp.Value;
        }

        [ClientRpc]
        void ImmuneClientRpc(Vector3 point, ulong shooter)
        {
            Vfx.ShieldHit(point);
            Sfx.Play("shield_hit", point, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f));
            AnyImmuneClient?.Invoke(this, point, shooter);
            if (shooter != NetworkManager.LocalClientId || string.IsNullOrEmpty(ImmuneHint) || Time.time < nextImmuneHint) return;
            nextImmuneHint = Time.time + 6f;
            EventBus.RaiseNotification(ImmuneHint);
            Sfx.PlayUI("ui_back");
        }

        [ClientRpc]
        void DamagedClientRpc(DamageInfo info)
        {
            DamagedClient?.Invoke(info);
            AnyDamagedClient?.Invoke(this, info);
        }

        /// <summary>Damage whatever IDamageable owns the hit collider, from any peer.</summary>
        public static void DamageHit(Collider col, Vector3 point, float amount, DamageType type, Vector3 direction, ulong sourceClientId, bool byPlayer)
        {
            var dmg = col.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            var info = new DamageInfo
            {
                Amount = amount, Type = type, Point = point, Direction = direction,
                SourceClientId = sourceClientId, ByPlayer = byPlayer
            };
            if (dmg is HealthComponent hc && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
                hc.TakeDamageServerRpc(info);
            else
                dmg.ApplyDamage(info);
        }

        /// <summary>Nearest living component of a team within range (server or client).</summary>
        public static HealthComponent NearestOfTeam(Team t, Vector3 pos, float range, Func<HealthComponent, bool> filter = null)
        {
            HealthComponent best = null;
            float bestSqr = range * range;
            for (int i = 0; i < All.Count; i++)
            {
                var h = All[i];
                if (h == null || h.team != t || !h.IsAlive) continue;
                if (filter != null && !filter(h)) continue;
                float d = (h.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = h; }
            }
            return best;
        }
    }

    /// <summary>Server-side combat events (stats, loot, run bookkeeping).</summary>
    public static class CombatEvents
    {
        public static event Action<HealthComponent, DamageInfo> Died;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Died = null;

        public static void RaiseDied(HealthComponent h, DamageInfo info) => Died?.Invoke(h, info);
    }
}
