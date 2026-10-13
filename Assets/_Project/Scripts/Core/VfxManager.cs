using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Pooled, code-built particle effects and local camera shake / screen flashes. Effects are layered:
    /// explosions = flash core + fireball + stretched spark streaks + shock ring + smoke + lit debris chunks;
    /// impacts (lasers, mining) = sparks + glow + dust. Additive layers share ParticleMaterial, smoke uses an
    /// alpha-blended material, debris renders a small rock mesh with the lit toon material.
    /// </summary>
    public class VfxManager : MonoBehaviour, IVfxService
    {
        public Material ParticleMaterial;
        public Material SmokeMaterial;
        public Material DebrisMaterial;
        public Mesh DebrisMesh;

        /// <summary>UI listens: full-screen flash (color, seconds).</summary>
        public static event Action<Color, float> ScreenFlash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ScreenFlash = null;

        enum Kind { Flash, Fireball, Spark, Shock, Smoke, Debris, Shield, Sparkle, Muzzle }
        readonly Dictionary<Kind, Queue<ParticleSystem>> pools = new Dictionary<Kind, Queue<ParticleSystem>>();
        readonly List<(ParticleSystem ps, Kind k, float until)> active = new List<(ParticleSystem, Kind, float)>();
        bool frozen;

        void Awake()
        {
            if (G.Get<IVfxService>() != null) { Destroy(gameObject); return; }
            G.Register<IVfxService>(this);
            DontDestroyOnLoad(gameObject);
            foreach (Kind k in Enum.GetValues(typeof(Kind))) pools[k] = new Queue<ParticleSystem>();
        }

        void OnDestroy()
        {
            if (ReferenceEquals(G.Get<IVfxService>(), this)) G.Unregister<IVfxService>();
        }

        void Update()
        {
            GameState.ShakeIntensity = Mathf.MoveTowards(GameState.ShakeIntensity, 0f, Time.deltaTime * 1.8f);
            GameState.FovPunch = Mathf.MoveTowards(GameState.FovPunch, 0f, Time.unscaledDeltaTime * 14f);
            // Local hit-stop: freeze the running effects for a few frames (impact frame), never Time.timeScale.
            bool hold = GameState.HitStopActive;
            if (hold != frozen)
            {
                frozen = hold;
                foreach (var e in active) if (e.ps != null) { var m = e.ps.main; m.simulationSpeed = hold ? 0.04f : 1f; }
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (Time.time < active[i].until) continue;
                var e = active[i];
                active.RemoveAt(i);
                if (e.ps == null) continue;
                e.ps.gameObject.SetActive(false);
                pools[e.k].Enqueue(e.ps);
            }
        }

        ParticleSystem Get(Kind k)
        {
            var q = pools[k];
            ParticleSystem ps = q.Count > 0 ? q.Dequeue() : Create(k);
            var mm = ps.main;
            mm.simulationSpeed = frozen ? 0.04f : 1f; // effects spawned during a hit-stop hold with the rest
            ps.gameObject.SetActive(true);
            return ps;
        }

        static Gradient Fade(Color a, Color b, float alphaEnd = 0f, float alphaHold = 0f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, alphaHold), new GradientAlphaKey(alphaEnd, 1f) });
            return g;
        }

        ParticleSystem Create(Kind k)
        {
            var go = new GameObject("vfx_" + k);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(Color.white, Color.white);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            switch (k)
            {
                case Kind.Flash:
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.2f)));
                    col.color = Fade(Color.white, new Color(1f, 0.8f, 0.5f));
                    break;
                case Kind.Fireball:
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f)));
                    col.color = Fade(new Color(1f, 0.95f, 0.8f), new Color(0.6f, 0.15f, 0.05f), 0f, 0.35f);
                    var limit = ps.limitVelocityOverLifetime;
                    limit.enabled = true; limit.dampen = 0.12f; limit.limit = 0f;
                    break;
                case Kind.Spark:
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.06f;
                    r.lengthScale = 1.5f;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                    col.color = Fade(Color.white, new Color(1f, 0.55f, 0.2f), 0f, 0.5f);
                    var dampS = ps.limitVelocityOverLifetime;
                    dampS.enabled = true; dampS.dampen = 0.06f; dampS.limit = 0f;
                    break;
                case Kind.Shock:
                    main.startRotation3D = false;
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f, 0f, 4f), new Keyframe(1f, 1f, 0f, 0f)));
                    col.color = Fade(Color.white, Color.white);
                    break;
                case Kind.Smoke:
                    if (SmokeMaterial != null) r.sharedMaterial = SmokeMaterial;
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.5f)));
                    col.color = Fade(new Color(0.9f, 0.9f, 0.9f), new Color(0.6f, 0.6f, 0.65f), 0f, 0.25f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    var rot = ps.rotationOverLifetime;
                    rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                    var dampSm = ps.limitVelocityOverLifetime;
                    dampSm.enabled = true; dampSm.dampen = 0.08f; dampSm.limit = 0f;
                    r.sortingFudge = 10f;
                    break;
                case Kind.Debris:
                    if (DebrisMesh != null && DebrisMaterial != null)
                    {
                        r.renderMode = ParticleSystemRenderMode.Mesh;
                        r.mesh = DebrisMesh;
                        r.sharedMaterial = DebrisMaterial;
                        r.alignment = ParticleSystemRenderSpace.Local;
                    }
                    main.startRotation3D = true;
                    main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    var spin = ps.rotationOverLifetime;
                    spin.enabled = true; spin.separateAxes = true;
                    spin.x = new ParticleSystem.MinMaxCurve(-4f, 4f); spin.y = new ParticleSystem.MinMaxCurve(-4f, 4f); spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
                    col.color = Fade(Color.white, Color.white, 1f);
                    var dampD = ps.limitVelocityOverLifetime;
                    dampD.enabled = true; dampD.dampen = 0.03f; dampD.limit = 0f;
                    break;
            }
            return ps;
        }

        void Emit(Kind k, Vector3 pos, int count, float speed, float sizeMin, float sizeMax, float life, Color c, float radius = 0.1f, float duration = 3f)
        {
            var ps = Get(k);
            ps.transform.position = pos;
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startColor = c;
            var shape = ps.shape;
            shape.radius = radius;
            if (!ps.isPlaying) ps.Play();
            ps.Emit(count);
            active.Add((ps, k, Time.time + Mathf.Max(duration, life + 0.2f)));
        }

        public void Explosion(Vector3 pos, float radius, Color tint)
        {
            radius = Mathf.Max(0.3f, radius);
            Color hot = Color.Lerp(new Color(1f, 0.85f, 0.6f), tint, 0.35f);
            Emit(Kind.Flash, pos, 1, 0f, radius * 2.4f, radius * 2.8f, 0.14f, hot * 3.5f, 0.01f, 0.5f);
            Emit(Kind.Fireball, pos, Mathf.Clamp((int)(radius * 5), 10, 90), radius * 2.4f, radius * 0.35f, radius * 0.95f, 0.85f, tint * 2.4f, radius * 0.3f);
            Emit(Kind.Spark, pos, Mathf.Clamp((int)(radius * 6), 10, 90), radius * 9f, radius * 0.03f + 0.05f, radius * 0.07f + 0.12f, 0.8f, new Color(2.6f, 1.9f, 1.1f), radius * 0.2f);
            Emit(Kind.Shock, pos, 1, 0f, radius * 4.5f, radius * 4.5f, 0.35f, tint * 0.9f, 0.01f, 0.6f);
            Emit(Kind.Smoke, pos, Mathf.Clamp((int)(radius * 3), 6, 40), radius * 1.1f, radius * 0.5f, radius * 1.1f, 2.6f, new Color(0.32f, 0.3f, 0.32f, 0.5f), radius * 0.4f, 3f);
            if (radius >= 1.4f)
                Emit(Kind.Debris, pos, Mathf.Clamp((int)(radius * 2.5f), 4, 30), radius * 4f, radius * 0.05f + 0.08f, radius * 0.12f + 0.15f, 2.4f, new Color(0.35f, 0.36f, 0.4f), radius * 0.3f, 3f);
            var cam = Camera.main;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, pos);
                Shake(Mathf.Clamp01(radius * 6f / Mathf.Max(10f, d)) * 0.6f);
            }
        }

        public void KillBurst(Vector3 pos, float radius, Color tint)
        {
            radius = Mathf.Max(0.5f, radius);
            Emit(Kind.Debris, pos, Mathf.Clamp((int)(radius * 4f), 8, 36), radius * 5f, radius * 0.06f + 0.1f, radius * 0.14f + 0.2f, 2.6f, new Color(0.42f, 0.44f, 0.5f), radius * 0.25f, 3f);
            Emit(Kind.Spark, pos, Mathf.Clamp((int)(radius * 8f), 14, 80), radius * 12f, 0.06f, radius * 0.05f + 0.14f, 0.6f, new Color(3f, 2.2f, 1.2f), radius * 0.15f);
            Emit(Kind.Shock, pos, 1, 0f, radius * 6.5f, radius * 6.5f, 0.45f, Color.Lerp(tint, Color.white, 0.4f) * 1.2f, 0.01f, 0.7f);
            Emit(Kind.Flash, pos, 1, 0f, radius * 1.4f, radius * 1.6f, 0.1f, new Color(3f, 3f, 3f), 0.01f, 0.4f);
        }

        public void MuzzleFlash(Vector3 pos, Vector3 dir)
        {
            Emit(Kind.Muzzle, pos, 4, 3f, 0.2f, 0.5f, 0.12f, new Color(2f, 1.8f, 1.2f));
            Emit(Kind.Spark, pos, 3, 10f, 0.03f, 0.06f, 0.15f, new Color(2.4f, 2f, 1.4f), 0.05f, 0.5f);
        }

        // Laser hits and the mining drill: hot spark streaks, a small glow and a puff of dust/chips.
        public void LaserImpact(Vector3 pos)
        {
            Emit(Kind.Spark, pos, 9, 9f, 0.03f, 0.08f, 0.4f, new Color(1.6f, 2.4f, 3f), 0.05f, 1f);
            Emit(Kind.Flash, pos, 1, 0f, 0.5f, 0.7f, 0.08f, new Color(1.5f, 2.6f, 3.2f), 0.01f, 0.4f);
            Emit(Kind.Smoke, pos, 2, 0.8f, 0.25f, 0.45f, 0.9f, new Color(0.45f, 0.45f, 0.5f, 0.35f), 0.1f, 1.2f);
        }

        public void ShieldHit(Vector3 pos)
        {
            Emit(Kind.Shield, pos, 18, 6f, 0.3f, 0.8f, 0.4f, new Color(0.5f, 1.6f, 3f), 0.5f);
            Emit(Kind.Shock, pos, 1, 0f, 3.2f, 3.6f, 0.3f, new Color(0.4f, 1.2f, 2.4f), 0.01f, 0.5f);
        }

        public void PickupSparkle(Vector3 pos, Color tint)
        {
            Emit(Kind.Sparkle, pos, 14, 2.5f, 0.08f, 0.22f, 0.8f, tint * 2f, 0.4f);
            Emit(Kind.Spark, pos, 6, 3f, 0.02f, 0.04f, 0.5f, tint * 2.4f, 0.2f, 1f);
        }

        public void JumpFlash(Vector3 pos)
        {
            ScreenFlash?.Invoke(new Color(0.85f, 0.95f, 1f, 1f), 1.4f);
            Shake(0.8f);
        }

        public void AtmosphereEntryFlash()
        {
            ScreenFlash?.Invoke(new Color(1f, 0.6f, 0.3f, 0.45f), 1.6f);
            Shake(0.4f);
        }

        public void Shake(float intensity) => GameState.ShakeIntensity = Mathf.Max(GameState.ShakeIntensity, Mathf.Clamp01(intensity) * Mathf.Clamp01(Vfx.ShakeScale));
    }
}
