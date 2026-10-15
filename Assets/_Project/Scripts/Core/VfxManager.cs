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
    /// Weapon feedback (#43): directional muzzle flashes with a short pooled point light, surface-aware impacts
    /// (metal sparks, rock chips, dirt, alien goo, shield) and pooled scorch decals (DecalPool).
    /// Textured layers (#96): when the builder assigns them, fireballs and smoke play Unity Labs flipbooks (frame blended,
    /// soft), flashes/rings/sparks/muzzle flames use Kenney sprites; a missing material keeps the untextured fallback.
    /// </summary>
    public class VfxManager : MonoBehaviour, IVfxService
    {
        public Material ParticleMaterial;
        public Material SmokeMaterial;
        public Material DebrisMaterial;
        public Mesh DebrisMesh;
        public Material DecalMaterial;
        [Header("Textured layers (#96, optional)")]
        public Material FireballMaterial;
        public Material FireballBMaterial;
        public Material SmokeSheetMaterial;
        public Material PuffMaterial;
        public Material FlashMaterial;
        public Material GlintMaterial;
        public Material RingMaterial;
        public Material RippleMaterial;
        public Material SparkMaterial;
        public Material MuzzleMaterial;

        /// <summary>UI listens: full-screen flash (color, seconds).</summary>
        public static event Action<Color, float> ScreenFlash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ScreenFlash = null;

        enum Kind { Flash, Fireball, Spark, Shock, Smoke, Debris, Shield, Sparkle, Muzzle, SparkCone, Chips, Dust, Goo, FireballB, Ripple }
        readonly Dictionary<Kind, Queue<ParticleSystem>> pools = new Dictionary<Kind, Queue<ParticleSystem>>();
        readonly List<(ParticleSystem ps, Kind k, float until)> active = new List<(ParticleSystem, Kind, float)>();
        bool frozen;
        DecalPool decals;

        // Short-lived point lights (muzzle flashes, impacts): a small ring, the oldest is reused.
        const int MaxLights = 10;
        readonly Light[] lights = new Light[MaxLights];
        readonly float[] lightStart = new float[MaxLights], lightLife = new float[MaxLights], lightPeak = new float[MaxLights];
        int nextLight;
        float nextImpactLight;

        void Awake()
        {
            if (G.Get<IVfxService>() != null) { Destroy(gameObject); return; }
            G.Register<IVfxService>(this);
            DontDestroyOnLoad(gameObject);
            foreach (Kind k in Enum.GetValues(typeof(Kind))) pools[k] = new Queue<ParticleSystem>();
            decals = new DecalPool(DecalMaterial, transform);
            EventBus.SystemEntered += OnSystemEntered;
            EventBus.PhaseChanged += OnPhaseChanged;
        }

        void OnDestroy()
        {
            EventBus.SystemEntered -= OnSystemEntered;
            EventBus.PhaseChanged -= OnPhaseChanged;
            if (ReferenceEquals(G.Get<IVfxService>(), this)) G.Unregister<IVfxService>();
        }

        // The floating origin moves on every jump: marks anchored to the world (far terrain) would end up wrong.
        void OnSystemEntered(int galaxy, int system) => decals?.Clear();
        void OnPhaseChanged(GamePhase phase) { if (phase != GamePhase.Playing && phase != GamePhase.Paused) decals?.Clear(); }

        /// <summary>Live scorch decals (debug / tests).</summary>
        public int DecalCount => decals != null ? decals.LiveCount : 0;

        /// <summary>Hide all scorch decals (dev tests).</summary>
        public void ClearDecals() => decals?.Clear();

        void Update()
        {
            GameState.ShakeIntensity = Mathf.MoveTowards(GameState.ShakeIntensity, 0f, Time.deltaTime * 1.8f);
            GameState.FovPunch = Mathf.MoveTowards(GameState.FovPunch, 0f, Time.unscaledDeltaTime * 14f);
            // Recoil springs back fast (exponential), so sustained fire reads as a jitter, not a drift.
            GameState.Recoil = GameState.Recoil > 0.002f ? GameState.Recoil * Mathf.Exp(-16f * Time.unscaledDeltaTime) : 0f;
            decals?.Tick();
            UpdateLights();
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

        void UpdateLights()
        {
            float now = Time.time;
            for (int i = 0; i < MaxLights; i++)
            {
                var l = lights[i];
                if (l == null || !l.enabled) continue;
                float t = (now - lightStart[i]) / lightLife[i];
                if (t >= 1f) { l.enabled = false; continue; }
                float k = 1f - t;
                l.intensity = lightPeak[i] * k * k;
            }
        }

        /// <summary>Brief point light at pos (HDR tint is normalised to a light colour), fading out over life seconds.</summary>
        void FlashLight(Vector3 pos, Color tint, float intensity, float range, float life)
        {
            int i = nextLight;
            nextLight = (nextLight + 1) % MaxLights;
            var l = lights[i];
            if (l == null)
            {
                var go = new GameObject("vfx_Light");
                go.transform.SetParent(transform, false);
                l = lights[i] = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
            }
            float m = Mathf.Max(1f, tint.maxColorComponent);
            l.color = new Color(tint.r / m, tint.g / m, tint.b / m, 1f);
            l.transform.position = pos;
            l.range = range;
            l.intensity = intensity;
            l.enabled = true;
            lightStart[i] = Time.time;
            lightLife[i] = Mathf.Max(0.02f, life);
            lightPeak[i] = intensity;
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

        static readonly List<ParticleSystemVertexStream> FlipbookStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend,
        };

        /// <summary>Use a textured material (#96): a flipbook played over the lifetime, or a random frame of a sprite sheet.</summary>
        static bool Textured(ParticleSystem ps, Material mat, int tilesX = 1, int tilesY = 1, bool playOverLife = false)
        {
            if (mat == null) return false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.SetActiveVertexStreams(FlipbookStreams);
            if (tilesX * tilesY > 1)
            {
                var t = ps.textureSheetAnimation;
                t.enabled = true;
                t.mode = ParticleSystemAnimationMode.Grid;
                t.numTilesX = tilesX;
                t.numTilesY = tilesY;
                t.animation = ParticleSystemAnimationType.WholeSheet;
                t.cycleCount = 1;
                if (playOverLife)
                {
                    t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    t.startFrame = 0f;
                }
                else
                {
                    t.frameOverTime = 0f;
                    t.startFrame = new ParticleSystem.MinMaxCurve(0f, tilesX * tilesY - 0.01f);
                }
            }
            return true;
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
                case Kind.Muzzle:
                case Kind.SparkCone:
                    // Directional streaks: emitted along the effect's forward (muzzle direction / surface normal).
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = k == Kind.Muzzle ? 9f : 55f;
                    shape.radius = 0.02f;
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = k == Kind.Muzzle ? 0.03f : 0.05f;
                    r.lengthScale = k == Kind.Muzzle ? 2.2f : 1.4f;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
                    col.color = k == Kind.Muzzle ? Fade(Color.white, Color.white) : Fade(Color.white, new Color(1f, 0.5f, 0.2f), 0f, 0.4f);
                    var dampC = ps.limitVelocityOverLifetime;
                    dampC.enabled = true; dampC.dampen = k == Kind.Muzzle ? 0.2f : 0.07f; dampC.limit = 0f;
                    break;
                case Kind.Dust:
                case Kind.Goo:
                    if (SmokeMaterial != null) r.sharedMaterial = SmokeMaterial;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = k == Kind.Dust ? 40f : 50f;
                    shape.radius = 0.08f;
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    if (k == Kind.Dust)
                    {
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1.6f)));
                        col.color = Fade(Color.white, Color.white, 0f, 0.15f);
                    }
                    else
                    {
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0.2f)));
                        col.color = Fade(Color.white, new Color(0.7f, 0.8f, 0.6f), 0f, 0.6f);
                    }
                    var dampG = ps.limitVelocityOverLifetime;
                    dampG.enabled = true; dampG.dampen = k == Kind.Dust ? 0.1f : 0.04f; dampG.limit = 0f;
                    r.sortingFudge = 10f;
                    break;
                case Kind.Chips:
                case Kind.Debris:
                    if (k == Kind.Chips) { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 45f; shape.radius = 0.05f; }
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
                case Kind.FireballB:
                case Kind.Ripple:
                    break;
            }
            ApplyTexture(ps, k, main, size, col);
            return ps;
        }

        /// <summary>Textured look per kind (#96); kinds without a material keep the procedural soft sprite.</summary>
        void ApplyTexture(ParticleSystem ps, Kind k, ParticleSystem.MainModule main, ParticleSystem.SizeOverLifetimeModule size, ParticleSystem.ColorOverLifetimeModule col)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            switch (k)
            {
                case Kind.Flash:
                    Textured(ps, FlashMaterial);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    break;
                case Kind.Fireball:
                case Kind.FireballB:
                    if (Textured(ps, k == Kind.Fireball ? FireballMaterial : (FireballBMaterial != null ? FireballBMaterial : FireballMaterial), 5, 5, true))
                    {
                        // the flipbook carries the fire -> smoke change itself: colour stays, alpha fades at the very end
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 0.95f), new Keyframe(1f, 1.15f)));
                        col.color = Fade(Color.white, Color.white, 0f, 0.8f);
                        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                        var rot = ps.rotationOverLifetime;
                        rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                        var damp = ps.limitVelocityOverLifetime;
                        damp.enabled = true; damp.dampen = 0.08f; damp.limit = 0f;
                        r.sortingFudge = 5f;
                    }
                    break;
                case Kind.Smoke:
                    if (Textured(ps, SmokeSheetMaterial, 8, 8, true)) col.color = Fade(Color.white, Color.white, 0f, 0.35f);
                    break;
                case Kind.Dust:
                case Kind.Goo:
                    Textured(ps, PuffMaterial, 4, 2);
                    break;
                case Kind.Spark:
                case Kind.SparkCone:
                    Textured(ps, SparkMaterial);
                    break;
                case Kind.Muzzle:
                    Textured(ps, MuzzleMaterial, 4, 1);
                    break;
                case Kind.Shock:
                    Textured(ps, RingMaterial);
                    break;
                case Kind.Ripple:
                    if (Textured(ps, RippleMaterial != null ? RippleMaterial : RingMaterial))
                    {
                        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));
                    }
                    break;
                case Kind.Shield:
                case Kind.Sparkle:
                    if (Textured(ps, GlintMaterial)) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    break;
            }
        }

        ParticleSystem Emit(Kind k, Vector3 pos, int count, float speed, float sizeMin, float sizeMax, float life, Color c, float radius = 0.1f, float duration = 3f, Quaternion? rot = null)
        {
            var ps = Get(k);
            ps.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
            var force = ps.forceOverLifetime;
            if (force.enabled) force.enabled = false; // pooled: only Settle() turns it on for this burst
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
            return ps;
        }

        /// <summary>Pull an effect's particles back towards the surface they flew off (chips and goo fall back down).</summary>
        static void Settle(ParticleSystem ps, Vector3 normal, float accel)
        {
            var f = ps.forceOverLifetime;
            f.enabled = accel > 0f;
            f.space = ParticleSystemSimulationSpace.World;
            Vector3 a = -normal * accel;
            f.x = a.x; f.y = a.y; f.z = a.z;
        }

        public void Explosion(Vector3 pos, float radius, Color tint)
        {
            radius = Mathf.Max(0.3f, radius);
            Color hot = Color.Lerp(new Color(1f, 0.85f, 0.6f), tint, 0.35f);
            Emit(Kind.Flash, pos, 1, 0f, radius * 2.4f, radius * 2.8f, 0.14f, hot * 3.5f, 0.01f, 0.5f);
            if (FireballMaterial != null)
            {
                // flipbook fireballs (#96): a few big overlapping blasts + smaller secondary bursts around them
                Color fire = Color.Lerp(Color.white, tint, 0.2f);
                Emit(Kind.Fireball, pos, Mathf.Clamp((int)(radius * 1.2f), 3, 9), radius * 0.9f, radius * 1.4f, radius * 2.2f, 1.6f, fire, radius * 0.3f);
                Emit(Kind.FireballB, pos, Mathf.Clamp((int)(radius * 1.6f), 3, 12), radius * 2.6f, radius * 0.6f, radius * 1.2f, 1.3f, fire, radius * 0.5f);
            }
            else
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

        public void MuzzleFlash(Vector3 pos, Vector3 dir, Color tint, float scale)
        {
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            scale = Mathf.Clamp(scale, 0.1f, 3f);
            var rot = Quaternion.LookRotation(dir);
            Color hot = Color.Lerp(tint, new Color(3f, 3f, 3f), 0.4f);
            // own guns seen from the cockpit (the camera a few metres behind the muzzle): a small, dim flash, so the
            // pilot keeps the view and the interior does not strobe (handheld guns, scale < 0.5, keep their flash)
            var cam = Camera.main;
            float lightK = 1f;
            if (cam != null && scale >= 0.5f)
            {
                Vector3 toFlash = pos - cam.transform.position;
                float d = toFlash.magnitude;
                if (d < 16f && Vector3.Dot(toFlash, dir) > -0.2f * d)
                {
                    float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 16f, d));
                    scale *= Mathf.Lerp(1f, 0.3f, near);
                    hot *= Mathf.Lerp(1f, 0.5f, near);
                    lightK = Mathf.Lerp(1f, 0.08f, near);
                }
            }
            // hot core bloom, a tight cone of flame streaks, a few sparks and a light that lights up the hull / hands
            Emit(Kind.Flash, pos + dir * (0.15f * scale), 1, 0f, 0.9f * scale, 1.3f * scale, 0.06f, hot, 0.01f, 0.3f);
            Emit(Kind.Muzzle, pos, 6, 28f * scale, 0.16f * scale, 0.32f * scale, 0.08f, hot, 0.02f, 0.4f, rot);
            Emit(Kind.SparkCone, pos, 3 + Mathf.RoundToInt(2f * scale), 14f * scale + 4f, 0.02f, 0.03f + 0.04f * scale, 0.18f, tint * 1.2f, 0.03f, 0.5f, rot);
            FlashLight(pos + dir * (0.3f * scale), tint, Mathf.Lerp(4f, 40f, Mathf.Clamp01(scale)) * lightK, Mathf.Lerp(5f, 22f, Mathf.Clamp01(scale)), 0.07f);
        }

        public void Impact(Vector3 pos, Vector3 normal, SurfaceKind surface, Color tint, float scale)
        {
            if (normal.sqrMagnitude < 0.01f) normal = UnityEngine.Random.onUnitSphere;
            normal.Normalize();
            float s = Mathf.Clamp(scale, 0.2f, 3f);
            var rot = Quaternion.LookRotation(normal);
            Vector3 lift = pos + normal * 0.08f;
            if (surface != SurfaceKind.Organic)
                Emit(Kind.Flash, lift, 1, 0f, 0.5f * s, 0.8f * s, 0.07f, tint * 0.9f, 0.01f, 0.3f);
            switch (surface)
            {
                case SurfaceKind.Metal:
                    // bright, long-lived spark spray + a wisp of smoke, lights the hull for a moment
                    Emit(Kind.SparkCone, lift, 5 + Mathf.RoundToInt(11f * s), 18f * s + 4f, 0.03f, 0.07f, 0.55f, new Color(3f, 2.1f, 1.1f), 0.05f, 1f, rot);
                    Emit(Kind.Smoke, lift + normal * 0.2f, 2, 0.8f, 0.3f * s, 0.6f * s, 1f, new Color(0.35f, 0.35f, 0.38f, 0.35f), 0.1f, 1.2f);
                    ImpactLight(lift + normal * 0.4f, new Color(1f, 0.72f, 0.42f), s);
                    break;
                case SurfaceKind.Rock:
                    Emit(Kind.SparkCone, lift, 3 + Mathf.RoundToInt(3f * s), 10f * s + 3f, 0.025f, 0.06f, 0.35f, new Color(2.6f, 1.6f, 0.8f), 0.05f, 1f, rot);
                    Settle(Emit(Kind.Chips, lift, 2 + Mathf.RoundToInt(4f * s), 7f * s + 2f, 0.05f * s, 0.13f * s, 1.3f, new Color(0.46f, 0.44f, 0.43f), 0.05f, 1.6f, rot), normal, 9f);
                    Emit(Kind.Dust, lift, 2 + Mathf.RoundToInt(s), 2.5f * s + 0.5f, 0.4f * s, 0.9f * s, 1.6f, new Color(0.52f, 0.5f, 0.48f, 0.45f), 0.08f, 2f, rot);
                    ImpactLight(lift + normal * 0.4f, tint, s * 0.6f);
                    break;
                case SurfaceKind.Dirt:
                    Settle(Emit(Kind.Dust, lift, 3 + Mathf.RoundToInt(2f * s), 4f * s + 0.5f, 0.6f * s, 1.3f * s, 2f, new Color(0.55f, 0.49f, 0.41f, 0.5f), 0.1f, 2.4f, rot), normal, 1.5f);
                    Settle(Emit(Kind.Chips, lift, 3 + Mathf.RoundToInt(3f * s), 6f * s + 2f, 0.04f * s, 0.11f * s, 1.2f, new Color(0.3f, 0.26f, 0.22f), 0.05f, 1.5f, rot), normal, 12f);
                    Emit(Kind.SparkCone, lift, 2 + Mathf.RoundToInt(2f * s), 8f * s + 2f, 0.02f, 0.05f, 0.3f, new Color(2.6f, 1.6f, 0.8f), 0.05f, 1f, rot);
                    ImpactLight(lift + normal * 0.4f, tint, s * 0.6f);
                    break;
                case SurfaceKind.Organic:
                    // alien ichor: glowing droplets that splash out and drip back, a little green mist
                    Settle(Emit(Kind.Goo, lift, 4 + Mathf.RoundToInt(8f * s), 6f * s + 1.5f, 0.06f * s, 0.16f * s, 0.8f, new Color(0.62f, 1f, 0.28f, 0.9f), 0.05f, 1.2f, rot), normal, 10f);
                    Emit(Kind.Dust, lift, 2, 1.5f, 0.3f * s, 0.6f * s, 0.9f, new Color(0.45f, 0.75f, 0.25f, 0.35f), 0.08f, 1.2f, rot);
                    Emit(Kind.Spark, lift, 3, 4f, 0.03f, 0.06f, 0.3f, new Color(1.2f, 2.6f, 0.6f), 0.05f, 0.6f);
                    break;
                case SurfaceKind.Shield:
                    Emit(Kind.SparkCone, lift, 4 + Mathf.RoundToInt(4f * s), 12f * s + 3f, 0.03f, 0.06f, 0.3f, new Color(0.6f, 1.8f, 3f), 0.05f, 0.8f, rot);
                    ImpactLight(lift, new Color(0.5f, 0.8f, 1f), s);
                    break;
                default:
                    Emit(Kind.Spark, pos, 9, 9f, 0.03f, 0.08f, 0.4f, tint, 0.05f, 1f);
                    Emit(Kind.Smoke, pos, 2, 0.8f, 0.25f, 0.45f, 0.9f, new Color(0.45f, 0.45f, 0.5f, 0.35f), 0.1f, 1.2f);
                    break;
            }
        }

        // Impact lights are throttled: a hail of laser bolts must not flicker every light in the pool.
        void ImpactLight(Vector3 pos, Color tint, float s)
        {
            if (Time.time < nextImpactLight) return;
            nextImpactLight = Time.time + 0.035f;
            FlashLight(pos, tint, 6f * s, 4f + 4f * s, 0.09f);
        }

        public void Scorch(Vector3 pos, Vector3 normal, Transform parent, float size, Color heat, float cool)
        {
            decals?.Place(pos, normal, parent, size, heat, cool);
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
            Emit(RippleMaterial != null ? Kind.Ripple : Kind.Shock, pos, 1, 0f, 3.2f, 3.6f, 0.3f, new Color(0.4f, 1.2f, 2.4f), 0.01f, 0.5f);
        }

        public void ShieldBreak(Vector3 pos, float radius)
        {
            float r = Mathf.Max(2f, radius);
            // Shards start on the bubble surface and fly outwards, so a cockpit camera inside it stays clear.
            Emit(Kind.Shield, pos, 36, r * 1.4f, 0.12f, 0.35f, 0.6f, new Color(0.5f, 1.6f, 3f), r * 1.1f);
            Emit(Kind.Spark, pos, 40, r * 3f, 0.04f, 0.09f, 0.6f, new Color(0.8f, 2f, 3.2f), r * 1.1f, 1f);
            Emit(Kind.Shock, pos, 1, 0f, r * 2.6f, r * 3f, 0.45f, new Color(0.4f, 1.2f, 2.4f), 0.01f, 0.6f);
            FlashLight(pos, new Color(0.45f, 0.75f, 1f), 8f, r * 4f, 0.25f);
        }

        // Explicit: the class already has a static ScreenFlash event (UIRoot draws it).
        void IVfxService.ScreenFlash(Color c, float duration) => ScreenFlash?.Invoke(c, duration);

        public void PickupSparkle(Vector3 pos, Color tint)
        {
            Emit(Kind.Sparkle, pos, 14, 2.5f, 0.08f, 0.22f, 0.8f, tint * 2f, 0.4f);
            Emit(Kind.Spark, pos, 6, 3f, 0.02f, 0.04f, 0.5f, tint * 2.4f, 0.2f, 1f);
        }

        public void LootSparkle(Vector3 pos, Color tint, float scale, int tier)
        {
            scale = Mathf.Max(0.2f, scale);
            Emit(Kind.Sparkle, pos, Mathf.RoundToInt((12 + tier * 8) * scale), 2.2f + tier * 0.8f, 0.07f, 0.2f + tier * 0.04f, 0.7f + tier * 0.15f, tint * (2f + tier * 0.5f), 0.35f);
            Emit(Kind.Spark, pos, Mathf.RoundToInt((5 + tier * 5) * scale), 3f + tier, 0.02f, 0.045f, 0.45f + tier * 0.1f, tint * 2.6f, 0.2f, 1f);
            if (tier >= 2)
            {
                // Rare / epic: a small shock ring and a light pop on top.
                Emit(Kind.Shock, pos, 1, 0f, 1.2f + tier * 0.6f, 1.4f + tier * 0.7f, 0.35f, tint * 1.4f, 0.01f, 0.5f);
                FlashLight(pos, tint, 3f + tier * 2f, 4f + tier * 2f, 0.3f);
            }
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
