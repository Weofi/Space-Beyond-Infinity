using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.EditorTools
{
    /// <summary>
    /// One-click content pipeline: "Voidward > Build All Content" creates every ScriptableObject,
    /// material, procedural mesh, prefab, the network prefab list and the game scene.
    /// Re-running is safe (assets are overwritten in place, GUIDs preserved where possible).
    /// </summary>
    public static partial class ContentBuilder
    {
        public const string Root = "Assets/_Project";
        public const string DataDir = Root + "/Data";
        public const string ArtDir = Root + "/Art";
        public const string ModelDir = ArtDir + "/Models";
        public const string MeshDir = Root + "/Art/Meshes";
        public const string MatDir = Root + "/Art/Materials";
        public const string PrefabDir = Root + "/Prefabs";
        public const string ResourcesDir = Root + "/Resources";
        public const string SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/Voidward.unity";

        static ContentCatalog catalog;
        static readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();
        static readonly Dictionary<string, BiomeDef> biomes = new Dictionary<string, BiomeDef>();
        static readonly Dictionary<string, LootTable> loot = new Dictionary<string, LootTable>();

        [MenuItem("Voidward/Build All Content", priority = 0)]
        public static void BuildAll()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                EnsureFolders();
                BuildMaterials();
                BuildData();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            BuildFonts();
            BuildPrefabs();
            BuildItemArt();
            BuildScene();
            BuildPlanetPreviewScene();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            ConfigureProject();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Voidward] Content build complete.");
        }

        [MenuItem("Voidward/Rebuild Data Only")]
        public static void BuildDataOnly()
        {
            EnsureFolders();
            BuildMaterials();
            BuildData();
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders()
        {
            foreach (var d in new[] { DataDir, DataDir + "/Items", DataDir + "/Modules", DataDir + "/Enemies", DataDir + "/Biomes", DataDir + "/Galaxies",
                         DataDir + "/Palettes", DataDir + "/Recipes", DataDir + "/Ships", DataDir + "/Loot", DataDir + "/Drive", PlanetDir, ArtDir, MeshDir, MatDir, TextureDir, PrefabDir, ResourcesDir, SceneDir })
                if (!AssetDatabase.IsValidFolder(d))
                {
                    var parent = Path.GetDirectoryName(d).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
                }
        }

        public static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }
            EditorUtility.SetDirty(a);
            return a;
        }

        // ================================================================ materials

        public static Material MatToon, MatTerrain, MatSkybox, MatCollapse, MatAtmosphere, MatUnlit, MatParticle, MatGlass, MatGlow, MatPlume;

        static Material Mat(string name, string shader)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("[Voidward] Shader not found: " + shader); sh = Shader.Find("Universal Render Pipeline/Unlit"); }
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildMaterials()
        {
            MatToon = Mat("VW_Toon", "VOIDWARD/ToonFlat");
            MatToon.SetFloat("_UseVertexColor", 1f);
            MatToon.EnableKeyword("_USEVERTEXCOLOR_ON");
            MatToon.SetFloat("_FlatShading", 1f);
            MatToon.EnableKeyword("_FLATSHADING_ON");
            MatToon.SetFloat("_Steps", 3f);
            MatToon.SetFloat("_Softness", 0.65f);
            MatToon.SetFloat("_AmbientBoost", 0.55f);
            // Direct-light floor stays tiny: in linear space even 0.03 reads as ~20% grey after sRGB conversion.
            MatToon.SetFloat("_ShadowFloor", 0.008f);
            MatToon.SetFloat("_ShadowStrength", 0.95f);
            MatToon.SetFloat("_LocalLightStrength", 1f);
            MatToon.SetFloat("_TerminatorWrap", 0.05f);
            MatToon.SetFloat("_RimStrength", 0.3f);
            MatToon.SetFloat("_RimPower", 3f);
            MatToon.SetColor("_RimColor", new Color(0.35f, 0.55f, 1f));
            MatToon.SetFloat("_SpecStrength", 0.9f);
            MatToon.SetFloat("_ReflStrength", 0.9f);
            MatToon.SetFloat("_GlowIntensity", 3f);
            MatToon.enableInstancing = true;

            // Planet surfaces: same look, but almost no rim (grazing ground would otherwise glow on the night side).
            MatTerrain = Mat("VW_Terrain", "VOIDWARD/ToonFlat");
            MatTerrain.CopyPropertiesFromMaterial(MatToon);
            MatTerrain.shaderKeywords = MatToon.shaderKeywords;
            MatTerrain.SetFloat("_RimStrength", 0.05f);
            MatTerrain.SetFloat("_ShadowFloor", 0f);
            MatTerrain.enableInstancing = true;

            MatSkybox = Mat("VW_Skybox", "VOIDWARD/NebulaSkybox");
            MatSkybox.SetFloat("_NebulaStrength", 0.45f);
            MatSkybox.SetFloat("_DustStrength", 0.7f);
            MatSkybox.SetFloat("_BodyStrength", 1f);
            MatSkybox.SetFloat("_StarDensity", 220f);
            MatSkybox.SetFloat("_GalaxyStrength", 1f);
            MatSkybox.SetFloat("_SunDiscStrength", 1f);
            ApplyStarMap(MatSkybox);
            MatCollapse = Mat("VW_Collapse", "VOIDWARD/Collapse");
            MatAtmosphere = Mat("VW_Atmosphere", "VOIDWARD/Atmosphere");
            MatAtmosphere.SetFloat("_Strength", 1f);
            MatAtmosphere.SetFloat("_SkyOpacity", 1.8f);
            MatAtmosphere.SetFloat("_HazeScale", 0.05f);
            MatUnlit = Mat("VW_Unlit", "VOIDWARD/Unlit");
            MatUnlit.SetColor("_BaseColor", new Color(1f, 1f, 1f));
            MatGlow = Mat("VW_Glow", "VOIDWARD/Unlit");
            MatGlow.SetColor("_BaseColor", new Color(0.7f, 1.8f, 3.4f));
            MatParticle = Mat("VW_Particle", "VOIDWARD/AdditiveParticle");
            MatPlume = Mat("VW_Plume", "VOIDWARD/Plume");
            MatPlume.SetColor("_BaseColor", new Color(1.6f, 1.9f, 2.6f, 1f));
            MatPlume.SetFloat("_EdgeSoftness", 1.4f);
            MatPlume.enableInstancing = true;
            MatGlass = Mat("VW_Glass", "VOIDWARD/Glass");
            BuildPlanetMaterials();
            BuildHullMaterial();
            BuildRockDetailMaterial();
        }

        // ================================================================ data

        static void BuildData()
        {
            catalog = Asset<ContentCatalog>(ResourcesDir + "/ContentCatalog.asset");
            items.Clear(); biomes.Clear(); loot.Clear();

            var palettes = BuildPalettes();
            var modules = BuildModules();
            BuildItems();
            BuildLoot();
            var enemies = BuildEnemies();
            BuildPlanetShapes();
            BuildBiomes();
            var galaxies = BuildGalaxies(palettes);
            var recipes = BuildRecipes();
            var ships = BuildShips();

            var itemList = new List<ItemDef>(items.Values);
            itemList.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            catalog.Items = itemList.ToArray();
            catalog.Modules = modules.ToArray();
            catalog.Enemies = enemies.ToArray();
            catalog.Biomes = new[] { biomes["Ice"], biomes["Desert"], biomes["ToxicJungle"], biomes["Crystal"], biomes["Volcanic"] };
            catalog.Palettes = palettes;
            catalog.Galaxies = galaxies;
            catalog.Recipes = recipes.ToArray();
            catalog.Ships = ships;
            catalog.WarpDrive = BuildWarpDrive();
            catalog.Story = BuildStory();
            catalog.LootTables = new[] { loot["loot_t1"], loot["loot_t2"], loot["loot_t3"], loot["loot_pirate"], loot["loot_drone"], loot["loot_alien"], loot["loot_guardian"], loot["loot_gunship"] };
            catalog.ToonMaterial = MatToon;
            catalog.TerrainMaterial = MatTerrain;
            catalog.PlanetMaterial = MatPlanet;
            catalog.OceanMaterial = MatOcean;
            catalog.SkyboxMaterial = MatSkybox;
            catalog.CollapseMaterial = MatCollapse;
            catalog.AtmosphereMaterial = MatAtmosphere;
            catalog.CloudMaterial = MatClouds;
            catalog.UnlitMaterial = MatUnlit;
            catalog.ParticleMaterial = MatParticle;
            catalog.GlassMaterial = MatGlass;
            catalog.RebuildLookup();
            EditorUtility.SetDirty(catalog);
        }

        static PaletteDef[] BuildPalettes()
        {
            PaletteDef P(string name, Color zen, Color hor, Color a, Color b, Color sun, Color amb, Color fog, float dens, Color ui)
            {
                var p = Asset<PaletteDef>($"{DataDir}/Palettes/Palette_{name.Replace(" ", "")}.asset");
                p.DisplayName = name; p.SkyZenith = zen; p.SkyHorizon = hor; p.NebulaA = a; p.NebulaB = b;
                p.SunColor = sun; p.AmbientColor = amb; p.FogColor = fog; p.FogDensity = dens; p.UiPrimary = ui; p.StarColor = new Color(1f, 0.96f, 0.92f);
                p.StarField = 1f; p.NebulaField = 1f;
                return p;
            }
            void Grade(PaletteDef p, Color filter, float sat, float contrast, float exposure, float temp, Color bloom)
            {
                p.GradeFilter = filter; p.GradeSaturation = sat; p.GradeContrast = contrast; p.GradeExposure = exposure; p.GradeTemperature = temp; p.BloomTint = bloom;
            }
            var list = new[]
            {
                // Deep black space: barely-there sky gradient, faint tinted nebulae, very low ambient so the sun carves hard light/shadow.
                P("Triangulum", new Color(0.002f, 0.003f, 0.008f), new Color(0.006f, 0.009f, 0.02f), new Color(0.15f, 0.3f, 0.75f), new Color(0.25f, 0.6f, 0.8f),
                  new Color(1f, 0.96f, 0.9f), new Color(0.09f, 0.11f, 0.17f), new Color(0.004f, 0.006f, 0.014f), 0.00005f, new Color(0.4f, 0.75f, 1f)),
                P("Andromeda", new Color(0.002f, 0.004f, 0.006f), new Color(0.004f, 0.012f, 0.016f), new Color(0.15f, 0.6f, 0.55f), new Color(0.2f, 0.35f, 0.75f),
                  new Color(0.95f, 1f, 0.96f), new Color(0.08f, 0.12f, 0.14f), new Color(0.003f, 0.01f, 0.012f), 0.00005f, new Color(0.45f, 1f, 0.85f)),
                // Nebulae and star birth: magenta/violet clouds.
                P("NGC 6822", new Color(0.004f, 0.002f, 0.008f), new Color(0.016f, 0.006f, 0.02f), new Color(0.85f, 0.3f, 0.7f), new Color(0.4f, 0.3f, 0.9f),
                  new Color(1f, 0.92f, 0.96f), new Color(0.12f, 0.09f, 0.15f), new Color(0.01f, 0.004f, 0.014f), 0.00005f, new Color(1f, 0.55f, 0.9f)),
                // Ancient, star-poor and dark: almost no nebula, cold grey light.
                P("Leo I", new Color(0.001f, 0.001f, 0.002f), new Color(0.003f, 0.003f, 0.005f), new Color(0.25f, 0.25f, 0.35f), new Color(0.15f, 0.2f, 0.3f),
                  new Color(0.9f, 0.92f, 1f), new Color(0.06f, 0.065f, 0.08f), new Color(0.002f, 0.002f, 0.004f), 0.00004f, new Color(0.7f, 0.8f, 1f)),
                P("Magellanic Cloud", new Color(0.004f, 0.002f, 0.006f), new Color(0.016f, 0.006f, 0.014f), new Color(0.8f, 0.32f, 0.18f), new Color(0.35f, 0.22f, 0.75f),
                  new Color(1f, 0.86f, 0.72f), new Color(0.12f, 0.09f, 0.12f), new Color(0.012f, 0.004f, 0.008f), 0.00006f, new Color(1f, 0.6f, 0.4f)),
            };
            // Per-galaxy grade: cool and clean -> teal and calm -> pink nebula -> cold and contrasty -> warm climax.
            // Tuned for PBR + ACES (#96): ACES brings its own contrast/saturation, so these stay small.
            Grade(list[0], new Color(0.97f, 0.99f, 1f), 6f, 6f, 0.35f, -8f, new Color(0.8f, 0.9f, 1f));
            Grade(list[1], new Color(0.95f, 1f, 0.98f), 3f, 7f, 0.3f, -3f, new Color(0.75f, 1f, 0.92f));
            Grade(list[2], new Color(1f, 0.96f, 1f), 8f, 7f, 0.3f, 2f, new Color(1f, 0.85f, 0.98f));
            Grade(list[3], new Color(0.95f, 0.97f, 1f), -10f, 12f, 0.2f, -10f, new Color(0.8f, 0.85f, 1f));
            Grade(list[4], new Color(1f, 0.95f, 0.92f), 8f, 10f, 0.3f, 8f, new Color(1f, 0.82f, 0.7f));
            // Sky density (#96): the dwarf galaxies show fewer stars, nebula-rich ones more cloud.
            list[2].NebulaField = 1.3f;
            list[3].StarField = 0.45f; list[3].NebulaField = 0.4f;
            list[4].StarField = 1.1f; list[4].NebulaField = 1.2f;
            return list;
        }

        static readonly List<(string id, ModuleDef def)> moduleDefs = new List<(string, ModuleDef)>();

        static List<ModuleDef> BuildModules()
        {
            moduleDefs.Clear();
            var list = new List<ModuleDef>();
            ModuleDef M(string id, string name, ModuleSlot slot, int tier, int value, Color tint, string desc, System.Action<ModuleDef> set)
            {
                var m = Asset<ModuleDef>($"{DataDir}/Modules/{id}.asset");
                m.Id = id; m.DisplayName = name; m.Slot = slot; m.Tier = tier; m.CreditValue = value; m.Tint = tint; m.Description = desc;
                m.ThrustMult = 1f; m.TurnMult = 1f; m.BoostMult = 1f; m.WeaponDamageMult = 1f; m.FireRateMult = 1f; m.ShieldMaxAdd = 0; m.ShieldRegenAdd = 0;
                m.HullMaxAdd = 0; m.CargoSlotsAdd = 0; m.ScannerRangeAdd = 0; m.FuelEfficiencyMult = 1f; m.FuelTankAdd = 0; m.Weapon = WeaponKind.None;
                m.AimAssistAdd = 0; m.ExtraProjectiles = 0; m.HitboxMult = 1f;
                set(m);
                list.Add(m);
                moduleDefs.Add((id, m));
                return m;
            }
            Color eng = new Color(1f, 0.7f, 0.4f), wep = new Color(1f, 0.45f, 0.55f), shd = new Color(0.5f, 0.75f, 1f), hul = new Color(0.8f, 0.8f, 0.9f),
                  car = new Color(0.85f, 0.7f, 0.5f), scn = new Color(0.6f, 1f, 0.8f), fue = new Color(1f, 0.9f, 0.4f),
                  tgt = new Color(0.4f, 0.95f, 1f), mod = new Color(1f, 0.6f, 0.9f);
            M("module_engine_mk1", "Engine Mk I", ModuleSlot.Engine, 1, 60, eng, "Standard ion drive.", m => { });
            M("module_engine_mk2", "Engine Mk II", ModuleSlot.Engine, 2, 180, eng, "Tuned drive: more thrust and agility.", m => { m.ThrustMult = 1.25f; m.TurnMult = 1.1f; });
            M("module_engine_mk3", "Engine Mk III", ModuleSlot.Engine, 3, 380, eng, "Racing drive.", m => { m.ThrustMult = 1.5f; m.TurnMult = 1.2f; m.BoostMult = 1.1f; });
            M("module_laser_mk1", "Laser Mk I", ModuleSlot.WeaponA, 1, 60, wep, "Rapid-fire energy bolts.", m => { m.Weapon = WeaponKind.Laser; });
            M("module_laser_mk2", "Laser Mk II", ModuleSlot.WeaponA, 2, 200, wep, "Hotter bolts, faster cycling.", m => { m.Weapon = WeaponKind.Laser; m.WeaponDamageMult = 1.15f; });
            M("module_laser_mk3", "Laser Mk III", ModuleSlot.WeaponA, 3, 400, wep, "Military-grade laser.", m => { m.Weapon = WeaponKind.Laser; m.WeaponDamageMult = 1.3f; });
            M("module_missile_mk1", "Missile Pod Mk I", ModuleSlot.WeaponA, 1, 150, wep, "Homing missiles. Lock on by aiming near a target.", m => { m.Weapon = WeaponKind.Missile; });
            M("module_missile_mk2", "Missile Pod Mk II", ModuleSlot.WeaponA, 2, 320, wep, "Faster reload, bigger warheads.", m => { m.Weapon = WeaponKind.Missile; m.WeaponDamageMult = 1.1f; });
            M("module_flak_mk1", "Flak Cannon Mk I", ModuleSlot.WeaponA, 1, 140, wep, "Proximity-fused area shells - great vs drones.", m => { m.Weapon = WeaponKind.Flak; });
            M("module_flak_mk2", "Flak Cannon Mk II", ModuleSlot.WeaponA, 2, 300, wep, "Denser flak walls.", m => { m.Weapon = WeaponKind.Flak; m.WeaponDamageMult = 1.15f; });
            M("module_shield_mk1", "Shield Mk I", ModuleSlot.Shield, 1, 70, shd, "Basic deflector.", m => { m.ShieldMaxAdd = 20; m.ShieldRegenAdd = 1; });
            M("module_shield_mk2", "Shield Mk II", ModuleSlot.Shield, 2, 220, shd, "Layered deflector.", m => { m.ShieldMaxAdd = 60; m.ShieldRegenAdd = 3; });
            M("module_shield_mk3", "Shield Mk III", ModuleSlot.Shield, 3, 420, shd, "Fortress deflector.", m => { m.ShieldMaxAdd = 120; m.ShieldRegenAdd = 6; });
            M("module_hull_mk1", "Hull Plating Mk I", ModuleSlot.Hull, 1, 60, hul, "Extra armor plates.", m => { m.HullMaxAdd = 30; });
            M("module_hull_mk2", "Hull Plating Mk II", ModuleSlot.Hull, 2, 200, hul, "Composite armor.", m => { m.HullMaxAdd = 90; m.ThrustMult = 0.97f; });
            M("module_hull_mk3", "Hull Plating Mk III", ModuleSlot.Hull, 3, 400, hul, "Ablative armor.", m => { m.HullMaxAdd = 180; m.ThrustMult = 0.95f; });
            M("module_cargo_mk1", "Cargo Rack Mk I", ModuleSlot.Cargo, 1, 80, car, "More cargo space.", m => { m.CargoSlotsAdd = 6; });
            M("module_cargo_mk2", "Cargo Rack Mk II", ModuleSlot.Cargo, 2, 200, car, "Much more cargo space.", m => { m.CargoSlotsAdd = 12; });
            M("module_scanner_mk1", "Scanner Mk I", ModuleSlot.Scanner, 1, 70, scn, "Longer HUD detection range.", m => { m.ScannerRangeAdd = 300; });
            M("module_scanner_mk2", "Scanner Mk II", ModuleSlot.Scanner, 2, 180, scn, "Deep-space scanner.", m => { m.ScannerRangeAdd = 800; });
            M("module_fueltank_mk1", "Fuel Tank Mk I", ModuleSlot.FuelTank, 1, 70, fue, "Bigger tank, slightly more efficient.", m => { m.FuelTankAdd = 40; m.FuelEfficiencyMult = 0.95f; });
            M("module_fueltank_mk2", "Fuel Tank Mk II", ModuleSlot.FuelTank, 2, 190, fue, "Long-range tank.", m => { m.FuelTankAdd = 100; m.FuelEfficiencyMult = 0.85f; });
            // Combat upgrades (#19): Targeting slot = aim, WeaponMod slot = more shots / bigger hitboxes / fire rate.
            M("module_targeting_mk1", "Targeting CPU Mk I", ModuleSlot.Targeting, 1, 120, tgt, "Snaps pilot fire onto the lead point of nearby targets.", m => { m.AimAssistAdd = 4f; m.TurnMult = 1.05f; });
            M("module_targeting_mk2", "Targeting CPU Mk II", ModuleSlot.Targeting, 2, 260, tgt, "Wider lock cone and sharper gimbals.", m => { m.AimAssistAdd = 8f; m.TurnMult = 1.1f; });
            M("module_targeting_mk3", "Targeting CPU Mk III", ModuleSlot.Targeting, 3, 420, tgt, "Military fire control: almost nothing escapes.", m => { m.AimAssistAdd = 12f; m.TurnMult = 1.15f; });
            M("module_splitter_mk1", "Splitter Array Mk I", ModuleSlot.WeaponMod, 1, 160, mod, "Splits every shot: +1 projectile in a narrow fan.", m => { m.ExtraProjectiles = 1; });
            M("module_splitter_mk2", "Splitter Array Mk II", ModuleSlot.WeaponMod, 2, 340, mod, "Triple fan: +2 projectiles per shot.", m => { m.ExtraProjectiles = 2; });
            M("module_focus_mk1", "Focus Lens Mk I", ModuleSlot.WeaponMod, 1, 130, mod, "Widens the bolt envelope: projectiles hit more easily.", m => { m.HitboxMult = 2f; });
            M("module_focus_mk2", "Focus Lens Mk II", ModuleSlot.WeaponMod, 2, 280, mod, "Huge hit envelope. Drones hate this.", m => { m.HitboxMult = 3f; });
            M("module_capacitor_mk1", "Capacitor Mk I", ModuleSlot.WeaponMod, 1, 140, mod, "Faster weapon cycling.", m => { m.FireRateMult = 1.3f; });
            M("module_capacitor_mk2", "Capacitor Mk II", ModuleSlot.WeaponMod, 2, 300, mod, "Overclocked weapon cycling.", m => { m.FireRateMult = 1.6f; });
            M("module_engine_ark", "Ark Drive", ModuleSlot.Engine, 2, 300, eng, "Heavy drive for capital ships.", m => { m.ThrustMult = 1.1f; });
            return list;
        }

        static ItemDef Item(string id, string name, ItemKind kind, int value, Color tint, string desc, int stack = 99)
        {
            var it = Asset<ItemDef>($"{DataDir}/Items/{id}.asset");
            it.Id = id; it.DisplayName = name; it.Kind = kind; it.CreditValue = value; it.Tint = tint; it.Description = desc; it.MaxStack = stack;
            it.Module = null; it.Drive = null; it.Gear = GearKind.None; it.GearTier = 1; it.HealAmount = 0; it.SoldAtTrader = true;
            items[id] = it;
            return it;
        }

        static void BuildItems()
        {
            Item("ore", "Ore", ItemKind.Resource, 6, new Color(0.9f, 0.6f, 0.45f), "Common metal ore. Refine into fuel or craft parts.").Resource = ResourceType.Ore;
            Item("crystal", "Crystal", ItemKind.Resource, 14, new Color(0.55f, 0.9f, 1f), "Energetic crystal used in electronics. Its veins sometimes hold Exotic Matter.").Resource = ResourceType.Crystal;
            Item("scrap", "Scrap", ItemKind.Resource, 5, new Color(0.72f, 0.74f, 0.85f), "Salvaged parts. Repairs hulls; crafting staple.").Resource = ResourceType.Scrap;
            Item("fuel", "Fuel", ItemKind.Resource, 8, new Color(1f, 0.85f, 0.35f), "Refined propellant. Refuel ships from the Ship tab.").Resource = ResourceType.Fuel;
            Item("exotic", "Exotic Matter", ItemKind.Resource, 40, new Color(0.72f, 0.45f, 1f),
                "Warp fuel. Load it into the crew's warp drive (Warp Drive tab). Found in anomalies, crystal veins, wrecks and alien nests.", 50).Resource = ResourceType.ExoticMatter;
            Item("medkit", "Medkit", ItemKind.Consumable, 25, new Color(1f, 0.5f, 0.62f), "Restores 50 health (hotbar 4).", 10).HealAmount = 50;
            Item("repair_kit", "Repair Kit", ItemKind.Consumable, 45, new Color(0.95f, 0.72f, 0.45f), "Restores 80 hull to a nearby ship.", 10).HealAmount = 80;

            void Gear(string id, string name, GearKind g, int tier, int value, string desc)
            {
                var it = Item(id, name, ItemKind.Gear, value, new Color(0.95f, 0.8f, 1f), desc, 1);
                it.Gear = g; it.GearTier = tier;
            }
            Gear("gear_suit_mk2", "Suit Mk II", GearKind.Suit, 2, 160, "+40 max health, 12% damage reduction.");
            Gear("gear_suit_mk3", "Suit Mk III", GearKind.Suit, 3, 320, "+80 max health, 24% damage reduction.");
            Gear("gear_o2_mk2", "Oxygen Tank Mk II", GearKind.Oxygen, 2, 120, "+60 s of oxygen.");
            Gear("gear_o2_mk3", "Oxygen Tank Mk III", GearKind.Oxygen, 3, 240, "+120 s of oxygen.");
            Gear("gear_jetpack_mk2", "Jetpack Mk II", GearKind.Jetpack, 2, 140, "More thrust and fuel.");
            Gear("gear_jetpack_mk3", "Jetpack Mk III", GearKind.Jetpack, 3, 280, "Flight-grade jetpack.");
            Gear("gear_drill_mk2", "Mining Drill Mk II", GearKind.MiningTool, 2, 130, "Mines faster and hurts more.");
            Gear("gear_drill_mk3", "Mining Drill Mk III", GearKind.MiningTool, 3, 260, "Industrial drill.");
            Gear("gear_rifle", "Pulse Rifle", GearKind.Rifle, 1, 200, "Automatic hitscan rifle (hotbar 3).");

            BuildDriveComponents();

            foreach (var (id, m) in moduleDefs)
            {
                var it = Item(id, m.DisplayName, ItemKind.Module, m.CreditValue, m.Tint, m.Description, 1);
                it.Module = m;
                if (id == "module_engine_ark" || m.Tier == 1 && (id.Contains("engine") || id.Contains("laser"))) it.SoldAtTrader = false;
            }
        }

        /// <summary>
        /// Warp drive components (#78): core, nav computer, cooling and Exotic tank in tiers 1-6 (tier 6 = Home Drive core /
        /// Warden parts). Each becomes an item (Kind=DriveComponent). Cores only come from Warden enclaves and wrecks.
        /// </summary>
        static void BuildDriveComponents()
        {
            var slots = new[] { DriveSlot.Core, DriveSlot.Navigation, DriveSlot.Cooling, DriveSlot.Tank };
            var tints = new[] { new Color(0.45f, 0.75f, 1f), new Color(0.4f, 1f, 0.85f), new Color(0.55f, 0.85f, 1f), new Color(0.72f, 0.45f, 1f) };
            var descs = new[]
            {
                "Warp core: sets the drive's top speed.",
                "Nav computer: sees obstacles earlier, so more of the core's speed is safe in fields. Match it to the core.",
                "Cooling: how long full throttle holds before the drive overheats. Match it to the core.",
                "Exotic Matter tank: more capacity = longer warps, skipping galaxies.",
            };
            for (int s = 0; s < slots.Length; s++)
                for (int tier = 1; tier <= DriveComponentDef.MaxTier; tier++)
                {
                    string id = DriveComponentDef.ItemId(slots[s], tier);
                    var d = Asset<DriveComponentDef>($"{DataDir}/Drive/{id}.asset");
                    d.Id = id; d.Slot = slots[s]; d.Tier = tier; d.DisplayName = DriveComponentDef.FullName(slots[s], tier); d.Description = descs[s];
                    int value = Mathf.RoundToInt((slots[s] == DriveSlot.Core ? 160f : 70f) * Mathf.Pow(tier, 1.6f));
                    var it = Item(id, d.DisplayName, ItemKind.DriveComponent, value, tints[s], d.Description, 1);
                    it.Drive = d;
                    // Traders sell nav/cooling/tank parts; cores are found (Warden enclaves, wrecks), never bought.
                    it.SoldAtTrader = slots[s] != DriveSlot.Core && tier < DriveComponentDef.MaxTier;
                    EditorUtility.SetDirty(d);
                }
        }

        static WarpDriveDef BuildWarpDrive()
        {
            var d = Asset<WarpDriveDef>($"{DataDir}/Drive/WarpDrive.asset");
            // Speeds relative to Mk I; with the legs in BuildGalaxies a balanced build gives (gameplan #72 targets):
            // next galaxy ~10 min with the core found there, skipping ~35-40 min, home: Mk I ~36 h, Mk II ~12 h, Mk III ~3.6 h,
            // Mk IV ~80 min, Mk V ~30 min, Home Drive from the LMC 5 min.
            d.CoreSpeed = new[] { 1f, 3f, 10f, 25f, 60f, 240f };
            d.TankCapacity = new[] { 12, 20, 30, 42, 56, 75 };
            d.FieldShare = 0.3f; d.NavMatched = 0.6f; d.NavStep = 0.12f; d.NavMin = 0.12f; d.CoolingStep = 0.15f; d.CoolingMin = 0.35f;
            d.SkipFuelSurcharge = 0.25f;
            d.StartExotic = 4;
            // #80: system cruise 1-3 km/s, system warps 1:30 (Mk I) ... ~13 s (Home Drive).
            d.CruiseSpeed = new[] { 1000f, 1400f, 1800f, 2200f, 2600f, 3000f };
            d.HopMinutes = 1.5f;
            d.HopSpeedExponent = 0.35f;
            EditorUtility.SetDirty(d);
            return d;
        }

        /// <summary>Story texts of the time loop (#86): deja-vu radio, echoes from earlier loops, crew logbook, reset banners.</summary>
        static StoryDef BuildStory()
        {
            var d = Asset<StoryDef>($"{DataDir}/Story.asset");
            StoryDef.RadioLine R(string who, string text) => new StoryDef.RadioLine { Speaker = who, Text = text };
            StoryDef.Echo E(int galaxy, int minLoop, string who, string text) => new StoryDef.Echo { Galaxy = galaxy, MinLoop = minLoop, Speaker = who, Text = text };
            d.DejaVu = new[]
            {
                R("VEGA", "Wormhole exit... again? We were here. I remember this light."),
                R("LYRA", "Same stars, same silence. Third time? Fourth? Logbook says we keep coming back."),
                R("ALTAIR", "Again. Fine. We know the way now - let's make it count this time."),
                R("DENEB", "The loop holds us, but it can't take what we remember. Go."),
            };
            d.Echoes = new[]
            {
                E(1, 2, "ECHO // VEGA", "If you hear this: the derelict Ark is worth claiming. Don't leave M33 without it."),
                E(1, 3, "ECHO // LYRA", "Exotic Matter first, sightseeing later. The tank decides how far we get."),
                E(2, 2, "ECHO // ALTAIR", "Andromeda's Wardens keep a Mk III core. Clear the enclave before you warp on."),
                E(2, 4, "ECHO // VEGA", "Swarms everywhere here. Stay in the ship until the guns are better."),
                E(3, 2, "ECHO // DENEB", "Nebulae blind the nav. Throttle down in the fields - a hull breach costs more time."),
                E(4, 2, "ECHO // LYRA", "Leo I is dark. Something hunts between the stars. Keep the shields up."),
                E(5, 2, "ECHO // ALTAIR", "The last enclave holds the Home Drive. Without it the flight home takes forever."),
                E(DriveMath.Home, 2, "ECHO // VEGA", "The Rift wakes near the end. Don't drop out, don't slow down. Just fly."),
            };
            d.LogLoopStart = "Loop {loop}. Woke at the wormhole exit in {galaxy}. Home ETA {eta}.";
            d.LogGalaxy = "Reached {galaxy}. Home ETA {eta}.";
            d.LogWarden = "Wardens of {galaxy} defeated.";
            d.LogDrive = "Installed {part}. Home ETA {eta}.";
            d.LogShipLost = "Lost the {ship}.";
            d.LogFlightHome = "Left {galaxy} for home. The Rift is waking.";
            d.LogCrewLost = "The whole crew was lost in {galaxy}.";
            d.LogRiftCaught = "The Rift caught us on the flight home.";
            d.LogHomecoming = "Home. Sol is outside the window.";
            d.ResetCrewLost = "Crew lost";
            d.ResetRiftCaught = "Caught by the Rift";
            EditorUtility.SetDirty(d);
            return d;
        }

        static void BuildLoot()
        {
            LootTable L(string id, int rolls, params (string item, int min, int max, float w)[] entries)
            {
                var t = Asset<LootTable>($"{DataDir}/Loot/{id}.asset");
                t.Rolls = rolls;
                t.Entries = new List<LootTable.Entry>();
                foreach (var e in entries)
                    if (items.TryGetValue(e.item, out var it)) t.Entries.Add(new LootTable.Entry { Item = it, Min = e.min, Max = e.max, Weight = e.w });
                    else Debug.LogWarning("[Voidward] Loot item missing: " + e.item);
                loot[id] = t;
                return t;
            }
            L("loot_t1", 3, ("ore", 2, 4, 3), ("crystal", 1, 3, 2), ("scrap", 2, 4, 3), ("fuel", 1, 2, 2), ("exotic", 1, 2, 1.6f), ("medkit", 1, 1, 1),
                ("module_shield_mk1", 1, 1, 0.4f), ("module_hull_mk1", 1, 1, 0.4f), ("module_cargo_mk1", 1, 1, 0.3f), ("module_scanner_mk1", 1, 1, 0.3f),
                ("module_fueltank_mk1", 1, 1, 0.3f), ("module_missile_mk1", 1, 1, 0.3f), ("module_flak_mk1", 1, 1, 0.3f), ("gear_o2_mk2", 1, 1, 0.3f), ("gear_rifle", 1, 1, 0.25f),
                ("module_targeting_mk1", 1, 1, 0.4f), ("module_splitter_mk1", 1, 1, 0.35f), ("module_focus_mk1", 1, 1, 0.35f), ("module_capacitor_mk1", 1, 1, 0.3f));
            L("loot_t2", 3, ("ore", 3, 5, 2), ("crystal", 2, 4, 2), ("scrap", 3, 5, 2), ("fuel", 2, 3, 2), ("exotic", 1, 3, 2), ("medkit", 1, 2, 1), ("repair_kit", 1, 1, 0.8f),
                ("module_engine_mk2", 1, 1, 0.35f), ("module_laser_mk2", 1, 1, 0.35f), ("module_shield_mk2", 1, 1, 0.35f), ("module_hull_mk2", 1, 1, 0.35f),
                ("module_missile_mk2", 1, 1, 0.25f), ("module_flak_mk2", 1, 1, 0.25f), ("module_cargo_mk2", 1, 1, 0.2f), ("module_fueltank_mk2", 1, 1, 0.2f),
                ("gear_suit_mk2", 1, 1, 0.3f), ("gear_jetpack_mk2", 1, 1, 0.3f), ("gear_drill_mk2", 1, 1, 0.3f), ("gear_rifle", 1, 1, 0.25f),
                ("module_targeting_mk2", 1, 1, 0.3f), ("module_splitter_mk2", 1, 1, 0.25f), ("module_focus_mk2", 1, 1, 0.25f), ("module_capacitor_mk2", 1, 1, 0.25f));
            L("loot_t3", 4, ("crystal", 3, 6, 2), ("scrap", 4, 6, 2), ("fuel", 2, 4, 2), ("exotic", 2, 4, 2), ("medkit", 1, 2, 1), ("repair_kit", 1, 2, 1),
                ("module_engine_mk3", 1, 1, 0.3f), ("module_laser_mk3", 1, 1, 0.3f), ("module_shield_mk3", 1, 1, 0.3f), ("module_hull_mk3", 1, 1, 0.3f),
                ("module_scanner_mk2", 1, 1, 0.2f), ("module_targeting_mk3", 1, 1, 0.3f), ("gear_suit_mk3", 1, 1, 0.25f), ("gear_o2_mk3", 1, 1, 0.25f), ("gear_jetpack_mk3", 1, 1, 0.25f), ("gear_drill_mk3", 1, 1, 0.25f));
            L("loot_pirate", 1, ("scrap", 1, 3, 3), ("fuel", 1, 2, 2), ("exotic", 1, 1, 0.35f), ("ore", 1, 2, 1));
            L("loot_drone", 1, ("scrap", 1, 1, 3), ("crystal", 1, 1, 0.5f));
            L("loot_alien", 1, ("crystal", 1, 2, 3), ("ore", 1, 1, 1), ("medkit", 1, 1, 0.25f));
            L("loot_guardian", 2, ("crystal", 3, 5, 2), ("exotic", 1, 2, 1), ("gear_drill_mk2", 1, 1, 0.3f), ("gear_suit_mk2", 1, 1, 0.2f));
            L("loot_gunship", 3, ("scrap", 3, 6, 2), ("exotic", 1, 2, 1), ("module_missile_mk1", 1, 1, 0.5f), ("module_flak_mk2", 1, 1, 0.4f), ("fuel", 2, 3, 1),
                ("module_splitter_mk1", 1, 1, 0.3f), ("module_targeting_mk2", 1, 1, 0.3f));
        }

        static List<EnemyDef> BuildEnemies()
        {
            var list = new List<EnemyDef>();
            EnemyDef E(EnemyKind k, string name, bool space, float hp, float speed, float turn, float dmg, float range, float cd, float aggro, float flee, int dust, string lootId, float scale = 1f)
            {
                var e = Asset<EnemyDef>($"{DataDir}/Enemies/Enemy_{k}.asset");
                e.Kind = k; e.DisplayName = name; e.IsSpace = space; e.MaxHp = hp; e.MoveSpeed = speed; e.TurnSpeed = turn; e.Damage = dmg;
                e.AttackRange = range; e.AttackCooldown = cd; e.AggroRange = aggro; e.FleeThreshold = flee; e.StardustKills = dust;
                e.Loot = lootId != null ? loot[lootId] : null; e.Scale = scale;
                list.Add(e);
                return e;
            }
            // Space enemies: leash = chased target lost beyond this distance, pursuit = max chase distance from home.
            // All are slower than a boosting ship, so players can always outrun them (#19).
            var pf = E(EnemyKind.PirateFighter, "Pirate Fighter", true, 45, 55, 140, 3.2f, 170, 1.0f, 650, 0.25f, 1, "loot_pirate");
            pf.LeashRange = 900f; pf.PursuitRange = 1600f;
            var ds = E(EnemyKind.DroneSwarm, "Swarm Drone", true, 18, 66, 200, 2f, 130, 0.7f, 550, 0f, 1, "loot_drone");
            ds.LeashRange = 800f; ds.PursuitRange = 1400f;
            var gs = E(EnemyKind.PirateGunship, "Pirate Gunship", true, 260, 30, 50, 6, 280, 1.3f, 750, 0.15f, 4, "loot_gunship");
            gs.LeashRange = 1000f; gs.PursuitRange = 1500f;
            E(EnemyKind.Crawler, "Crawler", false, 35, 7, 240, 10, 2.6f, 1f, 45, 0f, 1, "loot_alien");
            E(EnemyKind.Spitter, "Spitter", false, 30, 5, 200, 8, 28, 2f, 50, 0.3f, 1, "loot_alien");
            E(EnemyKind.FlyingCritter, "Skyskate", false, 22, 9, 200, 6, 22, 1.5f, 55, 0f, 1, "loot_alien");
            E(EnemyKind.NestGuardian, "Nest Guardian", false, 220, 5.5f, 160, 25, 4.2f, 2.2f, 60, 0f, 4, "loot_guardian", 2f);
            E(EnemyKind.BossMothership, "The Warden", true, 3000, 12, 12, 20, 600, 1f, 1400, 0f, 50, null, 1f);
            return list;
        }

        static void BuildBiomes()
        {
            BiomeDef B(Biome kind, string name, string shape, Color low, Color mid, Color high, Color atmo, bool breathable,
                params (ScatterKind k, int n)[] scatter)
            {
                var b = Asset<BiomeDef>($"{DataDir}/Biomes/Biome_{kind}.asset");
                b.Biome = kind; b.DisplayName = name; b.Shape = planetShapes[shape];
                b.LowColor = low; b.MidColor = mid; b.HighColor = high; b.ScatterTint = mid; b.HasAtmosphere = true; b.AtmosphereColor = atmo; b.Breathable = breathable;
                b.Scatter = new List<BiomeDef.ScatterEntry>();
                foreach (var s in scatter) b.Scatter.Add(new BiomeDef.ScatterEntry { Kind = s.k, Count = s.n });
                biomes[kind.ToString()] = b;
                return b;
            }
            B(Biome.Ice, "Frost World", "Ice", new Color(0.42f, 0.62f, 0.95f), new Color(0.64f, 0.8f, 0.97f), new Color(0.88f, 0.93f, 1f), new Color(0.6f, 0.85f, 1f, 0.55f), true,
                (ScatterKind.OreNode, 7), (ScatterKind.CrystalNode, 7), (ScatterKind.FuelNode, 3), (ScatterKind.LootCrate, 5), (ScatterKind.Nest, 1), (ScatterKind.Ruin, 1), (ScatterKind.CrashedShip, 1));
            B(Biome.Desert, "Dune World", "Desert", new Color(0.78f, 0.46f, 0.3f), new Color(0.92f, 0.7f, 0.44f), new Color(0.96f, 0.84f, 0.64f), new Color(1f, 0.78f, 0.5f, 0.45f), false,
                (ScatterKind.OreNode, 9), (ScatterKind.ScrapNode, 4), (ScatterKind.FuelNode, 4), (ScatterKind.LootCrate, 5), (ScatterKind.Nest, 1), (ScatterKind.Ruin, 2), (ScatterKind.CrashedShip, 1));
            B(Biome.ToxicJungle, "Toxic Jungle", "ToxicJungle", new Color(0.12f, 0.46f, 0.42f), new Color(0.33f, 0.8f, 0.42f), new Color(0.78f, 0.96f, 0.4f), new Color(0.55f, 1f, 0.5f, 0.6f), false,
                (ScatterKind.CrystalNode, 6), (ScatterKind.OreNode, 5), (ScatterKind.FuelNode, 3), (ScatterKind.LootCrate, 6), (ScatterKind.Nest, 3), (ScatterKind.Ruin, 1));
            B(Biome.Crystal, "Crystal World", "Crystal", new Color(0.18f, 0.24f, 0.55f), new Color(0.24f, 0.44f, 0.95f), new Color(0.55f, 0.72f, 1f), new Color(0.45f, 0.7f, 1f, 0.55f), true,
                (ScatterKind.CrystalNode, 12), (ScatterKind.OreNode, 4), (ScatterKind.LootCrate, 5), (ScatterKind.Nest, 2), (ScatterKind.Ruin, 2), (ScatterKind.CrashedShip, 1));
            B(Biome.Volcanic, "Ember World", "Volcanic", new Color(0.13f, 0.13f, 0.17f), new Color(0.32f, 0.24f, 0.3f), new Color(1f, 0.52f, 0.25f), new Color(1f, 0.45f, 0.3f, 0.55f), false,
                (ScatterKind.OreNode, 9), (ScatterKind.FuelNode, 5), (ScatterKind.LootCrate, 5), (ScatterKind.Nest, 2), (ScatterKind.CrashedShip, 2));
        }

        /// <summary>The 5 galaxies of the journey home (#46, gameplan #72). Difficulty and loot rise along the route.</summary>
        static GalaxyDef[] BuildGalaxies(PaletteDef[] pal)
        {
            GalaxyDef Gx(int i, string name, string designation, float mly, int drive, string intro, int minSys, int maxSys, float len,
                BiomeDef[] pool, EnemyKind[] space, int cap, float grace, float ramp, EnemyKind[] surface, float tier, int loot, int events, Color nextTint)
            {
                var g = Asset<GalaxyDef>($"{DataDir}/Galaxies/Galaxy_{i}.asset");
                g.Index = i; g.DisplayName = name; g.Designation = designation; g.DistanceMly = mly; g.DriveTier = drive; g.Intro = intro;
                g.MinSystems = minSys; g.MaxSystems = maxSys; g.SystemLength = len; g.BiomePool = pool;
                g.SpaceEnemies = space; g.SpaceEnemyCap = cap; g.SpaceEnemyGrace = grace; g.SpaceEnemyRamp = ramp; g.SurfaceEnemies = surface;
                g.EnemyTier = tier; g.LootTier = loot; g.EventCount = events; g.Palette = pal[i - 1]; g.NextGalaxyTint = nextTint;
                // #78: each leg takes ~10 min with the core this galaxy's enclave guards (LMC -> home ~5 min with the Home Drive).
                g.LegLength = new[] { 30f, 100f, 250f, 600f, 1200f }[i - 1];
                g.LegFuel = new[] { 8, 10, 12, 14, 16 }[i - 1];
                return g;
            }
            var fighters = new[] { EnemyKind.PirateFighter, EnemyKind.DroneSwarm, EnemyKind.PirateGunship };
            var critters = new[] { EnemyKind.Crawler, EnemyKind.Spitter, EnemyKind.FlyingCritter };
            return new[]
            {
                Gx(1, "Triangulum", "M33", 2.73f, 2, "The wormhole spat you out here. Calm space - explore, salvage and find better drive parts.", 4, 5, 8000,
                    new[] { biomes["Ice"], biomes["Desert"], biomes["Crystal"] }, new[] { EnemyKind.DroneSwarm, EnemyKind.PirateFighter }, 3, 120f, 180f,
                    new[] { EnemyKind.Crawler, EnemyKind.Spitter }, 1f, 1, 2, new Color(0.85f, 0.88f, 1f)),
                Gx(2, "Andromeda", "M31", 2.54f, 3, "Huge and dense, full of worlds. Swarms guard what you need.", 5, 6, 9000,
                    new[] { biomes["ToxicJungle"], biomes["Desert"], biomes["Ice"], biomes["Crystal"] }, fighters, 4, 75f, 120f,
                    critters, 1.4f, 2, 3, new Color(1f, 0.75f, 0.9f)),
                Gx(3, "Barnard's Galaxy", "NGC 6822", 1.6f, 4, "A dwarf galaxy of nebulae and newborn stars. Crystal beings drift between them.", 4, 5, 9000,
                    new[] { biomes["Crystal"], biomes["ToxicJungle"], biomes["Ice"] }, fighters, 4, 60f, 110f,
                    critters, 1.8f, 2, 3, new Color(1f, 0.92f, 0.75f)),
                Gx(4, "Leo I", "Leo I", 0.82f, 5, "Ancient, star-poor and dark. Something hunts in the void.", 4, 5, 10000,
                    new[] { biomes["Volcanic"], biomes["Desert"], biomes["Ice"] }, new[] { EnemyKind.PirateGunship, EnemyKind.PirateFighter, EnemyKind.DroneSwarm }, 5, 50f, 100f,
                    critters, 2.2f, 3, 3, new Color(0.9f, 0.9f, 1f)),
                Gx(5, "Large Magellanic Cloud", "LMC", 0.16f, 6, "The Milky Way fills the sky. The Wardens guard their last cores here.", 5, 6, 10000,
                    new[] { biomes["Volcanic"], biomes["ToxicJungle"], biomes["Crystal"], biomes["Desert"] }, fighters, 6, 45f, 90f,
                    critters, 2.7f, 3, 4, new Color(1f, 0.9f, 0.75f)),
            };
        }

        static List<RecipeDef> BuildRecipes()
        {
            var list = new List<RecipeDef>();
            void R(string output, int count, params (string id, int n)[] inputs)
            {
                var r = Asset<RecipeDef>($"{DataDir}/Recipes/Recipe_{output}.asset");
                r.Id = "recipe_" + output; r.Output = items[output]; r.OutputCount = count;
                r.Inputs = new List<RecipeDef.Ingredient>();
                foreach (var i in inputs) r.Inputs.Add(new RecipeDef.Ingredient { Item = items[i.id], Count = i.n });
                list.Add(r);
            }
            R("exotic", 1, ("crystal", 4), ("fuel", 2));
            R("fuel", 2, ("ore", 3));
            R("medkit", 1, ("crystal", 1), ("ore", 1));
            R("repair_kit", 1, ("scrap", 4));
            R("module_laser_mk2", 1, ("ore", 6), ("crystal", 4), ("scrap", 4));
            R("module_engine_mk2", 1, ("ore", 8), ("crystal", 4), ("fuel", 2));
            R("module_shield_mk2", 1, ("crystal", 8), ("scrap", 4));
            R("module_hull_mk2", 1, ("scrap", 10), ("ore", 6));
            R("module_missile_mk1", 1, ("scrap", 6), ("crystal", 4), ("fuel", 2));
            R("module_flak_mk1", 1, ("ore", 6), ("scrap", 4));
            R("module_cargo_mk1", 1, ("scrap", 6), ("ore", 4));
            R("module_fueltank_mk1", 1, ("scrap", 4), ("fuel", 3));
            R("module_targeting_mk1", 1, ("crystal", 4), ("scrap", 3));
            R("module_focus_mk1", 1, ("crystal", 5), ("ore", 3));
            R("module_splitter_mk1", 1, ("scrap", 6), ("crystal", 3), ("ore", 2));
            R("gear_suit_mk2", 1, ("scrap", 8), ("crystal", 4));
            R("gear_o2_mk2", 1, ("crystal", 4), ("ore", 4));
            R("gear_jetpack_mk2", 1, ("scrap", 5), ("fuel", 3), ("crystal", 2));
            R("gear_drill_mk2", 1, ("ore", 6), ("crystal", 4));
            R("gear_rifle", 1, ("scrap", 8), ("crystal", 4), ("ore", 4));
            return list;
        }

        static ShipDef[] BuildShips()
        {
            ShipDef S(ShipClass c, string name, float hull, float shield, float regen, float fuel, float burn, float thrust, float boost, float turn, float roll,
                float maxSpeed, float lin, float ang, StationRole[] stations, int cw, int ch, bool interior, string[] start)
            {
                var s = Asset<ShipDef>($"{DataDir}/Ships/Ship_{c}.asset");
                s.Class = c; s.DisplayName = name; s.MaxHull = hull; s.MaxShield = shield; s.ShieldRegen = regen; s.ShieldRegenDelay = 4f; s.MaxFuel = fuel;
                s.FuelBurnPerSecond = burn; s.Thrust = thrust; s.BoostMultiplier = boost; s.TurnSpeedDeg = turn; s.RollSpeedDeg = roll; s.MaxSpeed = maxSpeed;
                s.LinearDamping = lin; s.AngularDamping = ang; s.Stations = stations; s.CargoWidth = cw; s.CargoHeight = ch; s.HasInterior = interior;
                s.StartingModuleItemIds = start;
                return s;
            }
            return new[]
            {
                S(ShipClass.Dart, "Dart", 110, 50, 6, 150, 0.5f, 34, 2.0f, 110, 140, 95, 0.35f, 3f, new[] { StationRole.Pilot }, 4, 2, false,
                  new[] { "module_engine_mk1", "module_laser_mk1" }),
                S(ShipClass.Mule, "Mule", 180, 80, 7, 230, 0.6f, 26, 1.8f, 80, 110, 80, 0.4f, 3f, new[] { StationRole.Pilot }, 6, 3, false,
                  new[] { "module_engine_mk1", "module_laser_mk1", "module_flak_mk1", "module_cargo_mk1" }),
                S(ShipClass.Ark, "Ark", 600, 250, 14, 420, 0.7f, 16, 1.6f, 38, 45, 70, 0.4f, 3.5f,
                  new[] { StationRole.Pilot, StationRole.Turret, StationRole.Turret, StationRole.Engineer }, 8, 4, true,
                  new[] { "module_engine_ark", "module_laser_mk2", "module_missile_mk1", "module_shield_mk2", "module_hull_mk2" }),
            };
        }
    }
}
