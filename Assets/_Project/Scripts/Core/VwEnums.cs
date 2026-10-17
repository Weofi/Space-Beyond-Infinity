namespace Voidward.Core
{
    /// <summary>Which network transport the session uses.</summary>
    public enum TransportKind { UTP, Steam }

    /// <summary>High-level game flow state (local, per client).</summary>
    public enum GamePhase { Boot, MainMenu, Lobby, Hangar, Playing, Paused, GameOver, DemoComplete }

    /// <summary>Mineable / lootable resource types. Salvage is tracked separately as the run currency.</summary>
    public enum ResourceType { Ore, Crystal, Scrap, Fuel, ExoticMatter }

    public enum ItemKind { Resource, Module, Consumable, Gear, DriveComponent }

    /// <summary>Component sockets of the crew's warp drive (#78): core = top speed (drive tier), navigation = how much of it
    /// is safely flyable through fields, cooling = how long full throttle holds, tank = Exotic Matter capacity.</summary>
    public enum DriveSlot { Core, Navigation, Cooling, Tank }

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

    /// <summary>System cruise of a ship (#80): off, spooling up (key held), cruising.</summary>
    public enum CruiseMode : byte { Off, Spooling, Cruising }

    public enum Biome { Ice, Desert, ToxicJungle, Crystal, Volcanic }

    /// <summary>Roles for ship stations. One occupant per station.</summary>
    public enum StationRole { Pilot, Turret, Engineer }

    public enum PingKind { Look, Enemy, Loot, Gate }

    public enum EnemyKind { PirateFighter, DroneSwarm, PirateGunship, Crawler, Spitter, FlyingCritter, NestGuardian, BossMothership }

    public enum SectorEventKind { MeteorShower, DistressSignal, SolarFlare }

    /// <summary>What a star system inside a galaxy holds (#46).</summary>
    public enum SystemKind { Planetary, AsteroidField, Anomaly, Enclave, Nomads, Void }

    /// <summary>Scatter / points of interest placed on planet surfaces.</summary>
    public enum ScatterKind { OreNode, CrystalNode, ScrapNode, FuelNode, LootCrate, Nest, Ruin, CrashedShip }

    /// <summary>Hand equipment slots (hotbar indices 0-3).</summary>
    public enum ToolKind { Hands, MiningTool, Blaster, Rifle, Medkit }
}
