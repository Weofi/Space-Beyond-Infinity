using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Audio backend contract. Implemented by the AudioManager (registered in G).
    /// All clips are procedurally synthesized; ids are stable strings (see README/audio dev).
    /// Known ids: "laser","laser_ship","missile","flak","explosion","explosion_big","hit",
    /// "shield_hit","pickup","mine","craft","trade","ui_click","ui_back","ui_hover","alarm","low_oxygen",
    /// "heal","jump_gate","jump_charge","landing","thud","ping","spawn","boss_roar","memories",
    /// "intro_swell","whoosh","radio","blip","highlight","rumble","hitmarker","hit_crit","kill",
    /// "loot_pop","loot_land","loot_land_rare","pickup_uncommon","pickup_rare","pickup_epic","unlock".
    /// Weapon ids ("laser","laser_ship","rifle","missile","flak") are layered: _core + _tail + _mech.
    /// </summary>
    public interface IAudioService
    {
        void Play(string id, Vector3 pos, float volume = 1f, float pitch = 1f);
        void PlayUI(string id, float volume = 1f);
        /// <summary>Non-spatial one-shot with its own pitch (hit markers, kill confirms).</summary>
        void Play2D(string id, float volume = 1f, float pitch = 1f);
        /// <summary>0 = menu ambience, 1..3 = sector index.</summary>
        void SetMusicMood(int sectorIndex);
        void SetEngineLoop(bool on, float throttle01);
        float MasterVolume { get; set; }
        float MusicVolume { get; set; }
        float SfxVolume { get; set; }
    }

    /// <summary>Null-safe static access to the audio service.</summary>
    public static class Sfx
    {
        /// <summary>Player setting: combat / boss music stems follow the threat level (off = ambient only).</summary>
        public static bool DynamicMusic = true;

        public static void Play(string id, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            if (G.TryGet<IAudioService>(out var a)) a.Play(id, pos, volume, pitch);
        }

        public static void PlayUI(string id, float volume = 1f)
        {
            if (G.TryGet<IAudioService>(out var a)) a.PlayUI(id, volume);
        }

        public static void Play2D(string id, float volume = 1f, float pitch = 1f)
        {
            if (G.TryGet<IAudioService>(out var a)) a.Play2D(id, volume, pitch);
        }

        public static void Music(int sectorIndex)
        {
            if (G.TryGet<IAudioService>(out var a)) a.SetMusicMood(sectorIndex);
        }

        public static void Engine(bool on, float throttle01)
        {
            if (G.TryGet<IAudioService>(out var a)) a.SetEngineLoop(on, throttle01);
        }

        public static void SetVolumes(float master, float music, float sfx)
        {
            if (G.TryGet<IAudioService>(out var a))
            {
                a.MasterVolume = master;
                a.MusicVolume = music;
                a.SfxVolume = sfx;
            }
        }
    }
}
