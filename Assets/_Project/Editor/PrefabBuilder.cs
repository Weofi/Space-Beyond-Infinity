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
        // Stylized low-poly palette (matches the UI: white/blue hard surfaces, navy, gold accents, cyan glow)
        static readonly Color White = new Color(0.94f, 0.95f, 0.97f);
        static readonly Color Blue = new Color(0.1f, 0.42f, 0.95f);
        static readonly Color Navy = new Color(0.12f, 0.17f, 0.32f);
        static readonly Color Gold = new Color(1f, 0.72f, 0.1f);
        static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);
        static readonly Color Dark = new Color(0.15f, 0.16f, 0.2f);
        static readonly Color Mid = new Color(0.6f, 0.63f, 0.7f);
        static readonly Color Red = new Color(0.95f, 0.2f, 0.18f);
        static readonly Color Green = new Color(0.25f, 0.95f, 0.4f);
        static readonly Color Pink = new Color(1f, 0.42f, 0.6f);
        static readonly Color PirateRed = new Color(0.88f, 0.22f, 0.22f);
        static readonly Color PirateDark = new Color(0.2f, 0.2f, 0.25f);
        static readonly Color AlienGreen = new Color(0.45f, 0.88f, 0.3f);
        static readonly Color AlienPurple = new Color(0.55f, 0.3f, 0.9f);

        // Hero hard-surface palette (DESIGN.md): chalk white paint, dark slate chassis, gunmetal machinery.
        static readonly Color Chalk = new Color(0.89f, 0.91f, 0.94f);
        static readonly Color Slate = new Color(0.16f, 0.2f, 0.27f);
        static readonly Color Gunmetal = new Color(0.46f, 0.49f, 0.55f);
        static readonly Color Rubber = new Color(0.1f, 0.11f, 0.13f);

        const float Matte = 0.05f, Glossy = 0.65f, Mirror = 1f;
        const float Paint = 0.55f, Satin = 0.4f;

        /// <summary>Builder for hero hard surfaces (VW_Hull): weighted normals, panel detail, chamfered boxes.</summary>
        static MeshBuilder Hero(float bevel = 0.18f) => new MeshBuilder { Bevel = bevel, SmoothAngle = 50f, Panel = 1f, Gloss = Paint };

        /// <summary>Runs `add` with the given surface, then restores the previous one (panel detail optional).</summary>
        static void With(MeshBuilder mb, float gloss, float metal, float panel, System.Action add, float glow = 0f)
        {
            float g = mb.Gloss, gl = mb.Glow, m = mb.Metal, p = mb.Panel;
            mb.Gloss = gloss; mb.Glow = glow; mb.Metal = metal; mb.Panel = panel;
            add();
            mb.Gloss = g; mb.Glow = gl; mb.Metal = m; mb.Panel = p;
        }

        /// <summary>Chain of tubes through points (frame bars, cables, hand rails).</summary>
        static void Polyline(MeshBuilder mb, IList<Vector3> pts, float r, int sides, Color c)
        {
            for (int i = 0; i + 1 < pts.Count; i++) mb.AddTube(pts[i], pts[i + 1], r, r, sides, c);
            for (int i = 1; i + 1 < pts.Count; i++) mb.AddSphere(Matrix4x4.TRS(pts[i], Quaternion.identity, Vector3.one), r, 1, c);
        }

        static readonly List<GameObject> networkPrefabs = new List<GameObject>();

        /// <summary>MeshBuilder with chamfered box edges for the chunky toy look.</summary>
        static MeshBuilder MB(float bevel = 0.2f) => new MeshBuilder { Bevel = bevel };

        static void Glow(MeshBuilder mb, System.Action add, float glow = 1f) => With(mb, MeshBuilder.DefaultGloss, 0f, 0f, add, glow);

        static void Shiny(MeshBuilder mb, float gloss, System.Action add) => With(mb, gloss, mb.Metal, mb.Panel, add);

        /// <summary>Engine nozzle pointing -Z: body cone, glowing inner rim and dark core. rear = centre of the exhaust face.</summary>
        static void Nozzle(MeshBuilder mb, Vector3 rear, float r, float len, Color body, Color glow)
        {
            mb.AddPrism(TR(rear.x, rear.y, rear.z + len * 0.5f, 90, 0, 0), r, r * 0.84f, len, 8, body);
            Glow(mb, () => mb.AddPrism(TR(rear.x, rear.y, rear.z, 90, 0, 0), r * 0.88f, r * 0.88f, len * 0.05f, 8, glow), 0.8f);
            Shiny(mb, Matte, () => mb.AddPrism(TR(rear.x, rear.y, rear.z - len * 0.05f, 90, 0, 0), r * 0.74f, r * 0.74f, len * 0.06f, 8, Dark));
        }

        /// <summary>Unshadowed local light. URP point/spot falloff is inverse-square, so intensity scales with range squared.</summary>
        static Light AddLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range, float spotAngle = 0f, Quaternion? rot = null)
        {
            var go = Child(parent, name, pos, rot);
            var l = go.AddComponent<Light>();
            l.type = spotAngle > 0f ? LightType.Spot : LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            if (spotAngle > 0f) { l.spotAngle = spotAngle; l.innerSpotAngle = spotAngle * 0.45f; }
            l.shadows = LightShadows.None;
            return l;
        }

        static readonly Color LampWhite = new Color(1f, 0.95f, 0.86f);

        /// <summary>Ship headlights (driven by ShipLights) plus an always-on cyan ground beacon so parked ships are easy to find.</summary>
        static void ShipLighting(GameObject ship, Vector3 beacon, float beaconRange, params Vector3[] headlights)
        {
            var lights = new Light[headlights.Length];
            for (int i = 0; i < headlights.Length; i++)
                lights[i] = AddLight(ship.transform, "Headlight", headlights[i], LampWhite, 1100f, 170f, 48f, Quaternion.Euler(4f, 0f, 0f));
            ship.AddComponent<ShipLights>().Headlights = lights;
            AddLight(ship.transform, "Beacon", beacon, Cyan, beaconRange * 0.5f, beaconRange);
        }

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

        static Mesh AstronautMesh() => AstronautMesh(Blue, "Astronaut");

        /// <summary>Lofted limb segment from a to b (z runs along the bone), elliptical cross-sections.</summary>
        static void Limb(MeshBuilder mb, Vector3 a, Vector3 b, float rA, float rB, Color c, float squash = 0.92f)
        {
            Vector3 d = b - a;
            var m = Matrix4x4.TRS(a, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up), Vector3.one);
            float len = d.magnitude;
            mb.AddLoft(m, new[]
            {
                new MeshBuilder.Section(0f, rA * 0.82f, rA * 0.82f * squash, 2.2f),
                new MeshBuilder.Section(len * 0.15f, rA, rA * squash, 2.3f),
                new MeshBuilder.Section(len * 0.85f, rB, rB * squash, 2.3f),
                new MeshBuilder.Section(len, rB * 0.82f, rB * 0.82f * squash, 2.2f),
            }, 18, c, smoothing: 2);
        }

        /// <summary>Rubber bellows joint: stacked rings around an axis (knees, elbows, neck).</summary>
        static void Bellows(MeshBuilder mb, Vector3 center, Vector3 axis, float r, float len, int ribs, Color c)
        {
            var m = Matrix4x4.TRS(center, Quaternion.FromToRotation(Vector3.up, axis.normalized), Vector3.one);
            var prof = new List<Vector2>();
            for (int i = 0; i <= ribs * 2; i++)
                prof.Add(new Vector2(r * (i % 2 == 0 ? 0.86f : 1f), -len * 0.5f + len * i / (ribs * 2f)));
            mb.AddLathe(m, prof, 16, c);
        }

        /// <summary>
        /// Astronaut: collectible-figure proportions (big helmet, broad torso, stout limbs) but fully modelled -
        /// fabric suit with hard white armour shells, rubber bellows joints, gunmetal rings, mirror-gold visor,
        /// life-support backpack with O2 tanks. Feet at y = 0, facing +Z, eye height ~1.62.
        /// </summary>
        static Mesh AstronautMesh(Color suit, string meshName)
        {
            var mb = Hero(0.25f);
            const float Fabric = 0.22f, Shell = 0.6f;
            mb.Panel = 0f; mb.Gloss = Fabric;
            Color seam = Color.Lerp(suit, Color.black, 0.35f);

            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.15f;
                // boot: hard shell over a rubber sole, gunmetal ankle ring
                With(mb, Shell, 0f, 0.35f, () => mb.AddLoft(T(x, 0.1f, 0f), new[]
                {
                    new MeshBuilder.Section(-0.14f, 0.1f, 0.08f, 3f), new MeshBuilder.Section(-0.08f, 0.12f, 0.1f, 3f),
                    new MeshBuilder.Section(0.1f, 0.125f, 0.085f, 3f, -0.01f), new MeshBuilder.Section(0.2f, 0.11f, 0.06f, 3f, -0.03f), new MeshBuilder.Section(0.25f, 0.07f, 0.04f, 3f, -0.04f),
                }, 18, Chalk, smoothing: 2));
                With(mb, 0.15f, 0f, 0f, () => { mb.Bevel = 0.3f; mb.AddBox(T(x, 0.025f, 0.05f), new Vector3(0.25f, 0.05f, 0.42f), Rubber); mb.Bevel = 0.25f; });
                With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(T(x, 0.2f, -0.01f), new[] { new Vector2(0.1f, -0.03f), new Vector2(0.115f, -0.015f), new Vector2(0.115f, 0.015f), new Vector2(0.1f, 0.03f) }, 18, Gunmetal));
                // shin, knee bellows + hard knee cap, thigh
                Limb(mb, new Vector3(x, 0.2f, -0.01f), new Vector3(x, 0.46f, 0f), 0.1f, 0.11f, suit);
                Bellows(mb, new Vector3(x, 0.5f, 0f), Vector3.up, 0.105f, 0.09f, 3, Rubber);
                With(mb, Shell, 0f, 0.35f, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(x, 0.51f, 0.07f), Quaternion.identity, new Vector3(1f, 1f, 0.6f)), 0.1f, 2, Chalk));
                Limb(mb, new Vector3(x, 0.54f, 0f), new Vector3(x * 1.05f, 0.82f, 0f), 0.12f, 0.14f, suit);
                // thigh pocket
                With(mb, Fabric, 0f, 0f, () => mb.AddBox(T(s * 0.27f, 0.68f, 0.02f), new Vector3(0.05f, 0.12f, 0.13f), seam));

                // arm: pauldron, upper arm, elbow bellows, forearm, white cuff, gunmetal wrist ring, glove with thumb
                Vector3 sh = new Vector3(s * 0.4f, 1.27f, 0f), el = new Vector3(s * 0.45f, 1.04f, 0.02f), wr = new Vector3(s * 0.47f, 0.86f, 0.05f);
                With(mb, Shell, 0f, 0.4f, () =>
                {
                    mb.AddLathe(Matrix4x4.TRS(sh + new Vector3(s * 0.02f, 0.03f, 0f), Quaternion.Euler(0, 0, s * -18f), Vector3.one),
                        new[] { new Vector2(0.15f, -0.08f), new Vector2(0.165f, -0.03f), new Vector2(0.15f, 0.04f), new Vector2(0.1f, 0.09f), new Vector2(0f, 0.11f) }, 20, Chalk);
                });
                Limb(mb, sh, el + (sh - el).normalized * 0.04f, 0.1f, 0.09f, suit);
                Bellows(mb, el, sh - wr, 0.088f, 0.08f, 2, Rubber);
                Limb(mb, el + (wr - el).normalized * 0.04f, wr, 0.09f, 0.085f, suit);
                With(mb, Shell, 0f, 0.3f, () => mb.AddLathe(Matrix4x4.TRS(wr + (el - wr).normalized * 0.05f, Quaternion.FromToRotation(Vector3.up, el - wr), Vector3.one),
                    new[] { new Vector2(0.088f, -0.04f), new Vector2(0.105f, -0.03f), new Vector2(0.105f, 0.04f), new Vector2(0.088f, 0.05f) }, 16, Chalk));
                With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(Matrix4x4.TRS(wr, Quaternion.FromToRotation(Vector3.up, el - wr), Vector3.one),
                    new[] { new Vector2(0.075f, -0.015f), new Vector2(0.09f, 0f), new Vector2(0.075f, 0.015f) }, 16, Gunmetal));
                With(mb, 0.18f, 0f, 0f, () =>
                {
                    mb.AddSphere(Matrix4x4.TRS(wr + new Vector3(0, -0.085f, 0.01f), Quaternion.identity, new Vector3(0.75f, 1f, 0.95f)), 0.085f, 2, Rubber);
                    mb.AddSphere(Matrix4x4.TRS(wr + new Vector3(s * -0.045f, -0.06f, 0.06f), Quaternion.Euler(30, 0, 0), new Vector3(0.5f, 0.9f, 0.5f)), 0.05f, 1, Rubber);
                });
            }

            // pelvis + belt with buckle and pouches
            mb.AddLoft(TR(0, 0.84f, 0, 0, 90, 0), new[]
            {
                new MeshBuilder.Section(-0.27f, 0.13f, 0.11f, 2.5f), new MeshBuilder.Section(-0.2f, 0.19f, 0.14f, 2.8f),
                new MeshBuilder.Section(0.2f, 0.19f, 0.14f, 2.8f), new MeshBuilder.Section(0.27f, 0.13f, 0.11f, 2.5f),
            }, 16, suit, smoothing: 2);
            With(mb, 0.45f, 0.8f, 0f, () => mb.AddLathe(Matrix4x4.TRS(new Vector3(0, 0.9f, 0f), Quaternion.identity, new Vector3(1.32f, 1f, 1f)),
                new[] { new Vector2(0.19f, -0.04f), new Vector2(0.205f, -0.03f), new Vector2(0.205f, 0.03f), new Vector2(0.19f, 0.04f) }, 24, Gunmetal));
            With(mb, 0.7f, 0.9f, 0f, () => mb.AddBox(T(0, 0.9f, 0.21f), new Vector3(0.14f, 0.09f, 0.04f), Gold));
            With(mb, Fabric, 0f, 0f, () => { mb.AddBox(T(-0.2f, 0.87f, 0.15f), new Vector3(0.1f, 0.1f, 0.07f), seam); mb.AddBox(T(0.2f, 0.87f, 0.15f), new Vector3(0.1f, 0.1f, 0.07f), seam); });

            // torso (loft z = up), hard chest plate with status lights, collar ring
            mb.AddLoft(TR(0, 0f, 0, -90, 0, 0), new[]
            {
                new MeshBuilder.Section(0.9f, 0.25f, 0.18f, 2.6f), new MeshBuilder.Section(1.02f, 0.28f, 0.2f, 2.8f),
                new MeshBuilder.Section(1.2f, 0.33f, 0.22f, 3.2f), new MeshBuilder.Section(1.33f, 0.31f, 0.2f, 3f), new MeshBuilder.Section(1.41f, 0.2f, 0.16f, 2.6f),
            }, 24, suit, smoothing: 2);
            With(mb, Shell, 0f, 0.6f, () =>
            {
                mb.AddSlab(TR(0, 1.18f, 0.205f, -90, 0, 0), new[] { new Vector2(-0.22f, -0.13f), new Vector2(0.22f, -0.13f), new Vector2(0.25f, 0.1f), new Vector2(0.14f, 0.17f), new Vector2(-0.14f, 0.17f), new Vector2(-0.25f, 0.1f) },
                    0.06f, 0.02f, Chalk);
                mb.AddSlab(TR(0, 1.0f, 0.2f, -98, 0, 0), new[] { new Vector2(-0.17f, -0.06f), new Vector2(0.17f, -0.06f), new Vector2(0.15f, 0.06f), new Vector2(-0.15f, 0.06f) }, 0.05f, 0.015f, Chalk);
            });
            Glow(mb, () => mb.AddBox(T(0.08f, 1.24f, 0.24f), new Vector3(0.09f, 0.03f, 0.012f), Cyan), 1.2f);
            Glow(mb, () => mb.AddBox(T(-0.1f, 1.24f, 0.24f), new Vector3(0.03f, 0.03f, 0.012f), Green), 1.4f);
            With(mb, 0.6f, 1f, 0f, () => mb.AddBox(T(-0.1f, 1.13f, 0.24f), new Vector3(0.08f, 0.06f, 0.015f), Gunmetal));
            With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(T(0, 1.42f, 0f), new[] { new Vector2(0.2f, -0.03f), new Vector2(0.225f, -0.01f), new Vector2(0.225f, 0.03f), new Vector2(0.2f, 0.045f) }, 24, Gunmetal));

            // life-support backpack: rounded shell, twin O2 tanks, vents, status lights, hose to the chest
            With(mb, Shell, 0f, 0.7f, () => mb.AddLoft(TR(0, 1.15f, -0.33f, -90, 0, 0), new[]
            {
                new MeshBuilder.Section(-0.3f, 0.24f, 0.11f, 4f), new MeshBuilder.Section(-0.26f, 0.27f, 0.13f, 4f),
                new MeshBuilder.Section(0.24f, 0.27f, 0.13f, 4f), new MeshBuilder.Section(0.29f, 0.22f, 0.1f, 4f),
            }, 24, Chalk, smoothing: 2));
            for (int s = -1; s <= 1; s += 2)
            {
                int tank = mb.VertexCount;
                With(mb, 0.65f, 0.2f, 0.2f, () => mb.AddLathe(T(s * 0.14f, 1.13f, -0.47f), new[]
                {
                    new Vector2(0f, -0.27f), new Vector2(0.05f, -0.26f), new Vector2(0.08f, -0.22f), new Vector2(0.085f, -0.17f),
                    new Vector2(0.085f, 0.17f), new Vector2(0.08f, 0.22f), new Vector2(0.05f, 0.26f), new Vector2(0f, 0.27f),
                }, 18, Gold));
                mb.Paint(tank, p => Mathf.Abs(p.y - 1.13f) < 0.13f, suit, 0.4f, 0f);
                With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(T(s * 0.14f, 1.13f, -0.47f), new[] { new Vector2(0.083f, 0.14f), new Vector2(0.092f, 0.15f), new Vector2(0.092f, 0.17f), new Vector2(0.083f, 0.18f) }, 18, Gunmetal));
                Glow(mb, () => mb.AddBox(T(s * 0.29f, 1.02f, -0.36f), new Vector3(0.012f, 0.05f, 0.05f), s < 0 ? Red : Green), 1.4f);
            }
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (int v = 0; v < 3; v++) mb.AddBox(T(0, 1.34f - v * 0.04f, -0.46f), new Vector3(0.12f, 0.015f, 0.04f), Rubber);
            });
            Glow(mb, () => mb.AddPrism(TR(0, 1.44f, -0.33f, 0, 0, 90), 0.035f, 0.035f, 0.42f, 10, Cyan), 0.9f);
            With(mb, 0.55f, 0f, 0f, () => Polyline(mb, new[] { new Vector3(0.22f, 1.3f, -0.2f), new Vector3(0.3f, 1.12f, -0.05f), new Vector3(0.27f, 1.0f, 0.12f), new Vector3(0.16f, 1.02f, 0.23f) }, 0.028f, 8, Gold));

            // helmet: white shell, gunmetal visor frame, mirror-gold visor, side lamps + antenna
            With(mb, Shell, 0f, 0.25f, () => mb.AddSphere(T(0, 1.62f, -0.01f), 0.34f, 3, Chalk));
            // Visor bulges out of the shell; the slightly larger gunmetal frame shows as a rim around it.
            With(mb, 0.55f, 1f, 0f, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 1.6f, 0.17f), Quaternion.identity, new Vector3(1f, 0.8f, 0.62f)), 0.3f, 3, Slate));
            With(mb, 1f, 1f, 0f, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 1.6f, 0.2f), Quaternion.identity, new Vector3(0.95f, 0.74f, 0.6f)), 0.29f, 3, Gold));
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(Matrix4x4.TRS(new Vector3(s * 0.33f, 1.62f, -0.02f), Quaternion.Euler(0, 0, s * -90), Vector3.one),
                    new[] { new Vector2(0.09f, -0.03f), new Vector2(0.095f, 0.0f), new Vector2(0.08f, 0.035f), new Vector2(0f, 0.04f) }, 18, Gunmetal));
                Glow(mb, () => mb.AddLathe(Matrix4x4.TRS(new Vector3(s * 0.37f, 1.62f, -0.02f), Quaternion.Euler(0, 0, s * -90), Vector3.one),
                    new[] { new Vector2(0.045f, 0f), new Vector2(0.03f, 0.008f), new Vector2(0f, 0.01f) }, 14, Cyan), 1f);
            }
            With(mb, 0.4f, 1f, 0f, () => mb.AddTube(new Vector3(0.18f, 1.88f, -0.12f), new Vector3(0.22f, 2.12f, -0.18f), 0.012f, 0.007f, 6, Gunmetal));
            Glow(mb, () => mb.AddSphere(T(0.22f, 2.13f, -0.18f), 0.018f, 1, Red), 1.5f);
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 1.93f, 0.12f), Quaternion.identity, new Vector3(1f, 0.5f, 0.6f)), 0.05f, 2, LampWhite), 1.2f);
            return SaveMesh(mb, meshName);
        }

        static GameObject BuildPawn()
        {
            var go = NetRoot("PlayerPawn", true, NetworkTransform.AuthorityModes.Owner);
            go.GetComponent<NetworkObject>().DontDestroyWithOwner = false;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 80f; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.AddComponent<TerrainAnchor>().Radius = 90f;
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
            Visual(body.transform, "Astronaut", AstronautMesh(), MatHull);

            var cam = Child(go.transform, "CameraPoint", new Vector3(0, 1.62f, 0));
            var vcam = cam.AddComponent<CinemachineCamera>();
            var lens = vcam.Lens;
            lens.FieldOfView = 75f; lens.NearClipPlane = 0.05f; lens.FarClipPlane = 20000f;
            vcam.Lens = lens;
            vcam.Priority = 30;
            vcam.enabled = false;

            // First-person tool viewmodels
            var tools = new Transform[4];
            var drill = MB();
            drill.AddBox(T(0, 0, 0), new Vector3(0.13f, 0.15f, 0.36f), Gold);
            drill.AddBox(T(0, 0.02f, 0.02f), new Vector3(0.14f, 0.06f, 0.26f), White);
            Shiny(drill, Matte, () => drill.AddBox(T(0, -0.12f, -0.08f), new Vector3(0.08f, 0.18f, 0.1f), Dark));
            Glow(drill, () => drill.AddPrism(TR(0, 0, 0.26f, 90, 0, 0), 0.06f, 0.015f, 0.2f, 6, Cyan), 0.6f);
            var blaster = MB();
            blaster.AddBox(T(0, 0, 0), new Vector3(0.11f, 0.13f, 0.3f), Blue);
            blaster.AddBox(T(0, 0.05f, -0.02f), new Vector3(0.12f, 0.05f, 0.2f), White);
            Shiny(blaster, Matte, () => blaster.AddBox(T(0, -0.11f, -0.06f), new Vector3(0.07f, 0.16f, 0.09f), Dark));
            blaster.AddPrism(TR(0, 0.02f, 0.2f, 90, 0, 0), 0.035f, 0.035f, 0.14f, 6, White);
            Glow(blaster, () => blaster.AddBox(T(0, 0.08f, -0.02f), new Vector3(0.04f, 0.01f, 0.14f), Cyan));
            var rifle = MB();
            rifle.AddBox(T(0, 0, 0.05f), new Vector3(0.11f, 0.13f, 0.55f), Navy);
            rifle.AddBox(T(0, 0.02f, 0.18f), new Vector3(0.12f, 0.1f, 0.22f), White);
            Shiny(rifle, Matte, () => rifle.AddBox(T(0, -0.11f, -0.1f), new Vector3(0.07f, 0.16f, 0.09f), Dark));
            rifle.AddBox(T(0, 0.09f, 0.05f), new Vector3(0.05f, 0.05f, 0.2f), Gold);
            Glow(rifle, () => rifle.AddBox(T(0.057f, 0f, 0.05f), new Vector3(0.01f, 0.03f, 0.3f), Cyan));
            var medkit = MB();
            medkit.AddBox(T(0, 0, 0), new Vector3(0.22f, 0.16f, 0.14f), White);
            medkit.AddBox(T(0, 0, 0.075f), new Vector3(0.12f, 0.035f, 0.01f), Red);
            medkit.AddBox(T(0, 0, 0.075f), new Vector3(0.035f, 0.12f, 0.01f), Red);
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

            var lamp = AddLight(go.transform, "Headlamp", new Vector3(0, 1.74f, 0.4f), LampWhite, 180f, 60f, 58f);
            Set(go.AddComponent<PlayerHeadlamp>(), "lamp", lamp);
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
            var g = Visual(parent, "SeatedBody", AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Astronaut.asset"), MatHull);
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

        /// <summary>
        /// Exhaust plume pointing -Z, unit length (ShipEngineFx scales z with throttle): a long blue outer sheath around
        /// a short white-hot core. Smooth rings with per-ring colour; alpha (density) fades towards the tip.
        /// </summary>
        static Mesh PlumeMesh()
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            void Cone(float r0, float length, float bulge, Color c0, Color c1, int rings, int sides)
            {
                int start = verts.Count;
                for (int j = 0; j <= rings; j++)
                {
                    float t = j / (float)rings;
                    float r = r0 * (1f + bulge * Mathf.Sin(t * Mathf.PI)) * Mathf.Pow(1f - t, 0.75f) + 0.004f;
                    float slope = r0 * 0.75f / length; // approximate cone slope for the normals
                    var col = Color.Lerp(c0, c1, t);
                    col.a *= Mathf.Pow(1f - t, 1.6f);
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = i / (float)sides * Mathf.PI * 2f;
                        var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                        verts.Add(radial * r + new Vector3(0f, 0f, -t * length));
                        normals.Add((radial + new Vector3(0f, 0f, -slope)).normalized);
                        colors.Add(col);
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < sides; i++)
                    {
                        int a = start + j * (sides + 1) + i, b = a + sides + 1;
                        tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
            }
            Cone(0.38f, 1f, 0.12f, new Color(0.35f, 0.75f, 1f, 0.85f), new Color(0.15f, 0.3f, 1f, 0.6f), 12, 20);
            Cone(0.2f, 0.45f, 0.05f, new Color(1f, 1f, 1f, 1f), new Color(0.6f, 0.9f, 1f, 0.8f), 8, 16);
            var m = new Mesh { name = "ThrusterGlow" };
            m.SetVertices(verts); m.SetNormals(normals); m.SetColors(colors); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            string path = MeshDir + "/ThrusterGlow.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Transform[] Thrusters(Transform parent, params Vector3[] at)
        {
            var list = new Transform[at.Length];
            var mesh = PlumeMesh();
            for (int i = 0; i < at.Length; i++)
            {
                var g = Visual(parent, "Thruster", mesh, MatPlume);
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
            go.AddComponent<TerrainAnchor>().Radius = 70f;
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

        // Canopy bubble (ellipsoid) shared by hull frame and glass. Pilot eye sits at (0, 1.3, 0.9).
        static readonly Vector3 DartCanopyC = new Vector3(0f, 0.62f, 1.05f), DartCanopyR = new Vector3(0.68f, 0.8f, 1.5f);

        static Mesh DartCanopyMesh()
        {
            var glass = new MeshBuilder { SmoothAngle = 70f };
            glass.AddSphere(Matrix4x4.TRS(DartCanopyC, Quaternion.identity, DartCanopyR), 1f, 3, Color.white);
            return SaveMesh(glass, "Ship_Dart_Canopy");
        }

        /// <summary>
        /// Dart: agile starter fighter (DESIGN.md "Ship Design Language"). Lofted chalk-white fuselage with a dark slate
        /// belly, bulbous canopy, swept delta wings with blue accent plates and gold tip pods, twin oversized thruster
        /// bells and wing-root blasters. +Z forward, origin at the centre of mass.
        /// </summary>
        static Mesh DartHullMesh()
        {
            var mb = Hero();
            var cyanGlow = new Color(0.4f, 0.9f, 1f);

            // ---- fuselage
            int hullStart = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-4.15f, 0.82f, 0.52f, 2.8f),
                new MeshBuilder.Section(-3.6f, 0.95f, 0.6f, 3f),
                new MeshBuilder.Section(-2.4f, 1.08f, 0.68f, 3.2f),
                new MeshBuilder.Section(-0.5f, 1.12f, 0.7f, 3.2f),
                new MeshBuilder.Section(1.2f, 1.04f, 0.66f, 3f),
                new MeshBuilder.Section(2.5f, 0.86f, 0.54f, 2.8f, -0.05f),
                new MeshBuilder.Section(3.6f, 0.52f, 0.35f, 2.4f, -0.1f),
                new MeshBuilder.Section(4.3f, 0.2f, 0.15f, 2.2f, -0.12f),
                new MeshBuilder.Section(4.55f, 0f, 0f, 2f, -0.12f),
            }, 32, Chalk, smoothing: 3);
            // Livery: slate belly, blue cheek stripe, gunmetal nose tip and tail ring.
            mb.Paint(hullStart, p => p.y < -0.3f, Slate, Satin);
            mb.Paint(hullStart, p => p.y > 0.02f && p.y < 0.24f && Mathf.Abs(p.x) > 0.75f && p.z > -3.3f && p.z < 2.9f, Blue);
            mb.Paint(hullStart, p => p.z > 4.25f || p.z < -3.95f, Gunmetal, 0.6f, 1f);

            // ---- cockpit: dark tub, seat, side consoles, canopy frame
            With(mb, Satin, 0f, 0.3f, () =>
            {
                mb.AddBox(T(0, 0.66f, 1.1f), new Vector3(1.1f, 0.08f, 2.3f), Slate);                 // tub floor
                mb.AddBox(TR(0, 0.88f, 0.5f, -12, 0, 0), new Vector3(0.6f, 0.52f, 0.14f), Slate);      // seat back
                mb.AddBox(TR(0, 1.17f, 0.46f, -12, 0, 0), new Vector3(0.32f, 0.18f, 0.12f), Rubber);   // headrest
                for (int s = -1; s <= 1; s += 2)
                {
                    // side consoles sit beside/behind the pilot so they never cover the dashboard
                    mb.AddBox(TR(s * 0.53f, 0.74f, 0.95f, 0, 0, s * -15), new Vector3(0.18f, 0.14f, 0.8f), Slate);
                    Glow(mb, () => mb.AddBox(TR(s * 0.51f, 0.815f, 1.0f, 0, 0, s * -15), new Vector3(0.09f, 0.012f, 0.34f), cyanGlow), 0.9f);
                    Glow(mb, () => mb.AddBox(TR(s * 0.51f, 0.815f, 0.72f, 0, 0, s * -15), new Vector3(0.05f, 0.012f, 0.05f), s < 0 ? Red : Green), 1.2f);
                }
            });
            With(mb, 0.6f, 0.9f, 0f, () =>
            {
                // sill ring where the bubble meets the hull and a rear hoop behind the pilot's head (clear forward view)
                var sill = new List<Vector3>();
                var hoop = new List<Vector3>();
                for (int i = 0; i <= 32; i++)
                {
                    float a = i / 32f * Mathf.PI * 2f;
                    sill.Add(DartCanopyC + new Vector3(Mathf.Cos(a) * DartCanopyR.x * 1.0f, 0.06f, Mathf.Sin(a) * DartCanopyR.z * 1.0f));
                }
                float hz = 0.15f - DartCanopyC.z, k = Mathf.Sqrt(1f - (hz * hz) / (DartCanopyR.z * DartCanopyR.z));
                for (int i = 0; i <= 12; i++)
                {
                    float a = i / 12f * Mathf.PI;
                    hoop.Add(DartCanopyC + new Vector3(Mathf.Cos(a) * DartCanopyR.x * k * 1.02f, Mathf.Sin(a) * DartCanopyR.y * k * 1.02f, hz));
                }
                Polyline(mb, sill, 0.04f, 8, Gunmetal);
                Polyline(mb, hoop, 0.034f, 6, Gunmetal);
            });

            // ---- dorsal spine with vents, spine light and antennas
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-3.3f, 0.12f, 0.06f, 3f, 0.62f),
                new MeshBuilder.Section(-2.9f, 0.3f, 0.14f, 3.5f, 0.66f),
                new MeshBuilder.Section(-0.9f, 0.32f, 0.15f, 3.5f, 0.68f),
                new MeshBuilder.Section(-0.3f, 0.22f, 0.1f, 3f, 0.66f),
            }, 16, Chalk, smoothing: 2);
            With(mb, 0.2f, 0.6f, 0f, () =>
            {
                for (int i = 0; i < 6; i++)
                    mb.AddBox(T(0.17f * (i % 2 == 0 ? 1 : -1), 0.83f, -1.15f - (i / 2) * 0.22f), new Vector3(0.18f, 0.03f, 0.08f), Rubber);
            });
            Glow(mb, () => mb.AddBox(T(0, 0.835f, -2.35f), new Vector3(0.05f, 0.02f, 0.75f), Cyan), 0.9f);
            With(mb, 0.5f, 1f, 0f, () =>
            {
                mb.AddTube(new Vector3(0.22f, 0.8f, -2.7f), new Vector3(0.3f, 1.45f, -3.05f), 0.016f, 0.01f, 6, Gunmetal);
                mb.AddTube(new Vector3(-0.22f, 0.8f, -2.75f), new Vector3(-0.26f, 1.15f, -2.95f), 0.014f, 0.01f, 6, Gunmetal);
            });
            Glow(mb, () => mb.AddSphere(T(0.3f, 1.46f, -3.05f), 0.025f, 1, Red), 1.5f);

            // ---- dorsal fin (vertical slab: local x = height above the spine)
            mb.AddSlab(TR(0, 0.7f, 0, 0, 0, 90), new[] { new Vector2(0f, -1.6f), new Vector2(1.05f, -3.05f), new Vector2(1.1f, -3.6f), new Vector2(0f, -3.95f) },
                0.2f, 0.035f, Blue, new[] { 0.2f, 0.08f, 0.08f, 0.2f });
            mb.AddSlab(TR(0, 0.7f, 0, 0, 0, 90), new[] { new Vector2(0.86f, -2.78f), new Vector2(1.07f, -3.08f), new Vector2(1.12f, -3.62f), new Vector2(0.86f, -3.66f) },
                0.2f, 0.02f, Chalk, new[] { 0.15f, 0.13f, 0.13f, 0.15f });

            // ---- nose details: chin sensor turret + cannon, RCS blocks, headlamps
            With(mb, 0.85f, 0.2f, 0f, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, -0.42f, 3.0f), Quaternion.identity, new Vector3(1f, 0.7f, 1.2f)), 0.2f, 2, Slate));
            With(mb, 0.55f, 1f, 0f, () =>
            {
                mb.AddTube(new Vector3(0, -0.47f, 3.05f), new Vector3(0, -0.47f, 3.6f), 0.045f, 0.045f, 10, Gunmetal);
                mb.AddPrism(TR(0, -0.47f, 3.55f, 90, 0, 0), 0.065f, 0.065f, 0.1f, 10, Slate);
            });
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Bevel = 0.25f;
                With(mb, Satin, 0.4f, 0.2f, () => mb.AddBox(T(s * 0.6f, 0.02f, 3.0f), new Vector3(0.14f, 0.16f, 0.32f), Slate));
                With(mb, 0.3f, 1f, 0f, () =>
                {
                    mb.AddPrism(TR(s * 0.68f, 0.06f, 3.05f, 0, 0, s * -90), 0.025f, 0.035f, 0.05f, 6, Rubber);
                    mb.AddPrism(TR(s * 0.68f, -0.03f, 2.95f, 0, 0, s * -90), 0.025f, 0.035f, 0.05f, 6, Rubber);
                });
                mb.Bevel = 0.18f;
                Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(s * 0.38f, -0.24f, 3.78f), Quaternion.identity, new Vector3(1f, 0.6f, 0.5f)), 0.07f, 2, LampWhite), 1.6f);
            }

            for (int s = -1; s <= 1; s += 2)
            {
                var M = Matrix4x4.Scale(new Vector3(s, 1f, 1f)); // mirror; primitives pick winding from geometry
                var W = M * TR(0, -0.18f, 0, 0, 0, 5);          // wing frame with 5 deg dihedral

                // ---- delta wing, raised blue accent plate, slate flap, underwing pylon with missile
                mb.AddSlab(W, new[] { new Vector2(1.0f, 1.2f), new Vector2(4.0f, -1.5f), new Vector2(4.15f, -2.35f), new Vector2(1.0f, -3.1f) },
                    0.3f, 0.045f, Chalk, new[] { 0.34f, 0.12f, 0.12f, 0.3f });
                mb.AddSlab(W, new[] { new Vector2(0.98f, 1.23f), new Vector2(4.03f, -1.49f), new Vector2(4.08f, -1.8f), new Vector2(0.98f, 0.5f) },
                    0.3f, 0.02f, Blue, new[] { 0.37f, 0.15f, 0.15f, 0.36f });
                With(mb, Satin, 0.3f, 0.4f, () =>
                    mb.AddSlab(W * T(0, -0.02f, 0), new[] { new Vector2(1.6f, -2.98f), new Vector2(3.6f, -2.5f), new Vector2(3.6f, -2.78f), new Vector2(1.6f, -3.26f) },
                        0.08f, 0.015f, Slate));
                With(mb, Satin, 0.5f, 0.3f, () => mb.AddBox(W * T(2.6f, -0.2f, -1.0f), new Vector3(0.1f, 0.16f, 1.0f), Slate));
                int missile = mb.VertexCount;
                mb.AddLoft(W * T(2.6f, -0.38f, 0), new[]
                {
                    new MeshBuilder.Section(-1.75f, 0.07f, 0.07f, 2f), new MeshBuilder.Section(-1.6f, 0.09f, 0.09f, 2f),
                    new MeshBuilder.Section(-0.1f, 0.09f, 0.09f, 2f), new MeshBuilder.Section(0.2f, 0.05f, 0.05f, 2f), new MeshBuilder.Section(0.32f, 0f, 0f, 2f),
                }, 12, Chalk, smoothing: 2);
                mb.Paint(missile, p => p.z > -0.05f, Red, 0.6f);
                With(mb, Satin, 0.4f, 0f, () =>
                {
                    for (int f = 0; f < 4; f++)
                        mb.AddBox(W * T(2.6f, -0.38f, -1.55f) * TR(0, 0, 0, 0, 0, f * 90 + 45) * T(0, 0.13f, 0), new Vector3(0.012f, 0.1f, 0.22f), Slate);
                });

                // ---- wingtip pod: gold nose, navigation light
                int pod = mb.VertexCount;
                mb.AddLoft(W * T(4.13f, 0, 0), new[]
                {
                    new MeshBuilder.Section(-2.75f, 0.06f, 0.06f, 2f), new MeshBuilder.Section(-2.55f, 0.15f, 0.14f, 2.4f),
                    new MeshBuilder.Section(-1.3f, 0.16f, 0.15f, 2.4f), new MeshBuilder.Section(-0.8f, 0.09f, 0.09f, 2.2f), new MeshBuilder.Section(-0.6f, 0f, 0f, 2f),
                }, 14, Chalk, smoothing: 2);
                mb.Paint(pod, p => p.z > -1.25f, Gold, 0.75f, 0.85f);
                Glow(mb, () => mb.AddSphere(W * T(4.13f, 0.0f, -2.8f), 0.05f, 1, s < 0 ? Red : Green), 1.6f);

                // ---- intake scoop below the wing root with a dark grille
                int intake = mb.VertexCount;
                mb.AddLoft(M * T(1.02f, -0.28f, 0), new[]
                {
                    new MeshBuilder.Section(0.2f, 0.1f, 0.12f, 4f), new MeshBuilder.Section(0.9f, 0.22f, 0.2f, 4f), new MeshBuilder.Section(1.75f, 0.24f, 0.21f, 4f),
                }, 16, Chalk, smoothing: 2);
                mb.Paint(intake, p => p.z > 1.74f, Rubber, 0.1f);
                With(mb, 0.4f, 1f, 0f, () =>
                {
                    for (int g = -1; g <= 1; g++) mb.AddBox(M * T(1.02f + g * 0.09f, -0.28f, 1.76f), new Vector3(0.02f, 0.34f, 0.03f), Gunmetal);
                });

                // ---- wing-root blaster: armoured housing, barrel with cooling rings, muzzle brake
                With(mb, Satin, 0.6f, 0.6f, () =>
                    mb.AddLoft(M * T(1.22f, -0.32f, 0), new[]
                    {
                        new MeshBuilder.Section(0.3f, 0.12f, 0.1f, 3f), new MeshBuilder.Section(0.6f, 0.21f, 0.18f, 3.5f),
                        new MeshBuilder.Section(1.8f, 0.2f, 0.17f, 3.5f), new MeshBuilder.Section(2.15f, 0.13f, 0.12f, 3f),
                    }, 16, Slate, smoothing: 2));
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(M.MultiplyPoint3x4(new Vector3(1.22f, -0.32f, 2.1f)), M.MultiplyPoint3x4(new Vector3(1.22f, -0.32f, 3.1f)), 0.06f, 0.06f, 12, Gunmetal);
                    for (int r = 0; r < 3; r++) mb.AddPrism(M * TR(1.22f, -0.32f, 2.3f + r * 0.14f, 90, 0, 0), 0.085f, 0.085f, 0.05f, 12, Slate);
                    mb.AddPrism(M * TR(1.22f, -0.32f, 3.02f, 90, 0, 0), 0.09f, 0.08f, 0.16f, 12, Slate);
                });
                Glow(mb, () => mb.AddPrism(M * TR(1.22f, -0.32f, 3.11f, 90, 0, 0), 0.045f, 0.045f, 0.01f, 12, Cyan), 1.2f);

                // ---- engine nacelle + oversized thruster bell (lathe: outside up, over the lip, back down the inside)
                int nacelle = mb.VertexCount;
                mb.AddLoft(M * T(0.72f, 0, 0), new[]
                {
                    new MeshBuilder.Section(-4.3f, 0.5f, 0.5f, 2.2f), new MeshBuilder.Section(-3.4f, 0.56f, 0.55f, 2.3f),
                    new MeshBuilder.Section(-2.0f, 0.46f, 0.48f, 2.5f, 0.02f), new MeshBuilder.Section(-1.2f, 0.28f, 0.34f, 2.6f, 0.05f),
                }, 24, Chalk, capEnd: false, smoothing: 3);
                mb.Paint(nacelle, p => p.z < -3.95f, Gunmetal, 0.6f, 1f);
                mb.Paint(nacelle, p => p.z >= -3.95f && p.z < -3.75f, Blue);
                var bellM = M * Matrix4x4.TRS(new Vector3(0.72f, 0f, -4.3f), Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one);
                With(mb, 0.6f, 1f, 0.2f, () =>
                {
                    mb.AddLathe(bellM, new[]
                    {
                        new Vector2(0.44f, -0.02f), new Vector2(0.5f, 0.12f), new Vector2(0.56f, 0.38f), new Vector2(0.63f, 0.62f), new Vector2(0.62f, 0.67f),
                        new Vector2(0.57f, 0.63f), new Vector2(0.48f, 0.38f), new Vector2(0.37f, 0.16f), new Vector2(0.31f, 0.07f),
                    }, 28, Gunmetal);
                    for (int r = 0; r < 12; r++)
                    {
                        float a = r * 30f;
                        mb.AddBox(bellM * Matrix4x4.Rotate(Quaternion.Euler(0, a, 0)) * TR(0.56f, 0.34f, 0, 0, 0, -12), new Vector3(0.05f, 0.5f, 0.07f), Slate);
                    }
                });
                // glowing annulus at the throat around a gunmetal turbine spinner
                Glow(mb, () => mb.AddLathe(bellM, new[] { new Vector2(0.31f, 0.07f), new Vector2(0.21f, 0.04f) }, 28, Cyan), 1.3f);
                With(mb, 0.65f, 1f, 0f, () => mb.AddLathe(bellM, new[] { new Vector2(0.21f, 0.0f), new Vector2(0.2f, 0.08f), new Vector2(0.12f, 0.2f), new Vector2(0.03f, 0.26f), new Vector2(0f, 0.265f) }, 20, Slate));
                // heat-sink fins on the nacelle flank
                With(mb, 0.35f, 1f, 0f, () =>
                {
                    for (int f = 0; f < 5; f++)
                        mb.AddBox(M * T(1.22f, 0.12f, -2.7f - f * 0.22f), new Vector3(0.12f, 0.26f, 0.05f), Gunmetal);
                });
            }

            // ---- tail: central exhaust vent between the bells
            With(mb, 0.3f, 1f, 0f, () =>
            {
                mb.AddBox(T(0, 0.05f, -4.2f), new Vector3(0.5f, 0.3f, 0.12f), Rubber);
                for (int g = 0; g < 4; g++) mb.AddBox(T(0, -0.06f + g * 0.075f, -4.27f), new Vector3(0.46f, 0.02f, 0.03f), Gunmetal);
            });
            return SaveMesh(mb, "Ship_Dart");
        }

        static GameObject BuildDart()
        {
            var go = ShipRoot("Ship_Dart", ShipClass.Dart, 2000f, 4, 2);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", DartHullMesh(), MatHull);
            Visual(go.transform, "Canopy", DartCanopyMesh(), MatGlass);

            Box(go, new Vector3(0, 0, 0), new Vector3(2.3f, 1.5f, 9f));
            Box(go, new Vector3(0, -0.25f, -1.2f), new Vector3(9f, 0.5f, 2.6f));

            Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, 0.1f, 1f), new Vector3(0, 1.3f, 0.9f), new Vector3(-3.2f, 0.4f, 1.5f),
                new Vector3(0, 0.5f, 1.2f), new Vector3(2.8f, 2.2f, 5.5f));
            Dashboard(go.transform, new Vector3(0, 0.78f, 1.75f), 1.0f);
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, 0.2f, -3.2f));
            Box(term, Vector3.zero, new Vector3(2.6f, 2f, 2f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-1.22f, -0.32f, 3.15f)).transform, Child(go.transform, "MuzzleR", new Vector3(1.22f, -0.32f, 3.15f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleC", new Vector3(0, -0.47f, 3.65f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-3.2f, 0.5f, 0.5f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-0.72f, 0, -4.9f), new Vector3(0.72f, 0, -4.9f));
            ShipLighting(go, new Vector3(0, -1.2f, 0), 10f, new Vector3(-0.38f, -0.24f, 3.85f), new Vector3(0.38f, -0.24f, 3.85f));
            return SavePrefab(go);
        }

        static GameObject BuildMule()
        {
            var go = ShipRoot("Ship_Mule", ShipClass.Mule, 3500f, 6, 3);
            var core = go.GetComponent<ShipCore>();
            var mb = MB(0.2f);
            mb.AddBox(T(0, 0, -1f), new Vector3(3.4f, 2.4f, 8f), White);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, -0.2f, 3.9f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(3.4f, 2f, 1.8f), White);
            mb.AddBox(T(0, 0.2f, -1f), new Vector3(3.48f, 0.4f, 6.4f), Blue);                // side stripe
            mb.AddBox(T(-2.4f, -0.4f, -1.5f), new Vector3(1.4f, 1.6f, 6f), Blue);           // cargo pods
            mb.AddBox(T(2.4f, -0.4f, -1.5f), new Vector3(1.4f, 1.6f, 6f), Blue);
            mb.AddBox(T(-2.4f, -0.4f, 1.2f), new Vector3(1.5f, 1.2f, 0.5f), White);
            mb.AddBox(T(2.4f, -0.4f, 1.2f), new Vector3(1.5f, 1.2f, 0.5f), White);
            Shiny(mb, Glossy, () =>
            {
                mb.AddBox(T(-3.12f, -0.4f, -1.5f), new Vector3(0.1f, 0.3f, 4f), Gold);
                mb.AddBox(T(3.12f, -0.4f, -1.5f), new Vector3(0.1f, 0.3f, 4f), Gold);
            });
            mb.AddBox(T(0, -1.3f, -1f), new Vector3(3f, 0.3f, 7f), Navy);
            for (int i = 0; i < 4; i++)
                Nozzle(mb, new Vector3(i < 2 ? -1.0f : 1.0f, i % 2 == 0 ? 0.55f : -0.55f, -6.1f), 0.5f, 1.2f, Mid, Cyan);
            mb.AddBox(T(0, 1.35f, -2f), new Vector3(1.2f, 0.3f, 3f), Navy);
            Glow(mb, () => mb.AddBox(T(0, 1.52f, -2f), new Vector3(0.5f, 0.05f, 2.4f), Cyan), 0.8f);
            Shiny(mb, Matte, () =>
            {
                mb.AddBox(T(-1.6f, -0.9f, 2.2f), new Vector3(0.3f, 0.3f, 1.6f), Dark);
                mb.AddBox(T(1.6f, -0.9f, 2.2f), new Vector3(0.3f, 0.3f, 1.6f), Dark);
            });
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
            ShipLighting(go, new Vector3(0, -2f, -1f), 12f, new Vector3(-1.6f, -0.9f, 3.4f), new Vector3(1.6f, -0.9f, 3.4f));
            return SavePrefab(go);
        }

        static GameObject BuildArk()
        {
            var go = ShipRoot("Ship_Ark", ShipClass.Ark, 20000f, 8, 4);
            var core = go.GetComponent<ShipCore>();
            var hc = go.GetComponent<HealthComponent>();
            hc.MaxHealth = 600f;

            // Interior: floor y=0, 8 wide (x), 26 long (z), 4 high. Open doorway at the rear.
            var mb = MB(0.12f);
            mb.AddBox(T(0, -0.2f, 0), new Vector3(8f, 0.4f, 26f), Mid);                     // floor
            Glow(mb, () => mb.AddBox(T(0, 0f, 0), new Vector3(0.3f, 0.02f, 24f), Cyan), 0.6f); // floor light strip
            mb.AddBox(T(0, -0.01f, 0), new Vector3(2f, 0.02f, 24f), Navy);                   // floor runner
            mb.AddBox(T(-4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f), White);                // walls
            mb.AddBox(T(4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f), White);
            mb.AddBox(T(0, 4.2f, 0), new Vector3(8.8f, 0.4f, 26f), White);                  // ceiling
            mb.AddBox(T(0, 2f, 13.2f), new Vector3(8.8f, 4.4f, 0.4f), White);               // front wall
            mb.AddBox(T(-2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), White);         // rear wall L
            mb.AddBox(T(2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), White);          // rear wall R
            mb.AddBox(T(0, 3.7f, -13.2f), new Vector3(2.2f, 1.4f, 0.4f), White);            // lintel
            Shiny(mb, Glossy, () => mb.AddBox(T(0, 3.05f, -13.45f), new Vector3(2.4f, 0.12f, 0.12f), Gold)); // door frame
            // exterior shell
            mb.Bevel = 0.2f;
            mb.AddBox(T(0, -1.2f, 0), new Vector3(10f, 1.6f, 30f), Blue);
            mb.AddBox(T(0, -1.95f, 0), new Vector3(8f, 0.3f, 26f), Navy);
            mb.AddBox(T(-5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f), White);
            mb.AddBox(T(5.4f, 1.6f, 0), new Vector3(2f, 3f, 24f), White);
            mb.AddBox(T(-6.45f, 1.6f, 0), new Vector3(0.2f, 0.5f, 20f), Blue);              // hull stripes
            mb.AddBox(T(6.45f, 1.6f, 0), new Vector3(0.2f, 0.5f, 20f), Blue);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 1.6f, 16.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(9f, 5.2f, 6f), White);
            mb.AddBox(T(0, 5f, -2f), new Vector3(5f, 1.2f, 16f), Navy);
            Glow(mb, () => mb.AddBox(T(0, 5.65f, -2f), new Vector3(1.2f, 0.1f, 14f), Cyan), 0.7f);
            mb.AddBox(T(-8f, 0f, -4f), new Vector3(6f, 0.6f, 8f), White);                   // wings
            mb.AddBox(T(8f, 0f, -4f), new Vector3(6f, 0.6f, 8f), White);
            mb.AddBox(T(-8f, 0f, -1.2f), new Vector3(6.1f, 0.66f, 1.2f), Blue);
            mb.AddBox(T(8f, 0f, -1.2f), new Vector3(6.1f, 0.66f, 1.2f), Blue);
            Shiny(mb, Glossy, () =>
            {
                mb.AddBox(T(-11.1f, 0f, -4f), new Vector3(0.4f, 1f, 7f), Gold);
                mb.AddBox(T(11.1f, 0f, -4f), new Vector3(0.4f, 1f, 7f), Gold);
            });
            for (int i = 0; i < 4; i++)
                Nozzle(mb, new Vector3(i < 2 ? -3.6f : 3.6f, i % 2 == 0 ? 2.8f : 0.2f, -17f), 1.1f, 3f, Mid, Cyan);
            // interior furniture
            mb.Bevel = 0.15f;
            Shiny(mb, Matte, () =>
            {
                mb.AddBox(T(0, 0.5f, 11.4f), new Vector3(0.9f, 1f, 0.9f), Dark);                // pilot chair
                mb.AddBox(T(-3f, 0.5f, 3f), new Vector3(0.9f, 1f, 0.9f), Dark);                 // turret chairs
                mb.AddBox(T(3f, 0.5f, 3f), new Vector3(0.9f, 1f, 0.9f), Dark);
            });
            mb.AddBox(T(-3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f), Navy);            // engineer console
            mb.AddBox(T(3.5f, 0.6f, -4f), new Vector3(0.8f, 1.2f, 2.2f), Blue);             // fabricator
            mb.AddBox(T(3.5f, 0.6f, -8f), new Vector3(0.8f, 1.2f, 2.2f), Gold);             // cargo terminal
            Glow(mb, () =>
            {
                mb.AddBox(T(-3.08f, 1.0f, -4f), new Vector3(0.04f, 0.3f, 1.6f), Cyan);
                mb.AddBox(T(3.08f, 1.0f, -4f), new Vector3(0.04f, 0.3f, 1.6f), Cyan);
                mb.AddBox(T(3.08f, 1.0f, -8f), new Vector3(0.04f, 0.3f, 1.6f), Cyan);
                for (int i = 0; i < 6; i++) mb.AddBox(T(-3.96f, 1.6f, -10f + i * 4f), new Vector3(0.1f, 0.25f, 1.5f), Cyan); // wall lights
            }, 0.9f);
            Visual(go.transform, "Hull", SaveMesh(mb, "Ship_Ark"));
            var glass = new MeshBuilder();
            glass.AddBox(T(0, 2.6f, 13.45f), new Vector3(6f, 1.6f, 0.1f), Color.white);
            Visual(go.transform, "Windshield", SaveMesh(glass, "Ship_Ark_Glass"), MatGlass);

            // Turret heads
            var turretMesh = MB();
            turretMesh.AddPrism(T(0, 0.3f, 0), 1f, 0.9f, 0.6f, 8, Navy);
            var head = MB();
            head.AddBox(T(0, 0, 0), new Vector3(1.2f, 0.8f, 1.2f), White);
            head.AddBox(T(0, 0.1f, 0), new Vector3(1.26f, 0.2f, 1.26f), Blue);
            Shiny(head, Matte, () =>
            {
                head.AddBox(T(-0.3f, 0, 1f), new Vector3(0.2f, 0.2f, 1.4f), Dark);
                head.AddBox(T(0.3f, 0, 1f), new Vector3(0.2f, 0.2f, 1.4f), Dark);
            });
            Glow(head, () => head.AddBox(T(0, 0.2f, 0.61f), new Vector3(0.5f, 0.08f, 0.02f), Cyan));
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
            ShipLighting(go, new Vector3(0, -3f, 0), 24f, new Vector3(-3f, -1.2f, 17.2f), new Vector3(3f, -1.2f, 17.2f));
            // Interior: the hull blocks the sun, so the cabin gets its own cool ceiling lights.
            for (int i = 0; i < 3; i++) AddLight(go.transform, "CabinLight", new Vector3(0, 3.6f, -8f + i * 8f), new Color(0.75f, 0.88f, 1f), 9f, 9f);
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
            var mb = MB(0.18f);
            GameObject go;
            Vector3 muzzle = new Vector3(0, 0, 2f);
            switch (k)
            {
                case EnemyKind.PirateFighter:
                    go = EnemyRoot(k, Team.Pirates, 45);
                    mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 0.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(1.6f, 1f, 4.5f), PirateDark);
                    mb.AddBox(TR(-1.8f, 0, -0.6f, 0, 0, 25), new Vector3(2.4f, 0.2f, 2f), PirateRed);
                    mb.AddBox(TR(1.8f, 0, -0.6f, 0, 0, -25), new Vector3(2.4f, 0.2f, 2f), PirateRed);
                    mb.AddBox(TR(-2.9f, -0.5f, -0.6f, 0, 0, 25), new Vector3(0.2f, 0.6f, 1.6f), PirateDark);
                    mb.AddBox(TR(2.9f, -0.5f, -0.6f, 0, 0, -25), new Vector3(0.2f, 0.6f, 1.6f), PirateDark);
                    Shiny(mb, Mirror, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.5f, -0.6f), Quaternion.identity, new Vector3(0.8f, 0.6f, 1.4f)), 0.5f, 1, Red));
                    Nozzle(mb, new Vector3(0, 0, -2.4f), 0.45f, 0.8f, Dark, Red);
                    Box(go, Vector3.zero, new Vector3(5f, 1.6f, 4.6f));
                    muzzle = new Vector3(0, 0, 3f);
                    break;
                case EnemyKind.DroneSwarm:
                    go = EnemyRoot(k, Team.Pirates, 18);
                    mb.AddSphere(Matrix4x4.identity, 0.85f, 1, PirateDark);
                    mb.AddPrism(TR(0, 0, 0, 90, 0, 0), 1.4f, 1.4f, 0.15f, 8, PirateRed);
                    for (int i = 0; i < 4; i++)
                        mb.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, 0, i * 90f + 45f) * new Vector3(0, 1.2f, 0), Quaternion.Euler(0, 0, i * 90f + 45f), Vector3.one), new Vector3(0.25f, 0.6f, 0.25f), PirateDark);
                    Glow(mb, () => mb.AddSphere(T(0, 0, 0.72f), 0.22f, 1, Red), 1.5f);
                    go.AddComponent<SphereCollider>().radius = 1.3f;
                    muzzle = new Vector3(0, 0, 1.3f);
                    break;
                case EnemyKind.PirateGunship:
                    go = EnemyRoot(k, Team.Pirates, 260);
                    mb.AddBox(T(0, 0, 0), new Vector3(5f, 3f, 14f), PirateDark);
                    mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 8.5f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(5f, 3f, 3f), PirateDark);
                    mb.AddBox(T(0, 0.4f, 0), new Vector3(5.1f, 0.6f, 12f), PirateRed);
                    mb.AddBox(T(-3.6f, -0.5f, -1f), new Vector3(2.2f, 2f, 9f), PirateRed);
                    mb.AddBox(T(3.6f, -0.5f, -1f), new Vector3(2.2f, 2f, 9f), PirateRed);
                    mb.AddPrism(T(0, 2f, 2f), 1.2f, 1f, 0.8f, 8, Mid);
                    Shiny(mb, Matte, () => mb.AddBox(T(0, 2.5f, 3.2f), new Vector3(0.4f, 0.4f, 2.4f), Dark));
                    Glow(mb, () =>
                    {
                        mb.AddBox(T(-2.56f, 0.8f, 3f), new Vector3(0.04f, 0.2f, 4f), Red);
                        mb.AddBox(T(2.56f, 0.8f, 3f), new Vector3(0.04f, 0.2f, 4f), Red);
                    });
                    for (int i = 0; i < 3; i++) Nozzle(mb, new Vector3(-2f + i * 2f, 0, -8.3f), 0.8f, 1.4f, Mid, Red);
                    Box(go, new Vector3(0, 0, 0.5f), new Vector3(9.5f, 3.4f, 17f));
                    muzzle = new Vector3(0, 2.5f, 4.6f);
                    break;
                case EnemyKind.Crawler:
                    go = EnemyRoot(k, Team.Aliens, 35);
                    Shiny(mb, Glossy, () => mb.AddRock(Matrix4x4.TRS(new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(1f, 0.6f, 1.4f)), 0.6f, 3, AlienGreen));
                    mb.AddBox(T(0, 0.75f, 0.75f), new Vector3(0.5f, 0.3f, 0.3f), AlienPurple);
                    Glow(mb, () =>
                    {
                        mb.AddSphere(T(-0.14f, 0.82f, 0.92f), 0.07f, 0, Gold);
                        mb.AddSphere(T(0.14f, 0.82f, 0.92f), 0.07f, 0, Gold);
                    }, 1.5f);
                    for (int i = 0; i < 6; i++)
                    {
                        float side = i < 3 ? -1f : 1f;
                        float z = -0.4f + (i % 3) * 0.4f;
                        mb.AddBox(TR(side * 0.6f, 0.3f, z, 0, 0, side * 40f), new Vector3(0.7f, 0.12f, 0.12f), AlienPurple);
                    }
                    Box(go, new Vector3(0, 0.6f, 0), new Vector3(1.4f, 1f, 1.8f));
                    muzzle = new Vector3(0, 0.7f, 0.9f);
                    break;
                case EnemyKind.Spitter:
                    go = EnemyRoot(k, Team.Aliens, 30);
                    Shiny(mb, Glossy, () => mb.AddRock(Matrix4x4.TRS(new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(1f, 1.2f, 1f)), 0.7f, 5, new Color(0.85f, 0.95f, 0.3f)));
                    mb.AddPrism(TR(0, 1.5f, 0.6f, 70, 0, 0), 0.3f, 0.18f, 0.6f, 6, AlienPurple);
                    Glow(mb, () => mb.AddSphere(T(0, 1.62f, 0.86f), 0.12f, 0, AlienGreen), 1.5f);
                    for (int i = 0; i < 4; i++)
                        mb.AddBox(TR(Mathf.Cos(i * 1.57f) * 0.5f, 0.35f, Mathf.Sin(i * 1.57f) * 0.5f, 0, i * 90f, 20), new Vector3(0.14f, 0.8f, 0.14f), AlienGreen);
                    Box(go, new Vector3(0, 1.1f, 0), new Vector3(1.4f, 2.2f, 1.4f));
                    muzzle = new Vector3(0, 1.6f, 0.9f);
                    break;
                case EnemyKind.FlyingCritter:
                    go = EnemyRoot(k, Team.Aliens, 22);
                    Shiny(mb, Glossy, () => mb.AddRock(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.8f, 0.5f, 1.3f)), 0.5f, 9, Pink));
                    mb.AddBox(TR(-1f, 0.1f, 0, 0, 0, 15), new Vector3(1.6f, 0.08f, 0.9f), AlienPurple);
                    mb.AddBox(TR(1f, 0.1f, 0, 0, 0, -15), new Vector3(1.6f, 0.08f, 0.9f), AlienPurple);
                    mb.AddBox(T(0, 0, -0.9f), new Vector3(0.12f, 0.12f, 0.8f), Pink);
                    Glow(mb, () => mb.AddSphere(T(0, 0.1f, 0.6f), 0.1f, 0, Gold), 1.5f);
                    go.AddComponent<SphereCollider>().radius = 0.9f;
                    go.GetComponent<EnemyBrain>().HoverHeight = 5f;
                    muzzle = new Vector3(0, 0, 0.8f);
                    break;
                default: // NestGuardian
                    go = EnemyRoot(k, Team.Aliens, 220);
                    Shiny(mb, Glossy, () =>
                    {
                        mb.AddRock(Matrix4x4.TRS(new Vector3(0, 2f, 0), Quaternion.identity, new Vector3(1.3f, 1.1f, 1f)), 1.3f, 11, AlienPurple);
                        mb.AddRock(Matrix4x4.TRS(new Vector3(0, 3.3f, 0.6f), Quaternion.identity, Vector3.one), 0.6f, 12, AlienGreen);
                    });
                    mb.AddBox(TR(-1.6f, 1.6f, 0.5f, 20, 0, 20), new Vector3(0.5f, 2f, 0.5f), AlienGreen);
                    mb.AddBox(TR(1.6f, 1.6f, 0.5f, 20, 0, -20), new Vector3(0.5f, 2f, 0.5f), AlienGreen);
                    mb.AddBox(T(-0.6f, 0.5f, 0), new Vector3(0.6f, 1f, 0.6f), AlienPurple);
                    mb.AddBox(T(0.6f, 0.5f, 0), new Vector3(0.6f, 1f, 0.6f), AlienPurple);
                    Glow(mb, () => mb.AddBox(T(0, 3.4f, 1.15f), new Vector3(0.6f, 0.15f, 0.1f), Gold), 1.5f);
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

            var mb = MB(0.15f);
            mb.AddBox(T(0, 0, 0), new Vector3(26f, 14f, 70f), PirateDark);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 0, 45f), Quaternion.Euler(0, 180, 0), Vector3.one), new Vector3(26f, 14f, 20f), PirateDark);
            mb.AddBox(T(0, 2f, 0), new Vector3(26.4f, 3f, 60f), PirateRed);
            mb.AddBox(T(0, 10f, -5f), new Vector3(8f, 6f, 50f), PirateRed);
            mb.AddBox(T(-20f, -2f, -10f), new Vector3(14f, 4f, 30f), PirateRed);
            mb.AddBox(T(20f, -2f, -10f), new Vector3(14f, 4f, 30f), PirateRed);
            Shiny(mb, Matte, () =>
            {
                for (int i = 0; i < 7; i++) mb.AddWedge(Matrix4x4.TRS(new Vector3(0, 13.5f, -25f + i * 8f), Quaternion.identity, Vector3.one), new Vector3(2f, 4f, 5f), Dark);
            });
            Glow(mb, () =>
            {
                mb.AddBox(T(-13.25f, 5f, 10f), new Vector3(0.3f, 1f, 40f), Red);
                mb.AddBox(T(13.25f, 5f, 10f), new Vector3(0.3f, 1f, 40f), Red);
            });
            for (int i = 0; i < 4; i++) Nozzle(mb, new Vector3(-9f + i * 6f, 0, -39.5f), 3f, 5f, Mid, Red);
            Shiny(mb, Glossy, () => mb.AddRock(Matrix4x4.TRS(new Vector3(0, -8f, 10f), Quaternion.identity, new Vector3(1.6f, 0.8f, 2.4f)), 6f, 21, AlienPurple));
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
            var mb = MB();
            mb.AddBox(T(0, 0.35f, 0), new Vector3(1.4f, 0.7f, 0.9f), Blue);
            Shiny(mb, Glossy, () => mb.AddBox(T(0, 0.35f, 0), new Vector3(1.46f, 0.12f, 0.96f), Gold));
            mb.AddBox(T(-0.5f, 0.35f, 0), new Vector3(0.12f, 0.74f, 0.96f), White);
            mb.AddBox(T(0.5f, 0.35f, 0), new Vector3(0.12f, 0.74f, 0.96f), White);
            Shiny(mb, Matte, () => mb.AddBox(T(0, 0.04f, 0), new Vector3(1.42f, 0.08f, 0.92f), Dark));
            Glow(mb, () => mb.AddBox(T(0, 0.5f, 0.455f), new Vector3(0.3f, 0.06f, 0.02f), Cyan));
            Visual(go.transform, "Body", SaveMesh(mb, "Crate_Body"));
            AddLight(go.transform, "Glow", new Vector3(0, 0.9f, 0.7f), Cyan, 2.5f, 4f);
            var lid = Child(go.transform, "LidPivot", new Vector3(0, 0.7f, -0.45f));
            var lm = MB();
            lm.AddBox(T(0, 0.08f, 0.45f), new Vector3(1.44f, 0.16f, 0.94f), White);
            lm.AddBox(T(0, 0.17f, 0.45f), new Vector3(0.9f, 0.04f, 0.6f), Blue);
            Visual(lid.transform, "Lid", SaveMesh(lm, "Crate_Lid"));
            Box(go, new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 1f));
            Set(c, "lid", lid.transform);
            return SavePrefab(go);
        }

        static GameObject BuildNode()
        {
            var go = NetRoot("ResourceNode", false);
            var n = go.AddComponent<ResourceNode>();
            var ore = MB();
            Color stone = new Color(0.42f, 0.44f, 0.52f), oreCol = new Color(1f, 0.6f, 0.25f);
            Shiny(ore, Matte, () => ore.AddRock(Matrix4x4.TRS(new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(1.2f, 0.9f, 1f)), 1f, 41, stone));
            Shiny(ore, Glossy, () =>
            {
                ore.AddRock(Matrix4x4.TRS(new Vector3(0.7f, 0.4f, 0.4f), Quaternion.identity, Vector3.one), 0.38f, 42, oreCol);
                ore.AddRock(Matrix4x4.TRS(new Vector3(-0.6f, 0.7f, -0.3f), Quaternion.identity, Vector3.one), 0.32f, 43, oreCol);
                ore.AddRock(Matrix4x4.TRS(new Vector3(0.1f, 1.25f, 0.2f), Quaternion.identity, Vector3.one), 0.26f, 44, oreCol);
            });
            var crystal = MB();
            crystal.Surface(0.9f, 0.35f);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                crystal.AddPrism(TR(Mathf.Cos(a * Mathf.Deg2Rad) * 0.35f, 0.6f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.35f, Mathf.Sin(a) * 18f, a, 18f), 0.22f, 0.02f, 1.4f + (i % 3) * 0.4f, 5,
                    i % 2 == 0 ? Cyan : new Color(0.55f, 0.45f, 1f));
            }
            crystal.AddPrism(T(0, 0.9f, 0), 0.3f, 0.02f, 1.9f, 6, new Color(0.6f, 0.95f, 1f));
            crystal.Surface(Matte);
            crystal.AddRock(Matrix4x4.TRS(new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(1.2f, 0.4f, 1.2f)), 0.55f, 45, stone);
            crystal.Surface();
            var scrap = MB();
            scrap.AddBox(TR(0, 0.3f, 0, 0, 20, 10), new Vector3(1.4f, 0.5f, 0.9f), Mid);
            scrap.AddBox(TR(0.4f, 0.7f, 0.1f, 15, -10, 30), new Vector3(0.8f, 0.3f, 0.6f), Blue);
            scrap.AddBox(TR(-0.35f, 0.62f, 0.3f, -10, 60, 20), new Vector3(0.5f, 0.2f, 0.5f), White);
            Shiny(scrap, Matte, () => scrap.AddBox(TR(-0.4f, 0.6f, -0.2f, -20, 40, 0), new Vector3(0.3f, 0.9f, 0.3f), Dark));
            var fuel = MB();
            Shiny(fuel, Glossy, () => fuel.AddPrism(T(0, 0.6f, 0), 0.45f, 0.45f, 1.2f, 8, Gold));
            fuel.AddPrism(T(0, 0.3f, 0), 0.47f, 0.47f, 0.12f, 8, White);
            fuel.AddPrism(T(0, 0.9f, 0), 0.47f, 0.47f, 0.12f, 8, White);
            Shiny(fuel, Matte, () => fuel.AddPrism(T(0, 1.3f, 0), 0.2f, 0.15f, 0.2f, 8, Dark));
            Glow(fuel, () => fuel.AddBox(T(0, 0.6f, 0.44f), new Vector3(0.12f, 0.3f, 0.04f), Cyan));
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
            var mb = MB();
            Shiny(mb, Glossy, () => mb.AddRock(Matrix4x4.TRS(new Vector3(0, 0.8f, 0), Quaternion.identity, new Vector3(1.6f, 0.9f, 1.6f)), 2f, 51, AlienPurple));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                mb.AddRock(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 2.6f, 0.5f, Mathf.Sin(a) * 2.6f), Quaternion.identity, Vector3.one), 0.6f, 52 + i, AlienGreen);
            }
            Glow(mb, () => mb.AddPrism(T(0, 2.4f, 0), 0.6f, 0.1f, 1.4f, 6, Pink), 0.8f);
            Visual(go.transform, "Mound", SaveMesh(mb, "Nest"));
            AddLight(go.transform, "Glow", new Vector3(0, 3.6f, 0), Pink, 12f, 12f);
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 2.6f; sc.center = new Vector3(0, 1f, 0);
            return SavePrefab(go);
        }

        static GameObject BuildRuin()
        {
            var go = NetRoot("Ruin", false);
            var mb = MB(0.15f);
            Color stone = new Color(0.78f, 0.8f, 0.88f);
            mb.Surface(Matte);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float h = 2.5f + (i % 3) * 1.4f;
                mb.AddBox(TR(Mathf.Cos(a) * 6f, h * 0.5f, Mathf.Sin(a) * 6f, 0, i * 60f, (i % 2) * 6f), new Vector3(1f, h, 1f), stone);
                Box(go, new Vector3(Mathf.Cos(a) * 6f, h * 0.5f, Mathf.Sin(a) * 6f), new Vector3(1f, h, 1f));
            }
            mb.AddBox(T(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f), stone * 0.85f);
            mb.AddBox(TR(3f, 4.2f, 3f, 0, 45, 0), new Vector3(7f, 0.6f, 1f), stone);
            mb.Surface();
            Glow(mb, () =>
            {
                mb.AddPrism(T(0, 1.2f, 0), 0.6f, 0.2f, 2f, 5, Cyan);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f * Mathf.Deg2Rad;
                    mb.AddBox(TR(Mathf.Cos(a) * 5.45f, 1.6f, Mathf.Sin(a) * 5.45f, 0, i * 60f, 0), new Vector3(0.1f, 0.6f, 0.3f), Cyan);
                }
            }, 0.9f);
            Box(go, new Vector3(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f));
            Visual(go.transform, "Mesh", SaveMesh(mb, "Ruin"));
            AddLight(go.transform, "Glow", new Vector3(0, 2.6f, 0), Cyan, 20f, 15f);
            return SavePrefab(go);
        }

        static GameObject BuildCrashed()
        {
            var go = NetRoot("CrashedShip", false);
            var mb = MB();
            mb.AddBox(TR(0, 0.8f, 0, 8, 15, 12), new Vector3(3f, 2f, 9f), White);
            mb.AddBox(TR(0, 0.95f, 0, 8, 15, 12), new Vector3(3.06f, 0.4f, 7f), Blue);
            mb.AddWedge(Matrix4x4.TRS(new Vector3(1.2f, 0.4f, 5f), Quaternion.Euler(-10, 195, 20), Vector3.one), new Vector3(3f, 2f, 3f), White);
            mb.AddBox(TR(-3.5f, 0.3f, -1f, 0, 30, -25), new Vector3(4f, 0.3f, 2.5f), Blue);
            mb.AddBox(TR(4f, 0.2f, 2f, 10, -20, 40), new Vector3(3f, 0.3f, 2f), White);
            Shiny(mb, Matte, () => mb.AddPrism(TR(-1f, 0.6f, -4.5f, 80, 15, 0), 0.8f, 0.6f, 1.6f, 8, Dark));
            Glow(mb, () => mb.AddBox(TR(0.6f, 1.9f, 1f, 8, 15, 12), new Vector3(0.6f, 0.06f, 0.6f), Red), 0.8f);
            Visual(go.transform, "Mesh", SaveMesh(mb, "CrashedShip"));
            AddLight(go.transform, "Warning", new Vector3(0.6f, 2.6f, 1f), Red, 8f, 10f);
            Box(go, new Vector3(0, 0.8f, 0), new Vector3(3.4f, 2.2f, 9f));
            return SavePrefab(go);
        }

        static GameObject BuildWreck()
        {
            var go = NetRoot("Wreck", false);
            var mb = MB(0.15f);
            mb.AddBox(TR(0, 0, 0, 0, 0, 12), new Vector3(8f, 6f, 22f), Mid);
            mb.AddBox(TR(0, 0, 0, 0, 0, 12), new Vector3(8.1f, 1.2f, 18f), Navy);
            mb.AddBox(TR(-6f, 2f, -4f, 20, 10, 40), new Vector3(6f, 0.8f, 10f), White);
            mb.AddBox(TR(7f, -3f, 6f, -30, -20, 10), new Vector3(5f, 0.6f, 8f), Blue);
            Shiny(mb, Matte, () =>
            {
                mb.AddPrism(TR(0, 0, -13f, 90, 0, 0), 2.6f, 2f, 4f, 10, Dark);
                mb.AddBox(TR(2f, 4f, 9f, 0, 30, 60), new Vector3(1f, 6f, 1f), Dark);
                for (int i = 0; i < 6; i++) mb.AddRock(Matrix4x4.TRS(new Vector3(-8f + i * 3.2f, -6f + (i % 2) * 12f, -8f + i * 3f), Quaternion.Euler(i * 30, i * 50, 0), Vector3.one), 1.2f, 70 + i, Mid);
            });
            Visual(go.transform, "Mesh", SaveMesh(mb, "Wreck"));
            Box(go, Vector3.zero, new Vector3(8f, 6f, 22f));
            return SavePrefab(go);
        }

        static GameObject BuildStation()
        {
            var go = NetRoot("TraderStation", false);
            var ts = go.AddComponent<TraderStation>();
            var mb = MB(0.15f);
            mb.AddPrism(T(0, 0, 0), 10f, 10f, 26f, 10, White);
            mb.AddPrism(T(0, 4f, 0), 10.2f, 10.2f, 2f, 10, Blue);
            mb.AddPrism(T(0, -4f, 0), 10.2f, 10.2f, 2f, 10, Blue);
            Glow(mb, () => mb.AddPrism(T(0, 0, 0), 10.15f, 10.15f, 0.5f, 10, Cyan), 0.8f);
            mb.AddPrism(T(0, 14f, 0), 7f, 2f, 4f, 10, Navy);
            mb.AddPrism(T(0, -14f, 0), 2f, 7f, 4f, 10, Navy);
            Shiny(mb, Glossy, () => mb.AddPrism(T(0, 16.6f, 0), 1.2f, 0.3f, 1.4f, 6, Gold));
            mb.AddBox(T(0, 0, 18f), new Vector3(3f, 3f, 16f), Mid);                      // pad arm
            mb.AddBox(T(0, -1.5f, 34f), new Vector3(30f, 1f, 30f), Navy);                // landing pad
            mb.AddBox(T(0, -0.99f, 34f), new Vector3(26f, 0.02f, 2f), White);
            mb.AddBox(T(0, -0.99f, 34f), new Vector3(2f, 0.02f, 26f), White);
            Shiny(mb, Glossy, () => mb.AddBox(T(0, 0.5f, 22.5f), new Vector3(3f, 3f, 1.2f), Gold)); // trader console
            Glow(mb, () =>
            {
                mb.AddBox(T(0, 1.2f, 23.12f), new Vector3(2f, 0.9f, 0.04f), Cyan);
                for (int i = 0; i < 4; i++) mb.AddPrism(T(-14f + (i % 2) * 28f, 1f, 20f + (i / 2) * 28f), 0.3f, 0.3f, 4f, 6, Cyan);
            }, 0.9f);
            Visual(go.transform, "Hub", SaveMesh(mb, "Station_Hub"));
            for (int i = 0; i < 4; i++) AddLight(go.transform, "PadLight", new Vector3(-14f + (i % 2) * 28f, 3.5f, 20f + (i / 2) * 28f), Cyan, 40f, 22f);
            AddLight(go.transform, "ConsoleLight", new Vector3(0, 3.5f, 26f), LampWhite, 30f, 14f);
            var ring = MB(0.15f);
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                ring.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 24f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(9.6f, 2.4f, 2.4f), i % 2 == 0 ? White : Blue);
                ring.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 17f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(0.6f, 0.6f, 14f), Mid);
                if (i % 2 == 0)
                    Glow(ring, () => ring.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 25.22f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(5f, 0.5f, 0.04f), Cyan), 0.8f);
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
            var ring = MB(0.18f);
            const int seg = 28;
            const float R = 55f;
            for (int i = 0; i < seg; i++)
            {
                float a = i * 360f / seg;
                var rot = Quaternion.Euler(0, 0, a);
                if (i % 4 == 0) Shiny(ring, Glossy, () => ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R, 0), rot, Vector3.one), new Vector3(13f, 6f, 8f), Gold));
                else ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R, 0), rot, Vector3.one), new Vector3(13f, 6f, 8f), i % 2 == 0 ? Blue : White);
                if (i % 4 == 0) ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R + 5f, 0), rot, Vector3.one), new Vector3(3f, 5f, 3f), Navy);
                Glow(ring, () => ring.AddBox(Matrix4x4.TRS(rot * new Vector3(0, R - 3.02f, 0), rot, Vector3.one), new Vector3(8f, 0.1f, 2f), Cyan), 0.9f);
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
            var plat = MB(0.1f);
            plat.AddBox(T(0, -R - 10f, 0), new Vector3(16f, 1f, 16f), Navy);
            Shiny(plat, Glossy, () => plat.AddBox(T(0, -R - 8.5f, 0), new Vector3(2f, 2f, 2f), Gold));
            Glow(plat, () => plat.AddBox(T(0, -R - 9.49f, 0), new Vector3(12f, 0.02f, 0.3f), Cyan), 0.8f);
            Visual(go.transform, "Socket", SaveMesh(plat, "Gate_Socket"));
            Box(go, new Vector3(0, -R - 10f, 0), new Vector3(16f, 1f, 16f));
            var sock = Child(go.transform, "SocketTrigger", new Vector3(0, -R - 8.5f, 0));
            Box(sock, Vector3.zero, new Vector3(4f, 4f, 4f), true);
            var grav = Child(go.transform, "SocketGravity", new Vector3(0, -R - 5f, 0));
            Box(grav, Vector3.zero, new Vector3(16f, 9f, 16f), true);
            grav.AddComponent<GravityVolume>();
            Set(gate, "ring", ringGo.transform);
            AddLight(go.transform, "CoreLight", Vector3.zero, Cyan, 6000f, 140f);
            AddLight(go.transform, "SocketLight", new Vector3(0, -R - 5f, 0), Cyan, 40f, 16f);
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
