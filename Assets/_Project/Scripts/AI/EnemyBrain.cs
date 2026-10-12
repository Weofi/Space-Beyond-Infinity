using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;

namespace Voidward.AI
{
    /// <summary>
    /// Server-side state-machine AI (Patrol → Chase → Attack → Flee) for all regular enemies.
    /// Two locomotion modes: free 3D flight (space enemies, flying critters) and surface walking
    /// aligned to planet gravity. Transform is synced by a server-authoritative NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class EnemyBrain : NetworkBehaviour
    {
        public enum State { Patrol, Chase, Attack, Flee }

        public EnemyKind Kind = EnemyKind.Crawler;
        public Transform Muzzle;
        [Tooltip("Hover height above the surface (flying critters) or 0 for walkers.")]
        public float HoverHeight;
        [Tooltip("Visual root that gets a little bob / lean.")]
        public Transform Visual;

        public NetworkVariable<State> CurrentState = new NetworkVariable<State>(State.Patrol);

        EnemyDef def;
        HealthComponent health;
        float tier = 1f;
        Vector3 home;
        Vector3 patrolTarget;
        float attackTimer;
        float retargetTimer;
        float fleeTimer;
        float stateTimer;
        HealthComponent target;
        Vector3 velocity;
        GravitySource planet;
        readonly RaycastHit[] groundHits = new RaycastHit[4];

        public bool IsSpaceEnemy => def != null && def.IsSpace;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
        }

        /// <summary>Server: configure stats before Spawn().</summary>
        public void Configure(float tierMult, float hpScale)
        {
            tier = Mathf.Max(0.5f, tierMult);
            def = ContentCatalog.GetEnemy(Kind);
            if (def != null) health.MaxHealth = def.MaxHp * tier * hpScale;
        }

