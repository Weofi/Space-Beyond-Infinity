namespace Voidward.Core
{
    /// <summary>Which network transport the session uses.</summary>
    public enum TransportKind { UTP, Steam }

    /// <summary>High-level game flow state (local, per client).</summary>
    public enum GamePhase { Boot, MainMenu, Lobby, Hangar, Playing, Paused, GameOver, DemoComplete }

    /// <summary>Mineable / lootable resource types. Credits are tracked separately as currency.</summary>
    public enum ResourceType { Ore, Crystal, Scrap, Fuel, EnergyCell }

    public enum ItemKind { Resource, Module, Consumable, Gear }

    /// <summary>Player gear upgrade categories (Kind=Gear items).</summary>
    public enum GearKind { None, Suit, Oxygen, Jetpack, MiningTool, Rifle }

    /// <summary>Ship module socket types. A ship has one socket per slot (WeaponA/B are two weapon sockets).</summary>
    public enum ModuleSlot { Engine, WeaponA, WeaponB, Shield, Hull, Cargo, Scanner, FuelTank, Targeting, WeaponMod }

    public enum ModuleTier { Mk1 = 1, Mk2 = 2, Mk3 = 3 }

    public enum DamageType { Energy, Kinetic, Explosive, Mining, Collapse, Environmental }

    /// <summary>What a shot hit, for impact effects and scorch decals (#43). Purely cosmetic.</summary>
    public enum SurfaceKind : byte { None, Rock, Dirt, Metal, Organic, Shield }

    /// <summary>Faction used by combat. Same-team damage is ignored.</summary>
    public enum Team { Players, Pirates, Aliens, Neutral }

    /// <summary>Ship archetypes. Dart = agile starter, Mule = cargo starter, Ark = large co-op ship.</summary>
    public enum ShipClass { Dart, Mule, Ark }

    public enum Biome { Ice, Desert, ToxicJungle, Crystal, Volcanic }

    /// <summary>Roles for ship stations. One occupant per station.</summary>
    public enum StationRole { Pilot, Turret, Engineer }

    public enum PingKind { Look, Enemy, Loot, Gate }

    public enum EnemyKind { PirateFighter, DroneSwarm, PirateGunship, Crawler, Spitter, FlyingCritter, NestGuardian, BossMothership }

    public enum SectorEventKind { MeteorShower, DistressSignal, SolarFlare }

    /// <summary>Scatter / points of interest placed on planet surfaces.</summary>
    public enum ScatterKind { OreNode, CrystalNode, ScrapNode, FuelNode, LootCrate, Nest, Ruin, CrashedShip }

    /// <summary>Hand equipment slots (hotbar indices 0-3).</summary>
    public enum ToolKind { Hands, MiningTool, Blaster, Rifle, Medkit }
}
