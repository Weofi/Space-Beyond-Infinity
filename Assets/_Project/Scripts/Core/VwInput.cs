using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Voidward.Core
{
    /// <summary>
    /// Central input layer: all gameplay actions are defined in code (no .inputactions asset),
    /// so they can be created at runtime and rebound in Settings. Two maps:
    /// "Gameplay" (enabled only while playing) and "Global" (always enabled: cheats, pause).
    ///
    /// Consumers read actions directly (e.g. VwInput.Move.ReadValue&lt;Vector2&gt;()) and use
    /// VwInput.LookDelta for sensitivity/inversion-aware mouse look.
    /// </summary>
    public static class VwInput
    {
        [Serializable]
        class BindingOverrides { public string json = ""; }

        static InputActionMap gameplay;
        static InputActionMap global;
        static bool initialized;

        // Gameplay map
        public static InputAction Move { get; private set; }        // Vector2 WASD
        public static InputAction Look { get; private set; }        // Vector2 mouse delta
        public static InputAction Jump { get; private set; }        // Space
        public static InputAction Sprint { get; private set; }      // LeftShift (also ship Boost)
        public static InputAction Interact { get; private set; }    // F
        public static InputAction Fire { get; private set; }        // LMB
        public static InputAction AltFire { get; private set; }     // RMB
        public static InputAction Ping { get; private set; }        // T / MMB
        public static InputAction Inventory { get; private set; }   // Tab
        public static InputAction Map { get; private set; }         // M: galaxy map
        public static InputAction Roll { get; private set; }        // 1D axis Q/E (ships)
        public static InputAction Brake { get; private set; }       // X (ships)
        public static InputAction Flashlight { get; private set; }  // L: helmet lamp
        public static InputAction Vertical { get; private set; }    // 1D axis Space(+)/LeftCtrl(-): jetpack / ship thrust
        public static readonly InputAction[] Hotbar = new InputAction[5]; // digits 1-5

        // Global map
        public static InputAction Pause { get; private set; }       // Escape
        public static InputAction Cheat { get; private set; }       // F1

        public static float LookSensitivity = 1f;
        public static float ShipSensitivity = 1f;
        public static bool ShipAimAssist = true;
        public static bool InvertY;

        /// <summary>Mouse speed (px/s) that requests the ship's full turn rate at sensitivity 1.</summary>
        const float ShipMouseFullRate = 900f;

        /// <summary>
        /// Ship steering as a turn-rate request (1 = full turn rate per axis). Mouse speed maps to rate,
        /// so steering feels the same at any frame rate; a gamepad stick is used as-is.
        /// </summary>
        public static Vector2 ShipLookRate(float dt)
        {
            if (Look == null) return Vector2.zero;
            Vector2 v = Look.ReadValue<Vector2>();
            bool stick = Look.activeControl != null && Look.activeControl.device is Gamepad;
            Vector2 r = stick ? v * ShipSensitivity : v / Mathf.Max(dt, 1f / 250f) * (ShipSensitivity / ShipMouseFullRate);
            if (InvertY) r.y = -r.y;
            return r;
        }

        /// <summary>Sensitivity- and inversion-corrected look delta for this frame.</summary>
        public static Vector2 LookDelta
        {
            get
            {
                if (Look == null) return Vector2.zero;
                Vector2 d = Look.ReadValue<Vector2>() * (0.055f * LookSensitivity);
                if (InvertY) d.y = -d.y;
                return d;
            }
        }

        public static void Init()
        {
            if (initialized) return;
            initialized = true;

            gameplay = new InputActionMap("Gameplay");

            Move = gameplay.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Look = gameplay.AddAction("Look", InputActionType.Value);
            Look.AddBinding("<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick");

            Jump = Button(gameplay, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Sprint = Button(gameplay, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Interact = Button(gameplay, "Interact", "<Keyboard>/f", "<Gamepad>/buttonWest");
            Fire = Button(gameplay, "Fire", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            AltFire = Button(gameplay, "AltFire", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            Ping = Button(gameplay, "Ping", "<Keyboard>/t", null);
            Ping.AddBinding("<Mouse>/middleButton");
            Inventory = Button(gameplay, "Inventory", "<Keyboard>/tab", "<Gamepad>/buttonNorth");
            Map = Button(gameplay, "Map", "<Keyboard>/m", "<Gamepad>/select");
            Brake = Button(gameplay, "Brake", "<Keyboard>/x", "<Gamepad>/buttonEast");
            Flashlight = Button(gameplay, "Flashlight", "<Keyboard>/l", "<Gamepad>/dpad/up");

            Roll = gameplay.AddAction("Roll", InputActionType.Value);
            Roll.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");

            Vertical = gameplay.AddAction("Vertical", InputActionType.Value);
            Vertical.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftCtrl").With("Positive", "<Keyboard>/space");

            string[] digits = { "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5" };
            for (int i = 0; i < Hotbar.Length; i++)
                Hotbar[i] = Button(gameplay, "Hotbar" + (i + 1), digits[i], null);

            global = new InputActionMap("Global");
            Pause = Button(global, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
            Cheat = Button(global, "Cheat", "<Keyboard>/f1", null);

            global.Enable();
        }

        static InputAction Button(InputActionMap map, string name, string primary, string gamepad)
        {
            var a = map.AddAction(name, InputActionType.Button);
            if (primary != null) a.AddBinding(primary);
            if (gamepad != null) a.AddBinding(gamepad);
            return a;
        }

        public static void EnableGameplay()
        {
            Init();
            LoadOverrides();
            gameplay.Enable();
        }

        public static void DisableGameplay() => gameplay?.Disable();

        public static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame();
        public static bool Held(InputAction a) => a != null && a.IsPressed();

        public static string BindingDisplay(InputAction a)
        {
            Init();
            if (a == null || a.bindings.Count == 0) return "-";
            return a.GetBindingDisplayString(0);
        }

        [Serializable]
        public class RebindResult { public bool cancelled; }

        /// <summary>
        /// Starts an interactive rebind for the given action's first binding.
        /// onDone(true) = cancelled. The caller should show "press a key..." meanwhile.
        /// </summary>
        public static void StartRebind(InputAction action, Action<bool> onDone)
        {
            Init();
            if (action == null) { onDone?.Invoke(true); return; }
            IsRebinding = true;
            gameplay.Disable();
            action.Disable();
            var op = action.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(o =>
                {
                    o.Dispose();
                    action.Enable();
                    gameplay.Enable();
                    SaveOverrides();
                    IsRebinding = false;
                    onDone?.Invoke(false);
                })
                .OnCancel(o =>
                {
                    o.Dispose();
                    action.Enable();
                    gameplay.Enable();
                    IsRebinding = false;
                    onDone?.Invoke(true);
                });
            op.Start();
        }

        public static bool IsRebinding { get; private set; }

        public static void ResetBindings()
        {
            Init();
            gameplay.RemoveAllBindingOverrides();
            SaveOverrides();
        }

        /// <summary>Actions shown in the rebinding list.</summary>
        public static (string label, InputAction action)[] Rebindable => new[]
        {
            ("Interact", Interact), ("Jump / Jetpack", Jump), ("Sprint / Boost", Sprint), ("Inventory", Inventory), ("Galaxy Map", Map),
            ("Ping", Ping), ("Brake", Brake), ("Flashlight", Flashlight), ("Fire", Fire), ("Alt Fire", AltFire)
        };

        public static void SaveOverrides()
        {
            Init();
            SaveSystem.Save("bindings", new BindingOverrides { json = gameplay.SaveBindingOverridesAsJson() });
        }

        public static void LoadOverrides()
        {
            Init();
            var b = SaveSystem.Load<BindingOverrides>("bindings");
            if (!string.IsNullOrEmpty(b.json))
                gameplay.LoadBindingOverridesFromJson(b.json);
        }

        public static void SetCursor(bool uiVisible)
        {
            Cursor.visible = uiVisible;
            Cursor.lockState = uiVisible ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
