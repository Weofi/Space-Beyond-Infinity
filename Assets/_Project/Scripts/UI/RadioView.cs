using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;

namespace Voidward.UI
{
    /// <summary>
    /// Client-local radio and logbook ticker, driven by UIRoot (the board computer's voice, #140, speaks through it):
    /// - Radio: compact captions on the left (speaker plate + typed text), queued one after another.
    /// - Logbook ticker: each crew logbook entry (RunManager.ServerLog) shows briefly under the objective.
    /// (Until #145 this was LoopView with the time loop's collapse, deja-vu and echoes.)
    /// </summary>
    public class RadioView
    {
        const float TypeSpeed = 42f;
        const float RadioHold = 4.5f;
        const float TickerTime = 6f;

        static readonly Color Plate = new Color(0.25f, 0.6f, 1f);

        readonly RectTransform root, radio, ticker;
        readonly TextMeshProUGUI radioSpeaker, radioBody, tickerText;
        readonly CanvasGroup radioGroup, tickerGroup;

        class Message { public string Speaker, Text; public float Delay; }
        readonly Queue<Message> queue = new Queue<Message>();
        Message current;
        float radioTime, radioWait;
        float tickerUntil = -1f;

        public RadioView(RectTransform canvas)
        {
            root = UIKit.Rect(canvas, "Radio");
            UIKit.Stretch(root);

            // Radio: compact caption on the left, speaker plate + typed text.
            radio = UIKit.Image(root, "Radio", UIKit.Frame).rectTransform;
            UIKit.Place(radio, new Vector2(0f, 0.5f), new Vector2(40f, -120f), new Vector2(620f, 128f), new Vector2(0f, 0.5f));
            radioGroup = radio.gameObject.AddComponent<CanvasGroup>();
            radioGroup.blocksRaycasts = false;
            var inner = UIKit.Image(radio, "Inner", new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, 0.94f));
            UIKit.Stretch(inner.rectTransform, 4, 4, 4, 4);
            var plate = UIKit.Image(radio, "Speaker", Plate);
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

            EventBus.LogbookEntry += OnLogbook;
        }

        public void Dispose() => EventBus.LogbookEntry -= OnLogbook;

        /// <summary>Queue a radio caption (shown one after another).</summary>
        public void Radio(string speaker, string text, float delay = 0f)
        {
            queue.Enqueue(new Message { Speaker = speaker, Text = text, Delay = delay });
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
            if (radioTime == 0f) { Sfx.PlayUI("ui_click"); Debug.Log($"[Radio] {current.Speaker}: {current.Text}"); }
            radioTime += Time.unscaledDeltaTime;
            float typeTime = current.Text.Length / TypeSpeed;
            radioGroup.alpha = Mathf.Clamp01(radioTime * 4f) * Mathf.Clamp01((typeTime + RadioHold - radioTime) * 2f);
            int typed = Mathf.Min(current.Text.Length, Mathf.FloorToInt(radioTime * TypeSpeed));
            radioBody.text = current.Text.Substring(0, typed);
            if (radioTime > typeTime + RadioHold) current = null;
        }

        void OnLogbook(string text)
        {
            tickerText.text = "<size=80%><color=#7DD3FC><b>LOGBOOK</b></color></size>\n" + text;
            tickerUntil = Time.unscaledTime + TickerTime;
        }

        public void Tick()
        {
            bool playing = GameState.Phase == GamePhase.Playing;
            if (!playing && current != null) ClearRadio();
            TickRadio();
            float tk = playing ? Mathf.Clamp01((tickerUntil - Time.unscaledTime) * 1.5f) : 0f;
            tickerGroup.alpha = tk;
        }
    }
}
