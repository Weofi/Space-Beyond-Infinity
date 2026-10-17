using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// Ship archetype (data-driven since #49): stats, stations, cargo size, landing behaviour and the hardpoints for
    /// ship parts (#50). The model and the gameplay parts around it (seats, clamps, cryo pods) are built by PrefabBuilder.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Ship", fileName = "Ship_")]
    public class ShipDef : ScriptableObject
    {
        public ShipClass Class = ShipClass.Dart;
        public string DisplayName = "Dart";

        [Header("Stats")]
        public float MaxHull = 100f;
        public float MaxShield = 60f;
        public float ShieldRegen = 6f;       // per second, after delay
        public float ShieldRegenDelay = 4f;
        public float MaxFuel = 100f;
        public float FuelBurnPerSecond = 0.6f; // while thrusting
        public float Thrust = 22f;             // m/s^2
        public float BoostMultiplier = 2.2f;
        public float TurnSpeedDeg = 90f;       // deg/s pitch/yaw
        public float RollSpeedDeg = 120f;
        public float MaxSpeed = 90f;           // soft cap
        public float LinearDamping = 0.6f;
        public float AngularDamping = 2.5f;

        [Header("Interior & stations")]
        public StationRole[] Stations = { StationRole.Pilot };
        public int CargoWidth = 6;
        public int CargoHeight = 3;
        [Tooltip("Does this ship have a walkable interior (gravity volume)?")]
        public bool HasInterior;

        [Header("Landing (#49)")]
        [Tooltip("Heavy ship: slow, levelled descent and touchdown only on flat ground.")]
        public bool HeavyLanding;
        [Tooltip("Steepest ground (degrees) a heavy ship may set down on.")]
        public float MaxLandingSlope = 14f;
        [Tooltip("Fastest descent (m/s) of a heavy ship close to the ground.")]
        public float MaxDescentSpeed = 6f;

        [Header("Hardpoints (#50): where ship parts mount, ship-local")]
        public ShipHardpoint[] Hardpoints = new ShipHardpoint[0];

        [Header("Economy")]
        public bool Purchasable;
        public int SalvageCost = 0;

        [Header("Starting loadout (module item ids, may be empty)")]
        public string[] StartingModuleItemIds = { "module_engine_mk1", "module_weapon_laser_mk1" };
    }

    /// <summary>A mount point for a ship part (#50): the slot it takes and where it sits on the hull (ship-local).</summary>
    [System.Serializable]
    public struct ShipHardpoint
    {
        public string Name;
        public ModuleSlot Slot;
        public Vector3 Position;
        public Vector3 Normal;
    }
}
