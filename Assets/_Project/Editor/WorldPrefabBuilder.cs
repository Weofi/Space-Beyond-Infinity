using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Inventory;
using Voidward.Planet;
using Voidward.World;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Asset-based world objects + POIs (#99): the trader station, wreck, ruin, nest, loot crate, resource node and pickup
    /// built around the models of Tools/Blender/world_assets.py (WorldModelBuilder). Same components, colliders and
    /// gameplay anchors as the procedural builds in PrefabBuilder, which stay as the fallback when an FBX is missing.
    /// </summary>
    public static partial class ContentBuilder
    {
        // Station layout (world_assets.py prints the ring profile): the lower habitat ring's hull peaks at ~33 m radius
        // around y -6.6; the landing pad (World_Pad, origin = deck centre) docks there on a short arm.
        const float StationDeckY = -6.5f, StationPadZ = 52f, StationPadHalf = 15f, StationRingR = 31f;

        /// <summary>
        /// Trader station: the twin habitat rings (64 m, "Space Station 3") spin slowly; a static mesh collider copy keeps
        /// ships out (the rings are round, the cables inside barely move). In front of the lower ring the teleporter-pad
        /// platform is the landing deck (gravity volume, mesh collider) with the trader console at its back.
        /// </summary>
        static GameObject BuildAssetStation()
        {
            var go = NetRoot("TraderStation", false);
            var ts = go.AddComponent<TraderStation>();
            var spinner = Child(go.transform, "Ring", Vector3.zero);
            WorldModel(spinner.transform, "World_Station");
            Set(ts, "spinner", spinner.transform);
            var ringMesh = WorldMesh("World_Station");
            if (ringMesh != null) Child(go.transform, "RingCollider", Vector3.zero).AddComponent<MeshCollider>().sharedMesh = ringMesh;

            var deck = new Vector3(0f, StationDeckY, StationPadZ);
            var pad = Child(go.transform, "Pad", deck);
            if (WorldModelSolid(pad.transform, "World_Pad") == null)
                Box(pad, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));

            // docking arm from the pad's back edge into the lower ring, a walkway with railings and light strips
            float armFrom = StationRingR - 2f, armTo = StationPadZ - StationPadHalf + 1.5f, armLen = armTo - armFrom;
            var arm = Hero(0.12f);
            With(arm, 0.45f, 0.85f, 0.6f, () => arm.AddBox(T(0f, StationDeckY - 1.1f, armFrom + armLen * 0.5f), new Vector3(5f, 2.2f, armLen), Gunmetal));
            With(arm, 0.45f, 1f, 0f, () =>
            {
                for (int k = 0; k <= (int)armLen; k += 2) arm.AddBox(T(0f, StationDeckY + 0.02f, armFrom + k), new Vector3(5.2f, 0.06f, 0.25f), Slate);
                for (int s = -1; s <= 1; s += 2)
                    arm.AddTube(new Vector3(s * 2.4f, StationDeckY + 1.1f, armFrom), new Vector3(s * 2.4f, StationDeckY + 1.1f, armTo), 0.06f, 0.06f, 8, Gunmetal);
            });
            Glow(arm, () => { for (int s = -1; s <= 1; s += 2) arm.AddBox(T(s * 2.45f, StationDeckY + 0.05f, armFrom + armLen * 0.5f), new Vector3(0.08f, 0.05f, armLen), Cyan); }, 1.2f);
            Visual(go.transform, "Arm", SaveMesh(arm, "Station_Arm"), MatHull);
            Box(go, new Vector3(0f, StationDeckY - 1.1f, armFrom + armLen * 0.5f), new Vector3(5f, 2.2f, armLen));

            // trader console at the back of the deck, screen towards the pad centre (+z)
            var kioskPos = new Vector3(0f, StationDeckY, StationPadZ - 10f);
            var kiosk = Hero(0.12f);
            With(kiosk, Satin, 0.4f, 0.6f, () => kiosk.AddBox(T(0f, 1.5f, 0f), new Vector3(3f, 3f, 1.2f), Graphite));
            With(kiosk, 0.75f, 0.85f, 0f, () => kiosk.AddBox(T(0f, 0.15f, 0.6f), new Vector3(3.1f, 0.2f, 0.1f), Gold));
            Glow(kiosk, () => kiosk.AddBox(T(0f, 2.2f, 0.62f), new Vector3(2f, 0.9f, 0.04f), Cyan), 0.9f);
            var kv = Visual(go.transform, "Kiosk", SaveMesh(kiosk, "Station_Kiosk"), MatHull);
            kv.transform.localPosition = kioskPos;
            Box(go, kioskPos + new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 1.2f));
            var console = Child(go.transform, "TraderConsole", kioskPos + new Vector3(0f, 1.5f, 0.5f));
            Box(console, Vector3.zero, new Vector3(4f, 4f, 3f), true);
            AddLight(go.transform, "ConsoleLight", kioskPos + new Vector3(0f, 3.5f, 3f), LampWhite, 30f, 14f);

            var grav = Child(go.transform, "PadGravity", deck + new Vector3(0f, 7f, 0f));
            Box(grav, Vector3.zero, new Vector3(30f, 14f, 30f), true);
            grav.AddComponent<GravityVolume>();
            for (int i = 0; i < 4; i++)
                AddLight(go.transform, "PadLight", deck + new Vector3(-11f + (i % 2) * 22f, 4f, -11f + (i / 2) * 22f), Cyan, 30f, 20f);
            AddLight(go.transform, "RingLight", new Vector3(0f, 0f, StationRingR + 6f), LampWhite, 25f, 40f);
            return SavePrefab(go);
        }

        /// <summary>Derelict in space: the torn mid-section of the destroyer (mesh collider) with its loose turrets drifting around.</summary>
        static GameObject BuildAssetWreck()
        {
            var go = NetRoot("Wreck", false);
            WorldModelSolid(go.transform, "World_Wreck", "Mesh");
            WorldModel(go.transform, "World_Wreck_Chunks", "Debris");
            AddLight(go.transform, "Emergency", new Vector3(0f, 2f, 0f), Red, 6f, 18f);
            return SavePrefab(go);
        }

        /// <summary>Precursor ruin: the "Ancient Relic" painted like the Wardens (ivory, cyan crevices), 16 m across, mesh collider.</summary>
        static GameObject BuildAssetRuin(Mesh mesh)
        {
            var go = NetRoot("Ruin", false);
            var v = Visual(go.transform, "Mesh", mesh, MatHull);
            v.AddComponent<MeshCollider>().sharedMesh = mesh;
            AddLight(go.transform, "Glow", new Vector3(0f, 4.5f, 0f), Cyan, 18f, 16f);
            return SavePrefab(go);
        }

        /// <summary>Alien nest: the egg cluster (4.4 m), same health/collider as the procedural hive mound.</summary>
        static GameObject BuildAssetNest()
        {
            var go = NetRoot("AlienNest", false);
            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = 160f;
            var so = new SerializedObject(hc);
            so.FindProperty("team").enumValueIndex = (int)Team.Aliens;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<AlienNest>();
            WorldModel(go.transform, "World_Nest", "Mound");
            AddLight(go.transform, "Glow", new Vector3(0f, 2.4f, 0f), Pink, 10f, 10f);
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 2.4f; sc.center = new Vector3(0f, 0.9f, 0f);
            return SavePrefab(go);
        }

        // world_assets.py: crate lid hinge (unity) (0, 1.118, -0.725)
        static readonly Vector3 CrateHinge = new Vector3(0f, 1.118f, -0.725f);

        /// <summary>Loot crate: the sci-fi cargo crate, its top as the lid on a hinge at the back edge (LootCrate swings it open).</summary>
        static GameObject BuildAssetCrate()
        {
            var go = NetRoot("LootCrate", false);
            var c = go.AddComponent<LootCrate>();
            WorldModel(go.transform, "World_Crate", "Body");
            var lid = Child(go.transform, "LidPivot", CrateHinge);
            var lm = WorldModel(lid.transform, "World_Crate_Lid", "Lid");
            if (lm != null) lm.transform.localPosition = -CrateHinge;
            AddLight(go.transform, "Glow", new Vector3(0f, 1.6f, 0.9f), Cyan, 2.5f, 4f);
            Box(go, new Vector3(0f, 0.63f, 0f), new Vector3(1.45f, 1.27f, 1.45f));
            Set(c, "lid", lid.transform);
            return SavePrefab(go);
        }

        /// <summary>
        /// Resource node: Ore = the photoscanned boulder with copper chunks, Crystal = a smaller boulder grown with glowing
        /// shards, Scrap = a heap of destroyer plates, Fuel = the sci-fi battery as a fuel canister. Order as ResourceNode.
        /// </summary>
        static GameObject BuildAssetNode()
        {
            var go = NetRoot("ResourceNode", false);
            var n = go.AddComponent<ResourceNode>();
            Color oreCol = new Color(0.92f, 0.43f, 0.15f);
            var variants = new List<Transform>();

            var ore = Child(go.transform, "Ore", Vector3.zero);
            WorldModel(ore.transform, "World_Rock", "Rock");
            var veins = Stone();
            With(veins, 0.85f, 1f, 0f, () =>
            {
                veins.AddRock(Matrix4x4.TRS(new Vector3(0.75f, 0.45f, 0.5f), Quaternion.identity, Vector3.one), 0.36f, 42, oreCol);
                veins.AddRock(Matrix4x4.TRS(new Vector3(-0.7f, 0.55f, -0.3f), Quaternion.identity, Vector3.one), 0.32f, 43, oreCol);
                veins.AddRock(Matrix4x4.TRS(new Vector3(0.1f, 0.95f, 0.15f), Quaternion.identity, Vector3.one), 0.26f, 44, oreCol);
                veins.AddRock(Matrix4x4.TRS(new Vector3(-0.15f, 0.35f, 1.05f), Quaternion.identity, Vector3.one), 0.24f, 47, oreCol);
            });
            Glow(veins, () =>
            {
                veins.AddTube(new Vector3(0.66f, 0.6f, 0.52f), new Vector3(0.2f, 0.95f, 0.5f), 0.035f, 0.02f, 5, new Color(1f, 0.55f, 0.2f));
                veins.AddTube(new Vector3(-0.62f, 0.68f, -0.24f), new Vector3(-0.3f, 0.98f, 0.3f), 0.03f, 0.015f, 5, new Color(1f, 0.55f, 0.2f));
            }, 0.9f);
            Visual(ore.transform, "Veins", SaveMesh(veins, "Node_OreVeins"), MatHull);
            variants.Add(ore.transform);

            var crystal = Child(go.transform, "Crystal", Vector3.zero);
            var rock = WorldModel(crystal.transform, "World_Rock", "Rock");
            if (rock != null) rock.transform.localScale = new Vector3(0.62f, 0.45f, 0.62f);
            var shards = Stone();
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + (i % 2) * 17f;
                var m = TR(Mathf.Cos(a * Mathf.Deg2Rad) * 0.38f, 0.45f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.38f, Mathf.Sin(a) * 22f, a, 20f);
                Color col = i % 2 == 0 ? Cyan : new Color(0.55f, 0.45f, 1f);
                With(shards, 0.95f, 0.1f, 0f, () => Shard(shards, m * TR(0, 0, 0, -90, 0, 0), 1.4f + (i % 3) * 0.5f, 0.2f, 6, col), 0.35f);
            }
            With(shards, 0.95f, 0.1f, 0f, () => Shard(shards, TR(0, 0.85f, 0, -90, 0, 0), 2.2f, 0.3f, 6, new Color(0.6f, 0.95f, 1f)), 0.5f);
            Visual(crystal.transform, "Shards", SaveMesh(shards, "Node_CrystalShards"), MatHull);
            variants.Add(crystal.transform);

            var scrap = Child(go.transform, "Scrap", Vector3.zero);
            WorldModel(scrap.transform, "World_Scrap", "Heap");
            variants.Add(scrap.transform);

            var fuel = Child(go.transform, "Fuel", Vector3.zero);
            var can = WorldModel(fuel.transform, "World_Battery", "Canister");
            if (can != null) can.transform.localScale = Vector3.one * 2.1f;
            variants.Add(fuel.transform);

            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 1.25f; sc.center = new Vector3(0f, 0.65f, 0f);
            SetArray(n, "variants", variants.ToArray());
            return SavePrefab(go);
        }

        /// <summary>
        /// World pickup: items with their own model show it on the "Mesh" child (WorldPickup swaps mesh + materials);
        /// the rest show the generic energy cell (the sci-fi battery), lightly tinted in the item colour.
        /// </summary>
        static GameObject BuildAssetPickup()
        {
            var go = NetRoot("WorldPickup", false);
            var p = go.AddComponent<WorldPickup>();
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true; sc.radius = 0.7f; sc.center = new Vector3(0, 0.5f, 0);
            var vis = Child(go.transform, "Visual", new Vector3(0, 0.6f, 0));
            var mesh = Visual(vis.transform, "Mesh", null, MatHull);
            mesh.SetActive(false);
            var cell = WorldModel(vis.transform, "World_Battery", "Generic");
            if (cell != null)
            {
                cell.transform.localScale = Vector3.one * 0.75f;
                cell.transform.localPosition = new Vector3(0f, -0.23f, 0f);
                Set(p, "generic", cell);
            }
            Set(p, "visual", vis.transform);
            return SavePrefab(go);
        }
    }
}