        public override void OnNetworkSpawn()
        {
            if (def == null) def = ContentCatalog.GetEnemy(Kind);
            if (!IsServer) return;
            home = transform.position;
            patrolTarget = home;
            health.Died += OnDied;
            planet = def != null && !def.IsSpace ? GravitySource.Dominant(transform.position) : null;
            attackTimer = Random.Range(0.5f, 2f);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) health.Died -= OnDied;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || def == null || !health.IsAlive) return;
            float dt = Time.deltaTime;
            stateTimer += dt;

            retargetTimer -= dt;
            if (retargetTimer <= 0f)
            {
                retargetTimer = 0.5f + Random.value * 0.3f;
                target = FindTarget();
            }

            // State transitions.
            var st = CurrentState.Value;
            float dist = target != null ? Vector3.Distance(target.transform.position, transform.position) : float.MaxValue;
            if (st != State.Flee && def.FleeThreshold > 0f && health.Fraction < def.FleeThreshold && fleeTimer <= 0f)
            {
                SetState(State.Flee);
                fleeTimer = 6f;
            }
            else if (st == State.Flee)
            {
                fleeTimer -= dt;
                if (fleeTimer <= 0f) SetState(target != null ? State.Chase : State.Patrol);
            }
            else if (target == null) SetState(State.Patrol);
            else if (dist <= def.AttackRange) SetState(State.Attack);
            else SetState(State.Chase);

            switch (CurrentState.Value)
            {
                case State.Patrol: Patrol(dt); break;
                case State.Chase: MoveToward(target.transform.position, def.MoveSpeed, dt); break;
                case State.Attack: AttackTick(dt, dist); break;
                case State.Flee:
                    Vector3 away = target != null ? (transform.position - target.transform.position).normalized : transform.forward;
                    MoveToward(transform.position + away * 50f, def.MoveSpeed * 1.25f, dt);
                    break;
            }
        }

        void SetState(State s)
        {
            if (CurrentState.Value == s) return;
            CurrentState.Value = s;
            stateTimer = 0f;
        }

        HealthComponent FindTarget()
        {
            float range = def.AggroRange * (CurrentState.Value == State.Patrol ? 1f : 1.5f);
            bool space = def.IsSpace;
            return HealthComponent.NearestOfTeam(Team.Players, transform.position, range, h =>
            {
                var tag = h.GetComponent<TargetTag>();
                if (tag == null) return true;
                if (!space && tag.IsShip) return false; // walkers ignore ships
                if (tag.IsPawn && !h.gameObject.activeInHierarchy) return false;
                return true;
            });
        }

        void Patrol(float dt)
        {
            if ((transform.position - patrolTarget).sqrMagnitude < 9f || stateTimer > 8f)
            {
                stateTimer = 0f;
                Vector3 offset = Random.insideUnitSphere * (def.IsSpace ? 80f : 25f);
                patrolTarget = home + offset;
            }
            MoveToward(patrolTarget, def.MoveSpeed * 0.45f, dt);
        }

        void AttackTick(float dt, float dist)
        {
            Vector3 tp = target.transform.position;
            if (def.IsSpace)
            {
                // strafe/orbit around target while firing
                Vector3 to = tp - transform.position;
                Vector3 side = Vector3.Cross(to.normalized, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.1f) side = Vector3.right;
                float keep = def.AttackRange * 0.6f;
                Vector3 desired = tp - to.normalized * keep + side * Mathf.Sin(Time.time * 0.7f + NetworkObjectId) * 25f;
                MoveToward(desired, def.MoveSpeed * 0.8f, dt, tp);
            }
            else if (dist > def.AttackRange * 0.55f || Kind == EnemyKind.Crawler || Kind == EnemyKind.NestGuardian)
                MoveToward(tp, def.MoveSpeed * (dist > 3f ? 1f : 0.2f), dt);
            else
                Face(tp, dt);

            attackTimer -= dt;
            if (attackTimer > 0f) return;
            attackTimer = def.AttackCooldown * Random.Range(0.85f, 1.2f);
            DoAttack(tp, dist);
        }

        void DoAttack(Vector3 tp, float dist)
        {
            float dmg = def.Damage * tier;
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position + transform.up;
            Vector3 lead = tp + Random.insideUnitSphere * Mathf.Clamp(dist * 0.03f, 0.5f, 8f); // imperfect aim
            var trb = target.GetComponent<Rigidbody>();
            switch (Kind)
            {
                case EnemyKind.Crawler:
                case EnemyKind.NestGuardian:
                    if (dist <= def.AttackRange)
                    {
                        target.ApplyDamage(new DamageInfo { Amount = dmg, Type = DamageType.Kinetic, Point = tp, Direction = transform.forward });
                        AttackFxClientRpc(tp, Kind == EnemyKind.NestGuardian);
                    }
                    break;
                case EnemyKind.Spitter:
                    CombatNet.ServerFire(ProjectileKind.Spit, from, (tp + target.transform.up * 0.8f - from), dmg, Team.Aliens);
                    break;
                case EnemyKind.FlyingCritter:
                    CombatNet.ServerFire(ProjectileKind.Spit, from, (tp - from), dmg, Team.Aliens);
                    break;
                case EnemyKind.DroneSwarm:
                    if (trb != null && !trb.isKinematic) lead += trb.linearVelocity * (dist / 150f);
                    CombatNet.ServerFire(ProjectileKind.EnemyLaser, from, lead - from, dmg, Team.Pirates);
                    break;
                case EnemyKind.PirateFighter:
                    if (trb != null && !trb.isKinematic) lead += trb.linearVelocity * (dist / 150f);
                    CombatNet.ServerFire(ProjectileKind.EnemyLaser, from, lead - from, dmg, Team.Pirates);
                    CombatNet.ServerFire(ProjectileKind.EnemyLaser, from + transform.right * 1.5f, lead - from, dmg, Team.Pirates);
                    break;
                case EnemyKind.PirateGunship:
                    CombatNet.ServerFire(ProjectileKind.EnemyLaser, from, lead - from, dmg, Team.Pirates);
                    if (Random.value < 0.4f)
                        CombatNet.ServerFire(ProjectileKind.Missile, from + transform.up * 2f, transform.forward, dmg * 2.2f, Team.Pirates, 0, false, target.NetworkObject);
                    break;
            }
        }

        [ClientRpc]
        void AttackFxClientRpc(Vector3 at, bool heavy)
        {
            Sfx.Play(heavy ? "slam" : "bite", at, 0.9f, Random.Range(0.9f, 1.1f));
            if (heavy) Vfx.Explosion(at, 2f, new Color(0.7f, 1f, 0.4f));
        }

        // ------------------------------------------------------------ Locomotion

        void MoveToward(Vector3 goal, float speed, float dt, Vector3? lookAt = null)
        {
            if (def.IsSpace) MoveSpace(goal, speed, dt, lookAt ?? goal);
            else MoveSurface(goal, speed, dt);
        }

        void MoveSpace(Vector3 goal, float speed, float dt, Vector3 lookAt)
        {
            Vector3 to = goal - transform.position;
            Vector3 desired = to.sqrMagnitude > 1f ? to.normalized * speed : Vector3.zero;
            // simple separation from other enemies
            velocity = Vector3.Lerp(velocity, desired, 1.5f * dt);
            // avoid planets
            var src = GravitySource.Dominant(transform.position);
            if (src != null && src.SurfaceDistance(transform.position) < 60f)
                velocity += src.UpAt(transform.position) * speed * dt * 3f;
            transform.position += velocity * dt;
            Vector3 face = (lookAt - transform.position);
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), def.TurnSpeed * dt);
        }

        void MoveSurface(Vector3 goal, float speed, float dt)
        {
            if (planet == null) planet = GravitySource.Dominant(transform.position);
            if (planet == null) { MoveSpace(goal, speed, dt, goal); return; }
            Vector3 up = planet.UpAt(transform.position);
            Vector3 to = Vector3.ProjectOnPlane(goal - transform.position, up);
            Vector3 dir = to.sqrMagnitude > 0.25f ? to.normalized : Vector3.zero;
            velocity = Vector3.Lerp(velocity, dir * speed, 4f * dt);
            Vector3 p = transform.position + velocity * dt;

            // Stick to the terrain (or hover above it).
            Vector3 newUp = planet.UpAt(p);
            Vector3 probe = p + newUp * 30f;
            int n = Physics.RaycastNonAlloc(probe, -newUp, groundHits, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = p;
            for (int i = 0; i < n; i++)
            {
                if (groundHits[i].collider.transform.IsChildOf(transform)) continue;
                if (groundHits[i].collider.attachedRigidbody != null) continue; // ignore ships/pawns
                if (groundHits[i].distance < best) { best = groundHits[i].distance; ground = groundHits[i].point; }
            }
            // No collider streamed here (far from players): follow the height field instead.
            if (best == float.MaxValue) ground = planet.GroundPoint(p);
            p = ground + newUp * HoverHeight;
            if (HoverHeight > 0f) p += newUp * Mathf.Sin(Time.time * 2f + NetworkObjectId) * 0.6f;
            transform.position = p;

            Vector3 fwd = velocity.sqrMagnitude > 0.05f ? velocity : Vector3.ProjectOnPlane(transform.forward, newUp);
            if (fwd.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(fwd, newUp), def.TurnSpeed * dt);
        }

        void Face(Vector3 tp, float dt)
        {
            Vector3 up = planet != null ? planet.UpAt(transform.position) : transform.up;
            Vector3 f = Vector3.ProjectOnPlane(tp - transform.position, up);
            if (f.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(f, up), def.TurnSpeed * dt);
        }

        // ------------------------------------------------------------ Death

        void OnDied(DamageInfo info)
        {
            if (def != null && def.Loot != null)
            {
                var results = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ItemDef, int>>();
                def.Loot.Roll(new System.Random((int)NetworkObjectId * 31 + (int)(Time.time * 10)), results);
                foreach (var kv in results)
                    WorldPickup.SpawnServer(kv.Key.Id, kv.Value, transform.position + Random.insideUnitSphere * 1.5f);
            }
            if (info.ByPlayer)
            {
                var inv = NetInventory.ForClient(info.SourceClientId);
                if (inv != null) inv.Credits.Value += Mathf.RoundToInt((def != null ? def.StardustKills : 1) * 6 * tier);
            }
            DeathClientRpc(transform.position, def != null && def.IsSpace);
            NetworkObject.Despawn(true);
        }

        [ClientRpc]
        void DeathClientRpc(Vector3 pos, bool space)
        {
            Vfx.Explosion(pos, space ? 6f : 2.5f, space ? new Color(1f, 0.55f, 0.3f) : new Color(0.6f, 1f, 0.4f));
            Sfx.Play(space ? "explosion" : "squish", pos, 1f, Random.Range(0.85f, 1.1f));
        }
    }
}
