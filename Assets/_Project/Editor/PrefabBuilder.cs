using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using Voidward.AI;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Networking;
using Voidward.Planet;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.EditorTools
{
    /// <summary>Procedural low-poly meshes + every network prefab.</summary>
    public static partial class ContentBuilder
    {
        // Pastel palette for hard-surface props
        static readonly Color White = new Color(0.93f, 0.91f, 0.97f);
        static readonly Color Orange = new Color(1f, 0.62f, 0.42f);
        static readonly Color Teal = new Color(0.38f, 0.85f, 0.85f);
        static readonly Color Dark = new Color(0.26f, 0.25f, 0.36f);
        static readonly Color Mid = new Color(0.55f, 0.55f, 0.68f);
        static readonly Color Pink = new Color(1f, 0.55f, 0.68f);
        static readonly Color PirateRed = new Color(0.95f, 0.38f, 0.45f);
        static readonly Color PirateDark = new Color(0.3f, 0.26f, 0.36f);
        static readonly Color AlienGreen = new Color(0.55f, 0.9f, 0.45f);
        static readonly Color AlienPurple = new Color(0.65f, 0.45f, 0.9f);

        static readonly List<GameObject> networkPrefabs = new List<GameObject>();

        static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        static Matrix4x4 TR(float x, float y, float z, float rx, float ry, float rz) => Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(rx, ry, rz), Vector3.one);

        static Mesh SaveMesh(MeshBuilder mb, string name)
        {
            var m = mb.Build(name);
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(m, existing);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Visual(Transform parent, string name, Mesh mesh, Material mat = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : MatToon;
            return go;
        }

        static GameObject Child(Transform parent, string name, Vector3 pos, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            return go;
        }

        static BoxCollider Box(GameObject go, Vector3 center, Vector3 size, bool trigger = false)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = center; b.size = size; b.isTrigger = trigger;
            return b;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Voidward] Field {field} not found on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Voidward] Field {field} not found on {target.GetType().Name}"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject NetRoot(string name, bool syncTransform, NetworkTransform.AuthorityModes authority = NetworkTransform.AuthorityModes.Server)
        {
            var go = new GameObject(name);
            var no = go.AddComponent<NetworkObject>();
            no.AutoObjectParentSync = false;
            if (syncTransform)
            {
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = authority;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.Interpolate = true;
            }
            return go;
        }

        static GameObject SavePrefab(GameObject go, bool network = true)
        {
            string path = PrefabDir + "/" + go.name + ".prefab";
            // Overwriting an existing NetworkObject prefab can leave GlobalObjectIdHash at 0; always create fresh.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            if (network) networkPrefabs.Add(prefab);
            return prefab;
        }

        // ================================================================ build

        static void BuildPrefabs()
        {
            networkPrefabs.Clear();
            catalog.PlayerPawn = BuildPawn();
            catalog.PlanetPrefab = BuildPlanet();
            catalog.ShipDart = BuildDart();
            catalog.ShipMule = BuildMule();
            catalog.ShipArk = BuildArk();
            catalog.DerelictArk = catalog.ShipArk;
            catalog.EnemyPirateFighter = BuildEnemy(EnemyKind.PirateFighter);
            catalog.EnemyDrone = BuildEnemy(EnemyKind.DroneSwarm);
            catalog.EnemyGunship = BuildEnemy(EnemyKind.PirateGunship);
            catalog.EnemyCrawler = BuildEnemy(EnemyKind.Crawler);
            catalog.EnemySpitter = BuildEnemy(EnemyKind.Spitter);
            catalog.EnemyCritter = BuildEnemy(EnemyKind.FlyingCritter);
            catalog.EnemyGuardian = BuildEnemy(EnemyKind.NestGuardian);
            catalog.BossMothership = BuildBoss();
            catalog.WorldPickup = BuildPickup();
            catalog.LootCrate = BuildCrate();
            catalog.ResourceNode = BuildNode();
            catalog.NestPrefab = BuildNest();
            catalog.RuinPrefab = BuildRuin();
            catalog.CrashedShipPrefab = BuildCrashed();
            catalog.Wreck = BuildWreck();
            catalog.TraderStation = BuildStation();
            catalog.JumpGate = BuildGate();
            catalog.GameDirector = BuildDirector();
            catalog.LaserBolt = catalog.Missile = catalog.FlakBurst = catalog.SpitProjectile = catalog.Meteor = null;
            EditorUtility.SetDirty(catalog);
            BuildNetworkPrefabList();
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- player

        static Mesh AstronautMesh()
        {
            var mb = new MeshBuilder();
            mb.AddBox(T(0, 1.15f, 0), new Vector3(0.62f, 0.66f, 0.38f), White);          // torso
            mb.AddBox(T(0, 0.86f, 0), new Vector3(0.56f, 0.12f, 0.36f), Orange);          // belt
            mb.AddBox(T(-0.16f, 0.42f, 0), new Vector3(0.22f, 0.8f, 0.26f), White);       // legs
            mb.AddBox(T(0.16f, 0.42f, 0), new Vector3(0.22f, 0.8f, 0.26f), White);
            mb.AddBox(T(-0.16f, 0.05f, 0.04f), new Vector3(0.24f, 0.12f, 0.34f), Dark);   // boots
            mb.AddBox(T(0.16f, 0.05f, 0.04f), new Vector3(0.24f, 0.12f, 0.34f), Dark);
            mb.AddBox(TR(-0.42f, 1.12f, 0, 0, 0, 8), new Vector3(0.18f, 0.62f, 0.2f), White); // arms
            mb.AddBox(TR(0.42f, 1.12f, 0, 0, 0, -8), new Vector3(0.18f, 0.62f, 0.2f), White);
            mb.AddBox(T(0, 1.15f, -0.3f), new Vector3(0.5f, 0.6f, 0.24f), Orange);         // backpack
            mb.AddPrism(T(0, 1.66f, 0), 0.27f, 0.25f, 0.42f, 8, White);                   // helmet
            mb.AddBox(T(0, 1.66f, 0.2f), new Vector3(0.36f, 0.2f, 0.1f), Teal);            // visor
            return SaveMesh(mb, "Astronaut");
        }

        static GameObject BuildPawn()
        {
            var go = NetRoot("PlayerPawn", true, NetworkTransform.AuthorityModes.Owner);
            go.GetComponent<NetworkObject>().DontDestroyWithOwner = false;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 80f; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.height = 1.8f; cap.radius = 0.35f; cap.center = new Vector3(0, 0.9f, 0);
            var pm = new PhysicsMaterial("PawnFriction") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            string pmPath = MatDir + "/PawnFriction.physicMaterial";
            var existingPm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
            if (existingPm == null) { AssetDatabase.CreateAsset(pm, pmPath); existingPm = pm; }
            cap.sharedMaterial = existingPm;

            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = 100f; hc.GodModeEligible = true;
            var inv = go.AddComponent<NetInventory>();
            inv.Width = 6; inv.Height = 4; inv.IsPlayerInventory = true;
            go.AddComponent<PlayerGear>();
            go.AddComponent<NetworkPlayer>();
            go.AddComponent<NetTeleport>();
            go.AddComponent<TargetTag>().IsPawn = true;
            var pawn = go.AddComponent<PlayerPawn>();

            var body = Child(go.transform, "Body", Vector3.zero);
            Visual(body.transform, "Astronaut", AstronautMesh());

            var cam = Child(go.transform, "CameraPoint", new Vector3(0, 1.62f, 0));
            var vcam = cam.AddComponent<CinemachineCamera>();
            var lens = vcam.Lens;
            lens.FieldOfView = 75f; lens.NearClipPlane = 0.05f; lens.FarClipPlane = 20000f;
            vcam.Lens = lens;
            vcam.Priority = 30;
            vcam.enabled = false;

            // First-person tool viewmodels
            var tools = new Transform[4];
            var drill = new MeshBuilder();
            drill.AddBox(T(0, 0, 0), new Vector3(0.12f, 0.14f, 0.36f), Orange);
            drill.AddBox(T(0, -0.12f, -0.08f), new Vector3(0.08f, 0.18f, 0.1f), Dark);
            drill.AddPrism(TR(0, 0, 0.26f, 90, 0, 0), 0.06f, 0.015f, 0.2f, 6, Teal);
            var blaster = new MeshBuilder();
            blaster.AddBox(T(0, 0, 0), new Vector3(0.1f, 0.12f, 0.3f), Teal);
            blaster.AddBox(T(0, -0.11f, -0.06f), new Vector3(0.07f, 0.16f, 0.09f), Dark);
            blaster.AddPrism(TR(0, 0.02f, 0.2f, 90, 0, 0), 0.035f, 0.035f, 0.14f, 6, White);
            var rifle = new MeshBuilder();
            rifle.AddBox(T(0, 0, 0.05f), new Vector3(0.1f, 0.12f, 0.55f), Mid);
            rifle.AddBox(T(0, -0.11f, -0.1f), new Vector3(0.07f, 0.16f, 0.09f), Dark);
            rifle.AddBox(T(0, 0.09f, 0.05f), new Vector3(0.05f, 0.05f, 0.2f), Orange);
            var medkit = new MeshBuilder();
            medkit.AddBox(T(0, 0, 0), new Vector3(0.22f, 0.16f, 0.14f), White);
            medkit.AddBox(T(0, 0, 0.075f), new Vector3(0.12f, 0.035f, 0.01f), Pink);
            medkit.AddBox(T(0, 0, 0.075f), new Vector3(0.035f, 0.12f, 0.01f), Pink);
            var meshes = new[] { SaveMesh(drill, "Tool_Drill"), SaveMesh(blaster, "Tool_Blaster"), SaveMesh(rifle, "Tool_Rifle"), SaveMesh(medkit, "Tool_Medkit") };
            string[] names = { "Drill", "Blaster", "Rifle", "Medkit" };
            for (int i = 0; i < 4; i++)
            {
                var t = Child(cam.transform, "Tool_" + names[i], new Vector3(0.26f, -0.27f, 0.62f));
                t.transform.localScale = Vector3.one * 0.45f;
                var v = Visual(t.transform, "Mesh", meshes[i]);
                v.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.SetActive(false);
                tools[i] = t.transform;
            }
            var muzzle = Child(cam.transform, "Muzzle", new Vector3(0.26f, -0.25f, 0.78f));

            var beamGo = Child(go.transform, "Beam", Vector3.zero);
            var lr = beamGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.05f;
            var beamMat = Mat("VW_Beam", "VOIDWARD/Unlit");
            beamMat.SetColor("_BaseColor", new Color(0.6f, 2.4f, 3f));
            lr.sharedMaterial = beamMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;

            Set(pawn, "cameraPoint", cam.transform);
            Set(pawn, "body", body.transform);
            SetArray(pawn, "toolModels", tools);
            Set(pawn, "muzzle", muzzle.transform);
            Set(pawn, "beam", lr);
            return SavePrefab(go);
        }

        static GameObject BuildPlanet()
        {
            var go = NetRoot("Planet", false);
            go.AddComponent<PlanetBody>();
            return SavePrefab(go);
        }

        // ---------------------------------------------------------------- ships

        static GameObject SeatedBody(Transform parent)
        {
            var g = Visual(parent, "SeatedBody", AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Astronaut.asset"));
            g.transform.localPosition = new Vector3(0, -0.45f, 0);
            g.SetActive(false);
            return g;
        }

        static ShipSeat Seat(Transform ship, string name, StationRole role, Vector3 anchor, Vector3 view, Vector3 exit, Vector3 triggerCenter, Vector3 triggerSize, Quaternion? viewRot = null)
        {
            var s = Child(ship, name, Vector3.zero);
            var seat = s.AddComponent<ShipSeat>();
            seat.SeatRole = role;
            var a = Child(s.transform, "Anchor", anchor);
            var v = Child(s.transform, "ViewPoint", view, viewRot);
            var e = Child(s.transform, "Exit", exit);
            Box(s, triggerCenter, triggerSize, true);
            Set(seat, "seatAnchor", a.transform);
            Set(seat, "viewPoint", v.transform);
            Set(seat, "exitPoint", e.transform);
            Set(seat, "seatedBody", SeatedBody(a.transform));
            return seat;
        }

        static void Dashboard(Transform parent, Vector3 pos, float width)
        {
            var d = Child(parent, "Dashboard", pos, Quaternion.Euler(25f, 0f, 0f));
            var dash = d.AddComponent<ShipDashboard>();
            var panel = new MeshBuilder();
            panel.AddBox(T(0, 0, 0.02f), new Vector3(width, 0.36f, 0.04f), Dark);
            Visual(d.transform, "Panel", SaveMesh(panel, "Dashboard_" + width.ToString("0.0")));
            TextMeshPro Txt(string n, float x, TextAlignmentOptions align, float w)
            {
                var t = Child(d.transform, n, new Vector3(x, 0f, -0.005f));
                var tmp = t.AddComponent<TextMeshPro>();
                tmp.font = TMP_Settings.defaultFontAsset;
                tmp.fontSize = 0.2f;
                tmp.alignment = align;
                tmp.color = new Color(0.6f, 1f, 0.95f);
                tmp.rectTransform.sizeDelta = new Vector2(w, 0.34f);
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.text = "--";
                return tmp;
            }
            float third = width / 3f;
            dash.Left = Txt("Left", -third, TextAlignmentOptions.Left, third * 0.95f);
            dash.Center = Txt("Center", 0, TextAlignmentOptions.Center, third * 0.95f);
            dash.Right = Txt("Right", third, TextAlignmentOptions.Right, third * 0.95f);
        }

        static Transform[] Thrusters(Transform parent, params Vector3[] at)
        {
            var list = new Transform[at.Length];
            var glow = new MeshBuilder();
            glow.AddPrism(TR(0, 0, -0.5f, 90, 0, 0), 0.38f, 0.05f, 1f, 8, Color.white);
            var mesh = SaveMesh(glow, "ThrusterGlow");
            for (int i = 0; i < at.Length; i++)
            {
                var g = Visual(parent, "Thruster", mesh, MatGlow);
                g.transform.localPosition = at[i];
                g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                list[i] = g.transform;
            }
            return list;
        }

        static GameObject ShipRoot(string name, ShipClass cls, float mass, int cargoW, int cargoH)
        {
            var go = NetRoot(name, true, NetworkTransform.AuthorityModes.Owner);
            go.GetComponent<NetworkObject>().DontDestroyWithOwner = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass; rb.useGravity = false;
            go.AddComponent<HealthComponent>();
            var inv = go.AddComponent<NetInventory>();
            inv.Width = cargoW; inv.Height = cargoH; inv.IsPlayerInventory = false;
            var core = go.AddComponent<ShipCore>();
            core.Class = cls;
            var fl = go.AddComponent<ShipFlight>();
            fl.Mass = mass;
            go.AddComponent<NetTeleport>();
            go.AddComponent<TargetTag>().IsShip = true;
            return go;
        }

        static GameObject BuildDart()
        {
            var go = ShipRoot("Ship_Dart", ShipClass.Dart, 2000f, 4, 2);
            var core = go.GetComponent<ShipCore>();
            var mb = new MeshBuilder();
            mb.AddBox(T(0, 0, -0.5f), new Vector3(2.2f, 1.3f, 6f), White);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 3.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(2.2f, 1.3f, 2f), White);
            mb.AddBox(T(0, -0.55f, 0.5f), new Vector3(1.6f, 0.3f, 6.5f), Orange);
            mb.AddBox(TR(-2.6f, -0.2f, -1.2f, 0, 0, -6), new Vector3(3.6f, 0.22f, 2.6f), White);
            mb.AddBox(TR(2.6f, -0.2f, -1.2f, 0, 0, 6), new Vector3(3.6f, 0.22f, 2.6f), White);
            mb.AddBox(T(-4.3f, -0.4f, -1.4f), new Vector3(0.3f, 0.5f, 2.2f), Orange);
            mb.AddBox(T(4.3f, -0.4f, -1.4f), new Vector3(0.3f, 0.5f, 2.2f), Orange);
            mb.AddPrism(TR(-0.75f, 0, -3.8f, 90, 0, 0), 0.5f, 0.42f, 1.2f, 8, Dark);
            mb.AddPrism(TR(0.75f, 0, -3.8f, 90, 0, 0), 0.5f, 0.42f, 1.2f, 8, Dark);
            mb.AddBox(T(0, 0.9f, -2.8f), new Vector3(0.2f, 1.2f, 1.4f), Teal);
            mb.AddBox(T(-1.2f, -0.35f, 2.2f), new Vector3(0.25f, 0.25f, 1.4f), Dark);
            mb.AddBox(T(1.2f, -0.35f, 2.2f), new Vector3(0.25f, 0.25f, 1.4f), Dark);
            Visual(go.transform, "Hull", SaveMesh(mb, "Ship_Dart"));
            var glass = new MeshBuilder();
            glass.AddWedge(Matrix4x4.TRS(new Vector3(0, 0.95f, 1.2f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(1.4f, 0.7f, 2.2f), Color.white);
            Visual(go.transform, "Canopy", SaveMesh(glass, "Ship_Dart_Canopy"), MatGlass);

            Box(go, new Vector3(0, 0, 0), new Vector3(2.3f, 1.5f, 9f));
            Box(go, new Vector3(0, -0.25f, -1.2f), new Vector3(9f, 0.5f, 2.6f));

            Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, 0.1f, 1f), new Vector3(0, 1.3f, 0.9f), new Vector3(-3.2f, 0.4f, 1.5f),
                new Vector3(0, 0.5f, 1.2f), new Vector3(2.8f, 2.2f, 5.5f));
            Dashboard(go.transform, new Vector3(0, 0.78f, 1.75f), 1.0f);
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, 0.2f, -3.2f));
            Box(term, Vector3.zero, new Vector3(2.6f, 2f, 2f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-1.2f, -0.35f, 3.1f)).transform, Child(go.transform, "MuzzleR", new Vector3(1.2f, -0.35f, 3.1f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleC", new Vector3(0, -0.7f, 3.4f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-3.2f, 0.5f, 0.5f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-0.75f, 0, -4.4f), new Vector3(0.75f, 0, -4.4f));
            return SavePrefab(go);
        }

        static GameObject BuildMule()
        {
            var go = ShipRoot("Ship_Mule", ShipClass.Mule, 3500f, 6, 3);
            var core = go.GetComponent<ShipCore>();
            var mb = new MeshBuilder();
            mb.AddBox(T(0, 0, -1f), new Vector3(3.4f, 2.4f, 8f), White);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, -0.2f, 3.9f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(3.4f, 2f, 1.8f), White);
            mb.AddBox(T(-2.4f, -0.4f, -1.5f), new Vector3(1.4f, 1.6f, 6f), Orange);
            mb.AddBox(T(2.4f, -0.4f, -1.5f), new Vector3(1.4f, 1.6f, 6f), Orange);
            mb.AddBox(T(0, -1.3f, -1f), new Vector3(3f, 0.3f, 7f), Dark);
            for (int i = 0; i < 4; i++)
                mb.AddPrism(TR(i < 2 ? -1.0f : 1.0f, i % 2 == 0 ? 0.55f : -0.55f, -5.5f, 90, 0, 0), 0.5f, 0.42f, 1.2f, 8, Dark);
            mb.AddBox(T(0, 1.35f, -2f), new Vector3(1.2f, 0.3f, 3f), Teal);
            mb.AddBox(T(-1.6f, -0.9f, 2.2f), new Vector3(0.3f, 0.3f, 1.6f), Dark);
            mb.AddBox(T(1.6f, -0.9f, 2.2f), new Vector3(0.3f, 0.3f, 1.6f), Dark);
            Visual(go.transform, "Hull", SaveMesh(mb, "Ship_Mule"));
            var glass = new MeshBuilder();
            glass.AddWedge(Matrix4x4.TRS(new Vector3(0, 1.1f, 2.4f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(2f, 0.8f, 1.8f), Color.white);
            Visual(go.transform, "Canopy", SaveMesh(glass, "Ship_Mule_Canopy"), MatGlass);
            Box(go, new Vector3(0, 0, -0.6f), new Vector3(6.4f, 2.8f, 10.4f));

            Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, 0.4f, 2.2f), new Vector3(0, 1.45f, 2.0f), new Vector3(-4.2f, 0.4f, 1.5f),
                new Vector3(0, 0.6f, 1.6f), new Vector3(4f, 3f, 5.5f));
            Dashboard(go.transform, new Vector3(0, 1.2f, 3.0f), 1.6f);
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, 0, -4.8f));
            Box(term, Vector3.zero, new Vector3(3.6f, 2.6f, 2f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-1.6f, -0.9f, 3.2f)).transform, Child(go.transform, "MuzzleR", new Vector3(1.6f, -0.9f, 3.2f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleTop", new Vector3(0, 1.6f, 0.5f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-4.2f, 0.5f, 0f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-1f, 0.55f, -6.1f), new Vector3(-1f, -0.55f, -6.1f), new Vector3(1f, 0.55f, -6.1f), new Vector3(1f, -0.55f, -6.1f));
            return SavePrefab(go);
        }

        static GameObject BuildArk()
        {
            var go = ShipRoot("Ship_Ark", ShipClass.Ark, 20000f, 8, 4);
            var core = go.GetComponent<ShipCore>();
            var hc = go.GetComponent<HealthComponent>();
            hc.MaxHealth = 600f;

            // Interior: floor y=0, 8 wide (x), 26 long (z), 4 high. Open doorway at the rear.
            var mb = new MeshBuilder();
            mb.AddBox(T(0, -0.2f, 0), new Vector3(8f, 0.4f, 26f), Mid);                     // floor
            mb.AddBox(T(0, -0.01f, 0), new Vector3(2f, 0.02f, 24f), Teal);                  // floor stripe
            mb.AddBox(T(-4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f), White);                // walls
            mb.AddBox(T(4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f), White);
            mb.AddBox(T(0, 4.2f, 0), new Vector3(8.8f, 0.4f, 26f), White);                  // ceiling
            mb.AddBox(T(0, 2f, 13.2f), new Vector3(8.8f, 4.4f, 0.4f), White);               // front wall
            mb.AddBox(T(-2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), White);         // rear wall L
            mb.AddBox(T(2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), White);          // rear wall R
            mb.AddBox(T(0, 3.7f, -13.2f), new Vector3(2.2f, 1.4f, 0.4f), White);            // lintel
            // exterior shell
            mb.AddBox(T(0, -1.2f, 0), new Vector3(10f, 1.6f, 30f), Orange);
            mb.AddBox(T(-5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f), White);
            mb.AddBox(T(5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f), White);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 1.6f, 16.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(9f, 5.2f, 6f), White);
            mb.AddBox(T(0, 5f, -2f), new Vector3(5f, 1.2f, 16f), Teal);
            mb.AddBox(T(-8f, 0f, -4f), new Vector3(6f, 0.6f, 8f), White);                   // wings
            mb.AddBox(T(8f, 0f, -4f), new Vector3(6f, 0.6f, 8f), White);
            for (int i = 0; i < 4; i++)
                mb.AddPrism(TR(i < 2 ? -3.6f : 3.6f, i % 2 == 0 ? 2.8f : 0.2f, -15.5f, 90, 0, 0), 1.1f, 0.9f, 3f, 10, Dark);
            // interior furniture
            mb.AddBox(T(0, 0.5f, 11.4f), new Vector3(0.9f, 1f, 0.9f), Dark);                // pilot chair
            mb.AddBox(T(-3f, 0.5f, 3f), new Vector3(0.9f, 1f, 0.9f), Dark);                 // turret chairs
            mb.AddBox(T(3f, 0.5f, 3f), new Vector3(0.9f, 1f, 0.9f), Dark);
            mb.AddBox(T(-3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f), Teal);            // engineer console
            mb.AddBox(T(3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f), Orange);           // fabricator
            mb.AddBox(T(3.5f, 0.6f, -8f), new Vector3(0.8f, 1.2f, 2.2f), Pink);             // cargo terminal
            for (int i = 0; i < 6; i++) mb.AddBox(T(-3.6f, 1.6f, -10f + i * 4f), new Vector3(0.1f, 0.25f, 1.5f), Teal); // wall lights
            Visual(go.transform, "Hull", SaveMesh(mb, "Ship_Ark"));
            var glass = new MeshBuilder();
            glass.AddBox(T(0, 2.6f, 13.45f), new Vector3(6f, 1.6f, 0.1f), Color.white);
            Visual(go.transform, "Windshield", SaveMesh(glass, "Ship_Ark_Glass"), MatGlass);

            // Turret heads
            var turretMesh = new MeshBuilder();
            turretMesh.AddPrism(T(0, 0.3f, 0), 1f, 0.9f, 0.6f, 8, Mid);
            var head = new MeshBuilder();
            head.AddBox(T(0, 0, 0), new Vector3(1.2f, 0.8f, 1.2f), Orange);
            head.AddBox(T(-0.3f, 0, 1f), new Vector3(0.2f, 0.2f, 1.4f), Dark);
            head.AddBox(T(0.3f, 0, 1f), new Vector3(0.2f, 0.2f, 1.4f), Dark);
            var baseMesh = SaveMesh(turretMesh, "Turret_Base");
            var headMesh = SaveMesh(head, "Turret_Head");

            // Colliders (interior walkable)
            Box(go, new Vector3(0, -0.2f, 0), new Vector3(8f, 0.4f, 26f));
            Box(go, new Vector3(0, -1.2f, 0), new Vector3(10f, 1.6f, 30f));
            Box(go, new Vector3(-4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f));
            Box(go, new Vector3(4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f));
            Box(go, new Vector3(-5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f));
            Box(go, new Vector3(5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f));
            Box(go, new Vector3(0, 4.3f, 0), new Vector3(8.8f, 0.6f, 26f));
            Box(go, new Vector3(0, 5f, -2f), new Vector3(5f, 1.2f, 16f));
            Box(go, new Vector3(0, 2f, 13.2f), new Vector3(8.8f, 4.4f, 0.4f));
            Box(go, new Vector3(0, 1.6f, 16.5f), new Vector3(9f, 5.2f, 6f));
            Box(go, new Vector3(-2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f));
            Box(go, new Vector3(2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f));
            Box(go, new Vector3(0, 3.7f, -13.2f), new Vector3(2.2f, 1.4f, 0.4f));
            Box(go, new Vector3(-8f, 0f, -4f), new Vector3(6f, 0.6f, 8f));
            Box(go, new Vector3(8f, 0f, -4f), new Vector3(6f, 0.6f, 8f));
            Box(go, new Vector3(-3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f));
            Box(go, new Vector3(3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f));
            Box(go, new Vector3(3.5f, 0.6f, -8f), new Vector3(0.8f, 1.2f, 2.2f));

            // Interior gravity (moves with the ship; pawns ride along via platform carry)
            var interior = Child(go.transform, "Interior", new Vector3(0, 2f, -0.5f));
            Box(interior, Vector3.zero, new Vector3(7.8f, 4f, 27.5f), true);
            core.Interior = interior.AddComponent<GravityVolume>();

            Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, 0.4f, 11.4f), new Vector3(0, 2.2f, 11.6f), new Vector3(0, 0.2f, 9f),
                new Vector3(0, 1f, 11.4f), new Vector3(1.6f, 2f, 1.6f));
            Dashboard(go.transform, new Vector3(0, 1.5f, 12.6f), 3f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var tb = Visual(go.transform, "TurretBase" + i, baseMesh);
                tb.transform.localPosition = new Vector3(side * 3.5f, 4.5f, 4f);
                var yaw = Child(tb.transform, "Yaw", new Vector3(0, 0.9f, 0));
                var pitch = Child(yaw.transform, "Pitch", Vector3.zero);
                Visual(pitch.transform, "Head", headMesh);
                var seat = Seat(go.transform, "TurretSeat" + i, StationRole.Turret, new Vector3(side * 3f, 0.4f, 3f), Vector3.zero, new Vector3(side * 1.5f, 0.2f, 3f),
                    new Vector3(side * 3f, 1f, 3f), new Vector3(1.6f, 2f, 1.6f));
                // The turret camera lives on the turret head, outside the hull.
                var vp = seat.transform.Find("ViewPoint");
                vp.SetParent(pitch.transform, false);
                vp.localPosition = new Vector3(0, 1.7f, -1.8f);
                vp.localRotation = Quaternion.identity;
                seat.TurretYaw = yaw.transform;
                seat.TurretPitch = pitch.transform;
                seat.Muzzle = Child(pitch.transform, "Muzzle", new Vector3(0, 0, 2f)).transform;
                seat.TurretWeapon = i == 0 ? WeaponKind.Flak : WeaponKind.Laser;
                seat.TurretIndex = i;
            }
            Seat(go.transform, "EngineerSeat", StationRole.Engineer, new Vector3(-2.8f, 0.4f, -4f), new Vector3(-2.6f, 1.7f, -4f), new Vector3(-1.5f, 0.2f, -4f),
                new Vector3(-3.3f, 1f, -4f), new Vector3(1.4f, 2f, 2.4f), Quaternion.Euler(10f, -90f, 0f));

            var fab = Child(go.transform, "Fabricator", new Vector3(3.3f, 1f, -4f));
            Box(fab, Vector3.zero, new Vector3(1.4f, 2f, 2.4f), true);
            fab.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.Fabricator;
            var cargo = Child(go.transform, "CargoTerminal", new Vector3(3.3f, 1f, -8f));
            Box(cargo, Vector3.zero, new Vector3(1.4f, 2f, 2.4f), true);
            cargo.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;
            var outside = Child(go.transform, "HullTerminal", new Vector3(0, 1.5f, -14.5f));
            Box(outside, Vector3.zero, new Vector3(4f, 3f, 1.5f), true);
            outside.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-2.5f, -1.2f, 17f)).transform, Child(go.transform, "MuzzleR", new Vector3(2.5f, -1.2f, 17f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleMissile", new Vector3(0, 5.8f, 4f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(0, 0.3f, 0f)).transform;
            core.FabricatorPoint = fab.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-3.6f, 2.8f, -17.2f), new Vector3(-3.6f, 0.2f, -17.2f), new Vector3(3.6f, 2.8f, -17.2f), new Vector3(3.6f, 0.2f, -17.2f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(2.6f, 2.6f, 2.6f);
            return SavePrefab(go);
        }

        // ---------------------------------------------------------------- enemies

        static GameObject EnemyRoot(EnemyKind k, Team team, float hp)
        {
            var go = NetRoot("Enemy_" + k, true);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = hp;
            var so = new SerializedObject(hc);
            so.FindProperty("team").enumValueIndex = (int)team;
            so.ApplyModifiedPropertiesWithoutUndo();
            var brain = go.AddComponent<EnemyBrain>();
            brain.Kind = k;
            return go;
        }

        static GameObject BuildEnemy(EnemyKind k)
        {
            var mb = new MeshBuilder();
            GameObject go;
            Vector3 muzzle = new Vector3(0, 0, 2f);
            switch (k)
            {
                case EnemyKind.PirateFighter:
                    go = EnemyRoot(k, Team.Pirates, 45);
                    mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 0.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(1.6f, 1f, 4.5f), PirateDark);
                    mb.AddBox(TR(-1.8f, 0, -0.6f, 0, 0, 25), new Vector3(2.4f, 0.2f, 2f), PirateRed);
                    mb.AddBox(TR(1.8f, 0, -0.6f, 0, 0, -25), new Vector3(2.4f, 0.2f, 2f), PirateRed);
                    mb.AddBox(T(0, 0.55f, -0.8f), new Vector3(0.6f, 0.4f, 1.2f), Orange);
                    mb.AddPrism(TR(0, 0, -2f, 90, 0, 0), 0.45f, 0.3f, 0.8f, 6, Dark);
                    Box(go, Vector3.zero, new Vector3(5f, 1.6f, 4.6f));
                    muzzle = new Vector3(0, 0, 3f);
                    break;
                case EnemyKind.DroneSwarm:
                    go = EnemyRoot(k, Team.Pirates, 18);
                    mb.AddRock(Matrix4x4.identity, 0.8f, 7, Pink);
                    mb.AddPrism(TR(0, 0, 0, 90, 0, 0), 1.4f, 1.4f, 0.15f, 8, PirateDark);
                    mb.AddBox(T(0, 0, 0.8f), new Vector3(0.3f, 0.3f, 0.6f), Teal);
                    go.AddComponent<SphereCollider>().radius = 1.3f;
                    muzzle = new Vector3(0, 0, 1.3f);
                    break;
                case EnemyKind.PirateGunship:
                    go = EnemyRoot(k, Team.Pirates, 260);
                    mb.AddBox(T(0, 0, 0), new Vector3(5f, 3f, 14f), PirateDark);
                    mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 8.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(5f, 3f, 3f), PirateDark);
                    mb.AddBox(T(-3.6f, -0.5f, -1f), new Vector3(2.2f, 2f, 9f), PirateRed);
                    mb.AddBox(T(3.6f, -0.5f, -1f), new Vector3(2.2f, 2f, 9f), PirateRed);
                    mb.AddPrism(T(0, 2f, 2f), 1.2f, 1f, 0.8f, 8, Orange);
                    mb.AddBox(T(0, 2.5f, 3.2f), new Vector3(0.4f, 0.4f, 2.4f), Dark);
                    for (int i = 0; i < 3; i++) mb.AddPrism(TR(-2f + i * 2f, 0, -7.6f, 90, 0, 0), 0.8f, 0.6f, 1.4f, 8, Dark);
                    Box(go, new Vector3(0, 0, 0.5f), new Vector3(9.5f, 3.4f, 17f));
                    muzzle = new Vector3(0, 2.5f, 4.6f);
                    break;
                case EnemyKind.Crawler:
                    go = EnemyRoot(k, Team.Aliens, 35);
                    mb.AddRock(Matrix4x4.TRS(new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(1f, 0.6f, 1.4f)), 0.6f, 3, AlienGreen);
                    mb.AddBox(T(0, 0.75f, 0.75f), new Vector3(0.5f, 0.3f, 0.3f), AlienPurple);
                    for (int i = 0; i < 6; i++)
                    {
                        float side = i < 3 ? -1f : 1f;
                        float z = -0.4f + (i % 3) * 0.4f;
                        mb.AddBox(TR(side * 0.6f, 0.3f, z, 0, 0, side * 40f), new Vector3(0.7f, 0.1f, 0.1f), AlienPurple);
                    }
                    Box(go, new Vector3(0, 0.6f, 0), new Vector3(1.4f, 1f, 1.8f));
                    muzzle = new Vector3(0, 0.7f, 0.9f);
                    break;
                case EnemyKind.Spitter:
                    go = EnemyRoot(k, Team.Aliens, 30);
                    mb.AddRock(Matrix4x4.TRS(new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(1f, 1.2f, 1f)), 0.7f, 5, new Color(0.85f, 0.95f, 0.4f));
                    mb.AddPrism(TR(0, 1.5f, 0.6f, 70, 0, 0), 0.3f, 0.18f, 0.6f, 6, AlienPurple);
                    for (int i = 0; i < 4; i++)
                        mb.AddBox(TR(Mathf.Cos(i * 1.57f) * 0.5f, 0.35f, Mathf.Sin(i * 1.57f) * 0.5f, 0, i * 90f, 20), new Vector3(0.12f, 0.8f, 0.12f), AlienGreen);
                    Box(go, new Vector3(0, 1.1f, 0), new Vector3(1.4f, 2.2f, 1.4f));
                    muzzle = new Vector3(0, 1.6f, 0.9f);
                    break;
                case EnemyKind.FlyingCritter:
                    go = EnemyRoot(k, Team.Aliens, 22);
                    mb.AddRock(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.8f, 0.5f, 1.3f)), 0.5f, 9, Pink);
                    mb.AddBox(TR(-1f, 0.1f, 0, 0, 0, 15), new Vector3(1.6f, 0.06f, 0.9f), AlienPurple);
                    mb.AddBox(TR(1f, 0.1f, 0, 0, 0, -15), new Vector3(1.6f, 0.06f, 0.9f), AlienPurple);
                    mb.AddBox(T(0, 0, -0.9f), new Vector3(0.1f, 0.1f, 0.8f), Pink);
                    go.AddComponent<SphereCollider>().radius = 0.9f;
                    go.GetComponent<EnemyBrain>().HoverHeight = 5f;
                    muzzle = new Vector3(0, 0, 0.8f);
                    break;
                default: // NestGuardian
                    go = EnemyRoot(k, Team.Aliens, 220);
                    mb.AddRock(Matrix4x4.TRS(new Vector3(0, 2f, 0), Quaternion.identity, new Vector3(1.3f, 1.1f, 1f)), 1.3f, 11, AlienPurple);
                    mb.AddRock(Matrix4x4.TRS(new Vector3(0, 3.3f, 0.6f), Quaternion.identity, Vector3.one), 0.6f, 12, AlienGreen);
                    mb.AddBox(TR(-1.6f, 1.6f, 0.5f, 20, 0, 20), new Vector3(0.5f, 2f, 0.5f), AlienGreen);
                    mb.AddBox(TR(1.6f, 1.6f, 0.5f, 20, 0, -20), new Vector3(0.5f, 2f, 0.5f), AlienGreen);
                    mb.AddBox(T(-0.6f, 0.5f, 0), new Vector3(0.6f, 1f, 0.6f), AlienPurple);
                    mb.AddBox(T(0.6f, 0.5f, 0), new Vector3(0.6f, 1f, 0.6f), AlienPurple);
                    mb.AddBox(T(0, 3.4f, 1.15f), new Vector3(0.6f, 0.15f, 0.1f), new Color(1f, 0.95f, 0.5f));
                    Box(go, new Vector3(0, 2f, 0), new Vector3(3.4f, 4f, 2.6f));
                    muzzle = new Vector3(0, 2f, 1.6f);
                    break;
            }
            var brain = go.GetComponent<EnemyBrain>();
            var vis = Visual(go.transform, "Visual", SaveMesh(mb, "Enemy_" + k));
            brain.Visual = vis.transform;
            brain.Muzzle = Child(go.transform, "Muzzle", muzzle).transform;
            return SavePrefab(go);
        }

        static GameObject BuildBoss()
        {
            var go = NetRoot("Boss_Warden", true);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = 3000f;
            var so = new SerializedObject(hc);
            so.FindProperty("team").enumValueIndex = (int)Team.Pirates;
            so.ApplyModifiedPropertiesWithoutUndo();
            var boss = go.AddComponent<BossBrain>();

            var mb = new MeshBuilder();
            mb.AddBox(T(0, 0, 0), new Vector3(26f, 14f, 70f), PirateDark);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 45f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(26f, 14f, 20f), PirateDark);
            mb.AddBox(T(0, 10f, -5f), new Vector3(8f, 6f, 50f), PirateRed);
            mb.AddBox(T(-20f, -2f, -10f), new Vector3(14f, 4f, 30f), PirateRed);
            mb.AddBox(T(20f, -2f, -10f), new Vector3(14f, 4f, 30f), PirateRed);
            for (int i = 0; i < 7; i++) mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 13.5f, -25f + i * 8f), Quaternion.identity, Vector3.one), new Vector3(2f, 4f, 5f), Orange);
            for (int i = 0; i < 4; i++) mb.AddPrism(TR(-9f + i * 6f, 0, -37f, 90, 0, 0), 3f, 2.4f, 5f, 10, Dark);
            mb.AddRock(Matrix4x4.TRS(new Vector3(0, -8f, 10f), Quaternion.identity, new Vector3(1.6f, 0.8f, 2.4f)), 6f, 21, AlienPurple);
            Visual(go.transform, "Hull", SaveMesh(mb, "Boss_Warden"));
            Box(go, new Vector3(0, 0, 5f), new Vector3(26f, 14f, 90f));
            Box(go, new Vector3(0, -2f, -10f), new Vector3(54f, 4f, 30f));
            Box(go, new Vector3(0, 10f, -5f), new Vector3(8f, 6f, 50f));

            var podMesh = new MeshBuilder();
            podMesh.AddRock(Matrix4x4.identity, 3.2f, 33, Color.white);
            var podM = SaveMesh(podMesh, "Boss_Pod");
            var podMat = Mat("VW_BossPod", "VOIDWARD/Unlit");
            podMat.SetColor("_BaseColor", new Color(3f, 0.6f, 2.2f));
            Vector3[] podPos = { new Vector3(-21f, 2f, 0f), new Vector3(21f, 2f, 0f), new Vector3(0f, 14f, 18f) };
            var pods = new HealthComponent[3];
            for (int i = 0; i < 3; i++)
            {
                var p = Child(go.transform, "CorePod" + i, podPos[i]);
                Visual(p.transform, "Glow", podM, podMat);
                p.AddComponent<SphereCollider>().radius = 3.6f;
                var ph = p.AddComponent<HealthComponent>();
                ph.MaxHealth = 380f; ph.IsWeakPointPart = true; ph.WeakPointMultiplier = 1.5f;
                var pso = new SerializedObject(ph);
                pso.FindProperty("team").enumValueIndex = (int)Team.Pirates;
                pso.ApplyModifiedPropertiesWithoutUndo();
                pods[i] = ph;
            }
            boss.Pods = pods;
            var muzzles = new List<Transform>();
            Vector3[] mp = { new Vector3(-10f, 0, 50f), new Vector3(10f, 0, 50f), new Vector3(0, 12f, 20f), new Vector3(-24f, -2f, 6f), new Vector3(24f, -2f, 6f), new Vector3(0, -8f, 30f) };
            for (int i = 0; i < mp.Length; i++) muzzles.Add(Child(go.transform, "Muzzle" + i, mp[i]).transform);
            boss.Muzzles = muzzles.ToArray();
            var shield = new MeshBuilder();
            shield.AddRock(Matrix4x4.TRS(new Vector3(0, 2f, 5f), Quaternion.identity, new Vector3(1.1f, 0.6f, 1.6f)), 40f, 5, Color.white);
            var sh = Visual(go.transform, "Shield", SaveMesh(shield, "Boss_Shield"), MatGlass);
            sh.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boss.ShieldVisual = sh.transform;
            return SavePrefab(go);
        }

        // ---------------------------------------------------------------- props

        static GameObject BuildPickup()
        {
            var go = NetRoot("WorldPickup", false);
            var p = go.AddComponent<WorldPickup>();
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true; sc.radius = 0.7f; sc.center = new Vector3(0, 0.5f, 0);
            var mb = new MeshBuilder();
            mb.AddPrism(T(0, 0.15f, 0), 0.25f, 0.05f, 0.3f, 6, Color.white);
            mb.AddPrism(TR(0, -0.15f, 0, 180, 0, 0), 0.25f, 0.05f, 0.3f, 6, Color.white);
            var vis = Child(go.transform, "Visual", new Vector3(0, 0.6f, 0));
            Visual(vis.transform, "Mesh", SaveMesh(mb, "Pickup"));
            Set(p, "visual", vis.transform);
            return SavePrefab(go);
        }

        static GameObject BuildCrate()
        {
            var go = NetRoot("LootCrate", false);
            var c = go.AddComponent<LootCrate>();
            var mb = new MeshBuilder();
            mb.AddBox(T(0, 0.35f, 0), new Vector3(1.4f, 0.7f, 0.9f), Teal);
            mb.AddBox(T(0, 0.35f, 0), new Vector3(1.46f, 0.12f, 0.96f), Orange);
            mb.AddBox(T(-0.5f, 0.35f, 0), new Vector3(0.1f, 0.74f, 0.94f), White);
            mb.AddBox(T(0.5f, 0.35f, 0), new Vector3(0.1f, 0.74f, 0.94f), White);
            Visual(go.transform, "Body", SaveMesh(mb, "Crate_Body"));
            var lid = Child(go.transform, "LidPivot", new Vector3(0, 0.7f, -0.45f));
            var lm = new MeshBuilder();
            lm.AddBox(T(0, 0.08f, 0.45f), new Vector3(1.44f, 0.16f, 0.94f), White);
            Visual(lid.transform, "Lid", SaveMesh(lm, "Crate_Lid"));
            Box(go, new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 1f));
            Set(c, "lid", lid.transform);
            return SavePrefab(go);
        }

        static GameObject BuildNode()
        {
            var go = NetRoot("ResourceNode", false);
            var n = go.AddComponent<ResourceNode>();
            var ore = new MeshBuilder();
            ore.AddRock(Matrix4x4.TRS(new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(1.2f, 0.9f, 1f)), 1f, 41, new Color(0.62f, 0.5f, 0.5f));
            ore.AddRock(Matrix4x4.TRS(new Vector3(0.7f, 0.4f, 0.4f), Quaternion.identity, Vector3.one), 0.35f, 42, new Color(1f, 0.6f, 0.4f));
            ore.AddRock(Matrix4x4.TRS(new Vector3(-0.6f, 0.7f, -0.3f), Quaternion.identity, Vector3.one), 0.3f, 43, new Color(1f, 0.6f, 0.4f));
            var crystal = new MeshBuilder();
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                crystal.AddPrism(TR(Mathf.Cos(a * Mathf.Deg2Rad) * 0.35f, 0.6f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.35f, Mathf.Sin(a) * 18f, a, 18f), 0.22f, 0.02f, 1.4f + (i % 3) * 0.4f, 5,
                    i % 2 == 0 ? new Color(0.55f, 0.9f, 1f) : new Color(0.85f, 0.6f, 1f));
            }
            crystal.AddPrism(T(0, 0.9f, 0), 0.3f, 0.02f, 1.9f, 6, new Color(0.7f, 0.95f, 1f));
            var scrap = new MeshBuilder();
            scrap.AddBox(TR(0, 0.3f, 0, 0, 20, 10), new Vector3(1.4f, 0.5f, 0.9f), Mid);
            scrap.AddBox(TR(0.4f, 0.7f, 0.1f, 15, -10, 30), new Vector3(0.8f, 0.3f, 0.6f), Orange);
            scrap.AddBox(TR(-0.4f, 0.6f, -0.2f, -20, 40, 0), new Vector3(0.3f, 0.9f, 0.3f), Dark);
            var fuel = new MeshBuilder();
            fuel.AddPrism(T(0, 0.6f, 0), 0.45f, 0.45f, 1.2f, 8, new Color(1f, 0.85f, 0.35f));
            fuel.AddPrism(T(0, 1.3f, 0), 0.2f, 0.15f, 0.2f, 8, Dark);
            fuel.AddBox(T(0, 0.6f, 0.46f), new Vector3(0.5f, 0.3f, 0.02f), Dark);
            var variants = new List<Transform>();
            foreach (var (name, mb) in new[] { ("Ore", ore), ("Crystal", crystal), ("Scrap", scrap), ("Fuel", fuel) })
            {
                var v = Child(go.transform, name, Vector3.zero);
                Visual(v.transform, "Mesh", SaveMesh(mb, "Node_" + name));
                variants.Add(v.transform);
            }
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 1.1f; sc.center = new Vector3(0, 0.7f, 0);
            SetArray(n, "variants", variants.ToArray());
            return SavePrefab(go);
        }

        static GameObject BuildNest()
        {
            var go = NetRoot("AlienNest", false);
            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = 160f;
            var so = new SerializedObject(hc);
            so.FindProperty("team").enumValueIndex = (int)Team.Aliens;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<AlienNest>();
            var mb = new MeshBuilder();
            mb.AddRock(Matrix4x4.TRS(new Vector3(0, 0.8f, 0), Quaternion.identity, new Vector3(1.6f, 0.9f, 1.6f)), 2f, 51, AlienPurple);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                mb.AddRock(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 2.6f, 0.5f, Mathf.Sin(a) * 2.6f), Quaternion.identity, Vector3.one), 0.6f, 52 + i, new Color(0.7f, 1f, 0.45f));
            }
            mb.AddPrism(T(0, 2.4f, 0), 0.6f, 0.1f, 1.4f, 6, Pink);
            Visual(go.transform, "Mound", SaveMesh(mb, "Nest"));
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 2.6f; sc.center = new Vector3(0, 1f, 0);
            return SavePrefab(go);
        }

        static GameObject BuildRuin()
        {
            var go = NetRoot("Ruin", false);
            var mb = new MeshBuilder();
            Color stone = new Color(0.8f, 0.76f, 0.86f), glow = new Color(0.5f, 1f, 0.95f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float h = 2.5f + (i % 3) * 1.4f;
                mb.AddBox(TR(Mathf.Cos(a) * 6f, h * 0.5f, Mathf.Sin(a) * 6f, 0, i * 60f, (i % 2) * 6f), new Vector3(1f, h, 1f), stone);
                Box(go, new Vector3(Mathf.Cos(a) * 6f, h * 0.5f, Mathf.Sin(a) * 6f), new Vector3(1f, h, 1f));
            }
            mb.AddBox(T(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f), stone * 0.85f);
            mb.AddBox(TR(3f, 4.2f, 3f, 0, 45, 0), new Vector3(7f, 0.6f, 1f), stone);
            mb.AddPrism(T(0, 1.2f, 0), 0.6f, 0.2f, 2f, 5, glow);
            Box(go, new Vector3(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f));
            Visual(go.transform, "Mesh", SaveMesh(mb, "Ruin"));
            return SavePrefab(go);
        }

        static GameObject BuildCrashed()
        {
            var go = NetRoot("CrashedShip", false);
            var mb = new MeshBuilder();
            mb.AddBox(TR(0, 0.8f, 0, 8, 15, 12), new Vector3(3f, 2f, 9f), Mid);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(1.2f, 0.4f, 5f), Quaternion.Euler(-10, 195, 20), Vector3.one), new Vector3(3f, 2f, 3f), Mid);
            mb.AddBox(TR(-3.5f, 0.3f, -1f, 0, 30, -25), new Vector3(4f, 0.3f, 2.5f), Orange);
            mb.AddBox(TR(4f, 0.2f, 2f, 10, -20, 40), new Vector3(3f, 0.3f, 2f), Orange);
            mb.AddPrism(TR(-1f, 0.6f, -4.5f, 80, 15, 0), 0.8f, 0.6f, 1.6f, 8, Dark);
            Visual(go.transform, "Mesh", SaveMesh(mb, "CrashedShip"));
            Box(go, new Vector3(0, 0.8f, 0), new Vector3(3.4f, 2.2f, 9f));
            return SavePrefab(go);
        }

        static GameObject BuildWreck()
        {
            var go = NetRoot("Wreck", false);
            var mb = new MeshBuilder();
            mb.AddBox(TR(0, 0, 0, 0, 0, 12), new Vector3(8f, 6f, 22f), Mid);
            mb.AddBox(TR(-6f, 2f, -4f, 20, 10, 40), new Vector3(6f, 0.8f, 10f), White);
            mb.AddBox(TR(7f, -3f, 6f, -30, -20, 10), new Vector3(5f, 0.6f, 8f), Orange);
            mb.AddPrism(TR(0, 0, -13f, 90, 0, 0), 2.6f, 2f, 4f, 10, Dark);
            mb.AddBox(TR(2f, 4f, 9f, 0, 30, 60), new Vector3(1f, 6f, 1f), Dark);
            for (int i = 0; i < 6; i++) mb.AddRock(Matrix4x4.TRS(new Vector3(-8f + i * 3.2f, -6f + (i % 2) * 12f, -8f + i * 3f), Quaternion.Euler(i * 30, i * 50, 0), Vector3.one), 1.2f, 70 + i, Mid);
            Visual(go.transform, "Mesh", SaveMesh(mb, "Wreck"));
            Box(go, Vector3.zero, new Vector3(8f, 6f, 22f));
            return SavePrefab(go);
        }

        static GameObject BuildStation()
        {
            var go = NetRoot("TraderStation", false);
            var ts = go.AddComponent<TraderStation>();
            var mb = new MeshBuilder();
            mb.AddPrism(T(0, 0, 0), 10f, 10f, 26f, 10, White);
            mb.AddPrism(T(0, 14f, 0), 7f, 2f, 4f, 10, Orange);
            mb.AddPrism(T(0, -14f, 0), 2f, 7f, 4f, 10, Orange);
            mb.AddBox(T(0, 0, 18f), new Vector3(3f, 3f, 16f), Mid);                      // pad arm
            mb.AddBox(T(0, -1.5f, 34f), new Vector3(30f, 1f, 30f), Teal);                // landing pad
            mb.AddBox(T(0, -0.99f, 34f), new Vector3(26f, 0.02f, 2f), White);
            mb.AddBox(T(0, -0.99f, 34f), new Vector3(2f, 0.02f, 26f), White);
            mb.AddBox(T(0, 0.5f, 22.5f), new Vector3(3f, 3f, 1.2f), Orange);              // trader console
            for (int i = 0; i < 4; i++) mb.AddPrism(T(-14f + (i % 2) * 28f, 1f, 20f + (i / 2) * 28f), 0.3f, 0.3f, 4f, 6, Pink);
            Visual(go.transform, "Hub", SaveMesh(mb, "Station_Hub"));
            var ring = new MeshBuilder();
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                ring.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 24f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(9.6f, 2.4f, 2.4f), i % 2 == 0 ? White : Pink);
                ring.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 17f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(0.6f, 0.6f, 14f), Mid);
            }
            var spinner = Child(go.transform, "Ring", new Vector3(0, 6f, 0));
            Visual(spinner.transform, "RingMesh", SaveMesh(ring, "Station_Ring"));
            Set(ts, "spinner", spinner.transform);

            var hub = go.AddComponent<CapsuleCollider>();
            hub.radius = 10f; hub.height = 34f;
            Box(go, new Vector3(0, -1.5f, 34f), new Vector3(30f, 1f, 30f));
            Box(go, new Vector3(0, 0, 18f), new Vector3(3f, 3f, 16f));
            Box(go, new Vector3(0, 0.5f, 22.5f), new Vector3(3f, 3f, 1.2f));
            var pad = Child(go.transform, "PadGravity", new Vector3(0, 6f, 34f));
            Box(pad, Vector3.zero, new Vector3(30f, 14f, 30f), true);
            pad.AddComponent<GravityVolume>();
            var console = Child(go.transform, "TraderConsole", new Vector3(0, 1.5f, 22.5f));
            Box(console, Vector3.zero, new Vector3(4f, 4f, 3f), true);
            return SavePrefab(go);
        }

        static GameObject BuildGate()
        {
            var go = NetRoot("JumpGate", false);
            var gate = go.AddComponent<JumpGate>();
            var ring = new MeshBuilder();
            const int seg = 28;
            const float R = 55f;
            for (int i = 0; i < seg; i++)
            {
                float a = i * 360f / seg;
                var rot = Quaternion.Euler(0, 0, a);
                ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R, 0), rot, Vector3.one), new Vector3(13f, 6f, 8f), i % 4 == 0 ? Orange : White);
                if (i % 4 == 0) ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R + 5f, 0), rot, Vector3.one), new Vector3(3f, 5f, 3f), Teal);
            }
            var ringGo = Child(go.transform, "Ring", Vector3.zero);
            Visual(ringGo.transform, "RingMesh", SaveMesh(ring, "Gate_Ring"));
            for (int i = 0; i < seg; i += 2)
            {
                float a = i * 360f / seg;
                var rot = Quaternion.Euler(0, 0, a);
                var c = ringGo.AddComponent<BoxCollider>();
                c.center = rot * new Vector3(0, R, 0);
                c.size = new Vector3(26f, 6f, 8f);
            }
            var disc = new MeshBuilder();
            disc.AddPrism(TR(0, 0, 0, 90, 0, 0), R - 4f, R - 4f, 0.4f, 28, Color.white);
            var core = Visual(go.transform, "Core", SaveMesh(disc, "Gate_Core"), MatGlass);
            core.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Socket platform for manual insertion on foot
            var plat = new MeshBuilder();
            plat.AddBox(T(0, -R - 10f, 0), new Vector3(16f, 1f, 16f), Teal);
            plat.AddBox(T(0, -R - 8.5f, 0), new Vector3(2f, 2f, 2f), Orange);
            Visual(go.transform, "Socket", SaveMesh(plat, "Gate_Socket"));
            Box(go, new Vector3(0, -R - 10f, 0), new Vector3(16f, 1f, 16f));
            var sock = Child(go.transform, "SocketTrigger", new Vector3(0, -R - 8.5f, 0));
            Box(sock, Vector3.zero, new Vector3(4f, 4f, 4f), true);
            var grav = Child(go.transform, "SocketGravity", new Vector3(0, -R - 5f, 0));
            Box(grav, Vector3.zero, new Vector3(16f, 9f, 16f), true);
            grav.AddComponent<GravityVolume>();
            Set(gate, "ring", ringGo.transform);
            Set(gate, "core", core.GetComponent<MeshRenderer>());
            return SavePrefab(go);
        }

        static GameObject BuildDirector()
        {
            var go = NetRoot("GameDirector", false);
            go.AddComponent<RunManager>();
            go.AddComponent<CombatNet>();
            return SavePrefab(go);
        }

        static void BuildNetworkPrefabList()
        {
            const string path = "Assets/DefaultNetworkPrefabs.asset";
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }
            var old = new List<NetworkPrefab>(list.PrefabList);
            foreach (var p in old) list.Remove(p);
            foreach (var p in networkPrefabs)
            {
                var no = p.GetComponent<NetworkObject>();
                if (no.PrefabIdHash == 0) Debug.LogError("[Voidward] Prefab without GlobalObjectIdHash: " + p.name);
                list.Add(new NetworkPrefab { Prefab = p });
            }
            EditorUtility.SetDirty(list);
        }
    }
}
