using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Procedural audio: every SFX and the music are synthesized at startup (no asset dependencies). Pooled 3D
    /// sources for SFX, one 2D source for UI, a looping engine source and two cross-faded music decks.
    /// #43: weapons are layered (core transient + tail that carries far + close-range mechanics, each with its
    /// own pitch jitter and voice limit), and every sector deck has three sample-synced stems (ambient pad,
    /// combat drums/bass, boss drums/brass) mixed from the client-side Threat level.
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioService
    {
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly AudioClip[] music = new AudioClip[4];
        AudioSource ui, engine;
        int mood = -1;
        float engineKeepAlive;
        float engineTarget;

        /// <summary>One weapon layer: clip, volume, random pitch spread, 3D min distance, max simultaneous voices.</summary>
        struct Layer
        {
            public string Clip; public float Vol, Jitter, MinDist; public int Voices;
            public Layer(string clip, float vol, float jitter, float minDist, int voices) { Clip = clip; Vol = vol; Jitter = jitter; MinDist = minDist; Voices = voices; }
        }
        readonly Dictionary<string, Layer[]> layered = new Dictionary<string, Layer[]>();

        // Music decks: [0] ambient pad, [1] combat stem, [2] boss stem, all the same length and started together.
        const int Stems = 3;
        [System.Serializable] class Deck { public AudioSource[] Src = new AudioSource[Stems]; public int Mood = -1; public float Fade; }
        Deck[] decks = new Deck[2];
        int activeDeck;
        readonly AudioClip[,] stemClips = new AudioClip[4, Stems];
        readonly Task<float[]>[,] stemJobs = new Task<float[]>[4, Stems];
        float threatTick, combatLevel, combatHoldUntil;

        /// <summary>Current stem mix (0..1), for dev logs and tests.</summary>
        public static float CombatMix { get; private set; }
        public static float BossMix { get; private set; }

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
            for (int d = 0; d < 2; d++)
            {
                decks[d] = new Deck();
                for (int k = 0; k < Stems; k++)
                {
                    var m = decks[d].Src[k] = gameObject.AddComponent<AudioSource>();
                    m.loop = true; m.spatialBlend = 0f; m.volume = 0f; m.priority = 0;
                }
            }
            BuildClips();
            BuildLayers();
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
            if (layered.TryGetValue(id, out var ls))
            {
                foreach (var l in ls) PlayClip(l.Clip, pos, true, volume * l.Vol, pitch * (1f + Random.Range(-l.Jitter, l.Jitter)), l.MinDist, l.Voices);
                return;
            }
            PlayClip(id, pos, true, volume, pitch, 6f, 0);
        }

        /// <summary>One pooled voice. voices &gt; 0 caps simultaneous copies of this clip (the oldest is stolen).</summary>
        void PlayClip(string id, Vector3 pos, bool spatial, float volume, float pitch, float minDist, int voices)
        {
            if (!clips.TryGetValue(id, out var c)) return;
            AudioSource s = null;
            if (voices > 0)
            {
                int count = 0; AudioSource oldest = null;
                foreach (var p in pool)
                    if (p.isPlaying && p.clip == c) { count++; if (oldest == null || p.time > oldest.time) oldest = p; }
                if (count >= voices) s = oldest;
            }
            if (s == null) s = FreeSource();
            if (s == null) return;
            s.Stop();
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = minDist;
            s.transform.position = pos;
            s.pitch = pitch;
            s.volume = volume * SfxVolume * (spatial ? 1f : 0.8f);
            s.clip = c;
            s.Play();
        }

        public void PlayUI(string id, float volume = 1f)
        {
            if (clips.TryGetValue(id, out var c)) ui.PlayOneShot(c, volume * SfxVolume * 0.8f);
        }

        public void Play2D(string id, float volume = 1f, float pitch = 1f)
        {
            if (layered.TryGetValue(id, out var ls))
            {
                foreach (var l in ls) PlayClip(l.Clip, Vector3.zero, false, volume * l.Vol, pitch * (1f + Random.Range(-l.Jitter, l.Jitter)), 6f, l.Voices);
                return;
            }
            PlayClip(id, Vector3.zero, false, volume, pitch, 6f, 0);
        }

        AudioSource FreeSource()
        {
            foreach (var p in pool) if (!p.isPlaying) return p;
            if (pool.Count >= 64) return null;
            var s = NewSource();
            pool.Add(s);
            return s;
        }

        public void SetMusicMood(int sectorIndex)
        {
            sectorIndex = Mathf.Clamp(sectorIndex, 0, 3);
            if (sectorIndex == mood) return;
            mood = sectorIndex;
            if (music[sectorIndex] == null) music[sectorIndex] = BuildMusic(sectorIndex);
            activeDeck = 1 - activeDeck;
            var d = decks[activeDeck];
            foreach (var src in d.Src) { src.Stop(); src.clip = null; src.volume = 0f; }
            d.Mood = sectorIndex;
            d.Fade = 0f;
            d.Src[0].clip = music[sectorIndex];
            d.Src[0].Play();
            // Combat / boss stems are synthesized off the main thread the first time a sector plays (menu: none).
            if (sectorIndex > 0)
                for (int k = 1; k < Stems; k++)
                    if (stemClips[sectorIndex, k] == null && stemJobs[sectorIndex, k] == null)
                    {
                        int m = sectorIndex, stem = k;
                        stemJobs[m, k] = Task.Run(() => SynthStem(m, stem));
                    }
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
            float targetVol = engineKeepAlive > 0f ? (0.04f + t * 0.35f) * SfxVolume : 0f;
            engine.volume = Mathf.MoveTowards(engine.volume, targetVol, Time.unscaledDeltaTime * 1.5f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.7f + t * 0.7f, Time.unscaledDeltaTime * 3f);

            UpdateMusic(Time.unscaledDeltaTime);
        }

        void UpdateMusic(float dt)
        {
            if (decks[0] == null || decks[0].Src[0] == null) return; // script reload in play mode
            // Finished stem jobs become clips (AudioClip.Create needs the main thread).
            for (int m = 1; m < 4; m++)
                for (int k = 1; k < Stems; k++)
                {
                    var job = stemJobs[m, k];
                    if (job == null || !job.IsCompleted) continue;
                    stemJobs[m, k] = null;
                    if (job.IsFaulted) { Debug.LogWarning("[Audio] music stem failed: " + job.Exception.GetBaseException().Message); continue; }
                    var c = AudioClip.Create("music_" + m + (k == 1 ? "_combat" : "_boss"), job.Result.Length, 1, Rate, false);
                    c.SetData(job.Result, 0);
                    stemClips[m, k] = c;
                }

            // Threat -> combat level: rises in ~1 s, holds a few seconds after the fight, then fades slowly.
            threatTick -= dt;
            if (threatTick <= 0f)
            {
                var cam = Camera.main;
                bool playing = GameState.Phase == GamePhase.Playing || GameState.Phase == GamePhase.Paused;
                Threat.Evaluate(cam != null && playing ? cam.transform.position : new Vector3(1e7f, 1e7f, 1e7f), 0.2f - threatTick);
                threatTick = 0.2f;
            }
            float now = Time.unscaledTime;
            float target = Sfx.DynamicMusic ? Threat.Level : 0f;
            if (target > 0.3f) combatHoldUntil = now + 5f;
            if (target >= combatLevel) combatLevel = Mathf.MoveTowards(combatLevel, target, dt / 1.2f);
            else if (now > combatHoldUntil) combatLevel = Mathf.MoveTowards(combatLevel, target, dt / 7f);
            CombatMix = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, combatLevel));
            BossMix = Mathf.MoveTowards(BossMix, Sfx.DynamicMusic && Threat.BossNear ? 1f : 0f, dt / 3f);

            float vol = MusicVolume * 0.6f;
            for (int i = 0; i < 2; i++)
            {
                var d = decks[i];
                d.Fade = Mathf.MoveTowards(d.Fade, i == activeDeck ? 1f : 0f, dt / 3f);
                var lead = d.Src[0];
                if (d.Fade <= 0f && i != activeDeck)
                {
                    foreach (var src in d.Src) if (src.isPlaying) src.Stop();
                    continue;
                }
                // The pad ducks a little under the fight; stems join sample-aligned to it.
                lead.volume = d.Fade * vol * (1f - 0.3f * Mathf.Max(CombatMix, BossMix));
                for (int k = 1; k < Stems; k++)
                {
                    var src = d.Src[k];
                    float mix = k == 1 ? Mathf.Max(CombatMix, BossMix * 0.8f) : BossMix;
                    src.volume = d.Fade * vol * mix * (k == 1 ? 0.9f : 1f);
                    if (d.Mood <= 0 || !lead.isPlaying) continue;
                    var clip = stemClips[d.Mood, k];
                    if (clip == null) continue;
                    if (src.clip != clip || !src.isPlaying) { src.clip = clip; src.timeSamples = lead.timeSamples; src.Play(); }
                    else if (Mathf.Abs(src.timeSamples - lead.timeSamples) > 4096 && lead.timeSamples > 8192 && lead.timeSamples < clip.samples - 8192)
                        src.timeSamples = lead.timeSamples; // drifted (e.g. after a hitch): resync
                }
            }
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
        /// <summary>Notes entering one after another every step seconds, each decaying exponentially.</summary>
        static float Arp(float t, float[] freqs, float step, float decay)
        {
            float v = 0f;
            for (int i = 0; i < freqs.Length; i++)
            {
                float x = t - i * step;
                if (x < 0f) break;
                v += Sin(freqs[i], x) * Mathf.Exp(-x * decay) * Mathf.Clamp01(x / 0.004f);
            }
            return v;
        }

        /// <summary>Weapon ids resolve to layers; the tail carries further (bigger min distance), mechanics only up close.</summary>
        void BuildLayers()
        {
            layered["laser"] = new[] { new Layer("laser_core", 1f, 0.05f, 6f, 0), new Layer("laser_tail", 0.5f, 0.08f, 16f, 4), new Layer("laser_mech", 0.55f, 0.1f, 2f, 3) };
            layered["laser_ship"] = new[] { new Layer("laser_ship_core", 1f, 0.05f, 8f, 0), new Layer("laser_ship_tail", 0.55f, 0.07f, 24f, 4), new Layer("laser_ship_mech", 0.5f, 0.1f, 3f, 3) };
            layered["rifle"] = new[] { new Layer("rifle_core", 1f, 0.05f, 6f, 0), new Layer("rifle_tail", 0.6f, 0.06f, 20f, 3), new Layer("rifle_mech", 0.5f, 0.08f, 2f, 2) };
            layered["missile"] = new[] { new Layer("missile_core", 0.9f, 0.05f, 8f, 0), new Layer("missile_tail", 1f, 0.05f, 14f, 4), new Layer("missile_mech", 0.5f, 0.06f, 3f, 2) };
            layered["flak"] = new[] { new Layer("flak_core", 1f, 0.06f, 8f, 0), new Layer("flak_tail", 0.6f, 0.08f, 24f, 4), new Layer("flak_mech", 0.45f, 0.1f, 3f, 2) };
        }

        void BuildClips()
        {
            float lp = 0f, lp2 = 0f;
            // Layered weapons (#43): core = the transient you feel, tail = decay that carries over distance,
            // mech = close-range mechanics (capacitor click, bolt, rail clack, shell). Combined in BuildLayers.
            clips["laser_core"] = Make("laser_core", 0.14f, (t, u, r) => (Sq(Mathf.Lerp(1600f, 380f, u), t) * 0.17f + Sin(Mathf.Lerp(2600f, 700f, u), t) * 0.16f) * Env(u, 0.004f));
            clips["laser_tail"] = Make("laser_tail", 0.55f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.06f); return (lp * 0.5f + Sin(Mathf.Lerp(720f, 260f, u), t) * 0.12f * (0.6f + 0.4f * Sin(23f, t))) * Mathf.Exp(-t * 7f) * Mathf.Clamp01(t / 0.02f); }, 21);
            clips["laser_mech"] = Make("laser_mech", 0.12f, (t, u, r) => { float x = t - 0.025f; return x < 0f ? 0f : (Noise(r) * Mathf.Exp(-x * 600f) * 0.3f + Sin(5200f, x) * Mathf.Exp(-x * 220f) * 0.18f + Sin(Mathf.Lerp(3000f, 6200f, u), x) * 0.04f * Mathf.Exp(-x * 30f)); }, 22);
            clips["laser_ship_core"] = Make("laser_ship_core", 0.22f, (t, u, r) => (Sin(Mathf.Lerp(900f, 160f, u), t) * 0.5f + Sq(Mathf.Lerp(450f, 90f, u), t) * 0.2f) * Env(u, 0.006f));
            clips["laser_ship_tail"] = Make("laser_ship_tail", 0.9f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.04f); return (lp * 1.0f + Sin(Mathf.Lerp(190f, 70f, u), t) * 0.25f) * Mathf.Exp(-t * 4.5f) * Mathf.Clamp01(t / 0.03f); }, 23);
            clips["laser_ship_mech"] = Make("laser_ship_mech", 0.2f, (t, u, r) => { float x = t - 0.05f; return x < 0f ? 0f : (Sin(140f, x) * Mathf.Exp(-x * 40f) * 0.45f + Noise(r) * Mathf.Exp(-x * 160f) * 0.25f); }, 24);
            clips["rifle_core"] = Make("rifle_core", 0.1f, (t, u, r) => { float n = Noise(r); lp = Mathf.Lerp(lp, n, 0.3f); return ((n - lp) * 0.8f + Sin(Mathf.Lerp(190f, 60f, u), t) * 0.6f) * Env(u, 0.002f); }, 25);
            clips["rifle_tail"] = Make("rifle_tail", 0.8f, (t, u, r) =>
            {
                float n = Noise(r);
                lp = Mathf.Lerp(lp, n, 0.06f);
                float slap = t > 0.11f ? n * 0.22f * Mathf.Exp(-(t - 0.11f) * 60f) : 0f; // early reflection
                return (lp * 1.2f * Mathf.Exp(-t * 5f) + slap) * Mathf.Clamp01(t / 0.01f);
            }, 26);
            clips["rifle_mech"] = Make("rifle_mech", 0.14f, (t, u, r) =>
            {
                float a = t - 0.035f, b = t - 0.075f, v = 0f;
                if (a >= 0f) v += (Noise(r) * 0.5f + Sin(3100f, a)) * Mathf.Exp(-a * 400f) * 0.3f;
                if (b >= 0f) v += (Noise(r) * 0.5f + Sin(2300f, b)) * Mathf.Exp(-b * 300f) * 0.25f;
                return v;
            }, 27);
            clips["missile_core"] = Make("missile_core", 0.3f, (t, u, r) => (Sin(Mathf.Lerp(120f, 45f, u), t) * 0.65f + Noise(r) * 0.4f * Mathf.Exp(-t * 30f)) * Env(u, 0.003f), 28);
            clips["missile_tail"] = Make("missile_tail", 0.7f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.15f); return lp * 1.4f * Env(u, 0.05f); });
            clips["missile_mech"] = Make("missile_mech", 0.16f, (t, u, r) => { float x = t - 0.02f; return x < 0f ? 0f : Sin(820f, x) * Sin(1230f, x) * Mathf.Exp(-x * 30f) * 0.4f; });
            clips["flak_core"] = Make("flak_core", 0.2f, (t, u, r) => (Noise(r) * 0.7f + Sin(110f, t) * 0.4f) * Env(u, 0.003f), 29);
            clips["flak_tail"] = Make("flak_tail", 0.7f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.035f); return (lp * 1.5f + Sin(Mathf.Lerp(70f, 40f, u), t) * 0.3f) * Mathf.Exp(-t * 4f) * Mathf.Clamp01(t / 0.02f); }, 30);
            clips["flak_mech"] = Make("flak_mech", 0.32f, (t, u, r) =>
            {
                float a = t - 0.08f, b = t - 0.17f, v = 0f;
                if (a >= 0f) v += (Sin(4200f, a) + Sin(5650f, a) * 0.6f) * Mathf.Exp(-a * 35f) * 0.12f;
                if (b >= 0f) v += (Sin(3900f, b) + Sin(6100f, b) * 0.5f) * Mathf.Exp(-b * 45f) * 0.08f;
                return v;
            });
            clips["explosion"] = Make("explosion", 0.9f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.08f); return (lp * 2.2f + Sin(Mathf.Lerp(90f, 30f, u), t) * 0.5f) * Env(u, 0.004f); }, 3);
            clips["explosion_big"] = Make("explosion_big", 2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.04f); return (lp * 2.8f + Sin(Mathf.Lerp(60f, 22f, u), t) * 0.6f) * Env(u, 0.004f); }, 4);
            clips["hit"] = Make("hit", 0.08f, (t, u, r) => (Noise(r) * 0.5f + Sin(220f, t) * 0.4f) * Env(u, 0.002f));
            // Hit feedback (#43): crisp tick, metallic crit ping, kill confirm = low punch + bright two-note chime.
            clips["hitmarker"] = Make("hitmarker", 0.06f, (t, u, r) => (Sin(Mathf.Lerp(3200f, 2300f, u), t) * 0.35f + Noise(r) * 0.15f * (u < 0.15f ? 1f : 0f)) * Env(u, 0.004f));
            clips["hit_crit"] = Make("hit_crit", 0.16f, (t, u, r) => (Sin(1900f, t) * 0.3f + Sin(2870f, t) * 0.2f + Sin(4100f, t) * 0.08f + Noise(r) * 0.12f * (u < 0.08f ? 1f : 0f)) * Env(u, 0.003f), 15);
            clips["kill"] = Make("kill", 0.42f, (t, u, r) => Sin(Mathf.Lerp(150f, 45f, u), t) * 0.7f * Env(u, 0.004f) * (1f - u)
                + (Sin(1320f, t) * (u < 0.3f ? 1f : 0f) + Sin(1980f, t) * (u >= 0.18f ? 1f : 0f)) * 0.18f * Env(u, 0.01f)
                + Noise(r) * 0.25f * Mathf.Clamp01(1f - u * 8f), 16);
            // Damage taken (#43): dull body thud, lub-dub heartbeat, shield collapse (falling sweep + glassy shards), hull klaxon.
            clips["hurt"] = Make("hurt", 0.2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.25f); return (Sin(Mathf.Lerp(130f, 55f, u), t) * 0.6f + lp * 0.7f * Mathf.Clamp01(1f - u * 4f)) * Env(u, 0.003f); }, 17);
            clips["heartbeat"] = Make("heartbeat", 0.55f, (t, u, r) =>
            {
                float a = t < 0.16f ? Mathf.Sin(Mathf.PI * t / 0.16f) : 0f;
                float b = t >= 0.2f && t < 0.34f ? Mathf.Sin(Mathf.PI * (t - 0.2f) / 0.14f) * 0.7f : 0f;
                return (Sin(Mathf.Lerp(62f, 44f, u), t) + Sin(31f, t) * 0.5f) * 0.75f * (a * a + b * b);
            });
            clips["shield_break"] = Make("shield_break", 1.1f, (t, u, r) =>
                Sin(Mathf.Lerp(1500f, 120f, Mathf.Sqrt(u)), t) * 0.35f * Env(u, 0.004f)
                + (Sin(2637f, t) + Sin(3520f, t) * 0.7f + Sin(4699f, t) * 0.4f) * Noise(r) * 0.22f * Mathf.Clamp01(1f - u * 2.5f)
                + Sin(Mathf.Lerp(90f, 40f, u), t) * 0.45f * Env(u, 0.01f), 18);
            clips["hull_alarm"] = Make("hull_alarm", 0.5f, (t, u, r) => (Sin(u < 0.5f ? 587f : 440f, t) * 0.25f + Sq(u < 0.5f ? 587f : 440f, t) * 0.05f) * Mathf.Clamp01(u * 40f) * Mathf.Clamp01((1f - u) * 12f));
            clips["shield_hit"] = Make("shield_hit", 0.3f, (t, u, r) => (Sin(Mathf.Lerp(1200f, 600f, u), t) * Sin(37f, t)) * 0.5f * Env(u));
            clips["pickup"] = Make("pickup", 0.18f, (t, u, r) => Sin(u < 0.5f ? 880f : 1320f, t) * 0.35f * Env(u, 0.01f));
            // Loot (#43): pop out, land (rare loot chimes), pickup chime per rarity, perk unlock fanfare.
            clips["loot_pop"] = Make("loot_pop", 0.2f, (t, u, r) => { lp2 = Mathf.Lerp(lp2, Noise(r), 0.2f); return (Sin(Mathf.Lerp(320f, 980f, u), t) * 0.3f + lp2 * 0.3f * Mathf.Exp(-t * 40f)) * Env(u, 0.01f); }, 31);
            clips["loot_land"] = Make("loot_land", 0.16f, (t, u, r) => Sin(Mathf.Lerp(190f, 85f, u), t) * 0.5f * Env(u, 0.003f) + Sin(2400f, t) * Mathf.Exp(-t * 80f) * 0.1f);
            clips["loot_land_rare"] = Make("loot_land_rare", 1.1f, (t, u, r) => Sin(Mathf.Lerp(190f, 85f, Mathf.Clamp01(t / 0.16f)), t) * 0.5f * Mathf.Exp(-t * 18f) * Mathf.Clamp01(t / 0.003f)
                + (Sin(1568f, t) * 0.16f + Sin(2349f, t) * 0.1f + Sin(3136f, t) * 0.05f) * Mathf.Exp(-t * 3.5f) * Mathf.Clamp01(t / 0.01f));
            clips["pickup_uncommon"] = Make("pickup_uncommon", 0.4f, (t, u, r) => Arp(t, new[] { 880f, 1109f, 1319f }, 0.05f, 9f) * 0.26f * (1f - u));
            clips["pickup_rare"] = Make("pickup_rare", 0.7f, (t, u, r) => (Arp(t, new[] { 988f, 1245f, 1480f, 1976f }, 0.055f, 6f) * 0.24f + Sin(3951f, t) * 0.03f * Sin(11f, t) * Mathf.Clamp01(t / 0.2f)) * (1f - u));
            clips["pickup_epic"] = Make("pickup_epic", 1.4f, (t, u, r) =>
                (Arp(t, new[] { 784f, 988f, 1175f, 1568f, 1976f }, 0.06f, 3.5f) * 0.2f
                + (Sin(1568f, t) + Sin(1976f, t) * 0.7f + Sin(2349f, t) * 0.5f) * 0.05f * (0.6f + 0.4f * Sin(7f, t)) * Mathf.Clamp01((t - 0.3f) * 4f)
                + Sin(65f, t) * 0.45f * Mathf.Exp(-t * 6f)) * (1f - u) * Mathf.Clamp01(t / 0.004f));
            clips["unlock"] = Make("unlock", 1.6f, (t, u, r) =>
            {
                lp2 = Mathf.Lerp(lp2, Noise(r), 0.02f + u * 0.15f);
                float whoosh = lp2 * 0.6f * Mathf.Clamp01(1f - t / 0.35f) * Mathf.Clamp01(t / 0.25f);
                float hit = Sin(Mathf.Lerp(110f, 55f, Mathf.Clamp01(t / 0.6f)), t) * 0.5f * Mathf.Exp(-(t - 0.3f) * 5f) * (t > 0.3f ? 1f : 0f);
                float arp = t > 0.3f ? Arp(t - 0.3f, new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.075f, 2.5f) * 0.18f : 0f;
                float pad = (Sin(523f, t) + Sin(659f, t) * 0.8f + Sin(784f, t) * 0.7f + Sin(1046f, t) * 0.4f) * 0.045f * Mathf.Clamp01((t - 0.5f) * 3f) * (1f - u);
                return (whoosh + hit + arp + pad) * Mathf.Clamp01((1f - u) * 6f);
            }, 32);
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
            clips["memories"] = Make("memories", 0.8f, (t, u, r) => (Sin(1568f, t) + Sin(2093f, t) * Sin(4f, t)) * 0.15f * Env(u, 0.02f));
            clips["atmo_entry"] = Make("atmo_entry", 2f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.02f + u * 0.1f); return lp * 2f * Mathf.Sin(Mathf.PI * u); }, 7);
            clips["crate"] = Make("crate", 0.25f, (t, u, r) => (Noise(r) * 0.3f + Sin(160f, t) * 0.5f) * Env(u, 0.003f));
            clips["seat"] = Make("seat", 0.3f, (t, u, r) => (Sin(220f, t) * 0.3f + Sin(330f, t) * 0.2f * (u > 0.5f ? 1 : 0)) * Env(u, 0.01f));
            clips["spit"] = Make("spit", 0.25f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.3f); return lp * Env(u, 0.01f) + Sin(Mathf.Lerp(400f, 150f, u), t) * 0.2f * Env(u); }, 8);
            clips["bite"] = Make("bite", 0.15f, (t, u, r) => (Noise(r) * 0.5f + Sq(90f, t) * 0.3f) * Env(u, 0.003f));
            clips["slam"] = Make("slam", 0.6f, (t, u, r) => (Sin(Mathf.Lerp(80f, 30f, u), t) * 0.8f + Noise(r) * 0.2f) * Env(u, 0.003f));
            clips["squish"] = Make("squish", 0.35f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.2f); return (lp * 1.2f + Sin(Mathf.Lerp(300f, 90f, u), t) * 0.3f) * Env(u, 0.005f); }, 9);
            clips["nest"] = Make("nest", 0.5f, (t, u, r) => Sin(Mathf.Lerp(90f, 160f, u) + Sin(8f, t) * 20f, t) * 0.4f * Env(u, 0.05f));
            // Intro sequence: deep swell, shot whoosh, radio squelch, typewriter blip, POI highlight chime, Collapse rumble.
            clips["intro_swell"] = Make("intro_swell", 5f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.01f); return (Sin(41f, t) * 0.35f + Sin(61.7f, t) * 0.22f + Sin(82f + Sin(0.3f, t) * 2f, t) * 0.12f + lp * 0.8f) * Mathf.Sin(Mathf.PI * u); }, 11);
            clips["whoosh"] = Make("whoosh", 1.3f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.015f + u * 0.12f); return lp * 2.2f * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(Mathf.PI * u); }, 12);
            clips["radio"] = Make("radio", 0.4f, (t, u, r) => (Noise(r) * 0.35f * (u < 0.55f ? 1f : 0f) + Sin(u < 0.55f ? 1850f : 1250f, t) * 0.2f) * Env(u, 0.01f), 13);
            clips["blip"] = Make("blip", 0.035f, (t, u, r) => Sq(1150f, t) * 0.12f * Env(u, 0.05f));
            clips["highlight"] = Make("highlight", 0.6f, (t, u, r) => (Sin(u < 0.25f ? 988f : 1480f, t) * 0.28f + Sin(2960f, t) * 0.06f) * Env(u, 0.01f));
            clips["rumble"] = Make("rumble", 3f, (t, u, r) => { lp = Mathf.Lerp(lp, Noise(r), 0.012f); return (lp * 2.6f + Sin(32f + Sin(0.7f, t) * 3f, t) * 0.4f) * Mathf.Sin(Mathf.PI * u); }, 14);
            // Tonal hum without a noise bed (the old filtered-noise loop read as constant hiss in space).
            // All partials complete whole cycles in 2 s, so the loop is seamless.
            clips["engine"] = Make("engine", 2f, (t, u, r) => (Sin(55f, t) * 0.32f + Sin(55.5f, t) * 0.2f + Sin(110f, t) * 0.14f + Sin(165f, t) * 0.05f) * (0.85f + 0.15f * Sin(1f, t)), 10);
        }

        static readonly float[][] Scales =
        {
            new[] { 0f, 3f, 7f, 10f, 14f },   // menu: minor 7 pad
            new[] { 0f, 4f, 7f, 11f, 14f },   // sector 1: maj7 dreamy
            new[] { 0f, 2f, 7f, 9f, 14f },    // sector 2: sus/pentatonic
            new[] { 0f, 3f, 6f, 10f, 13f },   // sector 3: tense diminished-ish
        };

        static readonly float[] Roots = { 110f, 130.81f, 98f, 87.31f };
        static readonly int[] Prog = { 0, 5, 3, 4 };

        /// <summary>Chord root (Hz) of the pad at time t: same 4 chords x 6 s progression as BuildMusic.</summary>
        static float ChordRoot(int moodIndex, float t, int voice = 0)
        {
            var scale = Scales[moodIndex];
            int ci = (int)(t / 6f) % 4;
            int idx = Prog[ci] + voice * 2;
            float semi = scale[idx % scale.Length] + (idx >= scale.Length ? 12f : 0f);
            return Roots[moodIndex] * Mathf.Pow(2f, semi / 12f);
        }

        /// <summary>
        /// Combat (stem 1) or boss (stem 2) layer for a sector mood: 24 s at 120 BPM (12 bars, chord change every
        /// 3 bars), in the pad's key, so it can be faded in sample-aligned over the ambient pad. Runs on a worker
        /// thread: pure math only. Notes that ring past the loop end wrap around, so the loop is seamless.
        /// </summary>
        static float[] SynthStem(int moodIndex, int stem)
        {
            const float len = 24f, beat = 0.5f;
            int n = (int)(len * Rate), pad = 2 * Rate;
            var buf = new float[n + pad];
            var r = new System.Random(500 + moodIndex * 10 + stem);
            float[] noise = new float[Rate / 2];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)r.NextDouble() * 2f - 1f;

            void Hit(float start, float dur, System.Func<float, float> f)
            {
                int i0 = (int)(start * Rate), cnt = (int)(dur * Rate);
                for (int i = 0; i < cnt; i++) buf[i0 + i] += f(i / (float)Rate);
            }
            float Nz(float x) => noise[(int)(x * Rate) % noise.Length];

            int beats = (int)(len / beat);
            if (stem == 1)
            {
                for (int b = 0; b < beats; b++)
                {
                    float tb = b * beat;
                    int inBar = b % 4;
                    // Kick on 1 and 3 (+ a pickup before every other bar), soft snare on 2 and 4.
                    if (inBar == 0 || inBar == 2) Hit(tb, 0.35f, x => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 45f, Mathf.Clamp01(x * 6f)) * x) * Mathf.Exp(-x * 14f) * 0.55f);
                    if (inBar == 3 && (b / 4) % 2 == 1) Hit(tb + beat * 0.5f, 0.3f, x => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(110f, 50f, Mathf.Clamp01(x * 6f)) * x) * Mathf.Exp(-x * 16f) * 0.4f);
                    if (inBar == 1 || inBar == 3) Hit(tb, 0.25f, x => (Nz(x) * 0.5f + Mathf.Sin(2f * Mathf.PI * 190f * x) * 0.3f) * Mathf.Exp(-x * 22f) * 0.32f);
                    // 16th ticks with accents (drive).
                    for (int k = 0; k < 4; k++)
                    {
                        float acc = k == 2 ? 1f : 0.5f;
                        Hit(tb + k * beat / 4f, 0.05f, x => Nz(x + 0.1f) * Mathf.Exp(-x * 90f) * 0.07f * acc);
                    }
                    // Bass ostinato in 8ths on the chord root: root, root, octave, root ...
                    float root = ChordRoot(moodIndex, tb) * 0.5f;
                    while (root > 75f) root *= 0.5f;
                    for (int k = 0; k < 2; k++)
                    {
                        float f = (b * 2 + k) % 4 == 2 ? root * 2f : root;
                        Hit(tb + k * beat / 2f, 0.24f, x =>
                        {
                            float w = 2f * Mathf.PI * f * x;
                            return (Mathf.Sin(w) + Mathf.Sin(2f * w) * 0.45f + Mathf.Sin(3f * w) * 0.2f) * Mathf.Exp(-x * 7f) * Mathf.Clamp01(x / 0.005f) * 0.2f;
                        });
                    }
                    // High staccato pulse on chord tones (quiet).
                    float hi = ChordRoot(moodIndex, tb, b % 3) * 4f;
                    Hit(tb + beat * 0.5f, 0.12f, x => Mathf.Sin(2f * Mathf.PI * hi * x) * Mathf.Exp(-x * 30f) * 0.05f);
                }
            }
            else
            {
                // Taiko-like toms in a driving 8th pattern, low brass drone + stabs on chord changes, tremolo strings.
                int[] pattern = { 1, 0, 1, 1, 0, 1, 0, 1 };
                for (int b = 0; b < beats * 2; b++)
                {
                    float tb = b * beat * 0.5f;
                    if (pattern[b % 8] == 0) continue;
                    float accent = b % 8 == 0 ? 1f : 0.65f;
                    float f0 = b % 8 == 0 ? 70f : 95f;
                    Hit(tb, 0.4f, x => (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(f0, f0 * 0.6f, Mathf.Clamp01(x * 4f)) * x) + Nz(x) * 0.25f * Mathf.Exp(-x * 40f)) * Mathf.Exp(-x * 9f) * 0.5f * accent);
                }
                for (int c = 0; c < 4; c++)
                {
                    float tc = c * 6f;
                    float f1 = ChordRoot(moodIndex, tc) * 0.5f, f2 = ChordRoot(moodIndex, tc, 1) * 0.5f, f3 = ChordRoot(moodIndex, tc, 2) * 0.5f;
                    var chord = new[] { f1, f2, f3 };
                    // Stab: brassy (odd + even harmonics), fast attack, decays over a bar.
                    Hit(tc, 2.2f, x =>
                    {
                        float v = 0f;
                        foreach (var f in chord)
                            for (int h = 1; h <= 5; h++) v += Mathf.Sin(2f * Mathf.PI * f * h * x * 1.001f) / h;
                        return v * 0.07f * Mathf.Exp(-x * 1.6f) * Mathf.Clamp01(x / 0.02f);
                    });
                    // Drone swells through the chord.
                    Hit(tc, 6f, x =>
                    {
                        float w = 2f * Mathf.PI * f1 * 0.5f * x;
                        float v = Mathf.Sin(w) + Mathf.Sin(2f * w * 1.003f) * 0.5f + Mathf.Sin(3f * w) * 0.25f + Mathf.Sin(w * 1.498f) * 0.4f;
                        return v * 0.08f * Mathf.Sin(Mathf.PI * x / 6f);
                    });
                    // Dissonant high tremolo (minor second above the root, 8 Hz tremolo).
                    float fs = f1 * 8f * Mathf.Pow(2f, 1f / 12f);
                    Hit(tc, 6f, x => Mathf.Sin(2f * Mathf.PI * fs * x) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 8f * x)) * 0.025f * Mathf.Sin(Mathf.PI * x / 6f));
                }
            }
            // Wrap the overflow to the start (seamless loop) and normalise.
            var data = new float[n];
            float peak = 0.001f;
            for (int i = 0; i < n; i++)
            {
                float v = buf[i] + (i < pad ? buf[n + i] : 0f);
                data[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            float g = 0.5f / peak;
            for (int i = 0; i < n; i++) data[i] *= g;
            return data;
        }

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
