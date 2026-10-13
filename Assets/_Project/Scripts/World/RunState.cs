using System;
using System.Collections.Generic;
using Voidward.Core;

namespace Voidward.World
{
    /// <summary>
    /// Suspend save of the running journey (#46): one slot, owned by the host, written on entering a galaxy and when
    /// the host leaves, deleted when the run ends (death or homecoming). Ship positions are not stored: a resumed
    /// run starts at the arrival point of the saved system. Players are matched by name; unknown names start fresh.
    /// </summary>
    [Serializable]
    public class RunState
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public int RunSeed;
        public int Galaxy = 1;
        public int System;
        /// <summary>Bit i = system i of the current galaxy was visited (its loot was already handed out).</summary>
        public int VisitedMask;
        /// <summary>Bit i = the Warden enclave of galaxy i+1 was defeated.</summary>
        public int ClearedEnclaves;
        public float RunTime;
        public int RespawnsLeft;
        public float Distance;
        public int Kills;
        public int Loot;
        /// <summary>Crew warp drive (#78, version 2): tier per DriveSlot (core, nav, cooling, tank) and Exotic Matter in the tank.</summary>
        public int[] DriveTiers;
        public float Exotic;
        public List<PlayerSave> Players = new List<PlayerSave>();
        public List<ShipSave> Ships = new List<ShipSave>();
    }

    [Serializable]
    public class ItemSave
    {
        public string Id;
        public int Count;
    }

    [Serializable]
    public class PlayerSave
    {
        public string Name;
        public int Credits;
        public List<ItemSave> Items = new List<ItemSave>();
        public int Suit = 1, Oxygen = 1, Jetpack = 1, Drill = 1;
        public bool Rifle;
        public float BonusHp, BonusOxygen;
    }

    [Serializable]
    public class ShipSave
    {
        /// <summary>Player name of the owner; empty for a crew ship (the Ark).</summary>
        public string Owner = "";
        public ShipClass Class;
        public string Name = "";
        public bool Derelict;
        public float Fuel;
        public float Hull01 = 1f;
        public List<string> Modules = new List<string>();
        public List<ItemSave> Cargo = new List<ItemSave>();
    }
}
