using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;

namespace Voidward.AI
{
    /// <summary>Server-side helper to spawn configured enemies.</summary>
    public static class EnemyFactory
    {
        /// <summary>Server-side count of alive enemies (for caps).</summary>
        public static readonly List<EnemyBrain> Alive = new List<EnemyBrain>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Alive.Clear();

        public static EnemyBrain Spawn(EnemyKind kind, Vector3 pos, Quaternion rot, float tier, float hpScale = 1f)
        {
            var nm = NetworkManager.Singleton;
            var prefab = ContentCatalog.GetEnemyPrefab(kind);
            if (nm == null || !nm.IsServer || prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, rot);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.Configure(tier, hpScale);
            go.GetComponent<NetworkObject>().Spawn(true);
            if (brain != null)
            {
                Alive.Add(brain);
                brain.GetComponent<HealthComponent>().Died += _ => Alive.Remove(brain);
            }
            return brain;
        }

        public static int CountWhere(System.Func<EnemyBrain, bool> f)
        {
            int n = 0;
            for (int i = Alive.Count - 1; i >= 0; i--)
            {
                if (Alive[i] == null || !Alive[i].IsSpawned) { Alive.RemoveAt(i); continue; }
                if (f(Alive[i])) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Alien nest on planet surfaces: spawns crawlers/spitters/critters while players are near,
    /// plus one Nest Guardian the first time it is disturbed. Destroy it for crystal loot.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class AlienNest : NetworkBehaviour
    {
        public float TriggerRange = 70f;
        public int MaxAlive = 3;
        public float SpawnInterval = 9f;

        float tier = 1f;
        float timer = 2f;
        bool guardianSpawned;
        readonly List<EnemyBrain> spawned = new List<EnemyBrain>();
        HealthComponent health;

        public void Configure(float t) => tier = t;

        void Awake() => health = GetComponent<HealthComponent>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) health.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) health.Died -= OnDied;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = SpawnInterval;
            var near = HealthComponent.NearestOfTeam(Team.Players, transform.position, TriggerRange);
            if (near == null) return;
            spawned.RemoveAll(e => e == null || !e.IsSpawned);
            if (!guardianSpawned)
            {
                guardianSpawned = true;
                var g = EnemyFactory.Spawn(EnemyKind.NestGuardian, transform.position + transform.forward * 6f + transform.up, transform.rotation, tier);
                if (g != null) spawned.Add(g);
                return;
            }
            if (spawned.Count >= MaxAlive) return;
            float r = Random.value;
            EnemyKind k = r < 0.5f ? EnemyKind.Crawler : r < 0.8f ? EnemyKind.Spitter : EnemyKind.FlyingCritter;
            Vector3 p = transform.position + transform.up * 1.5f + Vector3.ProjectOnPlane(Random.onUnitSphere, transform.up).normalized * 4f;
            var e = EnemyFactory.Spawn(k, p, transform.rotation, tier);
            if (e != null) spawned.Add(e);
            SpawnFxClientRpc(p);
        }

        [ClientRpc]
        void SpawnFxClientRpc(Vector3 p)
        {
            Sfx.Play("nest", p);
            Vfx.PickupSparkle(p, new Color(0.6f, 1f, 0.3f));
        }

        void OnDied(DamageInfo info)
        {
            for (int i = 0; i < 4; i++)
                WorldPickup.SpawnServer(i < 2 ? "crystal" : "ore", Random.Range(2, 4), transform.position + transform.up * 1.5f + Random.insideUnitSphere * 2f);
            if (Random.value < 0.5f) WorldPickup.SpawnServer("exotic", 1 + Random.Range(0, 2), transform.position + transform.up * 2f);
            DeathClientRpc(transform.position, info.ByPlayer);
            NetworkObject.Despawn(true);
        }

        [ClientRpc]
        void DeathClientRpc(Vector3 p, bool byPlayer)
        {
            Vfx.Explosion(p, 5f, new Color(0.6f, 1f, 0.3f));
            Sfx.Play("squish", p, 1f, 0.6f);
            if (byPlayer) EventBus.RaiseNotification("Nest destroyed!");
        }
    }
}
