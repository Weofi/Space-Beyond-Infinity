using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Progression;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>
    /// Client-local presentation of the time loop (#86), driven by UIRoot:
    /// - Collapse: when a loop ends in death, time slows down and the Rift pulls the picture together (violet iris),
    ///   then the logbook (results screen) opens.
    /// - Deja-vu: a loop after the first starts with a violet wormhole flash and distorted crew radio, no cinematic.
    /// - Radio echoes: hints from earlier loops (StoryDef.Echoes) when the crew reaches a galaxy it has seen before.
    /// - Logbook ticker: each crew logbook entry (RunManager.ServerLog) shows briefly under the objective.
    /// </summary>
    public class LoopView
    {
        const float CollapseTime = 3.4f;
        const float SlowTo = 0.15f;
        const float TypeSpeed = 42f;
        const float RadioHold = 4.5f;
        const float TickerTime = 6f;
        const string GlitchChars = "#%/\\|<>*+=?";

        static readonly Color Violet = new Color(0.62f, 0.32f, 1f);

        readonly RectTransform root, iris, radio, ticker;
        readonly Image irisImage, black;
        readonly Image[] shutters = new Image[4];
        readonly TextMeshProUGUI resetText, radioSpeaker, radioBody, tickerText;
        readonly CanvasGroup radioGroup, tickerGroup, resetGroup;

        // collapse
        float collapseStart = -1f;
        Action collapseDone;
        // radio
        class Message { public string Speaker, Text; public bool Distorted; public float Delay; }
        readonly Queue<Message> queue = new Queue<Message>();
        Message current;
        float radioTime, radioWait;
        // echoes: galaxies (6 = flight home) already echoed in this loop
        readonly HashSet<int> echoed = new HashSet<int>();
        int echoLoopSeed = int.MinValue;
        // ticker
        float tickerUntil = -1f;
        // deja-vu radio, read a moment after the phase change: clients get the new loop number a little later
        float dejaVuRadioAt = -1f;
        const float DejaVuSettle = 0.6f;

        public bool Collapsing => collapseStart >= 0f;

        public LoopView(RectTransform canvas)
        {
            root = UIKit.Rect(canvas, "Loop");
            UIKit.Stretch(root);

            // Collapse: an iris of violet light closing on the screen centre, black behind it at the end.
            black = UIKit.Image(root, "Black", new Color(0, 0, 0, 0), false);
            UIKit.Stretch(black.rectTransform);
            black.raycastTarget = false;
            irisImage = UIKit.Image(root, "Iris", Color.white, false);
            irisImage.sprite = IrisSprite();
            irisImage.raycastTarget = false;
            iris = irisImage.rectTransform;
            UIKit.Place(iris, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000));
            // The iris sprite is a square: shutters on its four edges keep the dark outside closed while it shrinks.
            for (int i = 0; i < 4; i++)
            {
                var sh = UIKit.Image(iris, "Shutter" + i, Color.black, false);
                sh.raycastTarget = false;
                var rt = sh.rectTransform;
                Vector2 dir = i == 0 ? Vector2.left : i == 1 ? Vector2.right : i == 2 ? Vector2.up : Vector2.down;
                Vector2 edge = new Vector2(0.5f, 0.5f) + dir * 0.5f;
                rt.anchorMin = rt.anchorMax = edge;
                rt.pivot = new Vector2(0.5f, 0.5f) - dir * 0.5f;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = dir.x != 0f ? new Vector2(9000f, 18000f) : new Vector2(18000f, 9000f);
                shutters[i] = sh;
            }
            var resetRt = UIKit.Rect(root, "Reset");
            UIKit.Stretch(resetRt);
            resetGroup = resetRt.gameObject.AddComponent<CanvasGroup>();
            resetGroup.blocksRaycasts = false;
            resetText = UIKit.Text(resetRt, "", 64, TextAlignmentOptions.Center, UIKit.TextColor, FontStyles.Bold | FontStyles.UpperCase);
            UIKit.Place(resetText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600, 200));
            resetText.characterSpacing = 14;
            resetText.fontSharedMaterial = UIKit.ShadowMaterial;

            // Radio: compact caption on the left, speaker plate + typed text.
            radio = UIKit.Image(root, "Radio", UIKit.Frame).rectTransform;
            UIKit.Place(radio, new Vector2(0f, 0.5f), new Vector2(40f, -120f), new Vector2(620f, 128f), new Vector2(0f, 0.5f));
            radioGroup = radio.gameObject.AddComponent<CanvasGroup>();
            radioGroup.blocksRaycasts = false;
            var inner = UIKit.Image(radio, "Inner", new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, 0.94f));
            UIKit.Stretch(inner.rectTransform, 4, 4, 4, 4);
            var plate = UIKit.Image(radio, "Speaker", Violet);
            plate.sprite = UIKit.ChamferFace;
            UIKit.Place(plate.rectTransform, new Vector2(0f, 1f), new Vector2(-12f, 20f), new Vector2(330f, 42f), new Vector2(0f, 1f));
            radioSpeaker = UIKit.Text(plate.transform, "", 21, TextAlignmentOptions.Left, UIKit.TextColor, FontStyles.Bold | FontStyles.UpperCase);
            UIKit.Stretch(radioSpeaker.rectTransform, 18, 10, 0, 3);
            radioSpeaker.characterSpacing = 3;
            radioSpeaker.textWrappingMode = TextWrappingModes.NoWrap;
            radioBody = UIKit.Text(radio, "", 22, TextAlignmentOptions.TopLeft, UIKit.TextColor);
            UIKit.Stretch(radioBody.rectTransform, 22, 22, 32, 14);
            radioGroup.alpha = 0f;

            // Logbook ticker under the objective (top left).
            ticker = UIKit.Rect(root, "Ticker");
            UIKit.Place(ticker, new Vector2(0f, 1f), new Vector2(40f, -196f), new Vector2(640f, 60f), new Vector2(0f, 1f));
            tickerGroup = ticker.gameObject.AddComponent<CanvasGroup>();
            tickerGroup.blocksRaycasts = false;
            tickerText = UIKit.Text(ticker, "", 21, TextAlignmentOptions.TopLeft, UIKit.TextColor);
            UIKit.Stretch(tickerText.rectTransform);
            tickerText.fontSharedMaterial = UIKit.ShadowMaterial;
            tickerGroup.alpha = 0f;

            SetCollapseVisible(false);
            EventBus.LogbookEntry += OnLogbook;
            EventBus.SystemEntered += OnSystemEntered;
        }

        public void Dispose()
        {
            EventBus.LogbookEntry -= OnLogbook;
            EventBus.SystemEntered -= OnSystemEntered;
            Time.timeScale = 1f;
        }

        // ================================================================ collapse

        /// <summary>The loop ends in death: slow motion, the Rift closes the picture, then `done` (open the logbook).</summary>
        public void Collapse(RunSummary s, Action done)
        {
            collapseStart = Time.unscaledTime;
            collapseDone = done;
            resetText.text = "THE LOOP RESETS\n<size=45%><color=#C9A8FF>" + (s != null ? s.EndReason : "") + "</color></size>";
            ClearRadio();
            SetCollapseVisible(true);
            Vfx.Shake(0.8f);
            Vfx.ScreenFlash(new Color(Violet.r, Violet.g, Violet.b, 0.5f), 1.2f);
            Sfx.PlayUI("jump_charge");
        }

        /// <summary>Abort a running collapse (phase change) and restore time.</summary>
        public void StopCollapse()
        {
            if (collapseStart < 0f) return;
            collapseStart = -1f;
            collapseDone = null;
            Time.timeScale = 1f;
            SetCollapseVisible(false);
        }

        void SetCollapseVisible(bool on)
        {
            iris.gameObject.SetActive(on);
            black.gameObject.SetActive(on);
            resetGroup.gameObject.SetActive(on);
            if (!on) return;
            black.color = new Color(0, 0, 0, 0);
            resetGroup.alpha = 0f;
        }

        void TickCollapse()
        {
            float t = (Time.unscaledTime - collapseStart) / CollapseTime;
            float k = Mathf.Clamp01(t);
            // Time slows down to a crawl in the first third and stays there.
            Time.timeScale = Mathf.Lerp(1f, SlowTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 3f)));
            // The iris closes from beyond the screen corners to a point, accelerating like a pull.
            float pull = k * k * (3f - 2f * k);
            float size = Mathf.Lerp(4400f, 60f, pull);
            iris.sizeDelta = new Vector2(size, size);
            iris.localRotation = Quaternion.Euler(0, 0, -k * 160f);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 18f);
            irisImage.color = new Color(Mathf.Lerp(1f, 0.2f, k), Mathf.Lerp(0.8f, 0.05f, k), 1f, 1f) * pulse;
            // Same colour as the sprite's dark outside (0.08 x (0.8, 0.55, 1) x tint).
            var c = irisImage.color;
            var outside = new Color(c.r * 0.064f, c.g * 0.044f, c.b * 0.08f, 1f);
            foreach (var sh in shutters) sh.color = outside;
            black.color = new Color(0, 0, 0, Mathf.Clamp01((k - 0.55f) / 0.35f));
            resetGroup.alpha = Mathf.Clamp01((k - 0.62f) / 0.2f);
            if (UnityEngine.Random.value < 0.25f) Vfx.Shake(0.15f * (1f - k));
            if (t < 1.25f) return;
            var done = collapseDone;
            StopCollapse();
            done?.Invoke();
        }

        /// <summary>Transparent centre, violet rim, black outside: scaling it down closes the picture.</summary>
        static Sprite irisSprite;
        static Sprite IrisSprite()
        {
            if (irisSprite != null) return irisSprite;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VW_LoopIris" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float rim = Mathf.Exp(-Mathf.Pow((r - 0.42f) / 0.07f, 2f));
                    float outside = Mathf.SmoothStep(0.36f, 0.5f, r);
                    float a = Mathf.Clamp01(outside + rim);
                    // white rim (tinted violet by the image colour), dark violet to black outside
                    float bright = Mathf.Clamp01(rim * 1.4f) + (1f - outside) * 0.6f;
                    float c = Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(bright));
                    px[y * n + x] = new Color(c * 0.8f, c * 0.55f, c, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            irisSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return irisSprite;
        }

        // ================================================================ deja-vu + radio

        /// <summary>A loop after the first: violet wormhole flash and distorted crew radio, then the M33 echo if any.</summary>
        public void DejaVu()
        {
            Vfx.ScreenFlash(new Color(Violet.r, Violet.g, Violet.b, 0.85f), 1.8f);
            Vfx.Shake(0.5f);
            Sfx.PlayUI("jump_gate");
            // The phase arrives before the loop number and run seed on clients: queue the radio once they settled.
            dejaVuRadioAt = Time.unscaledTime + DejaVuSettle;
        }

        void DejaVuRadio()
        {
            dejaVuRadioAt = -1f;
            ResetEchoes();
            int loop = RunManager.Instance != null ? RunManager.Instance.Loop.Value : Meta.Loops + 1;
            var story = Story;
            var line = story != null ? story.DejaVuFor(loop) : null;
            if (line != null) Radio($"{line.Speaker} // LOOP {loop}", line.Text, true, 0.8f);
            TryEcho(1, loop);
        }

        /// <summary>Queue a radio caption (shown one after another).</summary>
        public void Radio(string speaker, string text, bool distorted, float delay = 0f)
        {
            queue.Enqueue(new Message { Speaker = speaker, Text = text, Distorted = distorted, Delay = delay });
        }

        void ClearRadio()
        {
            queue.Clear();
            current = null;
            radioGroup.alpha = 0f;
        }

        void TickRadio()
        {
            if (current == null)
            {
                if (queue.Count == 0) { radioGroup.alpha = Mathf.MoveTowards(radioGroup.alpha, 0f, Time.unscaledDeltaTime * 3f); return; }
                current = queue.Dequeue();
                radioWait = current.Delay;
                radioTime = 0f;
                radioSpeaker.text = current.Speaker;
                radioBody.text = "";
            }
            if (radioWait > 0f) { radioWait -= Time.unscaledDeltaTime; return; }
            if (radioTime == 0f) { Sfx.PlayUI("ui_click"); Debug.Log($"[Loop] radio {current.Speaker}: {current.Text}"); }
            radioTime += Time.unscaledDeltaTime;
            float typeTime = current.Text.Length / TypeSpeed;
            radioGroup.alpha = Mathf.Clamp01(radioTime * 4f) * Mathf.Clamp01((typeTime + RadioHold - radioTime) * 2f);
            int typed = Mathf.Min(current.Text.Length, Mathf.FloorToInt(radioTime * TypeSpeed));
            radioBody.text = current.Distorted ? Distort(current.Text, typed, radioTime) : current.Text.Substring(0, typed);
            if (radioTime > typeTime + RadioHold) current = null;
        }

        /// <summary>Typed text with a flickering glitch: a few characters of the typed part and a short tail of noise.</summary>
        static string Distort(string text, int typed, float time)
        {
            var sb = new StringBuilder(typed + 4);
            int seed = Mathf.FloorToInt(time * 14f);
            for (int i = 0; i < typed; i++)
            {
                char ch = text[i];
                uint h = (uint)(i * 73856093 ^ seed * 19349663);
                bool glitch = ch != ' ' && h % 23 == 0 && time < text.Length / TypeSpeed + 2.5f;
                sb.Append(glitch ? GlitchChars[(int)(h / 23 % (uint)GlitchChars.Length)] : ch);
            }
            if (typed < text.Length)
                for (int i = 0; i < 3; i++) sb.Append(GlitchChars[(seed + i * 5) % GlitchChars.Length]);
            return "<color=#D9C8FF>" + sb + "</color>";
        }

        // ================================================================ echoes

        static StoryDef Story => ContentCatalog.Instance != null ? ContentCatalog.Instance.Story : null;

        static int CurrentLoop => RunManager.Instance != null ? RunManager.Instance.Loop.Value : 1;

        void ResetEchoes()
        {
            echoed.Clear();
            echoLoopSeed = RunManager.Instance != null ? RunManager.Instance.RunSeed.Value : 0;
        }

        void OnSystemEntered(int galaxy, int system)
        {
            // A new run (seed) clears the echoes; the deja-vu itself plays the M33 echo.
            int seed = RunManager.Instance != null ? RunManager.Instance.RunSeed.Value : 0;
            if (seed != echoLoopSeed) { echoed.Clear(); echoLoopSeed = seed; echoed.Add(1); }
            TryEcho(galaxy, CurrentLoop);
        }

        /// <summary>Echo from an earlier loop: only where this player has been before (best galaxy of the meta save).</summary>
        void TryEcho(int galaxy, int loop)
        {
            if (loop < 2 || echoed.Contains(galaxy)) return;
            int best = Meta.Data.BestSector;
            if (galaxy > best && !(galaxy == DriveMath.Home && best >= GalaxyDef.Count)) return;
            var story = Story;
            var e = story != null ? story.EchoFor(galaxy, loop) : null;
            if (e == null) return;
            echoed.Add(galaxy);
            Radio(e.Speaker, e.Text, true, galaxy == 1 ? 1f : 4f);
        }

        // ================================================================ logbook ticker

        void OnLogbook(string text)
        {
            tickerText.text = "<size=80%><color=#C9A8FF><b>LOGBOOK</b></color></size>\n" + text;
            tickerUntil = Time.unscaledTime + TickerTime;
        }

        // ================================================================ per-frame

        public void Tick()
        {
            if (collapseStart >= 0f) TickCollapse();
            if (dejaVuRadioAt >= 0f && Time.unscaledTime >= dejaVuRadioAt) DejaVuRadio();
            bool playing = GameState.Phase == GamePhase.Playing;
            if (!playing && current != null) ClearRadio();
            TickRadio();
            // The flight home echo: when the finale warp starts.
            var wf = WarpFlight.Instance;
            if (playing && wf != null && wf.IsFinale) TryEcho(DriveMath.Home, CurrentLoop);
            float tk = playing ? Mathf.Clamp01((tickerUntil - Time.unscaledTime) * 1.5f) : 0f;
            tickerGroup.alpha = tk;
        }
    }
}
