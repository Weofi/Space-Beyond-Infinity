using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>
    /// Client-local intro cinematic at the start of a run: a title card, then eased camera moves over
    /// the key POIs (own ship, the destination galaxy, Ark, planet, nest, station) with fades, letterbox,
    /// radio-style captions and POI highlights. Each caption waits for the player (#24): click or any key
    /// reveals the full text, the next press moves on. Esc or hold Space skips. The last shot ends behind
    /// the local cockpit and Cinemachine blends into the player camera. Driven by UIRoot.
    /// </summary>
    public class IntroSequence
    {
        const float TitleCardTime = 2.6f;
        const float ShotFade = 0.45f;
        const float FirstFade = 1.1f;
        const float HoldToSkip = 0.8f;
        const float EndBlend = 1.8f;
        const float SkipFadeOut = 0.25f;
        const float SkipFadeIn = 0.6f;
        const float Letterbox = 118f;
        const float TypeSpeed = 48f;
        const int Priority = 100;

        class Shot
        {
            public string Title, Body, Label, Sfx;
            public float Duration;
            public Vector3 FromPos, ToPos, FromLook, ToLook;
            public Vector3? Highlight;
            public Vector3 Up = Vector3.up;
            public bool ShowDistance = true;
        }

        enum State { Idle, Playing, SkipFade, Release }

        readonly RectTransform root, caption, marker, markerLabelBg, skipHint, titleCard, continueHint;
        readonly Image black, barTop, barBottom, skipFill, liveDot;
        readonly TextMeshProUGUI captionTitle, captionBody, markerLabel, cardSector, cardName;
        readonly CanvasGroup captionGroup, markerGroup, cardGroup, continueGroup;
        readonly List<Func<Shot>> builders = new List<Func<Shot>>();

        CinemachineCamera cam;
        CinemachineBrain brain;
        CinemachineBlendDefinition savedBlend;
        State state;
        Shot shot;
        int shotIndex;
        float shotTime, totalTime, stateTime, holdTime, releaseDuration, blackAtSkip;
        int typed;
        bool highlightPlayed, revealAll;
        float outTime = -1f;     // >= 0 once the player asked for the next caption (fade-out clock)
        float spaceDown;
        Vector3 anchor;

        public bool Active => state != State.Idle;

        public IntroSequence(RectTransform canvas)
        {
            root = UIKit.Rect(canvas, "Intro");
            UIKit.Stretch(root);

            barTop = UIKit.Image(root, "BarTop", Color.black, false);
            UIKit.Place(barTop.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, Letterbox), new Vector2(0.5f, 0f));
            barTop.rectTransform.anchorMin = new Vector2(0f, 1f);
            barTop.rectTransform.anchorMax = new Vector2(1f, 1f);
            barBottom = UIKit.Image(root, "BarBottom", Color.black, false);
            UIKit.Place(barBottom.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, Letterbox), new Vector2(0.5f, 1f));
            barBottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            barBottom.rectTransform.anchorMax = new Vector2(1f, 0f);

            // POI highlight: callout pin (dot on the target, stem, gold diamond) + label plate.
            marker = UIKit.Rect(root, "Highlight");
            marker.sizeDelta = new Vector2(40, 40);
            markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            var dot = UIKit.Image(marker, "Dot", UIKit.Accent2);
            dot.sprite = UIKit.Circle;
            UIKit.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            var stem = UIKit.Image(marker, "Stem", new Color(UIKit.Accent2.r, UIKit.Accent2.g, UIKit.Accent2.b, 0.8f), false);
            UIKit.Place(stem.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(2, 30), new Vector2(0.5f, 0f));
            var outer = UIKit.Image(marker, "Diamond", UIKit.Accent2, false);
            UIKit.Place(outer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 52), new Vector2(30, 30));
            outer.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var inner = UIKit.Image(outer.transform, "Inner", new Color(0.04f, 0.08f, 0.18f, 0.85f), false);
            UIKit.Stretch(inner.rectTransform, 5, 5, 5, 5);
            var core = UIKit.Image(outer.transform, "Core", new Color(0.55f, 0.95f, 1f), false);
            UIKit.Place(core.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            var labelBg = UIKit.Image(marker, "LabelBg", new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, 0.82f));
            markerLabelBg = labelBg.rectTransform;
            UIKit.Place(markerLabelBg, new Vector2(0.5f, 0.5f), new Vector2(28, 52), new Vector2(200, 56), new Vector2(0f, 0.5f));
            markerLabel = UIKit.Text(markerLabelBg, "", 22, TextAlignmentOptions.MidlineLeft, UIKit.TextColor, FontStyles.Bold);
            UIKit.Stretch(markerLabel.rectTransform, 14, 10, 4, 4);
            markerLabel.textWrappingMode = TextWrappingModes.NoWrap;

            // Caption: chamfered frame, navy interior, blue title plate, typed body text.
            caption = UIKit.Image(root, "Caption", UIKit.Frame).rectTransform;
            UIKit.Place(caption, new Vector2(0f, 0f), new Vector2(90f, Letterbox + 34f), new Vector2(880f, 180f), Vector2.zero);
            captionGroup = caption.gameObject.AddComponent<CanvasGroup>();
            var capInner = UIKit.Image(caption, "Inner", new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, 0.94f));
            UIKit.Stretch(capInner.rectTransform, 5, 5, 5, 5);
            var plate = UIKit.Image(caption, "Title", UIKit.Primary);
            plate.sprite = UIKit.ChamferFace;
            UIKit.Place(plate.rectTransform, new Vector2(0f, 1f), new Vector2(-18f, 26f), new Vector2(600f, 58f), new Vector2(0f, 1f));
            captionTitle = UIKit.Text(plate.transform, "", 28, TextAlignmentOptions.Left, UIKit.TextColor, FontStyles.Bold | FontStyles.UpperCase);
            UIKit.Stretch(captionTitle.rectTransform, 22, 12, 0, 4);
            captionTitle.characterSpacing = 4;
            captionTitle.enableAutoSizing = true;
            captionTitle.fontSizeMin = 18;
            captionTitle.fontSizeMax = 28;
            captionTitle.fontSharedMaterial = UIKit.ShadowMaterial;
            captionTitle.textWrappingMode = TextWrappingModes.NoWrap;
            liveDot = UIKit.Image(caption, "Live", UIKit.Bad);
            liveDot.sprite = UIKit.Circle;
            UIKit.Place(liveDot.rectTransform, new Vector2(1f, 1f), new Vector2(-240f, -22f), new Vector2(12f, 12f), new Vector2(0.5f, 0.5f));
            var live = UIKit.Text(caption, "MISSION CONTROL // LIVE", 16, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold);
            UIKit.Place(live.rectTransform, new Vector2(1f, 1f), new Vector2(-226f, -22f), new Vector2(220f, 24f), new Vector2(0f, 0.5f));
            captionBody = UIKit.Text(caption, "", 25, TextAlignmentOptions.TopLeft, UIKit.TextColor);
            UIKit.Stretch(captionBody.rectTransform, 30, 30, 50, 34);

            // "Continue" prompt pinned to the caption's lower-right corner (pulses while waiting).
            var cont = UIKit.Image(caption, "Continue", UIKit.Accent2);
            cont.sprite = UIKit.ChamferFace;
            continueHint = cont.rectTransform;
            UIKit.Place(continueHint, new Vector2(1f, 0f), new Vector2(-16f, -20f), new Vector2(250f, 40f), new Vector2(1f, 0f));
            continueGroup = cont.gameObject.AddComponent<CanvasGroup>();
            var contText = UIKit.Text(continueHint, "CLICK / ANY KEY  >", 19, TextAlignmentOptions.Center, new Color(0.05f, 0.07f, 0.15f), FontStyles.Bold);
            UIKit.Stretch(contText.rectTransform, 8, 8, 0, 2);
            contText.characterSpacing = 3;
            continueGroup.alpha = 0f;

            // Skip hint with hold progress, inside the bottom letterbox bar.
            skipHint = UIKit.Rect(root, "SkipHint");
            UIKit.Place(skipHint, new Vector2(1f, 0f), new Vector2(-70f, Letterbox * 0.5f), new Vector2(720f, 50f), new Vector2(1f, 0.5f));
            var hint = UIKit.Text(skipHint, "<color=#FFBD40>CLICK</color> CONTINUE     <color=#FFBD40>ESC</color> SKIP     HOLD <color=#FFBD40>SPACE</color> SKIP", 20, TextAlignmentOptions.Right, UIKit.TextDim, FontStyles.Bold);
            UIKit.Place(hint.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(720f, 30f), new Vector2(1f, 1f));
            var track = UIKit.Image(skipHint, "Track", new Color(1f, 1f, 1f, 0.12f), false);
            UIKit.Place(track.rectTransform, new Vector2(1f, 0f), Vector2.zero, new Vector2(200f, 5f), new Vector2(1f, 0f));
            skipFill = UIKit.Image(track.transform, "Fill", UIKit.Accent2, false);
            UIKit.Stretch(skipFill.rectTransform);
            skipFill.type = Image.Type.Filled;
            skipFill.fillMethod = Image.FillMethod.Horizontal;
            skipFill.sprite = UIKit.Chamfer;
            skipFill.fillAmount = 0f;

            black = UIKit.Image(root, "Black", Color.black, false);
            UIKit.Stretch(black.rectTransform);

            titleCard = UIKit.Rect(root, "TitleCard");
            UIKit.Stretch(titleCard);
            cardGroup = titleCard.gameObject.AddComponent<CanvasGroup>();
            cardSector = UIKit.Text(titleCard, "", 28, TextAlignmentOptions.Center, UIKit.Accent, FontStyles.Bold);
            UIKit.Place(cardSector.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1400f, 40f));
            cardSector.characterSpacing = 18;
            cardName = UIKit.Text(titleCard, "", 86, TextAlignmentOptions.Center, UIKit.TextColor, FontStyles.Bold | FontStyles.UpperCase);
            UIKit.Place(cardName.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1600f, 110f));
            cardName.characterSpacing = 10;
            var rule = UIKit.Image(titleCard, "Rule", UIKit.Accent2, false);
            UIKit.Place(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -66f), new Vector2(360f, 4f));
            var sub = UIKit.Text(titleCard, "INCOMING TRANSMISSION", 22, TextAlignmentOptions.Center, UIKit.TextDim, FontStyles.Bold);
            UIKit.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(1000f, 32f));
            sub.characterSpacing = 12;

            builders.Add(ArrivalShot);
            builders.Add(LostShot);
            builders.Add(ArkShot);
            builders.Add(PlanetShot);
            builders.Add(NestShot);
            builders.Add(StationShot);
            builders.Add(HelmShot);

            root.gameObject.SetActive(false);
        }

        // ================================================================ control

        public void Begin()
        {
            if (Camera.main == null) return;
            brain = Camera.main.GetComponent<CinemachineBrain>();
            if (cam == null)
            {
                var go = new GameObject("IntroCamera");
                cam = go.AddComponent<CinemachineCamera>();
                var lens = cam.Lens;
                lens.FieldOfView = 52f; lens.NearClipPlane = 0.1f; lens.FarClipPlane = 20000f;
                cam.Lens = lens;
            }
            if (state == State.Idle && brain != null) savedBlend = brain.DefaultBlend;
            if (brain != null) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            cam.Priority = Priority;
            cam.enabled = true;

            var galaxy = ContentCatalog.GetGalaxy(1);
            cardSector.text = galaxy != null ? $"GALAXY 1 - {galaxy.Designation}" : "GALAXY 1";
            cardName.text = galaxy != null ? galaxy.DisplayName : "TRIANGULUM";

            state = State.Playing;
            shot = null;
            shotIndex = -1;
            shotTime = totalTime = stateTime = holdTime = 0f;
            GameState.Cinematic = true;
            root.gameObject.SetActive(true);
            black.color = Color.black;
            captionGroup.alpha = 0f;
            markerGroup.alpha = 0f;
            cardGroup.alpha = 0f;
            SetLetterbox(0f);
            Sfx.PlayUI("intro_swell", 0.9f);
            Debug.Log("[Intro] Started");
        }

        public void Skip()
        {
            if (state != State.Playing) return;
            state = State.SkipFade;
            stateTime = 0f;
            blackAtSkip = black.color.a;
            Sfx.PlayUI("ui_back", 0.6f);
            Debug.Log("[Intro] Skipped");
        }

        /// <summary>Immediate teardown (left the run or the session).</summary>
        public void Abort()
        {
            if (state == State.Idle) return;
            Finish();
        }

        void Release(float duration, bool cut)
        {
            state = State.Release;
            stateTime = 0f;
            releaseDuration = duration;
            if (brain != null)
                brain.DefaultBlend = cut
                    ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                    : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, duration);
            // Keep the intro camera alive (lowest priority) so the brain blends out of a valid pose.
            if (cam != null) cam.Priority = 0;
        }

        void Finish()
        {
            state = State.Idle;
            if (cam != null) cam.enabled = false;
            if (brain != null) brain.DefaultBlend = savedBlend;
            GameState.Cinematic = false;
            root.gameObject.SetActive(false);
            Debug.Log("[Intro] Finished");
        }

        // ================================================================ per-frame

        public void Update()
        {
            if (state == State.Idle) return;
            if (GameState.Phase != GamePhase.Playing || Camera.main == null) { Abort(); return; }
            float dt = Time.deltaTime;
            totalTime += dt;
            stateTime += dt;
            liveDot.enabled = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;

            switch (state)
            {
                case State.Playing: TickPlaying(dt); break;
                case State.SkipFade:
                    black.color = new Color(0, 0, 0, Mathf.Lerp(blackAtSkip, 1f, stateTime / SkipFadeOut));
                    captionGroup.alpha = markerGroup.alpha = Mathf.Min(captionGroup.alpha, 1f - stateTime / SkipFadeOut);
                    if (stateTime >= SkipFadeOut)
                    {
                        captionGroup.alpha = markerGroup.alpha = cardGroup.alpha = 0f;
                        Release(SkipFadeIn, true);
                    }
                    break;
                case State.Release:
                    float k = Mathf.Clamp01(stateTime / releaseDuration);
                    SetLetterbox(1f - Ease(k));
                    captionGroup.alpha = Mathf.Min(captionGroup.alpha, 1f - k * 3f);
                    markerGroup.alpha = 0f;
                    skipHint.gameObject.SetActive(false);
                    if (black.color.a > 0f) black.color = new Color(0, 0, 0, 1f - k);
                    // Don't hand over while Space is still held (it is also ship thrust).
                    if (k >= 1f && !VwInput.Held(VwInput.Jump)) Finish();
                    break;
            }
        }

        void TickPlaying(float dt)
        {
            SetLetterbox(Ease(Mathf.Clamp01(totalTime / 0.9f)));
            skipHint.gameObject.SetActive(true);

            holdTime = VwInput.Held(VwInput.Jump) ? holdTime + dt : Mathf.Max(0f, holdTime - dt * 2f);
            skipFill.fillAmount = holdTime / HoldToSkip;
            if (holdTime >= HoldToSkip) { Skip(); return; }

            if (shotIndex < 0)
            {
                // Title card over black while the sector finishes spawning on this client.
                cardGroup.alpha = Mathf.Clamp01(totalTime / 0.5f) * Mathf.Clamp01((TitleCardTime - totalTime) / 0.5f);
                if (totalTime >= TitleCardTime && !NextShot()) Release(EndBlend, false);
                return;
            }

            shotTime += dt;
            bool last = IsLastShot();
            float d = shot.Duration;
            float fadeIn = shotIndex == 0 ? FirstFade : ShotFade;

            // Caption text: typed out, a press reveals it all, the next press advances (no auto-advance).
            int visible = revealAll ? shot.Body.Length : Mathf.Clamp((int)((shotTime - 0.4f) * TypeSpeed), 0, shot.Body.Length);
            bool typedOut = visible >= shot.Body.Length;
            if (outTime < 0f && shotTime > 0.35f && AdvancePressed())
            {
                if (!typedOut) { revealAll = true; visible = shot.Body.Length; typed = visible; }
                else { outTime = 0f; Sfx.PlayUI("ui_click", 0.6f); }
            }
            if (outTime >= 0f) outTime += dt;

            float aIn = 1f - Mathf.Clamp01(shotTime / fadeIn);
            float aOut = last || outTime < 0f ? 0f : Mathf.Clamp01(outTime / ShotFade);
            black.color = new Color(0, 0, 0, Mathf.Max(aIn, aOut));
            cardGroup.alpha = 0f;

            // Eased move, then a slow drift past the end so a waiting shot never freezes.
            float e = Ease(Mathf.Clamp01(shotTime / d));
            float over = Mathf.Max(0f, shotTime - d);
            Vector3 drift = (shot.ToPos - shot.FromPos) * (0.06f * (1f - Mathf.Exp(-over / 6f)));
            Vector3 pos = Vector3.Lerp(shot.FromPos, shot.ToPos, e) + drift;
            Vector3 look = Vector3.Lerp(shot.FromLook, shot.ToLook, e) + drift;
            if ((look - pos).sqrMagnitude > 0.01f)
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, shot.Up));

            // Caption: slide in, type out with radio blips, fade once the player moves on.
            float capIn = Mathf.Clamp01((shotTime - 0.25f) / 0.35f);
            float capOut = outTime < 0f ? 1f : 1f - Mathf.Clamp01(outTime / 0.3f);
            captionGroup.alpha = capIn * capOut;
            caption.anchoredPosition = new Vector2(90f - (1f - Ease(capIn)) * 40f, Letterbox + 34f);
            captionBody.maxVisibleCharacters = visible;
            for (; typed < visible; typed++)
                if (typed % 3 == 0 && !char.IsWhiteSpace(shot.Body[typed])) Sfx.PlayUI("blip", 0.35f);
            continueGroup.alpha = typedOut && outTime < 0f ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;

            UpdateMarker();

            if (outTime >= 0f && (last || outTime >= ShotFade))
            {
                if (last || !NextShot()) Release(EndBlend, false);
            }
        }

        /// <summary>Click or any key except Esc (skip) and a held Space (hold-to-skip); a short Space tap counts.</summary>
        bool AdvancePressed()
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            bool tap = false;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) spaceDown = 0f;
                if (kb.spaceKey.isPressed) spaceDown += Time.unscaledDeltaTime;
                tap = kb.spaceKey.wasReleasedThisFrame && spaceDown < 0.3f;
            }
            if (tap) return true;
            if (ms != null && (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame)) return true;
            if (kb == null || !kb.anyKey.wasPressedThisFrame) return false;
            return !kb.escapeKey.wasPressedThisFrame && !kb.spaceKey.wasPressedThisFrame;
        }

        void UpdateMarker()
        {
            if (shot.Highlight == null) { markerGroup.alpha = 0f; return; }
            float t = shotTime - 0.6f;
            if (t >= 0f && !highlightPlayed) { highlightPlayed = true; Sfx.PlayUI("highlight", 0.7f); }
            var c = Camera.main;
            Vector3 sp = c.WorldToScreenPoint(shot.Highlight.Value);
            if (t < 0f || sp.z <= 0f) { markerGroup.alpha = 0f; return; }
            markerGroup.alpha = Mathf.Clamp01(t / 0.25f) * captionGroup.alpha;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var local);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = local;
            marker.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, Ease(Mathf.Clamp01(t / 0.35f)));
            float dist = Vector3.Distance(anchor, shot.Highlight.Value);
            string text = shot.ShowDistance
                ? $"{shot.Label}\n<size=75%><color=#9FBCE6>{(dist > 1000f ? (dist / 1000f).ToString("0.0") + " km" : dist.ToString("0") + " m")} from you</color></size>"
                : shot.Label;
            if (markerLabel.text != text)
            {
                markerLabel.text = text;
                Vector2 pref = markerLabel.GetPreferredValues(text);
                markerLabelBg.sizeDelta = new Vector2(pref.x + 28f, pref.y + 12f);
            }
        }

        bool NextShot()
        {
            while (++shotIndex < builders.Count)
            {
                anchor = AnchorPos();
                var s = builders[shotIndex]();
                if (s == null) continue;
                shot = s;
                shotTime = 0f;
                typed = 0;
                highlightPlayed = false;
                revealAll = false;
                outTime = -1f;
                continueGroup.alpha = 0f;
                captionTitle.text = s.Title;
                captionBody.text = s.Body;
                captionBody.maxVisibleCharacters = 0;
                markerGroup.alpha = 0f;
                Sfx.PlayUI(s.Sfx ?? "whoosh", 0.7f);
                if (shotIndex > 0) Sfx.PlayUI("radio", 0.45f);
                cam.transform.SetPositionAndRotation(s.FromPos, Quaternion.LookRotation(s.FromLook - s.FromPos, s.Up));
                cam.PreviousStateIsValid = false;
                return true;
            }
            return false;
        }

        bool IsLastShot() => shotIndex >= builders.Count - 1;

        void SetLetterbox(float k)
        {
            barTop.rectTransform.anchoredPosition = new Vector2(0f, -Letterbox * k);
            barBottom.rectTransform.anchoredPosition = new Vector2(0f, Letterbox * k);
            skipHint.anchoredPosition = new Vector2(-70f, Letterbox * 0.5f - Letterbox * (1f - k));
        }

        static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        // ================================================================ shots

        static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        static ShipCore LocalShip() => ShipCore.OwnedBy(LocalId);

        static Vector3 AnchorPos()
        {
            var ship = LocalShip();
            if (ship != null) return ship.transform.position;
            var p = PlayerPawn.Local;
            return p != null ? p.transform.position : Vector3.zero;
        }

        Shot ArrivalShot()
        {
            var ship = LocalShip();
            Vector3 s = anchor;
            Vector3 f = ship != null ? ship.transform.forward : Vector3.right;
            Vector3 r = ship != null ? ship.transform.right : Vector3.back;
            return new Shot
            {
                Title = "Your crew",
                Body = "You just dropped out of hyperspace at the edge of a dying sector. Every crew member flies their own ship - stick together.",
                Label = "YOUR SHIP", ShowDistance = false, Highlight = s,
                Duration = 5f,
                FromPos = s - f * 60f - r * 30f + Vector3.up * 16f,
                ToPos = s - f * 24f - r * 12f + Vector3.up * 6f,
                FromLook = s + f * 10f, ToLook = s + f * 40f,
            };
        }

        /// <summary>Where the crew is and where home lies: the destination galaxy on the sky (#46, story frame #72).</summary>
        Shot LostShot()
        {
            Vector3 s = anchor;
            Vector3 sky = s + SystemVisuals.DestinationDir * 3000f;
            return new Shot
            {
                Title = "2.73 million light-years from home",
                Body = "A wormhole threw the SELENE-4 crew into the Triangulum Galaxy. Upgrade the warp drive and fly galaxy by galaxy through the Local Group - Andromeda first, then on to the Milky Way. [M] opens the galaxy map.",
                Label = "ANDROMEDA (M31)", Highlight = sky, ShowDistance = false, Sfx = "rumble",
                Duration = 6.5f,
                FromPos = s + new Vector3(-30f, 6f, 24f),
                ToPos = s + new Vector3(-22f, 3f, 14f),
                FromLook = s + SystemVisuals.DestinationDir * 3000f + Vector3.down * 600f, ToLook = sky,
            };
        }

        Shot ArkShot()
        {
            ShipCore ark = null;
            foreach (var sc in ShipCore.All) if (sc != null && sc.Derelict.Value) { ark = sc; break; }
            if (ark == null) return null;
            Vector3 c = ark.transform.position;
            Vector3 toAnchor = Vector3.ProjectOnPlane(anchor - c, Vector3.up).normalized;
            if (toAnchor.sqrMagnitude < 0.01f) toAnchor = Vector3.left;
            return new Shot
            {
                Title = "Derelict Ark",
                Body = "A derelict capital ship drifts nearby. Bring 8 Scrap to restore its power - the whole crew can man its helm, turrets and engines.",
                Label = "DERELICT ARK", Highlight = c,
                Duration = 5f,
                FromPos = c + Quaternion.AngleAxis(-25f, Vector3.up) * toAnchor * 115f + Vector3.up * 38f,
                ToPos = c + Quaternion.AngleAxis(15f, Vector3.up) * toAnchor * 70f + Vector3.up * 16f,
                FromLook = c + Vector3.up * 10f, ToLook = c,
            };
        }

        Shot PlanetShot()
        {
            var p = NearestPlanet(anchor);
            if (p == null) return null;
            Vector3 c = p.transform.position;
            float r = Mathf.Max(50f, p.Radius.Value);
            Vector3 d = (anchor - c).normalized;
            return new Shot
            {
                Title = "Planets",
                Body = "Land to mine ore, crystals and fuel and to crack loot crates. Watch your oxygen - only some atmospheres are breathable.",
                Label = "PLANET", Highlight = c,
                Duration = 5f,
                FromPos = c + Quaternion.AngleAxis(-18f, Vector3.up) * d * r * 3.4f + Vector3.up * r * 0.5f,
                ToPos = c + Quaternion.AngleAxis(14f, Vector3.up) * d * r * 2.5f + Vector3.up * r * 0.2f,
                FromLook = c, ToLook = c,
            };
        }

        Shot NestShot()
        {
            AlienNest nest = null;
            float best = float.MaxValue;
            foreach (var h in HealthComponent.All)
            {
                if (h == null || !h.TryGetComponent<AlienNest>(out var n)) continue;
                float dd = (n.transform.position - anchor).sqrMagnitude;
                if (dd < best) { best = dd; nest = n; }
            }
            if (nest == null) return null;
            Vector3 c = nest.transform.position;
            Vector3 up = nest.transform.up;
            Vector3 t = nest.transform.forward;
            return new Shot
            {
                Title = "Hostile life",
                Body = "Alien nests guard the planets and spawn creatures when you get close. Destroy them for loot - some hide Exotic Matter.",
                Label = "ALIEN NEST", Highlight = c + up * 1.5f, Sfx = "nest",
                Duration = 5f,
                FromPos = c + up * 26f + t * 34f,
                ToPos = c + up * 9f + t * 17f,
                FromLook = c + up * 2f, ToLook = c + up * 1.5f, Up = up,
            };
        }

        Shot StationShot()
        {
            var st = TraderStation.Instance;
            if (st == null) return null;
            Vector3 c = st.transform.position;
            Vector3 toAnchor = Vector3.ProjectOnPlane(anchor - c, Vector3.up).normalized;
            if (toAnchor.sqrMagnitude < 0.01f) toAnchor = Vector3.left;
            return new Shot
            {
                Title = "Trader station",
                Body = "Buy Exotic Matter, drive parts, modules and fuel, sell what you find and repair your ship. Salvage comes from wrecks, crates and kills.",
                Label = "TRADER STATION", Highlight = c,
                Duration = 4.5f,
                FromPos = c + Quaternion.AngleAxis(30f, Vector3.up) * toAnchor * 120f + Vector3.up * 30f,
                ToPos = c + Quaternion.AngleAxis(-5f, Vector3.up) * toAnchor * 80f + Vector3.up * 12f,
                FromLook = c, ToLook = c,
            };
        }

        Shot HelmShot()
        {
            Vector3 s = anchor;
            var seat = ShipSeat.SeatOf(LocalId);
            var ship = LocalShip();
            Transform vp = seat != null ? seat.ViewPoint : ship != null ? ship.transform : null;
            Vector3 vpPos = vp != null ? vp.position : s;
            Vector3 f = vp != null ? vp.forward : Vector3.right;
            Vector3 u = vp != null ? vp.up : Vector3.up;
            Vector3 r = Vector3.Cross(u, f);
            return new Shot
            {
                Title = "Take the helm",
                Body = $"WASD to thrust, mouse to steer, Space / Ctrl up and down, {VwInput.BindingDisplay(VwInput.Sprint)} to boost. Press {VwInput.BindingDisplay(VwInput.Interact)} to leave your seat. Good luck, crew.",
                Label = null,
                Duration = 5f,
                FromPos = vpPos - f * 40f + r * 22f + u * 12f,
                ToPos = vpPos - f * 14f + u * 4f,
                FromLook = s, ToLook = vpPos + f * 40f - u * 3f, Up = u,
            };
        }

        static PlanetBody NearestPlanet(Vector3 pos)
        {
            PlanetBody best = null;
            float bestD = float.MaxValue;
            foreach (var p in PlanetBody.All)
            {
                if (p == null) continue;
                float d = (p.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }
    }
}
