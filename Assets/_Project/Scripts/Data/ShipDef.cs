using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// Ship archetype. The visual mesh is built procedurally by ShipFactory from Class;
    /// this asset carries stats, stations and cargo size.
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

        [Header("Economy")]
        public bool Purchasable;
        public int CreditCost = 0;

        [Header("Starting loadout (module item ids, may be empty)")]
        public string[] StartingModuleItemIds = { "module_engine_mk1", "module_weapon_laser_mk1" };
    }
}
