using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Procedural audio: every SFX and the ambient music are synthesized at startup (no asset
    /// dependencies). Pooled 3D sources for SFX, one 2D source for UI, a looping engine source and
    /// a cross-faded music pair per sector mood.
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioService
    {
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly AudioClip[] music = new AudioClip[4];
        AudioSource ui, engine, musicA, musicB;
        int mood = -1;
        float engineKeepAlive;
        float engineTarget;
        float musicFade = 1f;
        bool aIsActive = true;

        public float MasterVolume { get => AudioListener.volume; set => AudioListener.volume = Mathf.Clamp01(value); }
        public float MusicVolume { get; set; } = 0.55f;
        public float SfxVolume { get; set; } = 0.9f;

        void Awake()
        {
            if (G.Get<IAudioService>() != null) { Destroy(gameObject); return; }
            G.Register<IAudioService>(this);
            DontDestroyOnLoad(gameObject);
            ui = gameObject.AddComponent<AudioSource>();
            ui.spatialBlend = 0f;
            engine = gameObject.AddComponent<AudioSource>();
            engine.loop = true;
            engine.spatialBlend = 0f;
            engine.volume = 0f;
            musicA = gameObject.AddComponent<AudioSource>();
            musicB = gameObject.AddComponent<AudioSource>();
            foreach (var m in new[] { musicA, musicB }) { m.loop = true; m.spatialBlend = 0f; m.volume = 0f; }
            BuildClips();
            engine.clip = clips["engine"];
            engine.Play();
            for (int i = 0; i < 24; i++) pool.Add(NewSource());
        }

        void OnDestroy()
        {
            if (ReferenceEquals(G.Get<IAudioService>(), this)) G.Unregister<IAudioService>();
        }

        AudioSource NewSource()
        {
            var go = new GameObject("sfx");
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 6f;
            s.maxDistance = 400f;
            s.dopplerLevel = 0f;
            return s;
        }

        public void Play(string id, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            if (!clips.TryGetValue(id, out var c)) return;
            AudioSource s = null;
            foreach (var p in pool) if (!p.isPlaying) { s = p; break; }
            if (s == null) { if (pool.Count < 48) { s = NewSource(); pool.Add(s); } else return; }
            s.transform.position = pos;
            s.pitch = pitch;
            s.volume = volume * SfxVolume;
            s.clip = c;
            s.Play();
        }

        public void PlayUI(string id, float volume = 1f)
        {
            if (clips.TryGetValue(id, out var c)) ui.PlayOneShot(c, volume * SfxVolume * 0.8f);
        }

        public void SetMusicMood(int sectorIndex)
        {
            sectorIndex = Mathf.Clamp(sectorIndex, 0, 3);
            if (sectorIndex == mood) return;
            mood = sectorIndex;
            if (music[sectorIndex] == null) music[sectorIndex] = BuildMusic(sectorIndex);
            var next = aIsActive ? musicB : musicA;
            next.clip = music[sectorIndex];
            next.volume = 0f;
            next.Play();
            aIsActive = !aIsActive;
            musicFade = 0f;
        }

        public void SetEngineLoop(bool on, float throttle01)
        {
            if (on) { engineKeepAlive = 0.25f; engineTarget = throttle01; }
            else { engineKeepAlive = 0f; engineTarget = 0f; }
        }

        void Update()
        {
            engineKeepAlive -= Time.unscaledDeltaTime;
            float t = engineKeepAlive > 0f ? engineTarget : 0f;
            float targetVol = engineKeepAlive > 0f ? (0.12f + t * 0.35f) * SfxVolume : 0f;
            engine.volume = Mathf.MoveTowards(engine.volume, targetVol, Time.unscaledDeltaTime * 1.5f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.7f + t * 0.7f, Time.unscaledDeltaTime * 3f);

            musicFade = Mathf.MoveTowards(musicFade, 1f, Time.unscaledDeltaTime / 3f);
            var active = aIsActive ? musicA : musicB;
            var other = aIsActive ? musicB : musicA;
            active.volume = musicFade * MusicVolume * 0.6f;
            other.volume = (1f - musicFade) * MusicVolume * 0.6f;
            if (musicFade >= 1f && other.isPlaying) other.Stop();
        }

        // ------------------------------------------------------------------ synthesis

        delegate float Gen(float t, float u, System.Random r);

        AudioClip Make(string name, float seconds, Gen g, int seed = 1)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            var r = new System.Random(seed);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(g(t, t / seconds, r), -1f, 1f);
            }
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;
        static float Env(float u, float attack = 0.01f) => Mathf.Clamp01(u / attack) * (1f - u) * (1f - u);
        static float Sin(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Sq(float f, float t) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t));

        void BuildClips()
        {
            clips["laser"] = Make("laser", 0.18f, (t, u, r) => Sq(Mathf.Lerp(1400f, 300f, u), t) * 0.25f * Env(u));
            clips["laser_ship"] = Make("laser_ship", 0.22f, (t, u, r) => (Sin(Mathf.Lerp(900f, 160f, u), t) * 0.5f + Sq(Mathf.Lerp(450f, 90f, u), t) * 0.2f) * Env(u));
            clips["rifle"] = Make("rifle", 0.12f, (t, u, r) => (Noise(r) * 0.6f + Sq(Mathf.Lerp(600f, 140f, u), t) * 0.3f) * Env(u, 0.005f));
            float lp = 0f;
            clips["missile"] = Make("missile", 0.7f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.15f); return lp * 1.4f * Env(u, 0.05f); });
            clips["flak"] = Make("flak", 0.2f, (t, u, r) => (Noise(r) * 0.7f + Sin(110f, t) * 0.4f) * Env(u, 0.003f));
            clips["explosion"] = Make("explosion", 0.9f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.08f); return (lp * 2.2f + Sin(Mathf.Lerp(90f, 30f, u), t) * 0.5f) * Env(u, 0.004f); }, 3);
            clips["explosion_big"] = Make("explosion_big", 2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.04f); return (lp * 2.8f + Sin(Mathf.Lerp(60f, 22f, u), t) * 0.6f) * Env(u, 0.004f); }, 4);
            clips["hit"] = Make("hit", 0.08f, (t, u, r) => (Noise(r) * 0.5f + Sin(220f, t) * 0.4f) * Env(u, 0.002f));
            clips["shield_hit"] = Make("shield_hit", 0.3f, (t, u, r) => (Sin(Mathf.Lerp(1200f, 600f, u), t) * Sin(37f, t)) * 0.5f * Env(u));
            clips["pickup"] = Make("pickup", 0.18f, (t, u, r) => Sin(u < 0.5f ? 880f : 1320f, t) * 0.35f * Env(u, 0.01f));
            clips["mine"] = Make("mine", 0.2f, (t, u, r) => (Noise(r) * 0.3f + Sq(180f + 40f * Sin(30f, t), t) * 0.2f) * Env(u, 0.02f));
            clips["craft"] = Make("craft", 0.45f, (t, u, r) => (Sin(523f, t) + Sin(659f, t) * (u > 0.3f ? 1 : 0) + Sin(784f, t) * (u > 0.6f ? 1 : 0)) * 0.18f * Env(u, 0.02f));
            clips["trade"] = Make("trade", 0.3f, (t, u, r) => (Sin(1046f, t) * 0.3f + Sin(1568f, t) * 0.2f * (u > 0.4f ? 1 : 0)) * Env(u, 0.01f));
            clips["ui_click"] = Make("ui_click", 0.05f, (t, u, r) => Sin(1800f, t) * 0.3f * Env(u, 0.002f));
            clips["ui_back"] = Make("ui_back", 0.12f, (t, u, r) => Sin(Mathf.Lerp(500f, 300f, u), t) * 0.3f * Env(u, 0.005f));
            clips["ui_hover"] = Make("ui_hover", 0.04f, (t, u, r) => Sin(Mathf.Lerp(2400f, 2900f, u), t) * 0.12f * Env(u, 0.05f));
            clips["alarm"] = Make("alarm", 0.9f, (t, u, r) => Sq(Sin(3f, t) > 0 ? 880f : 660f, t) * 0.15f * (1f - u));
            clips["low_oxygen"] = Make("low_oxygen", 0.4f, (t, u, r) => Sin(740f, t) * 0.3f * Env(u, 0.02f));
            clips["heal"] = Make("heal", 0.5f, (t, u, r) => Sin(Mathf.Lerp(400f, 900f, u), t) * 0.3f * Env(u, 0.05f));
            clips["jump_charge"] = Make("jump_charge", 1.2f, (t, u, r) => (Sin(Mathf.Lerp(120f, 900f, u * u), t) * 0.35f + Noise(r) * 0.05f) * Mathf.Clamp01(u * 4f) * (1f - u * 0.5f));
            clips["jump_gate"] = Make("jump_gate", 2.2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.05f); return (Sin(Mathf.Lerp(900f, 60f, u), t) * 0.4f + lp * 1.5f) * Env(u, 0.02f); }, 5);
            clips["landing"] = Make("landing", 0.4f, (t, u, r) => Sin(70f, t) * 0.6f * Env(u, 0.005f));
            clips["thud"] = clips["landing"];
            clips["ping"] = Make("ping", 0.35f, (t, u, r) => Sin(1320f, t) * 0.3f * Env(u, 0.003f) + Sin(1980f, t) * 0.15f * Env(u, 0.003f));
            clips["spawn"] = Make("spawn", 0.5f, (t, u, r) => Sin(Mathf.Lerp(200f, 700f, u), t) * 0.25f * Env(u, 0.05f));
            clips["boss_roar"] = Make("boss_roar", 2.5f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.03f); return (Sq(Mathf.Lerp(55f, 40f, u) + Sin(5f, t) * 6f, t) * 0.25f + lp * 1.2f) * Env(u, 0.1f); }, 6);
            clips["boss_shot"] = Make("boss_shot", 0.4f, (t, u, r) => Sin(Mathf.Lerp(300f, 80f, u), t) * 0.5f * Env(u, 0.01f));
            clips["stardust"] = Make("stardust", 0.8f, (t, u, r) => (Sin(1568f, t) + Sin(2093f, t) * Sin(4f, t)) * 0.15f * Env(u, 0.02f));
            clips["atmo_entry"] = Make("atmo_entry", 2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.02f + u * 0.1f); return lp * 2f * Mathf.Sin(Mathf.PI * u); }, 7);
            clips["crate"] = Make("crate", 0.25f, (t, u, r) => (Noise(r) * 0.3f + Sin(160f, t) * 0.5f) * Env(u, 0.003f));
            clips["seat"] = Make("seat", 0.3f, (t, u, r) => (Sin(220f, t) * 0.3f + Sin(330f, t) * 0.2f * (u > 0.5f ? 1 : 0)) * Env(u, 0.01f));
            clips["spit"] = Make("spit", 0.25f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.3f); return lp * Env(u, 0.01f) + Sin(Mathf.Lerp(400f, 150f, u), t) * 0.2f * Env(u); }, 8);
            clips["bite"] = Make("bite", 0.15f, (t, u, r) => (Noise(r) * 0.5f + Sq(90f, t) * 0.3f) * Env(u, 0.003f));
            clips["slam"] = Make("slam", 0.6f, (t, u, r) => (Sin(Mathf.Lerp(80f, 30f, u), t) * 0.8f + Noise(r) * 0.2f) * Env(u, 0.003f));
            clips["squish"] = Make("squish", 0.35f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.2f); return (lp * 1.2f + Sin(Mathf.Lerp(300f, 90f, u), t) * 0.3f) * Env(u, 0.005f); }, 9);
            clips["nest"] = Make("nest", 0.5f, (t, u, r) => Sin(Mathf.Lerp(90f, 160f, u) + Sin(8f, t) * 20f, t) * 0.4f * Env(u, 0.05f));
            float e1 = 0f;
            clips["engine"] = Make("engine", 2f, (t, u, r) => { e1 = Mathf.Lerp(e1, Noise(r), 0.06f); return e1 * 0.9f + Sin(55f, t) * 0.25f + Sin(110f, t) * 0.12f; }, 10);
        }

        static readonly float[][] Scales =
        {
            new[] { 0f, 3f, 7f, 10f, 14f },   // menu: minor 7 pad
            new[] { 0f, 4f, 7f, 11f, 14f },   // sector 1: maj7 dreamy
            new[] { 0f, 2f, 7f, 9f, 14f },    // sector 2: sus/pentatonic
            new[] { 0f, 3f, 6f, 10f, 13f },   // sector 3: tense diminished-ish
        };

        AudioClip BuildMusic(int moodIndex)
        {
            const float len = 24f;
            float root = new[] { 110f, 130.81f, 98f, 87.31f }[moodIndex];
            var scale = Scales[moodIndex];
            int n = (int)(len * Rate);
            var data = new float[n];
            var r = new System.Random(100 + moodIndex);
            // slow chord pad: 4 chords x 6 s, crossfaded; plus sparse arpeggio sparkles
            int[] prog = { 0, 5, 3, 4 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int ci = (int)(t / 6f) % 4;
                float local = (t % 6f) / 6f;
                float env = Mathf.Clamp01(local * 3f) * Mathf.Clamp01((1f - local) * 3f);
                float v = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float semi = scale[(prog[ci] + k * 2) % scale.Length] + (prog[ci] >= scale.Length ? 12f : 0f);
                    float f = root * Mathf.Pow(2f, semi / 12f);
                    v += Mathf.Sin(2f * Mathf.PI * f * t + Mathf.Sin(2f * Mathf.PI * 0.2f * t) * 0.6f) * 0.12f;
                    v += Mathf.Sin(2f * Mathf.PI * f * 2.003f * t) * 0.03f;
                }
                data[i] = v * env;
            }
            // arpeggio sparkles
            for (int s = 0; s < 40; s++)
            {
                float start = (float)r.NextDouble() * (len - 1.5f);
                float semi = scale[r.Next(scale.Length)] + 24f;
                float f = root * Mathf.Pow(2f, semi / 12f);
                int i0 = (int)(start * Rate);
                int dur = (int)(1.2f * Rate);
                for (int i = 0; i < dur && i0 + i < n; i++)
                {
                    float t = i / (float)Rate;
                    data[i0 + i] += Mathf.Sin(2f * Mathf.PI * f * t) * 0.06f * Mathf.Exp(-t * 3f);
                }
            }
            // make loop seamless: short crossfade at the end
            int xf = Rate / 2;
            for (int i = 0; i < xf; i++) { float a = i / (float)xf; data[n - xf + i] = data[n - xf + i] * (1f - a) + data[i] * a; }
            var c = AudioClip.Create("music_" + moodIndex, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
