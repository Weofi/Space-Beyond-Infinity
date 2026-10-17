using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Networking;
using Voidward.World;

namespace Voidward.UI
{
    public enum Screen { None, Main, Join, Lobby, Hangar, Settings, Pause, Results, Connecting }

    /// <summary>
    /// Builds and drives the entire UI: screen flow (menu → lobby → game → results), modal
    /// gameplay panels, cursor lock and the GameState.UIOpen flag that gates gameplay input.
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }

        MenuScreens menus;
        HudView hud;
        InventoryView inventory;
        TradeView trade;
        GalaxyMapView galaxyMap;
        EngineerView engineer;
        IntroSequence intro;
        LoopView loop;
        GamePhase lastPhase = GamePhase.MainMenu;
        Image flash, veil;
        float flashTime, flashDuration;
        Color flashColor;

        Screen screen = Screen.Main;
        readonly Stack<Screen> back = new Stack<Screen>();
        readonly UIGate gate = new UIGate();

        public Screen Current => screen;
        public RunSummary LastSummary { get; private set; }

        void Awake()
        {
            Instance = this;
            G.Register<UIGate>(gate);
            VwInput.Init();
            VwInput.EnableGameplay();
            GameSettings.Current.Apply();
            if (!string.IsNullOrEmpty(GameSettings.Current.PlayerName) && SessionManager.Instance != null)
                SessionManager.Instance.SetLocalName(GameSettings.Current.PlayerName);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            var cgo = new GameObject("UICanvas");
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)cgo.transform;

            hud = new HudView(Root);
            menus = new MenuScreens(this, Root);
            inventory = new InventoryView(Root);
            trade = new TradeView(Root);
            galaxyMap = new GalaxyMapView(Root);
            engineer = new EngineerView(Root);

            // The warp bubble's veil (#108): hides the rebase when the crew flies into / out of a warp.
            veil = UIKit.Image(Root, "WarpVeil", new Color(1, 1, 1, 0), false);
            UIKit.Stretch(veil.rectTransform);
            veil.raycastTarget = false;
            flash = UIKit.Image(Root, "Flash", new Color(1, 1, 1, 0), false);
            UIKit.Stretch(flash.rectTransform);
            flash.raycastTarget = false;
            intro = new IntroSequence(Root);
            loop = new LoopView(Root);

            EventBus.RunEnded += OnRunEnded;
            EventBus.PhaseChanged += OnPhase;
            EventBus.TradePanelRequested += id => { CloseModals(); trade.Open(id); };
            EventBus.CraftPanelRequested += () => { CloseModals(); inventory.Open(InventoryView.Tab.Crafting); };
            EventBus.ShipPanelRequested += id => { CloseModals(); inventory.Open(InventoryView.Tab.Ship); };
            EventBus.DrivePanelRequested += () => { CloseModals(); inventory.Open(InventoryView.Tab.Drive); };
            EventBus.EngineerPanelRequested += id => { CloseModals(); engineer.Open(id); };
            VfxManager.ScreenFlash += (c, d) => { flashColor = c; flashDuration = d; flashTime = d; };

            Show(Screen.Main);
            Sfx.Music(0);
        }

        void OnDestroy()
        {
            EventBus.RunEnded -= OnRunEnded;
            EventBus.PhaseChanged -= OnPhase;
            loop.Dispose();
            G.Unregister<UIGate>();
            if (Instance == this) Instance = null;
        }

        void OnRunEnded(RunSummary s)
        {
            LastSummary = s;
            CloseModals();
            if (s.Victory) { Show(Screen.Results); Sfx.PlayUI("memories"); return; }
            // A death is the time loop (#86): the Rift closes the picture first, then the logbook opens.
            Sfx.PlayUI("alarm");
            loop.Collapse(s, () => { if (GameState.Phase == GamePhase.GameOver) Show(Screen.Results); });
        }

        void OnPhase(GamePhase p)
        {
            if (p != GamePhase.GameOver) loop.StopCollapse();
            if (p == GamePhase.MainMenu) { CloseModals(); back.Clear(); Show(Screen.Main); }
            if (p == GamePhase.Playing) { back.Clear(); Show(Screen.None); }
            if (p == GamePhase.Lobby) { CloseModals(); back.Clear(); Show(Screen.Lobby); }
            // Lobby/loop end -> Playing is a fresh run; late joiners arrive from MainMenu and skip the intro. The full intro
            // only plays for a player's first loop, later loops start with the short deja-vu (#86).
            bool fresh = lastPhase == GamePhase.Lobby || lastPhase == GamePhase.GameOver || lastPhase == GamePhase.DemoComplete;
            if (p == GamePhase.Playing && fresh)
            {
                CloseModals();
                if (Progression.Meta.Loops == 0) intro.Begin();
                else loop.DejaVu();
            }
            lastPhase = p;
        }

        public void Show(Screen s)
        {
            screen = s;
            menus.Show(s);
        }

        public void Push(Screen s)
        {
            back.Push(screen);
            Show(s);
        }

        public void Back()
        {
            Sfx.PlayUI("ui_back");
            Show(back.Count > 0 ? back.Pop() : DefaultScreen());
        }

        Screen DefaultScreen()
        {
            var sm = SessionManager.Instance;
            if (sm == null || !sm.InSession) return Screen.Main;
            var rm = RunManager.Instance;
            if (rm == null) return Screen.Connecting;
            switch (rm.Phase.Value)
            {
                case GamePhase.Lobby: return Screen.Lobby;
                case GamePhase.Playing: return Screen.None;
                default: return Screen.Results;
            }
        }

        /// <summary>Open the galaxy map (DevTest, tutorials).</summary>
        public void OpenGalaxyMap() { CloseModals(); galaxyMap.Open(); }

        public bool AnyModal => inventory.IsOpen || trade.IsOpen || engineer.IsOpen || galaxyMap.IsOpen;

        public void CloseModals()
        {
            inventory.Close();
            trade.Close();
            engineer.Close();
            galaxyMap.Close();
        }

        void Update()
        {
            var sm = SessionManager.Instance;
            bool inSession = sm != null && sm.InSession;

            // Keep the screen consistent with the session state.
            if (!inSession && screen != Screen.Main && screen != Screen.Join && screen != Screen.Hangar && screen != Screen.Settings)
            { back.Clear(); Show(Screen.Main); GameState.Phase = GamePhase.MainMenu; }
            if (inSession && (screen == Screen.Main || screen == Screen.Join)) Show(DefaultScreen());
            if (inSession && screen == Screen.Connecting && RunManager.Instance != null) Show(DefaultScreen());
            if (!inSession) { GameState.Phase = GamePhase.MainMenu; lastPhase = GamePhase.MainMenu; }

            bool playing = GameState.Phase == GamePhase.Playing;
            if (!playing && AnyModal) CloseModals();

            // Escape / pause handling
            if (VwInput.Pressed(VwInput.Pause))
            {
                if (VwInput.IsRebinding) { }
                else if (intro.Active) intro.Skip();
                else if (AnyModal) CloseModals();
                else if (screen == Screen.Settings || screen == Screen.Hangar || screen == Screen.Join) Back();
                else if (playing && screen == Screen.None) Push(Screen.Pause);
                else if (screen == Screen.Pause) Back();
            }

            // Inventory toggle
            if (playing && screen == Screen.None && !intro.Active && VwInput.Pressed(VwInput.Inventory))
            {
                if (inventory.IsOpen) inventory.Close();
                else { CloseModals(); inventory.Open(InventoryView.Tab.Inventory); }
            }

            // Galaxy map toggle (#46)
            if (playing && screen == Screen.None && !intro.Active && VwInput.Pressed(VwInput.Map))
            {
                if (galaxyMap.IsOpen) galaxyMap.Close();
                else { CloseModals(); galaxyMap.Open(); }
            }

            bool uiOpen = screen != Screen.None || AnyModal;
            gate.AnyModalOpen = uiOpen;
            GameState.UIOpen = uiOpen;
            bool cheatsVisible = CheatOverlayVisible();
            VwInput.SetCursor(uiOpen || cheatsVisible);
            if (cheatsVisible) GameState.UIOpen = true;

            hud.SetVisible(playing && !intro.Active && (screen == Screen.None || screen == Screen.Pause));
            hud.Update();
            intro.Update();
            loop.Tick();
            menus.Update();
            inventory.Update();
            trade.Update();
            galaxyMap.Update();
            engineer.Update();

            float v = JumpZones.Veil;
            if (v > 0.001f || veil.color.a > 0f)
            {
                var vc = JumpZones.VeilColor;
                vc.a = v * 0.97f;
                veil.color = vc;
            }
            if (flashTime > 0f)
            {
                flashTime -= Time.unscaledDeltaTime;
                var c = flashColor;
                c.a = flashColor.a * Mathf.Clamp01(flashTime / Mathf.Max(0.01f, flashDuration));
                flash.color = c;
            }
            else if (flash.color.a > 0f) flash.color = new Color(1, 1, 1, 0);
        }

        /// <summary>Skips the run intro (automated play tests via RunCommand, where the Game view has no input focus).</summary>
        public void SkipIntro() { if (intro != null && intro.Active) intro.Skip(); }

        static bool CheatOverlayVisible() => CheatManager.Instance != null && CheatManager.Instance.Visible;
    }
}
