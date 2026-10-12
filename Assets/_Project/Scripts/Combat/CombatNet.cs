using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Combat
{
    public enum ProjectileKind : byte { LaserBolt, Missile, Flak, Blaster, EnemyLaser, Spit, Meteor, BossOrb }

    /// <summary>
    /// Server-authoritative projectile system. Projectiles are NOT NetworkObjects: the server
    /// simulates hits, every client simulates an identical cosmetic copy (spawned via one RPC),
    /// and the server reports impacts so visuals end where the authoritative hit happened.
    /// Lives on the GameDirector network object.
    /// </summary>
    public class CombatNet : NetworkBehaviour
    {
        public static CombatNet Instance { get; private set; }

        struct Spec
        {
            public float Speed, Life, Radius, Aoe, Homing, Length, Width;
            public Color Color;
            public string Sfx;
        }

        static Spec GetSpec(ProjectileKind k)
        {
            switch (k)
            {
                case ProjectileKind.LaserBolt: return new Spec { Speed = 260f, Life = 2.2f, Radius = 0.6f, Length = 6f, Width = 0.35f, Color = new Color(0.4f, 3f, 3.2f), Sfx = "laser_ship" };
                case ProjectileKind.Missile: return new Spec { Speed = 95f, Life = 6f, Radius = 0.8f, Aoe = 9f, Homing = 120f, Length = 1.8f, Width = 0.5f, Color = new Color(3f, 1.6f, 0.4f), Sfx = "missile" };
                case ProjectileKind.Flak: return new Spec { Speed = 180f, Life = 1.6f, Radius = 0.5f, Aoe = 8f, Length = 1.2f, Width = 0.5f, Color = new Color(3f, 2.6f, 0.8f), Sfx = "flak" };
                case ProjectileKind.Blaster: return new Spec { Speed = 140f, Life = 1.5f, Radius = 0.15f, Length = 1.4f, Width = 0.12f, Color = new Color(0.6f, 2.5f, 3.2f), Sfx = "laser" };
                case ProjectileKind.EnemyLaser: return new Spec { Speed = 150f, Life = 2.5f, Radius = 0.5f, Length = 4f, Width = 0.3f, Color = new Color(3.2f, 0.5f, 0.9f), Sfx = "laser_ship" };
                case ProjectileKind.Spit: return new Spec { Speed = 32f, Life = 3f, Radius = 0.35f, Aoe = 2.5f, Length = 0.7f, Width = 0.7f, Color = new Color(0.9f, 3f, 0.4f), Sfx = "spit" };
                case ProjectileKind.Meteor: return new Spec { Speed = 45f, Life = 14f, Radius = 3f, Aoe = 14f, Length = 6f, Width = 6f, Color = new Color(2.5f, 1.1f, 0.4f), Sfx = "" };
                case ProjectileKind.BossOrb: return new Spec { Speed = 70f, Life = 6f, Radius = 1.6f, Aoe = 7f, Homing = 25f, Length = 3.2f, Width = 3.2f, Color = new Color(3.2f, 0.4f, 2.2f), Sfx = "boss_shot" };
                default: return new Spec { Speed = 100f, Life = 2f, Radius = 0.3f, Length = 1f, Width = 0.2f, Color = Color.white };
            }
        }

        class Proj
        {
            public int Id;
            public ProjectileKind Kind;
            public Spec Spec;
            public Vector3 Pos, Dir;
            public float Dmg, Age;
            public Team Team;
            public ulong Src;
            public bool ByPlayer;
            public Transform Target;
            public GameObject Visual;
            public TrailRenderer Trail;
        }

        readonly List<Proj> serverProjs = new List<Proj>();
        readonly List<Proj> visualProjs = new List<Proj>();
        readonly Dictionary<int, Proj> visualById = new Dictionary<int, Proj>();
        readonly Queue<GameObject> pool = new Queue<GameObject>();
        readonly Dictionary<int, Material> matCache = new Dictionary<int, Material>();
        readonly RaycastHit[] hitBuf = new RaycastHit[16];
        readonly Collider[] overlapBuf = new Collider[48];
        readonly HashSet<IDamageable> damagedSet = new HashSet<IDamageable>();
        int nextId = 1;
        Transform poolRoot;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            poolRoot = new GameObject("_ProjectilePool").transform;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            foreach (var p in visualProjs) if (p.Visual != null) Destroy(p.Visual);
            visualProjs.Clear();
            visualById.Clear();
            serverProjs.Clear();
            while (pool.Count > 0) { var g = pool.Dequeue(); if (g) Destroy(g); }
            if (poolRoot != null) Destroy(poolRoot.gameObject);
        }

        // ------------------------------------------------------------------ API

        /// <summary>Server: fire an authoritative projectile.</summary>
        public void Fire(ProjectileKind kind, Vector3 pos, Vector3 dir, float dmg, Team team, ulong srcClient, bool byPlayer, NetworkObject homingTarget = null)
        {
            if (!IsServer) return;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            var p = new Proj
            {
                Id = nextId++, Kind = kind, Spec = GetSpec(kind), Pos = pos, Dir = dir, Dmg = dmg,
                Team = team, Src = srcClient, ByPlayer = byPlayer,
                Target = homingTarget != null ? homingTarget.transform : null
            };
            serverProjs.Add(p);
            SpawnVisualClientRpc(p.Id, kind, pos, dir, homingTarget != null ? homingTarget.NetworkObjectId : 0UL);
        }

        /// <summary>Static convenience: safe to call when the system is missing.</summary>
        public static void ServerFire(ProjectileKind kind, Vector3 pos, Vector3 dir, float dmg, Team team, ulong srcClient = 0, bool byPlayer = false, NetworkObject target = null)
        {
            if (Instance != null) Instance.Fire(kind, pos, dir, dmg, team, srcClient, byPlayer, target);
        }

        /// <summary>Client: on-foot weapons request a projectile (server clamps damage).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestFireServerRpc(ProjectileKind kind, Vector3 pos, Vector3 dir, float dmg, RpcParams rp = default)
        {
            if (kind != ProjectileKind.Blaster) return;
            Fire(kind, pos, dir, Mathf.Clamp(dmg, 0f, 60f), Team.Players, rp.Receive.SenderClientId, true);
        }

        /// <summary>Client: show a hitscan tracer to everybody else.</summary>
        public void Tracer(Vector3 from, Vector3 to, Color c)
        {
            DrawTracer(from, to, c);
            if (IsSpawned) TracerServerRpc(from, to, c);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void TracerServerRpc(Vector3 from, Vector3 to, Color c, RpcParams rp = default)
        {
            TracerClientRpc(from, to, c, rp.Receive.SenderClientId);
        }

        [ClientRpc]
        void TracerClientRpc(Vector3 from, Vector3 to, Color c, ulong exclude)
        {
            if (NetworkManager.LocalClientId == exclude) return;
            DrawTracer(from, to, c);
        }

        void DrawTracer(Vector3 from, Vector3 to, Color c)
        {
            var go = GetVisual();
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation((to - from).normalized + Vector3.one * 1e-5f);
            go.transform.localScale = new Vector3(0.05f, 0.05f, Vector3.Distance(from, to));
            go.GetComponent<MeshRenderer>().sharedMaterial = MatFor(c);
            StartCoroutine(ReturnLater(go, 0.05f));
        }

        System.Collections.IEnumerator ReturnLater(GameObject go, float t)
        {
            yield return new WaitForSeconds(t);
            ReturnVisual(go);
        }

        // ------------------------------------------------------------ Simulation

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (IsServer) SimulateServer(dt);
        }

        void Update()
        {
            SimulateVisuals(Time.deltaTime);
        }

        void SimulateServer(float dt)
        {
            for (int i = serverProjs.Count - 1; i >= 0; i--)
            {
                var p = serverProjs[i];
                p.Age += dt;
                Steer(p, dt);
                float step = p.Spec.Speed * dt;

                bool done = false;
                Vector3 impact = p.Pos + p.Dir * step;

                // proximity fuse (flak)
                if (p.Kind == ProjectileKind.Flak && p.Age > 0.15f && EnemyNear(p.Pos, 6f, p.Team))
                {
                    impact = p.Pos;
                    done = true;
                }

                if (!done)
                {
                    int n = Physics.SphereCastNonAlloc(p.Pos, p.Spec.Radius, p.Dir, hitBuf, step, ~0, QueryTriggerInteraction.Collide);
                    float best = float.MaxValue;
                    Collider bestCol = null;
                    for (int h = 0; h < n; h++)
                    {
                        var hit = hitBuf[h];
                        var dmg = hit.collider.GetComponentInParent<IDamageable>();
                        if (hit.collider.isTrigger && dmg == null) continue;
                        if (dmg != null && dmg.Team == p.Team) continue;
                        if (hit.distance < best)
                        {
                            best = hit.distance;
                            bestCol = hit.collider;
                            impact = hit.distance > 0f ? hit.point : p.Pos;
                        }
                    }
                    if (bestCol != null)
                    {
                        done = true;
                        if (p.Spec.Aoe <= 0f)
                            DamageCollider(bestCol, impact, p);
                    }
                }

                if (!done && p.Age >= p.Spec.Life)
                {
                    done = true;
                    impact = p.Pos;
                    if (p.Spec.Aoe <= 0f) { serverProjs.RemoveAt(i); continue; } // fizzles silently on all peers
                }

                if (done)
                {
                    if (p.Spec.Aoe > 0f) Explode(impact, p);
                    serverProjs.RemoveAt(i);
                    ImpactClientRpc(p.Id, impact, p.Kind);
                }
                else p.Pos += p.Dir * step;
            }
        }

        void Steer(Proj p, float dt)
        {
            if (p.Spec.Homing <= 0f || p.Target == null || p.Age < 0.2f) return;
            Vector3 to = (p.Target.position - p.Pos).normalized;
            p.Dir = Vector3.RotateTowards(p.Dir, to, p.Spec.Homing * Mathf.Deg2Rad * dt, 0f).normalized;
        }

        bool EnemyNear(Vector3 pos, float r, Team team)
        {
            int n = Physics.OverlapSphereNonAlloc(pos, r, overlapBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var d = overlapBuf[i].GetComponentInParent<IDamageable>();
                if (d != null && d.Team != team && d.Team != Team.Neutral && d.IsAlive) return true;
            }
            return false;
        }

        void DamageCollider(Collider c, Vector3 point, Proj p)
        {
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || !d.IsAlive || d.Team == p.Team) return;
            var weak = c.GetComponentInParent<HealthComponent>();
            d.ApplyDamage(new DamageInfo
            {
                Amount = p.Dmg, Type = TypeOf(p.Kind), Point = point, Direction = p.Dir,
                SourceClientId = p.Src, ByPlayer = p.ByPlayer, IsWeakPoint = weak != null && weak.IsWeakPointPart
            });
        }

        void Explode(Vector3 pos, Proj p)
        {
            damagedSet.Clear();
            int n = Physics.OverlapSphereNonAlloc(pos, p.Spec.Aoe, overlapBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var d = overlapBuf[i].GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || d.Team == p.Team || !damagedSet.Add(d)) continue;
                float dist = Vector3.Distance(pos, overlapBuf[i].ClosestPoint(pos));
                float falloff = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(dist / p.Spec.Aoe));
                d.ApplyDamage(new DamageInfo
                {
                    Amount = p.Dmg * falloff, Type = TypeOf(p.Kind), Point = pos, Direction = p.Dir,
                    SourceClientId = p.Src, ByPlayer = p.ByPlayer
                });
            }
        }

        static DamageType TypeOf(ProjectileKind k)
        {
            switch (k)
            {
                case ProjectileKind.Missile:
                case ProjectileKind.Flak:
                case ProjectileKind.Meteor: return DamageType.Explosive;
                case ProjectileKind.Spit: return DamageType.Environmental;
                default: return DamageType.Energy;
            }
        }

        // --------------------------------------------------------------- Visuals

        [ClientRpc]
        void SpawnVisualClientRpc(int id, ProjectileKind kind, Vector3 pos, Vector3 dir, ulong targetId)
        {
            var spec = GetSpec(kind);
            var p = new Proj { Id = id, Kind = kind, Spec = spec, Pos = pos, Dir = dir };
            if (targetId != 0 && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var no))
                p.Target = no.transform;
            p.Visual = GetVisual();
            p.Visual.transform.localScale = new Vector3(spec.Width, spec.Width, spec.Length);
            p.Visual.GetComponent<MeshRenderer>().sharedMaterial = MatFor(spec.Color);
            p.Trail = p.Visual.GetComponent<TrailRenderer>();
            bool trail = kind == ProjectileKind.Missile || kind == ProjectileKind.Meteor || kind == ProjectileKind.BossOrb;
            p.Trail.Clear();
            p.Trail.emitting = trail;
            p.Trail.enabled = trail;
            if (trail)
            {
                p.Trail.widthMultiplier = spec.Width * 0.8f;
                p.Trail.sharedMaterial = MatFor(spec.Color * 0.6f);
            }
            PlaceVisual(p);
            visualProjs.Add(p);
            visualById[id] = p;
            if (!string.IsNullOrEmpty(spec.Sfx)) Sfx.Play(spec.Sfx, pos, 0.8f, Random.Range(0.92f, 1.08f));
            if (kind != ProjectileKind.Meteor) Vfx.MuzzleFlash(pos, dir);
        }

        [ClientRpc]
        void ImpactClientRpc(int id, Vector3 point, ProjectileKind kind)
        {
            if (visualById.TryGetValue(id, out var p))
            {
                RemoveVisual(p);
            }
            var spec = GetSpec(kind);
            if (spec.Aoe > 0f)
            {
                Vfx.Explosion(point, spec.Aoe * 0.6f, spec.Color * 0.5f);
                Sfx.Play(spec.Aoe > 8f ? "explosion_big" : "explosion", point, 1f, Random.Range(0.9f, 1.1f));
            }
            else
            {
                Vfx.LaserImpact(point);
                Sfx.Play("hit", point, 0.6f, Random.Range(0.9f, 1.2f));
            }
        }

        void SimulateVisuals(float dt)
        {
            for (int i = visualProjs.Count - 1; i >= 0; i--)
            {
                var p = visualProjs[i];
                p.Age += dt;
                Steer(p, dt);
                p.Pos += p.Dir * (p.Spec.Speed * dt);
                // safety lifetime (server impact normally ends it first)
                if (p.Age > p.Spec.Life + 0.5f) { RemoveVisual(p); continue; }
                PlaceVisual(p);
            }
        }

        void PlaceVisual(Proj p)
        {
            if (p.Visual == null) return;
            p.Visual.transform.SetPositionAndRotation(p.Pos, Quaternion.LookRotation(p.Dir));
        }

        void RemoveVisual(Proj p)
        {
            visualProjs.Remove(p);
            visualById.Remove(p.Id);
            if (p.Visual != null) ReturnVisual(p.Visual);
            p.Visual = null;
        }

        GameObject GetVisual()
        {
            GameObject go;
            if (pool.Count > 0) go = pool.Dequeue();
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Projectile";
                Destroy(go.GetComponent<Collider>());
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.6f;
                tr.minVertexDistance = 0.5f;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
                tr.enabled = false;
                go.transform.SetParent(poolRoot, false);
            }
            go.SetActive(true);
            return go;
        }

        void ReturnVisual(GameObject go)
        {
            if (go == null) return;
            var tr = go.GetComponent<TrailRenderer>();
            if (tr != null) { tr.Clear(); tr.enabled = false; }
            go.SetActive(false);
            pool.Enqueue(go);
        }

        Material MatFor(Color c)
        {
            int key = (Mathf.RoundToInt(c.r * 20) << 20) ^ (Mathf.RoundToInt(c.g * 20) << 10) ^ Mathf.RoundToInt(c.b * 20);
            if (matCache.TryGetValue(key, out var m) && m != null) return m;
            var cat = ContentCatalog.Instance;
            var src = cat != null ? cat.UnlitMaterial : null;
            m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            m.color = c;
            matCache[key] = m;
            return m;
        }
    }
}
