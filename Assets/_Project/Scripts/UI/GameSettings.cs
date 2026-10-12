using System;
using UnityEngine;
using Voidward.Core;

namespace Voidward.UI
{
    /// <summary>Player-facing settings (graphics, audio, mouse), persisted as JSON.</summary>
    [Serializable]
    public class GameSettings
    {
        public int Quality = -1;
        public bool Fullscreen = true;
        public bool VSync = true;
        public float Master = 0.9f;
        public float Music = 0.55f;
        public float SfxVol = 0.9f;
        public float Sensitivity = 1f;
        public bool InvertY;
        public float Fov = 75f;
        public string PlayerName = "";

        static GameSettings current;
        public static GameSettings Current => current ??= SaveSystem.Load<GameSettings>("settings");

        public static event Action Applied;

        public void Save()
        {
            SaveSystem.Save("settings", this);
            Apply();
        }

        public void Apply()
        {
            if (Quality < 0) Quality = QualitySettings.GetQualityLevel();
            Quality = Mathf.Clamp(Quality, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : 144;
            if (!Application.isEditor)
                UnityEngine.Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Sfx.SetVolumes(Master, Music, SfxVol);
            VwInput.LookSensitivity = Sensitivity;
            VwInput.InvertY = InvertY;
            GameState.FieldOfView = Fov;
            Applied?.Invoke();
        }
    }
}
