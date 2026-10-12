using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Pooled, code-built particle effects (explosions, sparks, shield ripples, sparkles) and
    /// local camera shake / screen flashes. Uses one shared additive particle material.
    /// </summary>
    public class VfxManager : MonoBehaviour, IVfxService
    {
        public Material ParticleMaterial;

        /// <summary>UI listens: full-screen flash (color, seconds).</summary>
        public static event Action<Color, float> ScreenFlash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ScreenFlash = null;

        enum Kind { Explosion, Spark, Shield, Sparkle, Muzzle }
        readonly Dictionary<Kind, Queue<ParticleSystem>> pools = new Dictionary<Kind, Queue<ParticleSystem>>();
        readonly List<(ParticleSystem ps, Kind k, float until)> active = new List<(ParticleSystem, Kind, float)>();

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
            ps.gameObject.SetActive(true);
            return ps;
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
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
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
            active.Add((ps, k, Time.time + duration));
        }

        public void Explosion(Vector3 pos, float radius, Color tint)
        {
            Emit(Kind.Explosion, pos, Mathf.Clamp((int)(radius * 6), 12, 120), radius * 3f, radius * 0.3f, radius * 0.9f, 0.9f, tint * 2.2f, radius * 0.3f);
            Emit(Kind.Spark, pos, Mathf.Clamp((int)(radius * 4), 8, 60), radius * 8f, 0.1f, 0.35f, 0.7f, new Color(2.5f, 2f, 1.2f), radius * 0.2f);
            var cam = Camera.main;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, pos);
                Shake(Mathf.Clamp01(radius * 6f / Mathf.Max(10f, d)) * 0.6f);
            }
        }

        public void MuzzleFlash(Vector3 pos, Vector3 dir) => Emit(Kind.Muzzle, pos, 4, 3f, 0.2f, 0.5f, 0.12f, new Color(2f, 1.8f, 1.2f));
        public void LaserImpact(Vector3 pos) => Emit(Kind.Spark, pos, 8, 8f, 0.05f, 0.18f, 0.35f, new Color(1.5f, 2.5f, 3f));
        public void ShieldHit(Vector3 pos) => Emit(Kind.Shield, pos, 18, 6f, 0.3f, 0.8f, 0.4f, new Color(0.5f, 1.6f, 3f), 0.5f);
        public void PickupSparkle(Vector3 pos, Color tint) => Emit(Kind.Sparkle, pos, 14, 2.5f, 0.08f, 0.22f, 0.8f, tint * 2f, 0.4f);

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

        public void Shake(float intensity) => GameState.ShakeIntensity = Mathf.Max(GameState.ShakeIntensity, Mathf.Clamp01(intensity));
    }
}
