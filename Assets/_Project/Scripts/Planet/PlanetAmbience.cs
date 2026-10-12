using Unity.Netcode;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Client-local planet environment around the camera: biome particles (dust, snow, ash, spores, motes), weather
    /// cycles (sandstorms, blizzards, ash fall...), surface haze, sky-tinted ambient light and a procedural wind loop.
    /// Fades in with descent through the atmosphere and restores the sector palette on the way out.
    /// </summary>
    public class PlanetAmbience : MonoBehaviour
    {
        static PlanetAmbience instance;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        AudioSource wind;
        PlanetParticles particleKind = (PlanetParticles)(-1);

        // Sector palette values (captured whenever something else writes RenderSettings).
        Color baseFog, baseSky, baseEquator, baseGround;
        float baseDensity;
        bool baseFogOn;
        Color wroteFog, wroteSky;
        float wroteDensity = -1f;
        bool overriding;

        /// <summary>0 = space, 1 = standing on the surface of the nearest atmospheric planet.</summary>
        public static float SurfaceFactor { get; private set; }
        /// <summary>Current weather intensity 0..1 on the nearest planet.</summary>
        public static float Storm { get; private set; }
        public static PlanetWeather Weather { get; private set; }

        public static void Ensure()
        {
            if (instance != null || !Application.isPlaying) return;
            var go = new GameObject("PlanetAmbience") { hideFlags = HideFlags.DontSave };
            instance = go.AddComponent<PlanetAmbience>();
        }

        void Awake()
        {
            wind = gameObject.AddComponent<AudioSource>();
            wind.loop = true;
            wind.spatialBlend = 0f;
            wind.volume = 0f;
            wind.clip = BuildWind();
            wind.Play();
        }

        void OnDestroy()
        {
            RestoreEnvironment();
            if (instance == this) instance = null;
            if (wind != null && wind.clip != null) Destroy(wind.clip);
        }

        static AudioClip BuildWind()
        {
            const int rate = 44100;
            int n = rate * 6;
            var data = new float[n];
            var rng = new System.Random(4242);
            float b1 = 0f, b2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                b1 = b1 * 0.995f + white * 0.05f;           // brown-ish rumble
                b2 = b2 * 0.9f + white * 0.1f;              // airy hiss
                float t = i / (float)n;
                float gust = 0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 2f * 3f) * Mathf.Sin(t * Mathf.PI * 2f);
                data[i] = (b1 * 2.2f + b2 * 0.35f * gust) * 0.5f;
            }
            // Crossfade the ends so the loop is seamless.
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            var clip = AudioClip.Create("Wind", n - fade, 1, rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            PlanetVisuals planet = null;
            float best = float.MaxValue;
            if (cam != null)
                foreach (var p in PlanetVisuals.All)
                {
                    if (p == null || p.Terrain == null || !p.Terrain.Shape.HasAtmosphere) continue;
                    float d = Vector3.Distance(p.transform.position, cam.transform.position) - p.Terrain.AtmosphereRadius;
                    if (d < best) { best = d; planet = p; }
                }

            float s = 0f, day = 1f;
            Vector3 up = Vector3.up;
            PlanetShapeDef shape = null;
            if (planet != null && best < 0f)
            {
                var t = planet.Terrain;
                shape = t.Shape;
                Vector3 rel = cam.transform.position - planet.transform.position;
                float dist = rel.magnitude;
                up = rel / Mathf.Max(0.01f, dist);
                float ground = t.GroundRadius(planet.transform.InverseTransformDirection(up));
                float h01 = Mathf.Clamp01((dist - ground) / Mathf.Max(1f, t.AtmosphereRadius - ground));
                s = 1f - Mathf.SmoothStep(0f, 1f, h01);
                var sun = RenderSettings.sun;
                if (sun != null) day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.25f, Vector3.Dot(up, -sun.transform.forward)));
            }
            SurfaceFactor = s;

            float storm = 0f;
            if (shape != null && shape.Weather != PlanetWeather.None)
            {
                double time = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
                float seed = (planet.Terrain.Seed & 1023) * 0.37f;
                float n = Mathf.PerlinNoise((float)(time / 75.0) + seed, seed * 0.5f);
                storm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.72f, n));
            }
            Storm = storm * s;
            Weather = shape != null ? shape.Weather : PlanetWeather.None;

            UpdateParticles(cam, shape, s, storm, up);
            UpdateEnvironment(shape, s, day, storm);
            UpdateWind(shape, s, storm);
        }

        // ---------------------------------------------------------------- particles

        void EnsureParticles(PlanetParticles kind)
        {
            if (particles == null)
            {
                var go = new GameObject("PlanetParticles") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                particles = go.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 2500;
                main.startSpeed = 0f;
                var shapeM = particles.shape;
                shapeM.shapeType = ParticleSystemShapeType.Sphere;
                shapeM.radius = 38f;
                shapeM.radiusThickness = 1f;
                var noise = particles.noise;
                noise.enabled = true;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.3f;
                var vel = particles.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                var col = particles.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                particleRenderer = go.GetComponent<ParticleSystemRenderer>();
                var cat = ContentCatalog.Instance;
                if (cat != null) particleRenderer.sharedMaterial = cat.ParticleMaterial;
                particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;
            }
            if (kind == particleKind) return;
            particleKind = kind;
            var m = particles.main;
            var nz = particles.noise;
            switch (kind)
            {
                case PlanetParticles.Snow:
                    m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                    nz.strength = 0.6f;
                    break;
                case PlanetParticles.Ash:
                    m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                    m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
                    nz.strength = 0.8f;
                    break;
                case PlanetParticles.Spores:
                    m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                    m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                    nz.strength = 0.5f;
                    break;
                case PlanetParticles.Motes:
                    m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                    m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
                    nz.strength = 0.3f;
                    break;
                default: // Dust
                    m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                    m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
                    nz.strength = 1.2f;
                    break;
            }
        }

        void UpdateParticles(Camera cam, PlanetShapeDef shape, float s, float storm, Vector3 up)
        {
            bool on = shape != null && shape.Particles != PlanetParticles.None && s > 0.05f && cam != null;
            if (!on)
            {
                if (particles != null && particles.isPlaying) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            EnsureParticles(shape.Particles);
            particles.transform.position = cam.transform.position;
            if (!particles.isPlaying) particles.Play();

            var m = particles.main;
            Color c = shape.ParticleColor;
            m.startColor = new Color(c.r, c.g, c.b, c.a * Mathf.Lerp(0.6f, 1f, storm));
            var em = particles.emission;
            float baseRate = shape.Particles == PlanetParticles.Dust ? 60f : shape.Particles == PlanetParticles.Motes ? 50f : 110f;
            em.rateOverTime = baseRate * s * s * (1f + storm * 5f);

            // Wind blows along a fixed tangent per planet; snow and ash fall, spores and embers rise.
            Vector3 windDir = Vector3.ProjectOnPlane(Quaternion.AngleAxis(37f, up) * Vector3.right, up).normalized;
            float windSpeed = 1.2f + storm * 14f;
            float vertical = shape.Particles == PlanetParticles.Snow ? -1.4f : shape.Particles == PlanetParticles.Ash ? -0.5f
                : shape.Particles == PlanetParticles.Spores || shape.Particles == PlanetParticles.Motes ? 0.35f : -0.1f;
            Vector3 v = windDir * windSpeed + up * vertical;
            var vel = particles.velocityOverLifetime;
            vel.x = v.x; vel.y = v.y; vel.z = v.z;
        }

        // ---------------------------------------------------------------- environment

        void CaptureBaseIfChanged()
        {
            bool external = !overriding
                || RenderSettings.fogColor != wroteFog
                || !Mathf.Approximately(RenderSettings.fogDensity, wroteDensity)
                || RenderSettings.ambientSkyColor != wroteSky;
            if (!external) return;
            baseFogOn = RenderSettings.fog;
            baseFog = RenderSettings.fogColor;
            baseDensity = RenderSettings.fogDensity;
            baseSky = RenderSettings.ambientSkyColor;
            baseEquator = RenderSettings.ambientEquatorColor;
            baseGround = RenderSettings.ambientGroundColor;
        }

        void UpdateEnvironment(PlanetShapeDef shape, float s, float day, float storm)
        {
            if (shape == null || s <= 0.001f)
            {
                RestoreEnvironment();
                return;
            }
            CaptureBaseIfChanged();
            overriding = true;
            bool hazy = shape.Weather == PlanetWeather.Sandstorm || shape.Weather == PlanetWeather.Blizzard || shape.Weather == PlanetWeather.AshFall || shape.Weather == PlanetWeather.SporeBloom;
            float stormFog = hazy ? storm : storm * 0.2f;
            Color haze = shape.HazeColor * Mathf.Lerp(0.08f, 1f, day);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(baseFog, haze, s);
            float density = Mathf.Lerp(0.0002f, 0.0042f, stormFog) * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(shape.AtmosphereDensity / 3f));
            RenderSettings.fogDensity = Mathf.Lerp(baseDensity, density, s);
            Color amb = shape.SurfaceAmbient * Mathf.Lerp(0.25f, 1f, day);
            float k = s * 0.6f;
            RenderSettings.ambientSkyColor = Color.Lerp(baseSky, amb, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(baseEquator, Color.Lerp(amb, haze, 0.4f) * 0.8f, k);
            RenderSettings.ambientGroundColor = Color.Lerp(baseGround, amb * 0.45f, k);
            wroteFog = RenderSettings.fogColor;
            wroteDensity = RenderSettings.fogDensity;
            wroteSky = RenderSettings.ambientSkyColor;
        }

        void RestoreEnvironment()
        {
            if (!overriding) return;
            overriding = false;
            // Only restore if nobody replaced the palette meanwhile.
            if (RenderSettings.fogColor == wroteFog && RenderSettings.ambientSkyColor == wroteSky)
            {
                RenderSettings.fog = baseFogOn;
                RenderSettings.fogColor = baseFog;
                RenderSettings.fogDensity = baseDensity;
                RenderSettings.ambientSkyColor = baseSky;
                RenderSettings.ambientEquatorColor = baseEquator;
                RenderSettings.ambientGroundColor = baseGround;
            }
        }

        void UpdateWind(PlanetShapeDef shape, float s, float storm)
        {
            if (wind == null) return;
            float sfx = G.TryGet<IAudioService>(out var a) ? a.SfxVolume : 0.9f;
            float target = shape != null ? s * (0.12f + 0.55f * storm) * sfx : 0f;
            wind.volume = Mathf.MoveTowards(wind.volume, target, Time.unscaledDeltaTime * 0.5f);
            float pitch = (shape != null ? shape.WindPitch : 1f) * (0.85f + storm * 0.35f);
            wind.pitch = Mathf.Lerp(wind.pitch, pitch, Time.unscaledDeltaTime);
        }
    }
}
