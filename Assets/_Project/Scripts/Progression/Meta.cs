using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Progression
{
    /// <summary>Persistent per-player meta progression (saved locally as JSON).</summary>
    [Serializable]
    public class MetaSave
    {
        public int Memories;
        public int TotalMemories;
        public int Runs;
        public int Victories;
        public int BestSector;
        public float BestDistance;
        /// <summary>Lowest Home ETA (flight minutes) a failed loop ended with; 0 = none yet (#86).</summary>
        public float BestEtaMinutes;
        public string SelectedShip = "Dart";
        public List<string> Unlocked = new List<string>();
        public List<string> Equipped = new List<string>();
    }

    public class PerkDef
    {
        public int Bit;
        public string Id, Name, Description;
        public int Cost;
        public PerkDef(int bit, string id, string name, int cost, string desc) { Bit = bit; Id = id; Name = name; Cost = cost; Description = desc; }
    }

    /// <summary>What a player brings into a run (sent to the server at run start).</summary>
    public struct RunLoadout : INetworkSerializable
    {
        public ShipClass Ship;
        public int PerkMask;

        public bool Has(string perkId)
        {
            var p = Meta.Perk(perkId);
            return p != null && (PerkMask & (1 << p.Bit)) != 0;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Ship);
            s.SerializeValue(ref PerkMask);
        }
    }

    /// <summary>Meta progression API: Memories, perk unlocks, ship variant selection.</summary>
    public static class Meta
    {
        public const string MuleLicense = "mule_license";
        const string SaveKey = "meta";

        public static readonly PerkDef[] Perks =
        {
            new PerkDef(0, "reinforced_suit", "Reinforced Suit", 60, "+30 max health on foot."),
            new PerkDef(1, "deep_lungs", "Deep Lungs", 50, "+40% oxygen capacity."),
            new PerkDef(2, "head_start", "Head Start", 40, "Start each run with +150 Salvage."),
            new PerkDef(3, "spare_cells", "Spare Exotics", 80, "Start with 2 Exotic Matter for the warp drive."),
            new PerkDef(4, MuleLicense, "Mule Hauler License", 100, "Unlocks the Mule: a tough cargo hauler with laser + flak and a big hold."),
            new PerkDef(5, "laser_mk2", "Prototype Laser", 120, "Start with a Laser Mk II installed."),
            new PerkDef(6, "shield_mk2", "Prototype Shield", 120, "Start with a Shield Mk II installed."),
            new PerkDef(7, "salvager", "Salvager's Kit", 50, "Start with 10 Scrap and 5 Fuel."),
            new PerkDef(8, "rifle_start", "Old Faithful", 90, "Start with the Pulse Rifle."),
            new PerkDef(9, "engine_mk2", "Tuned Engine", 110, "Start with an Engine Mk II installed."),
        };

        static MetaSave data;
        public static MetaSave Data
        {
            get
            {
                if (data == null) data = SaveSystem.Load<MetaSave>(SaveKey, Migrate);
                return data;
            }
        }

        public static event Action Changed;

        /// <summary>Saves from before #112 call the meta currency Stardust: keep the value as Memories.</summary>
        static string Migrate(string json) =>
            json.Contains("\"Memories\"") ? json : json.Replace("\"Stardust\":", "\"Memories\":").Replace("\"TotalStardust\":", "\"TotalMemories\":");

        public static void Save()
        {
            SaveSystem.Save(SaveKey, Data);
            Changed?.Invoke();
        }

        public static PerkDef Perk(string id)
        {
            foreach (var p in Perks) if (p.Id == id) return p;
            return null;
        }

        public static bool IsUnlocked(string id) => Data.Unlocked.Contains(id);
        public static bool IsEquipped(string id) => Data.Equipped.Contains(id);

        public static bool TryUnlock(string id)
        {
            var p = Perk(id);
            if (p == null || IsUnlocked(id) || Data.Memories < p.Cost) return false;
            Data.Memories -= p.Cost;
            Data.Unlocked.Add(id);
            if (id != MuleLicense) Data.Equipped.Add(id);
            Save();
            return true;
        }

        public static void ToggleEquip(string id)
        {
            if (!IsUnlocked(id) || id == MuleLicense) return;
            if (!Data.Equipped.Remove(id)) Data.Equipped.Add(id);
            Save();
        }

        public static ShipClass SelectedShip
        {
            get => Data.SelectedShip == "Mule" && IsUnlocked(MuleLicense) ? ShipClass.Mule : ShipClass.Dart;
            set { Data.SelectedShip = value == ShipClass.Mule ? "Mule" : "Dart"; Save(); }
        }

        public static RunLoadout CurrentLoadout()
        {
            int mask = 0;
            foreach (var p in Perks)
                if (IsUnlocked(p.Id) && (IsEquipped(p.Id) || p.Id == MuleLicense)) mask |= 1 << p.Bit;
            return new RunLoadout { Ship = SelectedShip, PerkMask = mask };
        }

        /// <summary>Loops this player has finished (every run is one loop of the time loop, #86).</summary>
        public static int Loops => Data.Runs;

        /// <summary>Called on every client when a run ends.</summary>
        public static void RecordRun(RunSummary s)
        {
            Data.Runs++;
            if (s.Victory) Data.Victories++;
            Data.Memories += s.MemoriesEarned;
            Data.TotalMemories += s.MemoriesEarned;
            Data.BestSector = Mathf.Max(Data.BestSector, s.GalaxyReached); // best galaxy (field name kept for save compatibility)
            Data.BestDistance = Mathf.Max(Data.BestDistance, s.DistanceTraveled);
            // Closest a failed loop got to home (a homecoming counts in Victories).
            if (!s.Victory && s.HomeEtaMinutes > 0f && !float.IsInfinity(s.HomeEtaMinutes) && (Data.BestEtaMinutes <= 0f || s.HomeEtaMinutes < Data.BestEtaMinutes))
                Data.BestEtaMinutes = s.HomeEtaMinutes;
            Save();
        }

        public static void ResetAll()
        {
            data = new MetaSave();
            Save();
        }
    }
}
