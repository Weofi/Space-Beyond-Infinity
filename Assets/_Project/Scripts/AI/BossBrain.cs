using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.AI
{
    /// <summary>
    /// Final boss "The Warden" — a pirate mothership grown into a living hulk. Three phases:
    /// 1) hull shielded, destroy the 3 glowing core pods (weak points);
    /// 2) hull exposed, drone waves + orb barrages;
    /// 3) below 40% hull: enraged, faster fire and flak walls.
    /// HP scales with player count.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class BossBrain : NetworkBehaviour, IThreatSource
    {
        public HealthComponent[] Pods = new HealthComponent[0];
        public Transform[] Muzzles = new Transform[0];
        public Transform ShieldVisual;

        public NetworkVariable<int> Phase = new NetworkVariable<int>(1);

        public static BossBrain Instance { get; private set; }
        /// <summary>Server: boss destroyed.</summary>
        public static event Action ServerDefeated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ServerDefeated = null; Instance = null; }

        HealthComponent hull;
        Vector3 anchor;
        float orbTimer = 3f, fanTimer = 6f, droneTimer = 10f, flakTimer = 4f;
        int muzzleIdx;
        float playerScale = 1f;

        void Awake() => hull = GetComponent<HealthComponent>();

        // Dynamic music (#43): the boss layer plays while the boss is alive and the listener is in the arena.
        Vector3 IThreatSource.Position => transform.position;
        float IThreatSource.Engagement => hull != null && hull.IsAlive ? 1f : 0f;
        float IThreatSource.ThreatRadius => 1600f;
        bool IThreatSource.IsBoss => hull != null && hull.IsAlive;

        /// <summary>Server: before Spawn().</summary>
        public void Configure(int players, float tier)
        {
            playerScale = 0.55f + 0.45f * Mathf.Max(1, players);
            hull.MaxHealth = 2600f * playerScale * tier;
            foreach (var p in Pods) if (p) p.MaxHealth = 380f * playerScale * tier;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Threat.Register(this);
            Phase.OnValueChanged += OnPhaseChanged;
            foreach (var p in Pods) if (p) p.HealthChanged += (hp, max) => { if (hp <= 0f) HidePod(p); };
            if (IsServer)
            {
                anchor = transform.position;
                hull.Invulnerable = true;
                hull.Died += OnDied;
            }
            RefreshShield();
            EventBus.RaiseBossSpawned();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            Threat.Unregister(this);
            Phase.OnValueChanged -= OnPhaseChanged;
            if (IsServer) hull.Died -= OnDied;
        }

        void OnPhaseChanged(int a, int b)
        {
            RefreshShield();
            if (b == 2) { EventBus.RaiseNotification("The Warden's shield is down - hit the hull!"); Sfx.PlayUI("boss_roar"); }
            if (b == 3) { EventBus.RaiseNotification("The Warden is enraged!"); Sfx.PlayUI("boss_roar"); Vfx.Shake(0.5f); }
        }

        void RefreshShield()
        {
            if (ShieldVisual) ShieldVisual.gameObject.SetActive(Phase.Value == 1);
        }

        static void HidePod(HealthComponent p)
        {
            foreach (var r in p.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var c in p.GetComponentsInChildren<Collider>()) c.enabled = false;
            Vfx.Explosion(p.transform.position, 8f, new Color(1f, 0.4f, 0.9f));
            Sfx.Play("explosion_big", p.transform.position);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !hull.IsAlive) return;
            float dt = Time.deltaTime;

            // Phase logic
            if (Phase.Value == 1)
            {
                bool anyPod = false;
                foreach (var p in Pods) if (p && p.IsAlive) anyPod = true;
                if (!anyPod) { Phase.Value = 2; hull.Invulnerable = false; }
            }
            else if (Phase.Value == 2 && hull.Fraction < 0.4f) Phase.Value = 3;

            var target = HealthComponent.NearestOfTeam(Team.Players, transform.position, 1400f);
            float rage = Phase.Value == 3 ? 1.7f : 1f;

            // Movement: keep a stand-off distance, drift slowly.
            if (target != null)
            {
                Vector3 to = target.transform.position - transform.position;
                float keep = Phase.Value == 3 ? 160f : 260f;
                Vector3 desired = target.transform.position - to.normalized * keep;
                desired = Vector3.Lerp(anchor, desired, 0.5f);
                transform.position = Vector3.MoveTowards(transform.position, desired, 12f * rage * dt);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 12f * dt);
            }
            if (target == null) return;

            orbTimer -= dt * rage;
            if (orbTimer <= 0f)
            {
                orbTimer = 1.6f;
                var m = NextMuzzle();
                CombatNet.ServerFire(ProjectileKind.BossOrb, m.position, target.transform.position - m.position, 22f, Team.Pirates, 0, false, target.NetworkObject);
            }

            fanTimer -= dt * rage;
            if (fanTimer <= 0f)
            {
                fanTimer = 7f;
                var m = NextMuzzle();
                Vector3 baseDir = (target.transform.position - m.position).normalized;
                for (int i = -4; i <= 4; i++)
                {
                    Vector3 d = Quaternion.AngleAxis(i * 4f, transform.up) * baseDir;
                    CombatNet.ServerFire(ProjectileKind.EnemyLaser, m.position, d, 14f, Team.Pirates);
                }
            }

            if (Phase.Value >= 2)
            {
                droneTimer -= dt;
                if (droneTimer <= 0f)
                {
                    droneTimer = Phase.Value == 3 ? 14f : 20f;
                    int alive = EnemyFactory.CountWhere(e => e.Kind == EnemyKind.DroneSwarm);
                    for (int i = 0; i < 3 && alive + i < 8; i++)
                        EnemyFactory.Spawn(EnemyKind.DroneSwarm, NextMuzzle().position + UnityEngine.Random.insideUnitSphere * 10f, transform.rotation, 1.6f);
                }
            }
            if (Phase.Value == 3)
            {
                flakTimer -= dt;
                if (flakTimer <= 0f)
                {
                    flakTimer = 0.5f;
                    var m = NextMuzzle();
                    Vector3 d = (target.transform.position - m.position).normalized + UnityEngine.Random.insideUnitSphere * 0.12f;
                    CombatNet.ServerFire(ProjectileKind.Flak, m.position, d, 16f, Team.Pirates);
                }
            }
        }

        Transform NextMuzzle()
        {
            if (Muzzles == null || Muzzles.Length == 0) return transform;
            muzzleIdx = (muzzleIdx + 1) % Muzzles.Length;
            return Muzzles[muzzleIdx] != null ? Muzzles[muzzleIdx] : transform;
        }

        void OnDied(DamageInfo info)
        {
            DeathClientRpc(transform.position);
            ServerDefeated?.Invoke();
            NetworkObject.Despawn(true);
        }

        [ClientRpc]
        void DeathClientRpc(Vector3 p)
        {
            for (int i = 0; i < 6; i++)
                Vfx.Explosion(p + UnityEngine.Random.insideUnitSphere * 40f, 25f, new Color(1f, 0.5f, 0.9f));
            Sfx.Play("explosion_big", p, 1f, 0.6f);
            Vfx.Shake(1f);
            EventBus.RaiseBossDefeated();
        }
    }
}
