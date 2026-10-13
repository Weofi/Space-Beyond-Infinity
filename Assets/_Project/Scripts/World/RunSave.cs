using UnityEngine;
using Voidward.Core;

namespace Voidward.World
{
    /// <summary>The single suspend-save slot of the journey (JSON with a version, see RunState).</summary>
    public static class RunSave
    {
        const string Key = "run";

        public static bool Exists => SaveSystem.Exists(Key);

        public static void Write(RunState s)
        {
            s.Version = RunState.CurrentVersion;
            SaveSystem.Save(Key, s);
            Debug.Log($"[Run] Suspend save written (galaxy {s.Galaxy}, system {s.System}, {s.Players.Count} players, {s.Ships.Count} ships)");
        }

        /// <summary>The saved run, or null if there is none or it is from an incompatible version.</summary>
        public static RunState Read()
        {
            if (!Exists) return null;
            var s = SaveSystem.Load<RunState>(Key);
            if (s == null || s.RunSeed == 0 || s.Version < 1 || s.Version > RunState.CurrentVersion) return null; // v1 (before #78) has no drive: Mk I
            return s;
        }

        public static void Delete()
        {
            if (!Exists) return;
            SaveSystem.Delete(Key);
            Debug.Log("[Run] Suspend save deleted");
        }
    }
}
