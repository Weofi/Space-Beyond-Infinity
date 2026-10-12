using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
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
    /// the key POIs (own ship, Collapse, Ark, planet, nest, station, gate) with fades, letterbox,
    /// radio-style captions and POI highlights. Esc or hold Space skips. The last shot ends behind the
    /// local cockpit and Cinemachine blends into the player camera. Driven by UIRoot.
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
            public bool ShowDistance = true;
        }

        enum State { Idle, Playing, SkipFade, Release }

        readonly RectTransform root, caption, marker, skipHint, titleCard;
        readonly Image black, barTop, barBottom, skipFill, liveDot;
        readonly TextMeshProUGUI captionTitle, captionBody, markerLabel, cardSector, cardName;
        readonly CanvasGroup captionGroup, markerGroup, cardGroup;
        readonly List<Func<Shot>> builders = new List<Func<Shot>>();

        CinemachineCamera cam;
        CinemachineBrain brain;
        CinemachineBlendDefinition savedBlend;
        State state;
        Shot shot;
        int shotIndex;
        float shotTime, totalTime, stateTime, holdTime, releaseDuration, blackAtSkip;
        int typed;
        bool highlightPlayed;
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

            // POI highlight: gold diamond + label.
            marker = UIKit.Rect(root, "Highlight");
            marker.sizeDelta = new Vector2(40, 40);
            markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            var outer = UIKit.Image(marker, "Diamond", UIKit.Accent2, false);
            UIKit.Place(outer.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            outer.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var inner = UIKit.Image(outer.transform, "Inner", new Color(0.04f, 0.08f, 0.18f, 0.85f), false);
            UIKit.Stretch(inner.rectTransform, 5, 5, 5, 5);
            var core = UIKit.Image(outer.transform, "Core", new Color(0.55f, 0.95f, 1f), false);
            UIKit.Place(core.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            markerLabel = UIKit.Text(marker, "", 24, TextAlignmentOptions.Left, UIKit.TextColor, FontStyles.Bold);
            UIKit.Place(markerLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(34, 0), new Vector2(420, 70), new Vector2(0f, 0.5f));
            markerLabel.fontSharedMaterial = UIKit.ShadowMaterial;
            markerLabel.textWrappingMode = TextWrappingModes.NoWrap;

            // Caption: chamfered frame, navy interior, blue title plate, typed body text.
            caption = UIKit.Image(root, "Caption", UIKit.Frame).rectTransform;
            UIKit.Place(caption, new Vector2(0f, 0f), new Vector2(90f, Letterbox + 34f), new Vector2(880f, 214f), Vector2.zero);
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
            UIKit.Stretch(captionBody.rectTransform, 30, 30, 50, 20);

            // Skip hint with hold progress, inside the bottom letterbox bar.
            skipHint = UIKit.Rect(root, "SkipHint");
            UIKit.Place(skipHint, new Vector2(1f, 0f), new Vector2(-70f, Letterbox * 0.5f), new Vector2(520f, 50f), new Vector2(1f, 0.5f));
            var hint = UIKit.Text(skipHint, "<color=#FFBD40>ESC</color> SKIP     HOLD <color=#FFBD40>SPACE</color> SKIP", 20, TextAlignmentOptions.Right, UIKit.TextDim, FontStyles.Bold);
            UIKit.Place(hint.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(520f, 30f), new Vector2(1f, 1f));
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
            builders.Add(CollapseShot);
            builders.Add(ArkShot);
            builders.Add(PlanetShot);
            builders.Add(NestShot);
            builders.Add(StationShot);
            builders.Add(GateShot);
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

            var sector = ContentCatalog.GetSector(1);
            cardSector.text = "SECTOR 1";
            cardName.text = sector != null ? sector.DisplayName : "THE OUTER RIM";

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
            float aIn = 1f - Mathf.Clamp01(shotTime / fadeIn);
            float aOut = last ? 0f : Mathf.Clamp01((shotTime - (d - ShotFade)) / ShotFade);
            black.color = new Color(0, 0, 0, Mathf.Max(aIn, aOut));
            cardGroup.alpha = 0f;

            float e = Ease(Mathf.Clamp01(shotTime / d));
            Vector3 pos = Vector3.Lerp(shot.FromPos, shot.ToPos, e);
            Vector3 look = Vector3.Lerp(shot.FromLook, shot.ToLook, e);
            if ((look - pos).sqrMagnitude > 0.01f)
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));

            // Caption: slide in, type out with radio blips, fade before the cut.
            float capIn = Mathf.Clamp01((shotTime - 0.25f) / 0.35f);
            float capOut = last ? 1f : Mathf.Clamp01((d - ShotFade - shotTime) / 0.3f);
            captionGroup.alpha = capIn * capOut;
            caption.anchoredPosition = new Vector2(90f - (1f - Ease(capIn)) * 40f, Letterbox + 34f);
            int visible = Mathf.Clamp((int)((shotTime - 0.4f) * TypeSpeed), 0, shot.Body.Length);
            captionBody.maxVisibleCharacters = visible;
            for (; typed < visible; typed++)
                if (typed % 3 == 0 && !char.IsWhiteSpace(shot.Body[typed])) Sfx.PlayUI("blip", 0.35f);

            UpdateMarker();

            if (shotTime >= d)
            {
                if (last) Release(EndBlend, false);
                else if (!NextShot()) Release(EndBlend, false);
            }
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
            markerLabel.text = shot.ShowDistance
                ? $"{shot.Label}\n<size=75%><color=#9FBCE6>{(dist > 1000f ? (dist / 1000f).ToString("0.0") + " km" : dist.ToString("0") + " m")} from you</color></size>"
                : shot.Label;
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
                captionTitle.text = s.Title;
                captionBody.text = s.Body;
                captionBody.maxVisibleCharacters = 0;
                markerGroup.alpha = 0f;
                Sfx.PlayUI(s.Sfx ?? "whoosh", 0.7f);
                if (shotIndex > 0) Sfx.PlayUI("radio", 0.45f);
                cam.transform.SetPositionAndRotation(s.FromPos, Quaternion.LookRotation(s.FromLook - s.FromPos, Vector3.up));
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
                Duration = 6f,
                FromPos = s + f * 70f - r * 44f + Vector3.up * 16f,
                ToPos = s + f * 30f - r * 20f + Vector3.up * 6f,
                FromLook = s - f * 20f, ToLook = s - f * 2f,
            };
        }

        Shot CollapseShot()
        {
            float cx = GameState.CollapseX;
            if (cx < -50000f) return null;
            Vector3 s = anchor;
            Vector3 wall = new Vector3(cx, s.y, s.z);
            return new Shot
            {
                Title = "The Collapse",
                Body = "Behind you a black hole is swallowing the sector, and it never stops. Anything it touches is lost for good - keep moving forward.",
                Label = "THE COLLAPSE", Highlight = wall, Sfx = "rumble",
                Duration = 6f,
                FromPos = s + new Vector3(-8f, 10f, 26f),
                ToPos = s + new Vector3(-34f, 5f, 12f),
                FromLook = wall + Vector3.up * 260f + Vector3.forward * 200f, ToLook = wall,
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
                Duration = 5.5f,
                FromPos = c + Quaternion.AngleAxis(-25f, Vector3.up) * toAnchor * 150f + Vector3.up * 45f,
                ToPos = c + Quaternion.AngleAxis(15f, Vector3.up) * toAnchor * 105f + Vector3.up * 22f,
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
                Duration = 5.5f,
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
                Body = "Alien nests guard the planets and spawn creatures when you get close. Destroy them for loot - some hide Energy Cells.",
                Label = "ALIEN NEST", Highlight = c + up * 1.5f, Sfx = "nest",
                Duration = 5.5f,
                FromPos = c + up * 26f + t * 34f,
                ToPos = c + up * 9f + t * 17f,
                FromLook = c + up * 2f, ToLook = c + up * 1.5f,
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
                Body = "Buy Energy Cells, modules and fuel, sell what you find and repair your ship. Credits come from salvage and loot.",
                Label = "TRADER STATION", Highlight = c,
                Duration = 5f,
                FromPos = c + Quaternion.AngleAxis(30f, Vector3.up) * toAnchor * 120f + Vector3.up * 30f,
                ToPos = c + Quaternion.AngleAxis(-5f, Vector3.up) * toAnchor * 80f + Vector3.up * 12f,
                FromLook = c, ToLook = c,
            };
        }

        Shot GateShot()
        {
            var gate = JumpGate.Instance;
            if (gate == null) return null;
            Vector3 c = gate.transform.position;
            int cells = gate.CellsRequired.Value;
            return new Shot
            {
                Title = "Mission: power the jump gate",
                Body = $"Bring {cells} Energy Cells to the gate at the end of the sector. Once it is charged, the whole crew gathers here to jump. Cross 3 sectors and defeat the Warden.",
                Label = "JUMP GATE", Highlight = c, Sfx = "jump_charge",
                Duration = 7f,
                FromPos = c + new Vector3(-190f, 40f, 95f),
                ToPos = c + new Vector3(-105f, 12f, 48f),
                FromLook = c + Vector3.up * 20f, ToLook = c,
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
                ToPos = vpPos - f * 9f + u * 2.2f,
                FromLook = s, ToLook = vpPos + f * 80f,
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
