using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Debug cheat overlay toggled with F1 (IMGUI, so it works in every context including builds).
    /// Systems register their own cheat buttons via CheatManager.Register(label, action).
    /// Built-ins: god mode flag (read by HealthComponent for player pawns) and FPS display.
    /// </summary>
    public class CheatManager : MonoBehaviour
    {
        public static bool GodMode;
        public static CheatManager Instance { get; private set; }

        static readonly List<KeyValuePair<string, Action>> Cheats = new List<KeyValuePair<string, Action>>();
        bool visible;
        float fps;
        Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            GodMode = false;
            Cheats.Clear();
            Instance = null;
        }

        public static void Register(string label, Action action)
        {
            for (int i = 0; i < Cheats.Count; i++)
                if (Cheats[i].Key == label) { Cheats[i] = new KeyValuePair<string, Action>(label, action); return; }
            Cheats.Add(new KeyValuePair<string, Action>(label, action));
        }

        public static void Unregister(string label)
        {
            Cheats.RemoveAll(c => c.Key == label);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            VwInput.Init();
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.06f);
            if (VwInput.Pressed(VwInput.Cheat))
            {
                visible = !visible;
                if (G.Get<UIGate>() == null) VwInput.SetCursor(visible);
            }
        }

        /// <summary>The overlay is open (UI keeps the cursor free).</summary>
        public bool Visible => visible;

        void OnGUI()
        {
            if (!visible) return;
            var r = new Rect(12, 12, 280, 360);
            GUI.Box(r, "VOIDWARD CHEATS (F1)");
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 26, r.width - 20, r.height - 36));
            GUILayout.Label($"FPS: {fps:0}");
            GodMode = GUILayout.Toggle(GodMode, " God Mode");
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var c in Cheats)
            {
                if (GUILayout.Button(c.Key))
                {
                    try { c.Value?.Invoke(); }
                    catch (Exception e) { Debug.LogWarning("[Cheats] " + c.Key + " failed: " + e.Message); }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Tiny Core-side flag interface the UI registers so CheatManager knows whether a modal
    /// UI panel is open (cursor handling). Avoids Core -> UI dependency.
    /// </summary>
    public class UIGate
    {
        public bool AnyModalOpen;
    }
}
