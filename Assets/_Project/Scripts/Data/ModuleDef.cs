using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>
    /// A ship module (Engine, Weapon, Shield, Hull, Cargo, Scanner, FuelTank) with a tier (Mk I-III).
    /// Stats are multiplicative/ additive modifiers applied by ShipCore when computing effective stats.
    /// </summary>
    [CreateAssetMenu(menuName = "Voidward/Module", fileName = "Module_")]
    public class ModuleDef : ScriptableObject
    {
        public string Id = "module_engine_mk1";
        public string DisplayName = "Engine Mk I";
        [TextArea] public string Description = "";
        public ModuleSlot Slot = ModuleSlot.Engine;
        [Range(1, 3)] public int Tier = 1;
        public int SalvageValue = 100;
        public Color Tint = Color.cyan;

        [Header("Stat modifiers (1 = no change for multipliers)")]
        public float ThrustMult = 1f;
        public float TurnMult = 1f;
        public float BoostMult = 1f;
        public float WeaponDamageMult = 1f;
        public float FireRateMult = 1f;
        public float ShieldMaxAdd;
        public float ShieldRegenAdd;
        public float HullMaxAdd;
        public int CargoSlotsAdd;
        public float ScannerRangeAdd;
        public float FuelEfficiencyMult = 1f;   // <1 = burns less
        public float FuelTankAdd;

        [Header("Combat upgrades (Targeting / WeaponMod slots)")]
        [Tooltip("Pilot aim assist cone (degrees) added to the base assist.")]
        public float AimAssistAdd;
        [Tooltip("Extra projectiles per pilot shot (fanned around the aim).")]
        public int ExtraProjectiles;
        [Tooltip("Projectile hit radius multiplier for pilot weapons.")]
        public float HitboxMult = 1f;

        [Header("Weapon modules only")]
        public WeaponKind Weapon = WeaponKind.None;

        public string StatsSummary()
        {
            var sb = new System.Text.StringBuilder();
            if (ThrustMult != 1f) sb.AppendLine($"Thrust x{ThrustMult:0.##}");
            if (TurnMult != 1f) sb.AppendLine($"Turn x{TurnMult:0.##}");
            if (WeaponDamageMult != 1f) sb.AppendLine($"Damage x{WeaponDamageMult:0.##}");
            if (FireRateMult != 1f) sb.AppendLine($"Fire rate x{FireRateMult:0.##}");
            if (ShieldMaxAdd != 0f) sb.AppendLine($"Shield +{ShieldMaxAdd:0}");
            if (ShieldRegenAdd != 0f) sb.AppendLine($"Shield regen +{ShieldRegenAdd:0.#}/s");
            if (HullMaxAdd != 0f) sb.AppendLine($"Hull +{HullMaxAdd:0}");
            if (CargoSlotsAdd != 0) sb.AppendLine($"Cargo +{CargoSlotsAdd} slots");
            if (ScannerRangeAdd != 0f) sb.AppendLine($"Scanner +{ScannerRangeAdd:0}m");
            if (FuelEfficiencyMult != 1f) sb.AppendLine($"Fuel use x{FuelEfficiencyMult:0.##}");
            if (FuelTankAdd != 0f) sb.AppendLine($"Fuel tank +{FuelTankAdd:0}");
            if (AimAssistAdd != 0f) sb.AppendLine($"Aim assist +{AimAssistAdd:0.#} deg");
            if (ExtraProjectiles != 0) sb.AppendLine($"+{ExtraProjectiles} projectile{(ExtraProjectiles > 1 ? "s" : "")} per shot");
            if (HitboxMult != 1f) sb.AppendLine($"Hit radius x{HitboxMult:0.##}");
            if (Weapon != WeaponKind.None) sb.AppendLine($"Weapon: {Weapon}");
            return sb.ToString().TrimEnd();
        }
    }

    public enum WeaponKind { None, Laser, Missile, Flak }
}
