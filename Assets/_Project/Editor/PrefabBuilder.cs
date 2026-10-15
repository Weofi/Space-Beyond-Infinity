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

        static readonly Color Ochre = new Color(0.96f, 0.62f, 0.04f);
        static readonly Color Graphite = new Color(0.2f, 0.25f, 0.33f);

        /// <summary>
        /// Diagonal hazard stripes (gold / black) on a strip in m's local XZ plane, centred, `length` along x.
        /// Each stripe is a bevelled parallelogram slab, clipped to the strip ends.
        /// </summary>
        static void Hazard(MeshBuilder mb, Matrix4x4 m, float length, float width, int count, float thickness = 0.03f)
        {
            float w = length / count, skew = width * 0.7f, hl = length * 0.5f, hw = width * 0.5f;
            With(mb, 0.45f, 0f, 0.2f, () =>
            {
                for (int i = -1; i <= count; i++)
                {
                    float x0 = -hl + i * w - skew * 0.5f;
                    var pts = new List<Vector2>();
                    foreach (var p in new[] { new Vector2(x0, -hw), new Vector2(x0 + w, -hw), new Vector2(x0 + w + skew, hw), new Vector2(x0 + skew, hw) })
                    {
                        var q = new Vector2(Mathf.Clamp(p.x, -hl, hl), p.y);
                        if (pts.Count == 0 || (pts[pts.Count - 1] - q).sqrMagnitude > 1e-6f) pts.Add(q);
                    }
                    if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f) pts.RemoveAt(pts.Count - 1);
                    float area = 0f;
                    for (int k = 0; k < pts.Count; k++) { var a = pts[k]; var b = pts[(k + 1) % pts.Count]; area += a.x * b.y - b.x * a.y; }
                    if (pts.Count < 3 || Mathf.Abs(area) < 1e-4f) continue;
                    mb.AddSlab(m, pts, thickness, thickness * 0.3f, (i & 1) == 0 ? Gold : Rubber);
                }
            });
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

        static Mesh SaveMesh(MeshBuilder mb, string name) => SaveMeshAsset(mb.Build(name), name);

        /// <summary>
        /// Loads a Blender-exported FBX (Tools/Blender, see AGENTS.md) and converts it to the MeshBuilder vertex layout:
        /// colour = base colour, UV map "surf" = (gloss, glow), UV map "detail" = (metal, baked AO). Returns null if missing.
        /// </summary>
        static Mesh ImportedMesh(string fbxPath, string meshName)
        {
            var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null) return null;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || imp.importAnimation || imp.importCameras || imp.importLights)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
                imp.SaveAndReimport();
            }
            Mesh src = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is Mesh m) { src = m; break; }
            if (src == null) return null;
            var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); src.GetUVs(1, uv1);
            int n = src.vertexCount;
            var surface = new List<Vector4>(n);
            var detail = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = i < uv0.Count ? uv0[i] : new Vector2(MeshBuilder.DefaultGloss, 0f);
                Vector2 b = i < uv1.Count ? uv1[i] : new Vector2(0f, 1f);
                surface.Add(new Vector4(a.x, a.y, b.x, 0f));
                detail.Add(new Vector2(0f, b.y));
            }
            var mesh = new Mesh { name = meshName, indexFormat = src.indexFormat };
            mesh.SetVertices(src.vertices);
            mesh.SetNormals(src.normals);
            mesh.SetColors(src.colors);
            mesh.SetUVs(0, surface);
            mesh.SetUVs(1, detail);
            mesh.SetTriangles(src.triangles, 0);
            mesh.RecalculateBounds();
            return SaveMeshAsset(mesh, meshName);
        }

        static Mesh Prefer(Mesh imported, System.Func<Mesh> fallback) => imported != null ? imported : fallback();

        static Mesh SaveMeshAsset(Mesh m, string name)
        {
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

        /// <summary>
        /// Crew identity strips (#60), white glow tinted per player by CrewLights: helmet crest (clear of the
        /// headlamp), a ring on each pauldron and a light bar between the O2 tanks. Fits AstronautMesh.
        /// </summary>
        static Mesh CrewStripMesh()
        {
            var mb = Hero(0.2f);
            var crest = new List<Vector3>();
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(12f, 118f, i / 8f) * Mathf.Deg2Rad;
                crest.Add(new Vector3(0f, 1.62f + Mathf.Cos(a) * 0.352f, -0.01f - Mathf.Sin(a) * 0.352f));
            }
            Glow(mb, () => Polyline(mb, crest, 0.022f, 8, Color.white), 1.6f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 sh = new Vector3(s * 0.4f, 1.27f, 0f);
                Glow(mb, () => mb.AddLathe(Matrix4x4.TRS(sh + new Vector3(s * 0.02f, 0.03f, 0f), Quaternion.Euler(0, 0, s * -18f), Vector3.one),
                    new[] { new Vector2(0.163f, -0.04f), new Vector2(0.176f, -0.03f), new Vector2(0.172f, 0.018f), new Vector2(0.155f, 0.03f) }, 20, Color.white), 1.6f);
            }
            Glow(mb, () => mb.AddBox(T(0f, 1.08f, -0.47f), new Vector3(0.035f, 0.26f, 0.025f), Color.white), 1.8f);
            return SaveMesh(mb, "Astronaut_CrewStrips");
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

            ResetCrew();
            var body = Child(go.transform, "Body", Vector3.zero);
            var crewLights = go.AddComponent<CrewLights>();

            var cam = Child(go.transform, "CameraPoint", new Vector3(0, 1.62f, 0));
            var vcam = cam.AddComponent<CinemachineCamera>();
            var lens = vcam.Lens;
            lens.FieldOfView = 75f; lens.NearClipPlane = 0.05f; lens.FarClipPlane = 20000f;
            vcam.Lens = lens;
            vcam.Priority = 30;
            vcam.enabled = false;

            // First-person tool viewmodels (hero detail: they sit in front of the camera all game)
            var tools = new Transform[4];
            // Drill: gold housing, chalk top shell, rubber grip, canister, spinning bit with a glowing emitter ring
            var drill = Hero(0.2f);
            int dBody = drill.VertexCount;
            drill.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-0.2f, 0.05f, 0.06f, 3f), new MeshBuilder.Section(-0.15f, 0.068f, 0.078f, 3.5f),
                new MeshBuilder.Section(0.1f, 0.07f, 0.08f, 3.5f), new MeshBuilder.Section(0.17f, 0.055f, 0.06f, 3f),
            }, 20, Gold, smoothing: 2);
            drill.Paint(dBody, p => p.y > 0.035f, Chalk, Paint, 0f);
            With(drill, 0.15f, 0f, 0f, () => drill.AddBox(TR(0, -0.11f, -0.08f, 12, 0, 0), new Vector3(0.06f, 0.16f, 0.08f), Rubber));
            With(drill, 0.6f, 1f, 0f, () =>
            {
                drill.AddLathe(TR(0.075f, 0.01f, -0.06f, 90, 0, 0), new[] { new Vector2(0f, -0.07f), new Vector2(0.025f, -0.065f), new Vector2(0.028f, 0.06f), new Vector2(0f, 0.065f) }, 12, Gunmetal);
                drill.AddLathe(TR(0, 0, 0.17f, 90, 0, 0), new[] { new Vector2(0.05f, 0f), new Vector2(0.055f, 0.02f), new Vector2(0.045f, 0.04f) }, 16, Gunmetal);
                for (int r = 0; r < 4; r++)
                    drill.AddLathe(TR(0, 0, 0.215f + r * 0.035f, 90, 0, 0), new[] { new Vector2(0.038f - r * 0.008f, 0f), new Vector2(0.042f - r * 0.008f, 0.012f), new Vector2(0.03f - r * 0.008f, 0.03f) }, 10, Slate);
                drill.AddLathe(TR(0, 0, 0.35f, 90, 0, 0), new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.04f) }, 8, Slate);
            });
            Glow(drill, () => drill.AddLathe(TR(0, 0, 0.205f, 90, 0, 0), new[] { new Vector2(0.048f, 0f), new Vector2(0.052f, 0.006f), new Vector2(0.048f, 0.012f) }, 16, Cyan), 1.4f);
            Glow(drill, () => drill.AddBox(T(-0.071f, 0.02f, -0.02f), new Vector3(0.005f, 0.02f, 0.12f), Cyan), 1.1f);

            // Blaster: chalk/blue sidearm with a vented barrel shroud and a glowing charge strip
            var blaster = Hero(0.2f);
            int bBody = blaster.VertexCount;
            blaster.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-0.16f, 0.045f, 0.06f, 4f), new MeshBuilder.Section(-0.12f, 0.055f, 0.068f, 4f),
                new MeshBuilder.Section(0.1f, 0.05f, 0.062f, 4f), new MeshBuilder.Section(0.14f, 0.04f, 0.05f, 3.5f, -0.005f),
            }, 18, Chalk, smoothing: 2);
            blaster.Paint(bBody, p => p.y < -0.02f, Blue);
            With(blaster, 0.15f, 0f, 0f, () => blaster.AddBox(TR(0, -0.1f, -0.07f, 14, 0, 0), new Vector3(0.05f, 0.15f, 0.07f), Rubber));
            With(blaster, 0.55f, 1f, 0f, () =>
            {
                blaster.AddTube(new Vector3(0, 0.01f, 0.12f), new Vector3(0, 0.01f, 0.27f), 0.026f, 0.026f, 12, Gunmetal);
                for (int r = 0; r < 3; r++) blaster.AddLathe(TR(0, 0.01f, 0.16f + r * 0.035f, 90, 0, 0), new[] { new Vector2(0.026f, 0f), new Vector2(0.034f, 0.005f), new Vector2(0.034f, 0.015f), new Vector2(0.026f, 0.02f) }, 12, Slate);
                blaster.AddBox(T(0, 0.07f, -0.06f), new Vector3(0.02f, 0.02f, 0.03f), Gunmetal);
            });
            Glow(blaster, () => blaster.AddLathe(TR(0, 0.01f, 0.272f, 90, 0, 0), new[] { new Vector2(0.026f, 0f), new Vector2(0.018f, 0.004f) }, 12, Cyan), 1.4f);
            Glow(blaster, () => blaster.AddBox(T(0, 0.064f, 0.02f), new Vector3(0.025f, 0.006f, 0.13f), Cyan), 1.1f);

            // Rifle: navy receiver, chalk handguard, scope, stock, glowing coil strips along the barrel
            var rifle = Hero(0.2f);
            int rBody = rifle.VertexCount;
            rifle.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-0.24f, 0.04f, 0.07f, 4f), new MeshBuilder.Section(-0.18f, 0.05f, 0.075f, 4f),
                new MeshBuilder.Section(0.12f, 0.05f, 0.07f, 4f), new MeshBuilder.Section(0.32f, 0.042f, 0.055f, 4f, 0.005f),
                new MeshBuilder.Section(0.36f, 0.035f, 0.045f, 3.5f, 0.005f),
            }, 18, Navy, smoothing: 2);
            rifle.Paint(rBody, p => p.z > 0.1f && p.y > -0.02f, Chalk, Paint, 0f);
            With(rifle, 0.15f, 0f, 0f, () =>
            {
                rifle.AddBox(TR(0, -0.1f, -0.1f, 14, 0, 0), new Vector3(0.05f, 0.15f, 0.07f), Rubber);
                rifle.AddBox(TR(0, -0.03f, -0.33f, -6, 0, 0), new Vector3(0.05f, 0.11f, 0.16f), Rubber);
            });
            With(rifle, 0.55f, 1f, 0f, () =>
            {
                rifle.AddTube(new Vector3(0, 0.0f, 0.35f), new Vector3(0, 0.0f, 0.5f), 0.022f, 0.022f, 12, Gunmetal);
                rifle.AddLathe(TR(0, 0.0f, 0.48f, 90, 0, 0), new[] { new Vector2(0.022f, 0f), new Vector2(0.032f, 0.005f), new Vector2(0.032f, 0.03f), new Vector2(0.022f, 0.035f) }, 12, Slate);
                rifle.AddTube(new Vector3(0, 0.1f, -0.06f), new Vector3(0, 0.1f, 0.14f), 0.028f, 0.028f, 12, Gunmetal);
                rifle.AddBox(T(0, 0.076f, 0.04f), new Vector3(0.02f, 0.03f, 0.1f), Gunmetal);
            });
            Glow(rifle, () => rifle.AddLathe(TR(0, 0.1f, 0.142f, 90, 0, 0), new[] { new Vector2(0.024f, 0f), new Vector2(0f, 0.004f) }, 12, Gold), 0.8f);
            Glow(rifle, () => { rifle.AddBox(T(0.051f, 0f, 0.15f), new Vector3(0.006f, 0.02f, 0.3f), Cyan); rifle.AddBox(T(-0.051f, 0f, 0.15f), new Vector3(0.006f, 0.02f, 0.3f), Cyan); }, 1.1f);

            // Medkit: rounded case with handle, latches and a raised red cross
            var medkit = Hero(0.25f);
            medkit.AddBox(T(0, 0, 0), new Vector3(0.22f, 0.16f, 0.14f), Chalk);
            With(medkit, 0.2f, 0f, 0f, () => medkit.AddBox(T(0, 0, 0), new Vector3(0.226f, 0.03f, 0.146f), Rubber));
            With(medkit, Paint, 0f, 0f, () =>
            {
                medkit.AddBox(T(0, 0.01f, 0.075f), new Vector3(0.11f, 0.034f, 0.012f), Red);
                medkit.AddBox(T(0, 0.01f, 0.075f), new Vector3(0.034f, 0.11f, 0.012f), Red);
            });
            With(medkit, 0.55f, 1f, 0f, () =>
            {
                Polyline(medkit, new[] { new Vector3(-0.05f, 0.08f, 0), new Vector3(-0.05f, 0.11f, 0), new Vector3(0.05f, 0.11f, 0), new Vector3(0.05f, 0.08f, 0) }, 0.01f, 6, Gunmetal);
                medkit.AddBox(T(-0.08f, 0.02f, 0.072f), new Vector3(0.02f, 0.035f, 0.01f), Gunmetal);
                medkit.AddBox(T(0.08f, 0.02f, 0.072f), new Vector3(0.02f, 0.035f, 0.01f), Gunmetal);
            });
            Glow(medkit, () => medkit.AddBox(T(0.085f, -0.055f, 0.072f), new Vector3(0.02f, 0.01f, 0.004f), Green), 1.4f);
            var meshes = new[] { SaveMesh(drill, "Tool_Drill"), SaveMesh(blaster, "Tool_Blaster"), SaveMesh(rifle, "Tool_Rifle"), SaveMesh(medkit, "Tool_Medkit") };
            // body (#95): the animated crew model with hand-held tools; the procedural astronaut without the UAL rig
            var crewBody = CrewBody(body.transform, meshes);
            var helmet = crewBody != null ? crewBody.transform.Find("Helmet") : null;
            if (helmet != null)
            {
                Set(crewLights, "strips", helmet.GetComponent<Renderer>());
                var so = new SerializedObject(crewLights);
                so.FindProperty("colorProperty").stringValue = "_RimColor";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (crewBody == null)
            {
                Visual(body.transform, "Astronaut", AstronautMesh(), MatHull);
                var strips = Visual(body.transform, "CrewStrips", CrewStripMesh(), MatHull);
                Set(crewLights, "strips", strips.GetComponent<MeshRenderer>());
            }
            string[] names = { "Drill", "Blaster", "Rifle", "Medkit" };
            // asset tools (#95) at real size, grip origin in the lower right of the view; procedural ones as before
            var gripAt = new[] { new Vector3(0.2f, -0.23f, 0.36f), new Vector3(0.19f, -0.18f, 0.33f), new Vector3(0.21f, -0.24f, 0.38f) };
            var muzzles = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                bool asset = i < gripAt.Length && HasToolModel(i);
                var t = Child(cam.transform, "Tool_" + names[i], asset ? gripAt[i] : new Vector3(0.26f, -0.27f, 0.62f), asset ? Quaternion.Euler(0f, -2f, 0f) : (Quaternion?)null);
                ToolVisual(t.transform, i, meshes[i], 1f, out _);
                if (!asset) t.transform.localScale = Vector3.one * 0.45f;
                NoShadows(t);
                t.SetActive(false);
                tools[i] = t.transform;
                muzzles[i] = asset ? t.transform.localPosition + t.transform.localRotation * ToolMuzzle[i] : new Vector3(0.26f, -0.25f, 0.78f);
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
            var pso = new SerializedObject(pawn);
            var tm = pso.FindProperty("toolMuzzles");
            tm.arraySize = muzzles.Length;
            for (int i = 0; i < muzzles.Length; i++) tm.GetArrayElementAtIndex(i).vector3Value = muzzles[i];
            pso.ApplyModifiedPropertiesWithoutUndo();
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
            var crew = CrewSeated(a.transform, v.transform);
            Set(seat, "seatedBody", crew != null ? crew : SeatedBody(a.transform));
            return seat;
        }

        static void Dashboard(Transform parent, Vector3 pos, float width)
        {
            var d = Child(parent, "Dashboard", pos, Quaternion.Euler(25f, 0f, 0f));
            var dash = d.AddComponent<ShipDashboard>();
            // Console: gunmetal bezel around a dark glass screen, a sun hood, and a switch row with indicator LEDs.
            var panel = Hero(0.2f);
            With(panel, 0.5f, 0.8f, 0.4f, () => panel.AddBox(T(0, -0.01f, 0.04f), new Vector3(width + 0.1f, 0.44f, 0.06f), Graphite));
            With(panel, 0.95f, 0.1f, 0f, () => panel.AddBox(T(0, 0.01f, 0.012f), new Vector3(width - 0.02f, 0.34f, 0.01f), new Color(0.03f, 0.05f, 0.08f)));
            With(panel, Satin, 0.4f, 0.4f, () => panel.AddBox(TR(0, 0.22f, -0.05f, -30, 0, 0), new Vector3(width + 0.08f, 0.03f, 0.16f), Graphite));
            int keys = Mathf.Max(3, Mathf.RoundToInt(width * 6f));
            for (int k = 0; k < keys; k++)
            {
                float x = -width * 0.42f + k * (width * 0.84f / (keys - 1));
                With(panel, 0.3f, 1f, 0f, () => panel.AddBox(T(x, -0.2f, 0.0f), new Vector3(0.035f, 0.03f, 0.025f), Slate));
                Color led = k % 4 == 0 ? Gold : (k % 4 == 1 ? Cyan : (k % 4 == 2 ? Green : Red));
                Glow(panel, () => panel.AddBox(T(x, -0.175f, 0.0f), new Vector3(0.015f, 0.008f, 0.01f), led), 1.4f);
            }
            Visual(d.transform, "Panel", SaveMesh(panel, "Dashboard_" + width.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), MatHull);
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
            // #96: a soft camera-facing glow in the nozzle (hidden with the plume when the engine is cold)
            var glowMat = GlowMat("VW_Fx_NozzleGlow", "K_Dot.png", new Color(0.55f, 0.9f, 2.2f), 0.2f);
            var quad = GlowQuadMesh();
            for (int i = 0; i < at.Length; i++)
            {
                var g = Visual(parent, "Thruster", mesh, MatPlume);
                g.transform.localPosition = at[i];
                g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (glowMat != null && quad != null)
                {
                    var glow = Visual(g.transform, "NozzleGlow", quad, glowMat);
                    glow.transform.localPosition = new Vector3(0f, 0f, -0.05f);
                    glow.transform.localScale = Vector3.one * 1.7f;
                    var r = glow.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
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
        static Mesh DartHullMesh(string meshName = "Ship_Dart", bool wreck = false)
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

                if (!wreck || s > 0) // the crashed variant lost its left wing
                {
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
                }

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
            if (wreck)
            {
                // scorched, sooty hull: smooth soot gradients plus heat-tinted burn cores, the paint loses its gloss
                mb.Weather(0, p => Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(p.x * 0.55f + 3.1f, p.z * 0.55f + p.y * 0.4f) - 0.3f) * 2.4f), new Color(0.07f, 0.065f, 0.065f));
                mb.Weather(0, p => 0.8f * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(p.x * 1.3f + 9f, p.z * 1.3f) - 0.62f) * 3f), new Color(0.16f, 0.08f, 0.03f));
            }
            return SaveMesh(mb, meshName);
        }

        static GameObject BuildDart()
        {
            var fab = FabShipMesh();
            if (fab != null) return BuildFabDart(fab);
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

        /// <summary>
        /// Dart with the textured Fab hull (#91): 15 m long, 17.8 m forward-swept wings, two engine pods at x = +-3.
        /// The canopy glass is cut out of the hull (Tools/Blender/fab_spaceship.py), so the pilot sits inside a cockpit tub
        /// that follows the hull (bulkheads at z 2.2 / 6.2, floor 1.9 m below the canopy roof) with seat, dashboard and stick.
        /// </summary>
        static GameObject BuildFabDart(Mesh hull)
        {
            var go = ShipRoot("Ship_Dart", ShipClass.Dart, 2400f, 4, 2);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", hull, FabShipMaterial());
            var canopy = FabShipCanopyMesh();
            if (canopy != null)
            {
                Visual(go.transform, "Canopy", canopy, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // the same panes seen from inside the cockpit (back faces): nearly clear, rim + sun glint
                Visual(go.transform, "CanopyInner", canopy, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bool fitted = FabDartInterior(go.transform);

            Box(go, new Vector3(0, -0.5f, 0.45f), new Vector3(3.4f, 2.2f, 14f));     // fuselage
            Box(go, new Vector3(0, -0.45f, -3.3f), new Vector3(8.3f, 3.1f, 8.6f));   // engine pods
            Box(go, new Vector3(0, -1.3f, 0f), new Vector3(17.8f, 0.7f, 6.9f));      // wings

            // Seated eye just under the canopy roof (high enough to see the nose over the front frame); anchor = eye - 1.2.
            // the fitted cockpit (dart_cockpit.py COCKPIT_SHIFT) puts the pilot 0.75 m further forward, mid-canopy
            float seatZ = fitted ? 0.75f : 0f;
            // [F] trigger: hugs the canopy (the interaction ray looks through the ship's own hull colliders), so a player
            // standing on the wing beside the cockpit is outside it and can aim at the glass
            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, -1.2f, 2.88f + seatZ), new Vector3(0, 0f, 2.92f + seatZ), new Vector3(-3.3f, -0.3f, 5.4f),
                fitted ? new Vector3(0, -0.3f, 4.4f) : new Vector3(0, 0f, 3.4f), fitted ? new Vector3(2.6f, 1.9f, 4.4f) : new Vector3(5.1f, 4.3f, 7.9f));
            // the seated crew body's hands on the side stick (right) and throttle (left) (#95)
            if (fitted) SeatGrips(pilot, go.transform.Find("Interior/Stick/GripIK"), go.transform.Find("Interior/Throttle/GripIK"));
            if (!fitted) Dashboard(go.transform, new Vector3(0, -0.68f, 4.05f), 1.2f);
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, 0.4f, -5.1f));
            Box(term, Vector3.zero, new Vector3(4.3f, 4.3f, 4.3f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-1.36f, -0.86f, 6.15f)).transform, Child(go.transform, "MuzzleR", new Vector3(1.36f, -0.86f, 6.15f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleC", new Vector3(0, -1.07f, 7.6f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-3.3f, 0f, 4.9f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-3f, -0.71f, -7.55f), new Vector3(3f, -0.71f, -7.55f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(2.3f, 2.3f, 2.2f);
            ShipLighting(go, new Vector3(0, -2.4f, 0), 16f, new Vector3(-0.5f, -0.45f, 7.1f), new Vector3(0.5f, -0.45f, 7.1f));
            return SavePrefab(go);
        }

        /// <summary>
        /// The Dart's cockpit interior under one "Interior" root (#94): the hull-following tub plus the fittings from
        /// Tools/Blender/dart_cockpit.py (Wasp Interdictor consoles and wall linings, McCloskey pilot chair; stick and
        /// throttle as their own objects with the pivot at the base, for #40: side stick right, throttle left), the own
        /// full-width dashboard with panel seams and light strips, the rear bay behind the seat, the canopy frame (roll
        /// bar, bows, sill rails), and six live CockpitScreens, one job each: flight, radar and nav in
        /// the dashboard's three bays, hull/shield, fuel and drive in the Wasp's monitor bezels below. A LODGroup culls it all once the ship is small on screen.
        /// Returns false (procedural fittings + text dashboard) when the fitting FBX files are missing.
        /// </summary>
        static bool FabDartInterior(Transform ship)
        {
            var root = Child(ship, "Interior", Vector3.zero);
            var tub = FabShipCockpitMesh();
            var interior = InteriorMaterial();
            if (tub != null) Visual(root.transform, "CockpitTub", tub, interior);
            bool fitted = DartFittings(root.transform, true);
            if (!fitted)
            {
                Visual(root.transform, "Cockpit", FabDartCockpitMesh(), interior);
                AddLight(root.transform, "CockpitLight", new Vector3(0, -0.75f, 3.7f), new Color(0.55f, 0.8f, 1f), 0.5f, 2.4f);
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.035f, root.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
            return fitted;
        }

        /// <summary>
        /// The Dart's cockpit fittings (dart_cockpit.py) under `root`, in Dart ship coordinates (pilot eye at 0, 0, 3.67):
        /// console, chair, side stick + throttle with their hand IK goals, the dashboard with its light strips and the six
        /// live screens, cockpit lights. `dartHull` adds the parts shaped to the Dart's own hull (wall linings, rear bay,
        /// canopy frame); other ships (Mule, #97) move `root` so the eye lands on theirs. `walls` = the side consoles beside the seat. False when the FBX files are missing.
        /// </summary>
        static bool DartFittings(Transform rootT, bool dartHull, bool walls = true)
        {
            var root = rootT.gameObject;
            var console = DartCockpitMesh("Console");
            bool fitted = console != null;
            if (fitted)
            {
                var center = WaspMaterial("center");
                Visual(root.transform, "Console", console, center);
                if (walls) Visual(root.transform, "Walls", DartCockpitMesh("Walls"), WaspMaterial("cockpit"));
                Visual(root.transform, "Seat", DartCockpitMesh("Seat"), ChairMaterial());
                var dash = DartCockpitMesh("Dash");
                if (dash != null)
                {
                    Visual(root.transform, "Dash", dash, WaspMaterial("cockpit"));
                    var seams = DartCockpitMesh("DashSeams");
                    if (seams != null) Visual(root.transform, "DashSeams", seams, SeamMaterial());
                    // HDR just above 1 keeps the hue under bloom (3x burnt out to white / pale yellow)
                    Visual(root.transform, "DashGlowCyan", DartCockpitMesh("DashGlowCyan"), GlowMaterial("VW_CockpitGlowCyan", new Color(0.2f, 0.75f, 1f) * 1.8f));
                    Visual(root.transform, "DashGlowAmber", DartCockpitMesh("DashGlowAmber"), GlowMaterial("VW_CockpitGlowAmber", new Color(1f, 0.42f, 0.1f) * 1.3f));
                }
                // rear bay behind the seat, roll bar + canopy bows and sill rails
                var rear = dartHull ? DartCockpitMesh("Rear") : null;
                if (rear != null) Visual(root.transform, "RearBay", rear, WaspMaterial("cockpit"));
                var frame = dartHull ? DartCockpitMesh("Frame") : null;
                if (frame != null) Visual(root.transform, "CanopyFrame", frame, WaspMaterial("cockpit"));
                // pivots printed by dart_cockpit.py
                var stick = Child(root.transform, "Stick", new Vector3(0.44f, -0.737f, 4.0f)).transform;
                var throttle = Child(root.transform, "Throttle", new Vector3(-0.438f, -0.743f, 4.003f)).transform;
                var stickMesh = DartCockpitMesh("Stick");
                var throttleMesh = DartCockpitMesh("Throttle");
                Visual(stick, "Grip", stickMesh, center);
                Visual(throttle, "Lever", throttleMesh, center);
                // hand IK goals for the seated crew body (#95): just under the top of each lever, palm on the grip.
                // forward = the fingers, up = the back of the hand (CrewSeatPose): fingers forward, palms facing inwards
                if (stickMesh != null)
                    Child(stick, "GripIK", GripPoint(stickMesh.bounds, 0.11f, 1f), Quaternion.Euler(0f, 0f, -90f));
                if (throttleMesh != null)
                    Child(throttle, "GripIK", GripPoint(throttleMesh.bounds, 0.06f, -1f), Quaternion.Euler(0f, 0f, 90f));

                // big displays in the dashboard bays (frames printed by dart_cockpit.py), the Wasp monitors below them
                var screens = Child(root.transform, "Screens", Vector3.zero).transform;
                CockpitScreenAt(screens, ScreenKind.Flight, new Vector3(0f, -0.478f, 4.755f), new Vector3(0f, -0.406f, 0.914f), new Vector2(0.40f, 0.22f));
                CockpitScreenAt(screens, ScreenKind.Radar, new Vector3(-0.403f, -0.478f, 4.683f), new Vector3(-0.372f, -0.371f, 0.835f), new Vector2(0.30f, 0.22f));
                CockpitScreenAt(screens, ScreenKind.Nav, new Vector3(0.403f, -0.478f, 4.683f), new Vector3(0.372f, -0.371f, 0.835f), new Vector2(0.30f, 0.22f));
                CockpitScreenAt(screens, ScreenKind.Hull, new Vector3(-0.309f, -0.761f, 4.614f), new Vector3(-0.177f, -0.47f, 0.865f), new Vector2(0.142f, 0.192f));
                CockpitScreenAt(screens, ScreenKind.Fuel, new Vector3(0f, -0.663f, 4.63f), new Vector3(0f, -0.413f, 0.911f), new Vector2(0.197f, 0.088f));
                CockpitScreenAt(screens, ScreenKind.Drive, new Vector3(0.309f, -0.761f, 4.614f), new Vector3(0.177f, -0.47f, 0.865f), new Vector2(0.142f, 0.192f));

                AddLight(root.transform, "CockpitLight", new Vector3(0, -0.3f, 3.85f), new Color(0.75f, 0.85f, 1f), 0.35f, 2.2f);
                AddLight(root.transform, "ScreenGlow", new Vector3(0, -0.45f, 4.4f), new Color(0.35f, 0.85f, 1f), 0.45f, 1.2f);
                AddLight(root.transform, "FootwellGlow", new Vector3(0, -1.4f, 4.6f), new Color(1f, 0.55f, 0.2f), 0.3f, 1.4f);
                if (dartHull) AddLight(root.transform, "RearBayGlow", new Vector3(0, -0.9f, 2.9f), new Color(0.35f, 0.8f, 1f), 0.5f, 1.5f);
            }
            return fitted;
        }

        /// <summary>A live display facing along `look` (the pilot's view direction into the screen), pulled 2 mm out of its bezel.</summary>
        static CockpitScreen CockpitScreenAt(Transform parent, ScreenKind kind, Vector3 pos, Vector3 look, Vector2 size)
        {
            var rot = Quaternion.LookRotation(look.normalized, Vector3.up);
            var go = Child(parent, "Screen" + kind, pos - look.normalized * 0.002f, rot);
            var sc = go.AddComponent<CockpitScreen>();
            sc.Kind = kind;
            sc.Size = size;
            sc.ElementMaterial = ScreenMaterial();
            return sc;
        }

        static Material ScreenMaterial() => Mat("VW_Screen", "VOIDWARD/Screen");

        /// <summary>Unlit HDR glow for light strips and LEDs (bloom picks it up).</summary>
        static Material GlowMaterial(string name, Color hdr)
        {
            var m = Mat(name, "VOIDWARD/Unlit");
            m.SetColor("_BaseColor", hdr);
            return m;
        }

        /// <summary>Procedural fallback fittings inside the Fab Dart's tub: floor plate, pilot seat, dash console, side consoles, stick and throttle.</summary>
        static Mesh FabDartCockpitMesh()
        {
            var mb = Hero(0.04f);
            // floor plate with a grip strip
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(0, -1.32f, 4.1f), new Vector3(1.5f, 0.06f, 3.6f), Graphite));
            With(mb, 0.2f, 0f, 0f, () => mb.AddBox(T(0, -1.285f, 3.9f), new Vector3(0.7f, 0.01f, 1.6f), Rubber));
            // seat: pedestal, cushion, tilted back, headrest, armrests with a gold edge, harness straps
            With(mb, 0.5f, 0.9f, 0.5f, () =>
            {
                mb.AddBox(T(0, -1.18f, 2.98f), new Vector3(0.34f, 0.24f, 0.4f), Gunmetal);
                mb.AddBox(TR(0, -0.62f, 2.6f, -10, 0, 0), new Vector3(0.66f, 0.98f, 0.1f), Gunmetal);
                mb.AddBox(T(-0.34f, -0.86f, 2.98f), new Vector3(0.06f, 0.08f, 0.5f), Gunmetal);
                mb.AddBox(T(0.34f, -0.86f, 2.98f), new Vector3(0.06f, 0.08f, 0.5f), Gunmetal);
            });
            With(mb, 0.25f, 0f, 0f, () =>
            {
                mb.AddBox(T(0, -1.0f, 3.0f), new Vector3(0.58f, 0.14f, 0.56f), Slate);
                mb.AddBox(TR(0, -0.6f, 2.67f, -10, 0, 0), new Vector3(0.56f, 0.86f, 0.08f), Slate);
                mb.AddBox(TR(0, -0.02f, 2.58f, -10, 0, 0), new Vector3(0.34f, 0.24f, 0.12f), Slate);
                mb.AddBox(TR(-0.17f, -0.5f, 2.72f, -10, 0, 0), new Vector3(0.06f, 0.7f, 0.02f), Ochre);
                mb.AddBox(TR(0.17f, -0.5f, 2.72f, -10, 0, 0), new Vector3(0.06f, 0.7f, 0.02f), Ochre);
            });
            With(mb, Paint, 0.3f, 0f, () => { mb.AddBox(T(-0.34f, -0.815f, 2.98f), new Vector3(0.065f, 0.012f, 0.5f), Ochre); mb.AddBox(T(0.34f, -0.815f, 2.98f), new Vector3(0.065f, 0.012f, 0.5f), Ochre); });
            // dash console under the screens (low, so the view over the nose stays open), foot well behind it
            With(mb, 0.45f, 0.7f, 0.8f, () => mb.AddBox(T(0, -1.1f, 4.35f), new Vector3(1.5f, 0.42f, 0.55f), Graphite));
            // side consoles with button rows and glowing strips
            foreach (float sx in new[] { -1f, 1f })
            {
                With(mb, 0.45f, 0.7f, 0.8f, () => mb.AddBox(T(sx * 0.66f, -0.98f, 3.45f), new Vector3(0.28f, 0.5f, 1.1f), Graphite));
                With(mb, 0.9f, 0.1f, 0f, () => mb.AddBox(T(sx * 0.66f, -0.725f, 3.5f), new Vector3(0.22f, 0.01f, 0.8f), new Color(0.03f, 0.05f, 0.08f)));
                for (int i = 0; i < 6; i++)
                {
                    Color led = i % 3 == 0 ? Cyan : (i % 3 == 1 ? Gold : Green);
                    Glow(mb, () => mb.AddBox(T(sx * 0.66f + (i % 2 == 0 ? -0.05f : 0.05f), -0.715f, 3.2f + (i / 2) * 0.22f), new Vector3(0.05f, 0.012f, 0.05f), led), 1.3f);
                }
                Glow(mb, () => mb.AddBox(T(sx * 0.81f, -0.8f, 3.45f), new Vector3(0.01f, 0.02f, 1.0f), Cyan), 0.9f);
            }
            // flight stick (right hand) and throttle (left console)
            With(mb, 0.5f, 0.9f, 0.3f, () =>
            {
                mb.AddTube(new Vector3(0.22f, -0.98f, 3.38f), new Vector3(0.22f, -0.72f, 3.44f), 0.025f, 0.02f, 8, Gunmetal);
                mb.AddBox(T(0.22f, -0.65f, 3.45f), new Vector3(0.07f, 0.14f, 0.08f), Rubber);
                mb.AddBox(TR(-0.62f, -0.66f, 3.55f, 15, 0, 0), new Vector3(0.06f, 0.12f, 0.08f), Rubber);
            });
            Glow(mb, () => mb.AddBox(T(0.22f, -0.575f, 3.47f), new Vector3(0.02f, 0.012f, 0.02f), Red), 1.5f);
            return SaveMesh(mb, "Ship_Dart_Fab_CockpitProps");
        }

        static Mesh MuleCanopyMesh()
        {
            var glass = new MeshBuilder { SmoothAngle = 70f };
            glass.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(0.85f, 0.95f, 0.5f, 4f, 1.3f), new MeshBuilder.Section(2.6f, 1.02f, 0.55f, 4f, 1.3f),
                new MeshBuilder.Section(3.5f, 0.92f, 0.4f, 3.6f, 1.2f), new MeshBuilder.Section(3.95f, 0.72f, 0.16f, 3f, 1.06f),
            }, 28, Color.white, smoothing: 3);
            return SaveMesh(glass, "Ship_Mule_Canopy");
        }

        /// <summary>
        /// Mule: cargo hauler (DESIGN.md). Ochre forward cab under a wide glass canopy, open graphite roll cage carrying two
        /// clamped freight containers, four swivelling engine pods on pylons, hazard stripes on bumpers and clamps. +Z forward.
        /// </summary>
        static Mesh MuleHullMesh()
        {
            var mb = Hero(0.2f);
            var amber = new Color(1f, 0.55f, 0.15f);

            // ---- cab: boxy loft, graphite lower half, glass canopy on top (separate mesh), roof cap behind the glass
            int cab = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(0.3f, 1.3f, 0.95f, 5f), new MeshBuilder.Section(1.2f, 1.4f, 1.02f, 5f),
                new MeshBuilder.Section(3.2f, 1.38f, 1.0f, 5f, -0.05f), new MeshBuilder.Section(4.2f, 1.2f, 0.82f, 4.5f, -0.15f),
                new MeshBuilder.Section(4.62f, 0.9f, 0.55f, 4f, -0.25f),
            }, 32, Ochre, smoothing: 3);
            mb.Paint(cab, p => p.y < -0.5f, Graphite, Satin);
            mb.Paint(cab, p => p.y >= -0.5f && p.y < -0.38f && p.z > 0.6f, Chalk);
            With(mb, Satin, 0.2f, 0.6f, () => mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(0.2f, 1.2f, 0.32f, 4f, 1.15f), new MeshBuilder.Section(0.95f, 1.1f, 0.34f, 4f, 1.18f),
                new MeshBuilder.Section(1.15f, 0.95f, 0.2f, 4f, 1.1f),
            }, 24, Graphite, smoothing: 2));
            With(mb, 0.6f, 0.9f, 0f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                    Polyline(mb, new[] { new Vector3(s * 0.98f, 0.98f, 1.0f), new Vector3(s * 1.04f, 1.7f, 1.2f), new Vector3(s * 1.0f, 1.78f, 2.6f), new Vector3(s * 0.86f, 1.56f, 3.5f), new Vector3(s * 0.7f, 1.02f, 3.9f) }, 0.045f, 8, Graphite);
            });
            // cockpit interior: seat, side consoles
            With(mb, Satin, 0f, 0.2f, () =>
            {
                mb.AddBox(T(0, 1.02f, 2.2f), new Vector3(2.0f, 0.06f, 2.6f), Slate);
                mb.AddBox(TR(0, 1.35f, 1.55f, -10, 0, 0), new Vector3(0.65f, 0.6f, 0.15f), Slate);
                mb.AddBox(TR(0, 1.62f, 1.5f, -10, 0, 0), new Vector3(0.34f, 0.16f, 0.12f), Rubber);
                for (int s = -1; s <= 1; s += 2) mb.AddBox(TR(s * 0.75f, 1.15f, 2.2f, 0, 0, s * -12), new Vector3(0.3f, 0.2f, 0.9f), Slate);
            });
            for (int s = -1; s <= 1; s += 2)
                Glow(mb, () => mb.AddBox(TR(s * 0.72f, 1.26f, 2.25f, 0, 0, s * -12), new Vector3(0.16f, 0.012f, 0.4f), Cyan), 0.9f);

            // ---- front: bumper with hazard stripes, headlamps, grille, side door
            With(mb, Satin, 0.4f, 0.4f, () => mb.AddBox(T(0, -0.68f, 4.55f), new Vector3(2.5f, 0.38f, 0.34f), Graphite));
            Hazard(mb, TR(0, -0.68f, 4.73f, 90, 0, 0), 2.3f, 0.26f, 10);
            With(mb, 0.4f, 1f, 0f, () => { for (int g = 0; g < 4; g++) mb.AddBox(T(0, -0.05f - g * 0.11f, 4.62f), new Vector3(0.9f, 0.04f, 0.06f), Gunmetal); });
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, 0.5f, 0.5f, 0f, () => mb.AddBox(T(s * 0.88f, -0.18f, 4.45f), new Vector3(0.42f, 0.22f, 0.2f), Graphite));
                Glow(mb, () => mb.AddBox(T(s * 0.88f, -0.18f, 4.56f), new Vector3(0.34f, 0.14f, 0.02f), LampWhite), 1.6f);
                Glow(mb, () => mb.AddBox(T(s * 1.2f, -0.18f, 4.36f), new Vector3(0.08f, 0.12f, 0.02f), amber), 1.4f);
            }
            // left side door (crew hatch faces the exit point)
            mb.AddSlab(TR(-1.39f, 0.05f, 2.0f, 0, 0, 90), new[] { new Vector2(-0.75f, -0.5f), new Vector2(0.8f, -0.5f), new Vector2(0.8f, 0.5f), new Vector2(-0.75f, 0.5f) }, 0.05f, 0.015f, Ochre);
            With(mb, 0.4f, 1f, 0f, () => mb.AddBox(T(-1.43f, 0.0f, 2.35f), new Vector3(0.04f, 0.06f, 0.22f), Gunmetal));
            Hazard(mb, TR(-1.4f, -0.68f, 2.0f, 0, 0, 90), 1.0f, 0.12f, 6, 0.02f);

            // ---- chin gun pods on the cab cheeks
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, Satin, 0.6f, 0.5f, () => mb.AddLoft(T(s * 1.5f, -0.85f, 0), new[]
                {
                    new MeshBuilder.Section(1.9f, 0.12f, 0.12f, 3f), new MeshBuilder.Section(2.2f, 0.2f, 0.2f, 3.5f),
                    new MeshBuilder.Section(2.9f, 0.2f, 0.2f, 3.5f), new MeshBuilder.Section(3.05f, 0.13f, 0.13f, 3f),
                }, 16, Graphite, smoothing: 2));
                With(mb, Satin, 0.5f, 0f, () => mb.AddBox(T(s * 1.36f, -0.85f, 2.5f), new Vector3(0.18f, 0.14f, 0.5f), Graphite));
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 1.5f, -0.85f, 3.0f), new Vector3(s * 1.5f, -0.85f, 3.25f), 0.055f, 0.055f, 10, Gunmetal);
                    mb.AddPrism(TR(s * 1.5f, -0.85f, 3.2f, 90, 0, 0), 0.075f, 0.07f, 0.1f, 10, Slate);
                });
            }

            // ---- roof: beacon, antennas, mast turret above the canopy
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0.75f, 1.38f, 0.55f), Quaternion.identity, new Vector3(1f, 0.7f, 1f)), 0.09f, 2, amber), 1.5f);
            With(mb, 0.4f, 1f, 0f, () =>
            {
                mb.AddTube(new Vector3(-0.8f, 1.42f, 0.5f), new Vector3(-0.9f, 2.3f, 0.35f), 0.015f, 0.01f, 6, Gunmetal);
                mb.AddTube(new Vector3(0, 1.45f, 0.55f), new Vector3(0, 1.85f, 0.55f), 0.07f, 0.07f, 10, Gunmetal);
            });
            With(mb, Satin, 0.4f, 0.4f, () => mb.AddLathe(T(0, 1.85f, 0.55f), new[] { new Vector2(0.26f, 0f), new Vector2(0.28f, 0.08f), new Vector2(0.22f, 0.22f), new Vector2(0f, 0.27f) }, 18, Graphite));
            With(mb, 0.55f, 1f, 0f, () =>
            {
                mb.AddTube(new Vector3(-0.08f, 2.0f, 0.65f), new Vector3(-0.08f, 2.0f, 1.45f), 0.04f, 0.04f, 8, Gunmetal);
                mb.AddTube(new Vector3(0.08f, 2.0f, 0.65f), new Vector3(0.08f, 2.0f, 1.45f), 0.04f, 0.04f, 8, Gunmetal);
            });

            // ---- chassis: keel beam, landing skids
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(0, -1.2f, -2.2f), new Vector3(1.0f, 0.4f, 6.4f), Graphite));
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, 0.5f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 1.0f, -1.38f, 3.6f), new Vector3(s * 1.0f, -1.38f, -4.8f), 0.07f, 0.07f, 10, Gunmetal);
                    mb.AddTube(new Vector3(s * 1.0f, -1.38f, 3.6f), new Vector3(s * 1.0f, -1.2f, 4.1f), 0.07f, 0.07f, 10, Gunmetal);
                    foreach (float z in new[] { 2.6f, -0.6f, -3.8f }) mb.AddTube(new Vector3(s * 0.75f, -0.95f, z), new Vector3(s * 1.0f, -1.38f, z), 0.05f, 0.05f, 8, Gunmetal);
                });
            }

            // ---- roll cage: four rails, three ring frames, side braces
            var rails = new[] { new Vector2(-1.55f, -1.05f), new Vector2(1.55f, -1.05f), new Vector2(1.55f, 1.2f), new Vector2(-1.55f, 1.2f) };
            With(mb, 0.45f, 0.7f, 0.2f, () =>
            {
                foreach (var r in rails) mb.AddTube(new Vector3(r.x, r.y, 0.45f), new Vector3(r.x, r.y, -5.3f), 0.09f, 0.09f, 10, Graphite);
                foreach (float z in new[] { 0.35f, -2.38f, -5.2f })
                    for (int k = 0; k < 4; k++)
                    {
                        var a = rails[k]; var b = rails[(k + 1) % 4];
                        mb.AddTube(new Vector3(a.x, a.y, z), new Vector3(b.x, b.y, z), 0.08f, 0.08f, 10, Graphite);
                    }
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.AddTube(new Vector3(s * 1.55f, -1.05f, 0.35f), new Vector3(s * 1.55f, 1.2f, -2.38f), 0.05f, 0.05f, 8, Graphite);
                    mb.AddTube(new Vector3(s * 1.55f, 1.2f, -2.38f), new Vector3(s * 1.55f, -1.05f, -5.2f), 0.05f, 0.05f, 8, Graphite);
                }
            });
            Hazard(mb, TR(0, -1.05f, -5.32f, 90, 0, 0), 3.0f, 0.18f, 12, 0.02f);

            // ---- freight containers: corrugated sides, corner castings, magnetic clamps
            (float z, Color c)[] boxes = { (-1.0f, Blue), (-3.72f, Chalk) };
            foreach (var (z, c) in boxes)
            {
                mb.Bevel = 0.06f;
                mb.AddBox(T(0, 0.08f, z), new Vector3(2.6f, 2.0f, 2.4f), c);
                With(mb, 0.4f, 0f, 0.3f, () =>
                {
                    for (int s = -1; s <= 1; s += 2)
                        for (int k = 0; k < 7; k++) mb.AddBox(T(s * 1.31f, 0.08f, z - 1.02f + k * 0.34f), new Vector3(0.05f, 1.8f, 0.14f), c);
                });
                With(mb, 0.45f, 1f, 0f, () =>
                {
                    foreach (var cx in new[] { -1.22f, 1.22f })
                        foreach (var cy in new[] { -0.84f, 1.0f })
                            foreach (var cz in new[] { z - 1.12f, z + 1.12f }) mb.AddBox(T(cx, cy, cz), new Vector3(0.2f, 0.2f, 0.2f), Gunmetal);
                });
                mb.Bevel = 0.2f;
                With(mb, 0.4f, 0.6f, 0.4f, () => mb.AddBox(T(0, 1.22f, z), new Vector3(0.9f, 0.22f, 0.7f), Graphite));
                Hazard(mb, T(0, 1.34f, z), 0.85f, 0.5f, 5, 0.02f);
                Glow(mb, () => mb.AddBox(T(0.48f, 1.22f, z), new Vector3(0.02f, 0.06f, 0.3f), Cyan), 1.1f);
            }

            // ---- engine pods: pylon, nacelle, bell with glowing throat, intake ring
            for (int s = -1; s <= 1; s += 2)
                for (int v = -1; v <= 1; v += 2)
                {
                    float px = s * 2.45f, py = v * 0.72f;
                    With(mb, Satin, 0.4f, 0.5f, () => mb.AddSlab(TR(s * 2.0f, py, -4.4f, 0, 0, 0),
                        new[] { new Vector2(-0.5f, -0.6f), new Vector2(0.5f, -0.75f), new Vector2(0.5f, 0.15f), new Vector2(-0.5f, 0.35f) }, 0.14f, 0.03f, Graphite));
                    int pod = mb.VertexCount;
                    mb.AddLoft(T(px, py, 0), new[]
                    {
                        new MeshBuilder.Section(-5.55f, 0.36f, 0.36f, 2.2f), new MeshBuilder.Section(-5.2f, 0.44f, 0.44f, 2.3f),
                        new MeshBuilder.Section(-3.6f, 0.44f, 0.44f, 2.3f), new MeshBuilder.Section(-3.25f, 0.36f, 0.36f, 2.2f),
                    }, 20, Ochre, smoothing: 2);
                    mb.Paint(pod, p => p.z < -4.95f, Graphite, Satin);
                    With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(Matrix4x4.TRS(new Vector3(px, py, -3.25f), Quaternion.FromToRotation(Vector3.up, Vector3.forward), Vector3.one),
                        new[] { new Vector2(0.36f, -0.02f), new Vector2(0.38f, 0.05f), new Vector2(0.3f, 0.08f), new Vector2(0.24f, 0.02f), new Vector2(0.1f, -0.06f), new Vector2(0f, -0.07f) }, 20, Gunmetal));
                    var bell = Matrix4x4.TRS(new Vector3(px, py, -5.55f), Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one);
                    With(mb, 0.6f, 1f, 0.2f, () => mb.AddLathe(bell, new[]
                    {
                        new Vector2(0.33f, -0.02f), new Vector2(0.38f, 0.15f), new Vector2(0.45f, 0.42f), new Vector2(0.44f, 0.46f),
                        new Vector2(0.4f, 0.42f), new Vector2(0.31f, 0.18f), new Vector2(0.24f, 0.06f),
                    }, 20, Gunmetal));
                    Glow(mb, () => mb.AddLathe(bell, new[] { new Vector2(0.24f, 0.06f), new Vector2(0.15f, 0.04f) }, 20, Cyan), 1.3f);
                    With(mb, 0.65f, 1f, 0f, () => mb.AddLathe(bell, new[] { new Vector2(0.15f, 0f), new Vector2(0.12f, 0.1f), new Vector2(0f, 0.17f) }, 14, Slate));
                }
            return SaveMesh(mb, "Ship_Mule");
        }

        static GameObject BuildMule()
        {
            var r35 = MuleR35Mesh();
            if (r35 != null) return BuildR35Mule(r35);
            var go = ShipRoot("Ship_Mule", ShipClass.Mule, 3500f, 6, 3);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", MuleHullMesh(), MatHull);
            Visual(go.transform, "Canopy", MuleCanopyMesh(), MatGlass);
            Box(go, new Vector3(0, 0, -0.6f), new Vector3(6.4f, 2.8f, 10.4f));

            Seat(go.transform, "PilotSeat", StationRole.Pilot, new Vector3(0, 0.4f, 2.2f), new Vector3(0, 1.45f, 2.0f), new Vector3(-4.2f, 0.4f, 1.5f),
                new Vector3(0, 0.6f, 1.6f), new Vector3(4f, 3f, 5.5f));
            Dashboard(go.transform, new Vector3(0, 1.2f, 3.0f), 1.6f);
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, 0, -4.8f));
            Box(term, Vector3.zero, new Vector3(3.6f, 2.6f, 2f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-1.5f, -0.85f, 3.3f)).transform, Child(go.transform, "MuzzleR", new Vector3(1.5f, -0.85f, 3.3f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleTop", new Vector3(0, 2.0f, 1.5f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-4.2f, 0.5f, 0f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-2.45f, 0.72f, -6.0f), new Vector3(-2.45f, -0.72f, -6.0f), new Vector3(2.45f, 0.72f, -6.0f), new Vector3(2.45f, -0.72f, -6.0f));
            foreach (var t in fx.Thrusters) t.localScale = Vector3.one * 1.1f;
            ShipLighting(go, new Vector3(0, -2f, -1f), 12f, new Vector3(-0.88f, -0.18f, 4.7f), new Vector3(0.88f, -0.18f, 4.7f));
            return SavePrefab(go);
        }

        /// <summary>Seated eye of the Mule's pilot (under the R35 canopy, r35_dropship.py EYE).</summary>
        static readonly Vector3 MuleEye = new Vector3(0f, 0.55f, 3.3f);

        /// <summary>
        /// Mule with the textured R35 hull (#97): 15 m long, 11.4 m across the engine pods, rear ramp. The upper nose is
        /// cut out as a canopy (Tools/Blender/r35_dropship.py), the pilot sits under it in a tub that follows the nose,
        /// on a raised deck with the Dart's cockpit fittings (chair, console, side stick + throttle, dashboard, live screens).
        /// Four engine nozzles on the pods, chin laser under the nose, cargo terminal at the ramp.
        /// </summary>
        static GameObject BuildR35Mule(Mesh hull)
        {
            var go = ShipRoot("Ship_Mule", ShipClass.Mule, 3500f, 6, 3);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", hull, MuleR35Material());
            var canopy = MuleR35CanopyMesh();
            if (canopy != null)
            {
                Visual(go.transform, "Canopy", canopy, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Visual(go.transform, "CanopyInner", canopy, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var interior = Child(go.transform, "Interior", Vector3.zero);
            var tub = MuleR35CockpitMesh();
            if (tub != null)
            {
                // the convex tub pokes through the concave hull in places: inward faces only, so no shadows from outside
                var t = Visual(interior.transform, "CockpitTub", tub, InteriorMaterial());
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Visual(interior.transform, "Deck", MuleDeckMesh(), InteriorMaterial());
            // the Dart's fittings, moved so the Dart's eye (0, 0, 3.67) lands on the Mule's
            var fit = Child(interior.transform, "Fittings", MuleEye - new Vector3(0f, 0f, 3.67f));
            bool fitted = DartFittings(fit.transform, false);
            if (!fitted) Dashboard(interior.transform, MuleEye + new Vector3(0f, -0.55f, 0.85f), 1.0f);
            var lod = interior.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.035f, interior.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();

            Box(go, new Vector3(0, -0.25f, -2.65f), new Vector3(6.8f, 4.1f, 9.7f));    // body
            Box(go, new Vector3(0, -0.55f, 4.55f), new Vector3(3.0f, 3.5f, 4.7f));     // nose
            Box(go, new Vector3(-4.55f, 0.81f, -1.6f), new Vector3(2.3f, 3.4f, 6.5f)); // engine pods
            Box(go, new Vector3(4.55f, 0.81f, -1.6f), new Vector3(2.3f, 3.4f, 6.5f));
            Box(go, new Vector3(-2.55f, -1.75f, -0.6f), new Vector3(1.7f, 1.7f, 2.4f)); // landing gear
            Box(go, new Vector3(2.55f, -1.75f, -0.6f), new Vector3(1.7f, 1.7f, 2.4f));

            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, MuleEye + new Vector3(0f, -1.2f, -0.04f), MuleEye, new Vector3(-2.9f, -1.3f, 4.2f),
                new Vector3(0f, 0.55f, 4.0f), new Vector3(3.2f, 2.2f, 4.2f));
            if (fitted) SeatGrips(pilot, fit.transform.Find("Stick/GripIK"), fit.transform.Find("Throttle/GripIK"));
            // cargo terminal at the rear ramp
            var term = Child(go.transform, "CargoTerminal", new Vector3(0, -1.2f, -7.4f));
            Box(term, Vector3.zero, new Vector3(3.4f, 3f, 2.6f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-0.3f, -1.93f, 7.55f)).transform, Child(go.transform, "MuzzleR", new Vector3(0.3f, -1.93f, 7.55f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleTop", new Vector3(0, 2.0f, -1.0f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-7.4f, -1.5f, 2.5f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-5.2f, 0.94f, -4.6f), new Vector3(-3.88f, 0.94f, -4.6f), new Vector3(3.88f, 0.94f, -4.6f), new Vector3(5.2f, 0.94f, -4.6f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(0.8f, 0.8f, 1.6f);
            ShipLighting(go, new Vector3(0, -2.9f, 0), 14f, new Vector3(-0.55f, -0.9f, 6.2f), new Vector3(0.55f, -0.9f, 6.2f));
            return SavePrefab(go);
        }

        /// <summary>Raised deck in the Mule's cockpit tub: the floor under the Dart chair (eye - 1.36) with a grip strip (narrow: the nose tapers).</summary>
        static Mesh MuleDeckMesh()
        {
            var mb = Hero(0.04f);
            float y = MuleEye.y - 1.36f;
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(0, y - 0.03f, MuleEye.z + 0.1f), new Vector3(1.7f, 0.06f, 2.4f), Graphite));
            With(mb, 0.2f, 0f, 0f, () => mb.AddBox(T(0, y + 0.005f, MuleEye.z + 0.3f), new Vector3(0.7f, 0.01f, 1.6f), Rubber));
            return SaveMesh(mb, "Ship_Mule_R35_Deck");
        }

        /// <summary>Superellipse outline (half extents, power) clipped to an axis-aligned rectangle: a convex bulkhead piece.</summary>
        static List<Vector2> SuperellipsePiece(float hw, float hh, float power, float cy, Rect clip, int samples = 64)
        {
            var poly = new List<Vector2>();
            float e = 2f / power;
            for (int i = 0; i < samples; i++)
            {
                float a = i / (float)samples * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                poly.Add(new Vector2(Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), e) * hw, cy + Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), e) * hh));
            }
            // Sutherland-Hodgman against the four rectangle edges.
            List<Vector2> ClipEdge(List<Vector2> input, System.Func<Vector2, bool> inside, System.Func<Vector2, Vector2, Vector2> cross)
            {
                var output = new List<Vector2>();
                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 cur = input[i], prev = input[(i + input.Count - 1) % input.Count];
                    bool ci = inside(cur), pi = inside(prev);
                    if (ci) { if (!pi) output.Add(cross(prev, cur)); output.Add(cur); }
                    else if (pi) output.Add(cross(prev, cur));
                }
                return output;
            }
            Vector2 AtX(Vector2 a, Vector2 b, float x) => Vector2.Lerp(a, b, (x - a.x) / (b.x - a.x));
            Vector2 AtY(Vector2 a, Vector2 b, float y) => Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y));
            poly = ClipEdge(poly, p => p.x >= clip.xMin, (a, b) => AtX(a, b, clip.xMin));
            poly = ClipEdge(poly, p => p.x <= clip.xMax, (a, b) => AtX(a, b, clip.xMax));
            poly = ClipEdge(poly, p => p.y >= clip.yMin, (a, b) => AtY(a, b, clip.yMin));
            poly = ClipEdge(poly, p => p.y <= clip.yMax, (a, b) => AtY(a, b, clip.yMax));
            var clean = new List<Vector2>();
            foreach (var p in poly) if (clean.Count == 0 || (clean[clean.Count - 1] - p).sqrMagnitude > 1e-6f) clean.Add(p);
            if (clean.Count > 1 && (clean[0] - clean[clean.Count - 1]).sqrMagnitude < 1e-6f) clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        /// <summary>Flat bulkhead in the XY plane at depth z: superellipse hull section minus a rectangular opening.</summary>
        static void Bulkhead(MeshBuilder mb, float z, float hw, float hh, float power, float cy, Rect hole, float thickness, Color c)
        {
            float big = 100f;
            var pieces = new[]
            {
                new Rect(-big, -big, big + hole.xMin, big * 2f),                 // left of the hole
                new Rect(hole.xMax, -big, big, big * 2f),                         // right
                new Rect(hole.xMin, hole.yMax, hole.width, big),                  // above
                new Rect(hole.xMin, -big, hole.width, big + hole.yMin),           // below
            };
            foreach (var r in pieces)
            {
                var outline = SuperellipsePiece(hw, hh, power, cy, r);
                if (outline.Count < 3) continue;
                // slab outline lives in local XZ; Euler(90,0,0) maps local z to -y, so feed (x, -y)
                var o2 = new List<Vector2>();
                foreach (var p in outline) o2.Add(new Vector2(p.x, -p.y));
                mb.AddSlab(TR(0, 0, z, 90, 0, 0), o2, thickness, Mathf.Min(0.05f, thickness * 0.3f), c);
            }
        }

        static readonly Color ArkNavy = new Color(0.12f, 0.16f, 0.23f);

        /// <summary>
        /// Ark exterior (DESIGN.md): navy catamaran command ship. Lofted pressure hull around the walkable cabin, off-white
        /// dorsal armour slabs with cyan conduits, wide observation window under a brow, lower nose with chin cannons,
        /// twin outrigger pylons carrying big engine nacelles. Rear bulkhead keeps the airlock opening free.
        /// </summary>
        static void ArkExterior(MeshBuilder mb)
        {
            var amber = new Color(1f, 0.55f, 0.15f);
            // ---- pressure hull (open at both ends; bulkheads close it around the window and the airlock)
            int shell = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-13.55f, 5.0f, 3.0f, 4.5f, 1.5f), new MeshBuilder.Section(-11f, 5.4f, 3.25f, 4.5f, 1.6f),
                new MeshBuilder.Section(0f, 5.6f, 3.35f, 4.5f, 1.6f), new MeshBuilder.Section(11f, 5.4f, 3.25f, 4.5f, 1.6f),
                new MeshBuilder.Section(13.75f, 5.2f, 3.15f, 4.5f, 1.6f),
            }, 40, ArkNavy, false, false, 3);
            mb.Paint(shell, p => p.y < -0.9f, Slate, Satin);
            mb.Paint(shell, p => p.y > 0.6f && p.y < 1.05f && Mathf.Abs(p.x) > 4.5f, Chalk);
            Bulkhead(mb, -13.62f, 5.0f, 3.0f, 4.5f, 1.5f, new Rect(-1.1f, -0.05f, 2.2f, 3.1f), 0.24f, ArkNavy);
            Bulkhead(mb, 13.82f, 5.2f, 3.15f, 4.5f, 1.6f, new Rect(-3.05f, 1.75f, 6.1f, 1.7f), 0.24f, ArkNavy);
            // airlock frame + hazard threshold
            With(mb, 0.7f, 0.85f, 0f, () =>
            {
                mb.AddBox(T(-1.18f, 1.5f, -13.75f), new Vector3(0.16f, 3.1f, 0.2f), Gold);
                mb.AddBox(T(1.18f, 1.5f, -13.75f), new Vector3(0.16f, 3.1f, 0.2f), Gold);
                mb.AddBox(T(0, 3.1f, -13.75f), new Vector3(2.52f, 0.16f, 0.2f), Gold);
            });
            Hazard(mb, T(0, 0.01f, -13.0f), 2.2f, 0.5f, 8, 0.02f);
            for (int s = -1; s <= 1; s += 2) Glow(mb, () => mb.AddBox(T(s * 1.4f, 2.6f, -13.78f), new Vector3(0.12f, 0.3f, 0.06f), amber), 1.5f);

            // ---- dorsal armour slabs with cyan conduits between them
            float[] plates = { -10.8f, -6.4f, -2.0f, 2.4f, 6.8f, 10.6f };
            for (int i = 0; i < plates.Length; i++)
            {
                float z = plates[i], len = i == plates.Length - 1 ? 3.2f : 4.0f;
                With(mb, Paint, 0f, 1f, () => mb.AddSlab(T(0, 4.98f, z), new[]
                {
                    new Vector2(-2.6f, -len * 0.5f), new Vector2(2.6f, -len * 0.5f), new Vector2(2.9f, -len * 0.5f + 0.4f),
                    new Vector2(2.9f, len * 0.5f - 0.4f), new Vector2(2.6f, len * 0.5f), new Vector2(-2.6f, len * 0.5f),
                    new Vector2(-2.9f, len * 0.5f - 0.4f), new Vector2(-2.9f, -len * 0.5f + 0.4f),
                }, 0.22f, 0.05f, Chalk));
                if (i + 1 < plates.Length) Glow(mb, () => mb.AddBox(T(0, 4.97f, z + len * 0.5f + 0.2f), new Vector3(4.6f, 0.06f, 0.08f), Cyan), 1.0f);
            }
            for (int s = -1; s <= 1; s += 2) Glow(mb, () => mb.AddBox(T(s * 3.1f, 4.86f, -0.2f), new Vector3(0.07f, 0.06f, 23f), Cyan), 0.9f);
            // missile launcher (MuzzleMissile at (0, 5.8, 4))
            With(mb, Satin, 0.5f, 0.5f, () => mb.AddBox(T(0, 5.45f, 3.4f), new Vector3(1.6f, 0.75f, 1.6f), Graphite));
            With(mb, 0.4f, 1f, 0f, () => { for (int k = 0; k < 4; k++) mb.AddPrism(TR(-0.38f + (k % 2) * 0.76f, 5.3f + (k / 2) * 0.32f, 4.22f, 90, 0, 0), 0.12f, 0.12f, 0.06f, 10, Rubber); });
            // comm dish + sensor mast on the aft deck
            With(mb, 0.5f, 0.6f, 0f, () =>
            {
                mb.AddTube(new Vector3(-1.6f, 5.1f, -11f), new Vector3(-1.6f, 6.0f, -11f), 0.08f, 0.08f, 10, Gunmetal);
                mb.AddLathe(Matrix4x4.TRS(new Vector3(-1.6f, 6.1f, -11f), Quaternion.Euler(-35f, 0f, 0f), Vector3.one),
                    new[] { new Vector2(0f, 0f), new Vector2(0.4f, 0.05f), new Vector2(0.75f, 0.2f), new Vector2(0.72f, 0.24f), new Vector2(0.38f, 0.1f), new Vector2(0f, 0.05f) }, 20, Chalk);
                mb.AddTube(new Vector3(1.8f, 5.1f, -12f), new Vector3(1.8f, 7.2f, -12f), 0.04f, 0.025f, 8, Gunmetal);
            });
            Glow(mb, () => mb.AddSphere(T(1.8f, 7.25f, -12f), 0.06f, 1, Red), 1.6f);

            // ---- observation window brow, lower nose with chin cannons and headlamps
            With(mb, Satin, 0.3f, 0.6f, () => mb.AddSlab(T(0, 3.6f, 14.25f), new[]
            {
                new Vector2(-3.5f, -0.5f), new Vector2(3.5f, -0.5f), new Vector2(3.2f, 0.5f), new Vector2(-3.2f, 0.5f),
            }, 0.24f, 0.06f, Graphite));
            for (int m = -1; m <= 1; m += 2)
                With(mb, 0.55f, 0.9f, 0f, () => mb.AddBox(TR(m * 1.02f, 2.6f, 13.98f, 0, 0, 0), new Vector3(0.14f, 1.75f, 0.14f), Graphite));
            int nose = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(13.7f, 5.2f, 1.75f, 4f, -0.05f), new MeshBuilder.Section(16f, 4.6f, 1.45f, 3.8f, -0.05f),
                new MeshBuilder.Section(18.4f, 3.3f, 1.05f, 3.2f, -0.2f), new MeshBuilder.Section(19.6f, 1.6f, 0.5f, 2.6f, -0.35f),
                new MeshBuilder.Section(19.9f, 0f, 0f, 2f, -0.35f),
            }, 36, ArkNavy, false, true, 3);
            mb.Paint(nose, p => p.y > 0.95f, Chalk);
            mb.Paint(nose, p => p.y < -0.9f, Slate, Satin);
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, Satin, 0.6f, 0.5f, () => mb.AddLoft(T(s * 2.5f, -1.3f, 0), new[]
                {
                    new MeshBuilder.Section(14.2f, 0.25f, 0.2f, 3f), new MeshBuilder.Section(14.8f, 0.42f, 0.34f, 3.5f),
                    new MeshBuilder.Section(16.4f, 0.4f, 0.32f, 3.5f), new MeshBuilder.Section(16.8f, 0.26f, 0.22f, 3f),
                }, 16, Graphite, smoothing: 2));
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 2.5f, -1.3f, 16.6f), new Vector3(s * 2.5f, -1.3f, 17.3f), 0.1f, 0.1f, 12, Gunmetal);
                    for (int r = 0; r < 3; r++) mb.AddPrism(TR(s * 2.5f, -1.3f, 16.75f + r * 0.16f, 90, 0, 0), 0.14f, 0.14f, 0.06f, 12, Slate);
                });
                Glow(mb, () => mb.AddPrism(TR(s * 2.5f, -1.3f, 17.32f, 90, 0, 0), 0.06f, 0.06f, 0.02f, 12, Cyan), 1.2f);
                With(mb, 0.5f, 0.5f, 0f, () => mb.AddBox(T(s * 2.0f, -0.55f, 18.55f), new Vector3(0.7f, 0.3f, 0.3f), Graphite));
                Glow(mb, () => mb.AddBox(T(s * 2.0f, -0.55f, 18.71f), new Vector3(0.56f, 0.18f, 0.02f), LampWhite), 1.6f);
                // port windows along the hull sides
                for (int w = 0; w < 6; w++)
                    With(mb, 1f, 0.2f, 0f, () => mb.AddBox(T(s * 5.62f, 2.6f, -9.5f + w * 3.6f), new Vector3(0.08f, 0.55f, 1.1f), new Color(0.15f, 0.35f, 0.55f)), 0.25f);
            }

            // ---- command bridge tower on the forward deck with a window band
            int tower = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(7.6f, 2.0f, 0.15f, 3.5f, 5.0f), new MeshBuilder.Section(8.4f, 2.5f, 0.62f, 3.5f, 5.45f),
                new MeshBuilder.Section(11.4f, 2.4f, 0.62f, 3.5f, 5.45f), new MeshBuilder.Section(12.5f, 1.7f, 0.4f, 3.2f, 5.3f),
                new MeshBuilder.Section(12.8f, 1.2f, 0.2f, 3f, 5.25f),
            }, 28, Chalk, smoothing: 3);
            mb.Paint(tower, p => p.y > 5.42f && p.y < 5.66f && (Mathf.Abs(p.x) > 1.6f || p.z > 11.5f), new Color(0.12f, 0.3f, 0.45f), 1f, 0.2f);
            Glow(mb, () => mb.AddBox(T(0, 6.1f, 9.6f), new Vector3(0.9f, 0.06f, 2.2f), Cyan), 0.8f);

            // ---- side chines: a sloped skirt that turns the box section into a ship hull
            for (int s = -1; s <= 1; s += 2)
            {
                var M = Matrix4x4.Scale(new Vector3(s, 1f, 1f));
                mb.AddSlab(M * TR(0, -0.35f, 0, 0, 0, -8), new[] { new Vector2(4.9f, 13.4f), new Vector2(6.6f, 10.8f), new Vector2(6.6f, -11.2f), new Vector2(4.9f, -13.4f) },
                    0.4f, 0.06f, ArkNavy, new[] { 0.45f, 0.22f, 0.22f, 0.45f });
                mb.AddSlab(M * TR(0, -0.33f, 0, 0, 0, -8), new[] { new Vector2(6.05f, 11.4f), new Vector2(6.66f, 10.75f), new Vector2(6.66f, -11.15f), new Vector2(6.05f, -11.6f) },
                    0.4f, 0.03f, Chalk, new[] { 0.3f, 0.26f, 0.26f, 0.3f });
                Glow(mb, () => mb.AddBox(M * TR(6.55f, -0.62f, 0, 0, 0, -8), new Vector3(0.04f, 0.04f, 20f), Cyan), 0.9f);
            }

            // ---- outrigger pylons + engine nacelles
            for (int s = -1; s <= 1; s += 2)
            {
                var M = Matrix4x4.Scale(new Vector3(s, 1f, 1f));
                mb.AddSlab(M * T(0, 0.6f, 0), new[] { new Vector2(5.2f, -3.2f), new Vector2(9.0f, -6.0f), new Vector2(9.0f, -12.5f), new Vector2(5.2f, -12.0f) },
                    0.7f, 0.08f, ArkNavy, new[] { 0.8f, 0.5f, 0.5f, 0.8f });
                mb.AddSlab(M * T(0, 0.62f, 0), new[] { new Vector2(5.4f, -3.6f), new Vector2(9.0f, -6.3f), new Vector2(9.0f, -8.0f), new Vector2(5.4f, -6.0f) },
                    0.7f, 0.04f, Chalk, new[] { 0.86f, 0.56f, 0.56f, 0.86f });
                int nac = mb.VertexCount;
                mb.AddLoft(M * T(10f, 0.6f, 0), new[]
                {
                    new MeshBuilder.Section(-16.8f, 1.45f, 1.45f, 2.3f), new MeshBuilder.Section(-15.6f, 1.7f, 1.7f, 2.4f),
                    new MeshBuilder.Section(-5.2f, 1.7f, 1.7f, 2.4f), new MeshBuilder.Section(-3.6f, 1.35f, 1.35f, 2.2f),
                }, 32, Chalk, false, false, 3);
                mb.Paint(nac, p => p.z < -15.2f || p.z > -4.6f, Gunmetal, 0.6f, 1f);
                mb.Paint(nac, p => p.z > -11.6f && p.z < -10.6f, ArkNavy);
                var intake = M * Matrix4x4.TRS(new Vector3(10f, 0.6f, -3.6f), Quaternion.FromToRotation(Vector3.up, Vector3.forward), Vector3.one);
                With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(intake, new[] { new Vector2(1.35f, -0.02f), new Vector2(1.42f, 0.1f), new Vector2(1.2f, 0.2f), new Vector2(1.05f, 0.05f) }, 32, Gunmetal));
                With(mb, 0.5f, 1f, 0f, () => mb.AddLathe(intake, new[] { new Vector2(1.05f, 0.05f), new Vector2(0.4f, -0.3f), new Vector2(0f, -0.25f) }, 24, Slate));
                With(mb, 0.4f, 1f, 0f, () =>
                {
                    for (int b = 0; b < 12; b++)
                        mb.AddBox(intake * Matrix4x4.Rotate(Quaternion.Euler(0, b * 30f, 0)) * TR(0.72f, -0.12f, 0, 0, 0, 25), new Vector3(0.6f, 0.03f, 0.18f), Gunmetal);
                });
                var bell = M * Matrix4x4.TRS(new Vector3(10f, 0.6f, -16.8f), Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one);
                With(mb, 0.6f, 1f, 0.2f, () =>
                {
                    mb.AddLathe(bell, new[]
                    {
                        new Vector2(1.3f, -0.05f), new Vector2(1.42f, 0.4f), new Vector2(1.62f, 1.25f), new Vector2(1.6f, 1.35f),
                        new Vector2(1.5f, 1.25f), new Vector2(1.25f, 0.5f), new Vector2(1.0f, 0.18f),
                    }, 32, Gunmetal);
                    for (int r = 0; r < 16; r++)
                        mb.AddBox(bell * Matrix4x4.Rotate(Quaternion.Euler(0, r * 22.5f, 0)) * TR(1.48f, 0.7f, 0, 0, 0, -10), new Vector3(0.1f, 1.1f, 0.14f), Slate);
                });
                Glow(mb, () => mb.AddLathe(bell, new[] { new Vector2(1.0f, 0.18f), new Vector2(0.62f, 0.1f) }, 32, Cyan), 1.3f);
                With(mb, 0.65f, 1f, 0f, () => mb.AddLathe(bell, new[] { new Vector2(0.62f, 0f), new Vector2(0.58f, 0.3f), new Vector2(0.3f, 0.65f), new Vector2(0f, 0.75f) }, 24, Slate));
                With(mb, 0.4f, 1f, 0f, () => { for (int f = 0; f < 6; f++) mb.AddBox(M * T(10f, 2.35f, -13.6f + f * 0.5f), new Vector3(0.12f, 0.28f, 0.3f), Gunmetal); });
                Glow(mb, () => mb.AddSphere(M * T(11.75f, 0.6f, -4.5f), 0.12f, 1, s < 0 ? Red : Green), 1.6f);
            }
        }

        /// <summary>
        /// Ark cabin (floor y = 0, x within +-4, z within +-13): wainscoted panel walls with structural ribs, ceiling light
        /// panels, pipe runs and cyan conduits, pilot station with an observation window, turret and engineer stations,
        /// fabricator, cargo lockers, hyperdrive coil and the airlock. Walls/ceiling colliders are added by BuildArk.
        /// </summary>
        static void ArkInterior(MeshBuilder mb)
        {
            var floorCol = new Color(0.36f, 0.39f, 0.45f);
            // floor, runner and gutters
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, -0.2f, 0), new Vector3(8f, 0.4f, 26f), floorCol));
            With(mb, Satin, 0.2f, 0.4f, () => mb.AddBox(T(0, -0.005f, 0), new Vector3(2f, 0.02f, 24.6f), Navy));
            Glow(mb, () => mb.AddBox(T(0, 0.01f, 0), new Vector3(0.12f, 0.02f, 24f), Cyan), 0.7f);
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                    for (int g = 0; g < 48; g++) mb.AddBox(T(s * 3.7f, 0.005f, -12.2f + g * 0.52f), new Vector3(0.55f, 0.02f, 0.12f), Rubber);
            });
            // walls: lower wainscot (slate) + upper panels; ceiling
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, Paint, 0f, 1f, () => mb.AddBox(T(s * 4.2f, 2f, 0), new Vector3(0.4f, 4.4f, 26f), Chalk));
                With(mb, Satin, 0.2f, 0.8f, () => mb.AddBox(T(s * 3.97f, 0.5f, 0), new Vector3(0.08f, 1.0f, 25.8f), Slate));
                Glow(mb, () => mb.AddBox(T(s * 3.92f, 1.02f, 0), new Vector3(0.02f, 0.04f, 25.6f), Cyan), 0.8f);
            }
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, 4.2f, 0), new Vector3(8.8f, 0.4f, 26f), Chalk));
            // front wall with the observation window opening (the wall collider stays solid), rear wall around the airlock
            With(mb, Paint, 0f, 1f, () =>
            {
                mb.AddBox(T(0, 0.9f, 13.2f), new Vector3(8.8f, 1.8f, 0.4f), Chalk);
                mb.AddBox(T(0, 3.8f, 13.2f), new Vector3(8.8f, 0.8f, 0.4f), Chalk);
                mb.AddBox(T(-3.7f, 2.6f, 13.2f), new Vector3(1.4f, 1.6f, 0.4f), Chalk);
                mb.AddBox(T(3.7f, 2.6f, 13.2f), new Vector3(1.4f, 1.6f, 0.4f), Chalk);
                mb.AddBox(T(-2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), Chalk);
                mb.AddBox(T(2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f), Chalk);
                mb.AddBox(T(0, 3.7f, -13.2f), new Vector3(2.2f, 1.4f, 0.4f), Chalk);
            });
            Hazard(mb, TR(0, 3.05f, -12.98f, 90, 0, 0), 2.2f, 0.12f, 8, 0.02f);

            // structural ribs (U-frames) and ceiling light panels between them
            float[] ribs = { -11.6f, -6.0f, -2.0f, 1.6f, 5.4f, 9.4f };
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                foreach (float z in ribs)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        mb.AddBox(T(s * 3.86f, 2.0f, z), new Vector3(0.28f, 4.0f, 0.36f), Slate);
                        mb.AddWedge(Matrix4x4.TRS(new Vector3(s * 3.55f, 3.62f, z), Quaternion.Euler(0, s < 0 ? 90 : -90, 180), Vector3.one), new Vector3(0.3f, 0.5f, 0.5f), Slate);
                    }
                    mb.AddBox(T(0, 3.86f, z), new Vector3(7.8f, 0.28f, 0.36f), Slate);
                }
            });
            for (int i = 0; i + 1 < ribs.Length; i++)
            {
                float zc = (ribs[i] + ribs[i + 1]) * 0.5f, len = (ribs[i + 1] - ribs[i]) * 0.55f;
                With(mb, Satin, 0.3f, 0f, () => mb.AddBox(T(0, 3.985f, zc), new Vector3(2.7f, 0.05f, len + 0.2f), Graphite));
                Glow(mb, () => mb.AddBox(T(0, 3.96f, zc), new Vector3(2.4f, 0.02f, len), new Color(0.85f, 0.93f, 1f)), 0.75f);
            }
            // pipe runs with brackets, cyan conduit along the ceiling edge
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 3.7f, 3.35f, -12.9f), new Vector3(s * 3.7f, 3.35f, 12.9f), 0.07f, 0.07f, 10, Gunmetal);
                    mb.AddTube(new Vector3(s * 3.72f, 3.55f, -12.9f), new Vector3(s * 3.72f, 3.55f, 12.9f), 0.045f, 0.045f, 8, Gold);
                    foreach (float z in ribs) mb.AddBox(T(s * 3.74f, 3.45f, z + 0.3f), new Vector3(0.18f, 0.36f, 0.06f), Graphite);
                });
                Glow(mb, () => mb.AddBox(T(s * 3.62f, 3.98f, 0), new Vector3(0.05f, 0.03f, 25.6f), Cyan), 1.0f);
            }

            // ---- pilot station: desk under the dashboard, chair
            With(mb, Satin, 0.3f, 0.5f, () =>
            {
                mb.AddBox(T(0, 0.55f, 12.55f), new Vector3(3.4f, 1.1f, 0.8f), Slate);
                mb.AddBox(TR(0, 1.16f, 12.35f, 20, 0, 0), new Vector3(3.4f, 0.1f, 0.7f), Graphite);
                for (int s = -1; s <= 1; s += 2) mb.AddBox(TR(s * 1.95f, 1.0f, 12.2f, 0, s * 25, 0), new Vector3(0.7f, 0.9f, 0.6f), Slate);
            });
            for (int b = 0; b < 8; b++)
                Glow(mb, () => mb.AddBox(TR(-1.2f + b * 0.34f, 1.22f, 12.25f, 20, 0, 0), new Vector3(0.12f, 0.02f, 0.08f), b % 3 == 0 ? Gold : (b % 3 == 1 ? Cyan : Green)), 1.2f);
            for (int s = -1; s <= 1; s += 2)
                Glow(mb, () => mb.AddBox(TR(s * 1.98f, 1.2f, 12.05f, -30, s * 25, 0), new Vector3(0.5f, 0.02f, 0.36f), Cyan), 0.9f);
            ArkChair(mb, new Vector3(0, 0, 11.4f), 0f);

            // ---- turret stations (chairs + wall screens)
            for (int s = -1; s <= 1; s += 2)
            {
                ArkChair(mb, new Vector3(s * 3f, 0, 3f), 0f);
                With(mb, Satin, 0.3f, 0.3f, () => mb.AddBox(T(s * 3.88f, 1.7f, 3.9f), new Vector3(0.12f, 0.9f, 1.3f), Graphite));
                Glow(mb, () => mb.AddBox(T(s * 3.81f, 1.7f, 3.9f), new Vector3(0.02f, 0.72f, 1.12f), s < 0 ? Red : Cyan), 0.6f);
            }

            // ---- engineer console (collider 0.8 x 1.2 x 2.2 at (-3.5, 0.6, -4)), fabricator, cargo lockers
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(-3.5f, 0.45f, -4f), new Vector3(0.8f, 0.9f, 2.2f), Navy);
                mb.AddBox(TR(-3.45f, 1.0f, -4f, 0, 0, -25), new Vector3(0.75f, 0.14f, 2.1f), Graphite);
                mb.AddBox(T(-3.8f, 1.75f, -4f), new Vector3(0.15f, 1.0f, 2.0f), Graphite);
            });
            Glow(mb, () => mb.AddBox(TR(-3.42f, 1.08f, -4f, 0, 0, -25), new Vector3(0.55f, 0.02f, 1.8f), Cyan), 0.9f);
            Glow(mb, () => mb.AddBox(T(-3.71f, 1.75f, -4f), new Vector3(0.02f, 0.8f, 1.8f), new Color(0.3f, 0.6f, 1f)), 0.6f);
            With(mb, Paint, 0.2f, 0.7f, () =>
            {
                mb.AddBox(T(3.5f, 1.2f, -4f), new Vector3(0.8f, 2.4f, 2.2f), Chalk);
                mb.AddBox(T(3.1f, 0.15f, -4f), new Vector3(0.1f, 0.3f, 2.2f), Blue);
            });
            With(mb, 1f, 0.2f, 0f, () => mb.AddBox(T(3.1f, 1.3f, -4f), new Vector3(0.04f, 1.0f, 1.4f), new Color(0.1f, 0.25f, 0.35f)));
            Glow(mb, () => mb.AddBox(T(3.2f, 1.3f, -4f), new Vector3(0.02f, 0.7f, 1.0f), Cyan), 0.5f);
            With(mb, 0.55f, 1f, 0f, () => mb.AddTube(new Vector3(3.3f, 2.0f, -4f), new Vector3(3.3f, 1.6f, -4f), 0.05f, 0.02f, 8, Gunmetal));
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(3.5f, 1.2f, -8f), new Vector3(0.8f, 2.4f, 2.2f), Graphite);
                for (int d = 0; d < 3; d++) mb.AddBox(T(3.08f, 0.45f + d * 0.75f, -8f), new Vector3(0.04f, 0.62f, 2.0f), Slate);
            });
            With(mb, 0.7f, 0.85f, 0f, () => mb.AddBox(T(3.06f, 2.35f, -8f), new Vector3(0.04f, 0.08f, 2.1f), Gold));
            for (int d = 0; d < 3; d++) Glow(mb, () => mb.AddBox(T(3.05f, 0.65f + d * 0.75f, -7.2f), new Vector3(0.02f, 0.08f, 0.08f), d == 0 ? Green : Gold), 1.4f);

            // ---- hyperdrive jump coil (aft left): caged column of glowing rings
            var coil = new Vector3(-3.1f, 0f, -10.2f);
            With(mb, 0.55f, 1f, 0f, () =>
            {
                mb.AddLathe(T(coil.x, 0f, coil.z), new[] { new Vector2(0.7f, 0f), new Vector2(0.72f, 0.3f), new Vector2(0.5f, 0.38f), new Vector2(0f, 0.4f) }, 20, Gunmetal);
                mb.AddLathe(T(coil.x, 3.6f, coil.z), new[] { new Vector2(0f, -0.4f), new Vector2(0.5f, -0.38f), new Vector2(0.72f, -0.3f), new Vector2(0.7f, 0f) }, 20, Gunmetal);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + 0.4f;
                    mb.AddTube(coil + new Vector3(Mathf.Cos(a) * 0.62f, 0.35f, Mathf.Sin(a) * 0.62f), coil + new Vector3(Mathf.Cos(a) * 0.62f, 3.25f, Mathf.Sin(a) * 0.62f), 0.05f, 0.05f, 8, Gunmetal);
                }
            });
            With(mb, 0.6f, 0.2f, 0f, () => mb.AddTube(coil + Vector3.up * 0.38f, coil + Vector3.up * 3.22f, 0.22f, 0.22f, 16, new Color(0.1f, 0.3f, 0.5f)), 0.6f);
            for (int r = 0; r < 7; r++)
                Glow(mb, () => mb.AddLathe(T(coil.x, 0.7f + r * 0.38f, coil.z), new[] { new Vector2(0.3f, -0.05f), new Vector2(0.44f, 0f), new Vector2(0.3f, 0.05f), new Vector2(0.3f, -0.05f) }, 20, Cyan), 1.4f);
        }

        static void ArkChair(MeshBuilder mb, Vector3 at, float yaw)
        {
            var m = Matrix4x4.TRS(at, Quaternion.Euler(0, yaw, 0), Vector3.one);
            With(mb, 0.55f, 1f, 0f, () => mb.AddLathe(m * T(0, 0f, 0), new[] { new Vector2(0.32f, 0f), new Vector2(0.3f, 0.06f), new Vector2(0.08f, 0.1f), new Vector2(0.07f, 0.4f), new Vector2(0f, 0.42f) }, 14, Gunmetal));
            With(mb, 0.25f, 0f, 0f, () =>
            {
                mb.AddBox(m * T(0, 0.48f, 0), new Vector3(0.62f, 0.14f, 0.6f), Slate);
                mb.AddBox(m * TR(0, 0.95f, -0.3f, -10, 0, 0), new Vector3(0.6f, 0.85f, 0.14f), Slate);
                mb.AddBox(m * TR(0, 1.45f, -0.38f, -10, 0, 0), new Vector3(0.36f, 0.2f, 0.12f), Rubber);
                for (int s = -1; s <= 1; s += 2) mb.AddBox(m * T(s * 0.34f, 0.68f, 0.02f), new Vector3(0.08f, 0.08f, 0.5f), Rubber);
            });
            Glow(mb, () => mb.AddBox(m * T(0.34f, 0.73f, 0.18f), new Vector3(0.05f, 0.012f, 0.05f), Cyan), 1.4f);
        }

        static Mesh ArkTurretBaseMesh()
        {
            var mb = Hero(0.15f);
            mb.AddLathe(Matrix4x4.identity, new[] { new Vector2(1.1f, 0f), new Vector2(1.05f, 0.35f), new Vector2(0.9f, 0.6f), new Vector2(0.55f, 0.66f), new Vector2(0f, 0.68f) }, 28, Graphite);
            With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(T(0, 0.6f, 0), new[] { new Vector2(0.6f, 0f), new Vector2(0.65f, 0.08f), new Vector2(0.6f, 0.16f) }, 24, Gunmetal));
            Glow(mb, () => mb.AddLathe(T(0, 0.3f, 0), new[] { new Vector2(1.08f, -0.03f), new Vector2(1.1f, 0f), new Vector2(1.08f, 0.03f) }, 28, Cyan), 0.9f);
            return SaveMesh(mb, "Turret_Base");
        }

        static Mesh ArkTurretHeadMesh()
        {
            var mb = Hero(0.15f);
            int head = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-0.7f, 0.5f, 0.38f, 3f), new MeshBuilder.Section(-0.45f, 0.66f, 0.46f, 3.5f),
                new MeshBuilder.Section(0.35f, 0.64f, 0.44f, 3.5f), new MeshBuilder.Section(0.7f, 0.42f, 0.3f, 3f),
            }, 24, Gunmetal, smoothing: 2);
            mb.Paint(head, p => p.y < -0.18f, Graphite);
            mb.Paint(head, p => p.y >= -0.18f && p.y < -0.05f, Slate);
            Glow(mb, () => mb.AddBox(T(0, 0.15f, 0.69f), new Vector3(0.5f, 0.07f, 0.04f), Cyan), 1.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, Satin, 0.6f, 0.4f, () => mb.AddLoft(T(s * 0.3f, -0.02f, 0), new[]
                {
                    new MeshBuilder.Section(0.5f, 0.15f, 0.15f, 3f), new MeshBuilder.Section(1.0f, 0.15f, 0.15f, 3f), new MeshBuilder.Section(1.15f, 0.1f, 0.1f, 2.5f),
                }, 14, Graphite));
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 0.3f, -0.02f, 1.1f), new Vector3(s * 0.3f, -0.02f, 1.95f), 0.06f, 0.06f, 12, Gunmetal);
                    mb.AddPrism(TR(s * 0.3f, -0.02f, 1.88f, 90, 0, 0), 0.085f, 0.08f, 0.14f, 12, Slate);
                });
            }
            return SaveMesh(mb, "Turret_Head");
        }

        // ---------------------------------------------------------------- Ark from the cargo ship asset (#97)

        // One deck through the fuselage and the cab (Tools/Blender/cargo_ark.py ROOMS / DOOR), Unity coordinates.
        const float ArkFloor = -1.55f;
        const float ArkHallX = 1.5f, ArkHallTop = 0.76f, ArkHallCorner = 0.3f, ArkHallCeilX = 1.25f, ArkHallZ0 = -8.6f, ArkHallZ1 = 6.6f;     // corridor (fuselage)
        const float ArkBridgeX = 1.36f, ArkBridgeTop = 1.6f, ArkBridgeZ1 = 16.85f;                // bridge (cab), front wall
        const float ArkDoorZ0 = -2.5f, ArkDoorZ1 = -0.5f, ArkDoorTop = 0.55f, ArkSkinX = 1.91f;  // airlock, left flank
        const float ArkPlatform = -1.16f, ArkPlatformZ = 13.0f;                                  // raised pilot platform
        static readonly Vector3 ArkEye = new Vector3(0f, 0.2f, 14.6f);
        // the fuselage is chamfered at the top (half width 1.67 at y 0.4, 1.44 at 0.76): walls end at ArkHallCorner,
        // a slanted panel leads to the ceiling (half width ArkHallCeilX)
        static readonly Color ArkPanel = new Color(0.33f, 0.37f, 0.43f);
        // the bridge window (cut out of the cab front): top edge (z, y) and bottom edge
        static readonly Vector2 ArkWindowTop = new Vector2(15.38f, 1.34f), ArkWindowBottom = new Vector2(16.92f, -0.47f);

        /// <summary>
        /// Ark with the textured cargo ship hull (#97): 36 m, gunmetal with tan trim, four engine nacelles, crates on the
        /// spine. One walkable deck: corridor through the fuselage, bridge in the cab (pilot on a raised platform behind
        /// the window, the Dart's fittings), engineering nook at the stern; airlock in the left flank with a boarding
        /// ramp to the ground. Turrets on the cab roof and the tail. The derelict variant is the same prefab:
        /// ShipDerelictLook darkens the hull and switches the cabin to red emergency light while ShipCore.Derelict is set.
        /// </summary>
        static GameObject BuildCargoArk(Mesh hull)
        {
            var go = ShipRoot("Ship_Ark", ShipClass.Ark, 20000f, 8, 4);
            var core = go.GetComponent<ShipCore>();
            go.GetComponent<HealthComponent>().MaxHealth = 600f;
            var hullR = Visual(go.transform, "Hull", hull, ArkCargoMaterial()).GetComponent<MeshRenderer>();
            var glass = ArkCargoGlassMesh();
            if (glass != null)
            {
                Visual(go.transform, "Window", glass, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Visual(go.transform, "WindowInner", glass, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // ---- interior: deck shell + fittings
            var interior = Child(go.transform, "Interior", Vector3.zero);
            var deckR = Visual(interior.transform, "Deck", ArkDeckMesh(), InteriorMaterial()).GetComponent<MeshRenderer>();
            var fit = Child(interior.transform, "Fittings", ArkEye - new Vector3(0f, 0f, 3.67f));
            bool fitted = DartFittings(fit.transform, false, true);
            if (!fitted) Dashboard(interior.transform, ArkEye + new Vector3(0f, -0.55f, 0.85f), 1.4f);

            // ---- colliders: a hollow shell around the deck, solid nacelles, wings, tail
            float hallMid = (ArkHallZ0 + ArkHallZ1) * 0.5f, hallLen = ArkHallZ1 - ArkHallZ0;
            Box(go, new Vector3(0, (ArkFloor - 2.4f) * 0.5f, hallMid), new Vector3(3.8f, 2.4f - (-ArkFloor) + 0.01f, hallLen));              // under the corridor floor
            Box(go, new Vector3(0, ArkHallTop + 0.11f, hallMid), new Vector3(3.9f, 0.22f, hallLen));                                         // corridor roof
            Box(go, new Vector3(0, 1.5f, -0.5f), new Vector3(3.4f, 1.1f, 12f));                                                             // crates on the spine
            Box(go, new Vector3(ArkHallX + 0.16f, (ArkFloor + ArkHallTop) * 0.5f, hallMid), new Vector3(0.32f, ArkHallTop - ArkFloor, hallLen)); // right wall
            float lz0 = (ArkHallZ0 + ArkDoorZ0) * 0.5f, lz1 = (ArkDoorZ1 + ArkHallZ1) * 0.5f;
            Box(go, new Vector3(-ArkHallX - 0.16f, (ArkFloor + ArkHallTop) * 0.5f, lz0), new Vector3(0.32f, ArkHallTop - ArkFloor, ArkDoorZ0 - ArkHallZ0));
            Box(go, new Vector3(-ArkHallX - 0.16f, (ArkFloor + ArkHallTop) * 0.5f, lz1), new Vector3(0.32f, ArkHallTop - ArkFloor, ArkHallZ1 - ArkDoorZ1));
            Box(go, new Vector3(-ArkHallX - 0.16f, (ArkDoorTop + ArkHallTop) * 0.5f, (ArkDoorZ0 + ArkDoorZ1) * 0.5f), new Vector3(0.32f, ArkHallTop - ArkDoorTop, ArkDoorZ1 - ArkDoorZ0));
            Box(go, new Vector3(0, -0.9f, -12.85f), new Vector3(3.8f, 3.1f, 8.5f));                                                         // tail (behind the stern wall)
            float bMid = (ArkHallZ1 + ArkBridgeZ1) * 0.5f, bLen = ArkBridgeZ1 - ArkHallZ1;
            Box(go, new Vector3(0, (ArkFloor - 3.08f) * 0.5f, bMid), new Vector3(3.4f, ArkFloor + 3.08f, bLen));                           // under the bridge floor
            Box(go, new Vector3(0, (ArkFloor + ArkPlatform) * 0.5f, (ArkPlatformZ + ArkBridgeZ1) * 0.5f), new Vector3(2.72f, ArkPlatform - ArkFloor, ArkBridgeZ1 - ArkPlatformZ));
            float wz = (ArkHallZ1 + ArkWindowTop.x) * 0.5f, wl = ArkWindowTop.x - ArkHallZ1;
            Box(go, new Vector3(-ArkBridgeX - 0.22f, 0.05f, wz), new Vector3(0.44f, 3.4f, wl));                                            // bridge walls
            Box(go, new Vector3(ArkBridgeX + 0.22f, 0.05f, wz), new Vector3(0.44f, 3.4f, wl));
            Box(go, new Vector3(0, ArkBridgeTop + 0.4f, wz), new Vector3(3.6f, 0.8f, wl));                                                  // bridge roof
            Box(go, new Vector3(0, (ArkHallTop + ArkBridgeTop) * 0.5f, ArkHallZ1), new Vector3(2.72f, ArkBridgeTop - ArkHallTop, 0.2f));    // header over the bridge door
            Box(go, new Vector3(0, (ArkWindowBottom.y - 3.08f) * 0.5f, 17.1f), new Vector3(3.0f, ArkWindowBottom.y + 3.08f, 0.5f));        // nose under the window
            var wTop = new Vector3(0, ArkWindowTop.y, ArkWindowTop.x);
            var wBot = new Vector3(0, ArkWindowBottom.y, ArkWindowBottom.x);
            var wUp = (wTop - wBot).normalized;
            var wn = Vector3.Cross(Vector3.right, wUp);
            var wc = Child(go.transform, "WindowCollider", (wTop + wBot) * 0.5f + wn * 0.06f, Quaternion.LookRotation(wn, wUp));
            Box(wc, Vector3.zero, new Vector3(3.0f, (wTop - wBot).magnitude + 0.1f, 0.12f));
            for (int s = -1; s <= 1; s += 2)
            {
                Box(go, new Vector3(s * 3.55f, -1.48f, 8.4f), new Vector3(2.2f, 2.2f, 11.4f));    // front nacelles
                Box(go, new Vector3(s * 3.55f, -0.2f, -11.7f), new Vector3(2.2f, 2.2f, 11.4f));   // rear nacelles
                Box(go, new Vector3(s * 4.5f, -2.1f, 8.0f), new Vector3(5.0f, 0.6f, 10f));        // front wings (root, tip)
                Box(go, new Vector3(s * 9.7f, -2.5f, 5.5f), new Vector3(5.6f, 0.5f, 5f));
                Box(go, new Vector3(s * 4.5f, -0.5f, -11.8f), new Vector3(5.0f, 0.6f, 10.5f));    // rear wings
                Box(go, new Vector3(s * 9.7f, -0.9f, -14.5f), new Vector3(5.6f, 0.5f, 5f));
            }
            // boarding ramp: from the airlock sill down to the ground beside the ship
            var rampFrom = new Vector3(-ArkSkinX, ArkFloor, 0f);
            var rampTo = new Vector3(-5.5f, -3.15f, 0f);
            var rampDir = rampFrom - rampTo;
            var ramp = Child(go.transform, "RampCollider", (rampFrom + rampTo) * 0.5f + new Vector3(0, -0.06f, (ArkDoorZ0 + ArkDoorZ1) * 0.5f),
                Quaternion.Euler(0f, 0f, Mathf.Atan2(rampDir.y, rampDir.x) * Mathf.Rad2Deg));
            Box(ramp, Vector3.zero, new Vector3(rampDir.magnitude + 0.1f, 0.12f, ArkDoorZ1 - ArkDoorZ0));

            // ---- interior gravity (moves with the ship; pawns ride along via platform carry): corridor, bridge, airlock
            var grav = Child(go.transform, "Interior Gravity", Vector3.zero);
            var hallG = Child(grav.transform, "Corridor", new Vector3(0, (ArkFloor + ArkHallTop) * 0.5f, hallMid));
            Box(hallG, Vector3.zero, new Vector3(ArkHallX * 2f, ArkHallTop - ArkFloor + 0.3f, hallLen), true);
            core.Interior = hallG.AddComponent<GravityVolume>();
            var bridgeG = Child(grav.transform, "Bridge", new Vector3(0, (ArkFloor + ArkBridgeTop) * 0.5f, bMid));
            Box(bridgeG, Vector3.zero, new Vector3(ArkBridgeX * 2f, ArkBridgeTop - ArkFloor + 0.3f, bLen), true);
            bridgeG.AddComponent<GravityVolume>();
            var lockG = Child(grav.transform, "Airlock", new Vector3(-(ArkHallX + ArkSkinX) * 0.5f, (ArkFloor + ArkDoorTop) * 0.5f, (ArkDoorZ0 + ArkDoorZ1) * 0.5f));
            Box(lockG, Vector3.zero, new Vector3(ArkSkinX - ArkHallX + 0.1f, ArkDoorTop - ArkFloor + 0.3f, ArkDoorZ1 - ArkDoorZ0), true);
            lockG.AddComponent<GravityVolume>();

            // ---- stations
            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, ArkEye + new Vector3(0f, -1.2f, -0.04f), ArkEye, new Vector3(0.75f, ArkPlatform + 0.1f, 13.6f),
                ArkEye + new Vector3(0f, -0.5f, 0.2f), new Vector3(1.6f, 1.8f, 1.6f));
            if (fitted) SeatGrips(pilot, fit.transform.Find("Stick/GripIK"), fit.transform.Find("Throttle/GripIK"));

            var baseMesh = ArkTurretBaseMesh();
            var headMesh = ArkTurretHeadMesh();
            // turret 0 (flak) on the cab roof, seat at the bridge's rear left; turret 1 (laser) on the tail, seat in the stern nook
            var turrets = new[]
            {
                (top: new Vector3(0f, 2.45f, 8.6f), anchor: new Vector3(-0.75f, ArkFloor + 0.16f, 8.1f), exit: new Vector3(-0.1f, ArkFloor + 0.1f, 9.4f)),
                (top: new Vector3(0f, 0.62f, -12.6f), anchor: new Vector3(0.85f, ArkFloor + 0.16f, -7.7f), exit: new Vector3(0.2f, ArkFloor + 0.1f, -6.3f)),
            };
            for (int i = 0; i < turrets.Length; i++)
            {
                var tb = Visual(go.transform, "TurretBase" + i, baseMesh, MatHull);
                tb.transform.localPosition = turrets[i].top;
                var yaw = Child(tb.transform, "Yaw", new Vector3(0, 0.9f, 0));
                var pitch = Child(yaw.transform, "Pitch", Vector3.zero);
                Visual(pitch.transform, "Head", headMesh, MatHull);
                ArkChairAt(interior.transform, turrets[i].anchor, 0f);
                var seat = Seat(go.transform, "TurretSeat" + i, StationRole.Turret, turrets[i].anchor, Vector3.zero, turrets[i].exit,
                    turrets[i].anchor + new Vector3(0f, 0.8f, 0.1f), new Vector3(1.2f, 1.8f, 1.3f));
                // the turret camera lives on the turret head, outside the hull
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
            // engineer: stern nook, facing the console on the left wall
            var engAnchor = new Vector3(-0.55f, ArkFloor + 0.16f, -7.3f);
            var engRot = Quaternion.Euler(0f, -90f, 0f);
            ArkChairAt(interior.transform, engAnchor, -90f);
            Seat(go.transform, "EngineerSeat", StationRole.Engineer, engAnchor, engAnchor + engRot * new Vector3(0f, 1.2f, 0.04f), new Vector3(0.1f, ArkFloor + 0.1f, -6.2f),
                engAnchor + new Vector3(-0.2f, 0.8f, 0f), new Vector3(1.4f, 1.8f, 1.3f), Quaternion.Euler(10f, -90f, 0f));

            var fab = Child(go.transform, "Fabricator", new Vector3(ArkHallX - 0.35f, ArkFloor + 1.0f, -4.2f));
            Box(fab, Vector3.zero, new Vector3(0.9f, 2f, 1.8f), true);
            fab.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.Fabricator;
            var cargo = Child(go.transform, "CargoTerminal", new Vector3(ArkHallX - 0.35f, ArkFloor + 1.0f, 2.2f));
            Box(cargo, Vector3.zero, new Vector3(0.9f, 2f, 2.2f), true);
            cargo.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;
            // on the flank beside the airlock, where a player at the ramp looks (a trigger around the player gets no ray hit)
            var outside = Child(go.transform, "HullTerminal", new Vector3(-ArkSkinX - 0.2f, ArkFloor + 0.3f, ArkDoorZ1 + 1.1f));
            Box(outside, Vector3.zero, new Vector3(0.7f, 2.0f, 1.4f), true);
            outside.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-3.55f, -1.48f, 14.4f)).transform, Child(go.transform, "MuzzleR", new Vector3(3.55f, -1.48f, 14.4f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleMissile", new Vector3(0, 2.7f, 6.4f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(0, ArkFloor + 0.3f, 3.5f)).transform;
            core.FabricatorPoint = fab.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-3.55f, -0.2f, -17.35f), new Vector3(3.55f, -0.2f, -17.35f), new Vector3(-3.55f, -1.48f, 2.75f), new Vector3(3.55f, -1.48f, 2.75f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(1.1f, 1.1f, 2f);
            ShipLighting(go, new Vector3(0, -3.6f, 2f), 26f, new Vector3(-0.9f, -1.6f, 17.3f), new Vector3(0.9f, -1.6f, 17.3f));

            // cabin lights (the hull blocks the sun) and one box-projected reflection probe rendered when the ship spawns
            var cabin = new List<Light>();
            foreach (float z in new[] { -6.5f, -2.5f, 1.5f, 5f }) cabin.Add(AddLight(interior.transform, "CabinLight", new Vector3(0, ArkHallTop - 0.15f, z), new Color(0.75f, 0.88f, 1f), 2.2f, 5.5f));
            foreach (float z in new[] { 8.6f, 12.4f }) cabin.Add(AddLight(interior.transform, "CabinLight", new Vector3(0, ArkBridgeTop - 0.15f, z), new Color(0.75f, 0.88f, 1f), 2.2f, 6f));
            cabin.Add(AddLight(interior.transform, "AirlockLight", new Vector3(-ArkHallX + 0.2f, ArkDoorTop - 0.1f, (ArkDoorZ0 + ArkDoorZ1) * 0.5f), new Color(1f, 0.6f, 0.25f), 1.2f, 3.5f));
            var probeGo = Child(go.transform, "CabinProbe", new Vector3(0, -0.2f, 4f));
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(ArkHallX * 2f, 3.4f, ArkBridgeZ1 - ArkHallZ0);
            probe.center = new Vector3(0, 0f, (ArkHallZ0 + ArkBridgeZ1) * 0.5f - 4f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.importance = 2;

            var lod = interior.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.02f, interior.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();

            var look = go.AddComponent<ShipDerelictLook>();
            look.Renderers = new Renderer[] { hullR, deckR };
            look.Derelict = new[] { ArkDerelictMaterial(), InteriorDerelictMaterial() };
            cabin.AddRange(fit.GetComponentsInChildren<Light>());
            look.Lights = cabin.ToArray();
            return SavePrefab(go);
        }

        /// <summary>The Dart chair (Sci-Fi Pilot Chair) standing at a seat anchor (seated hip, eye - 1.2), turned by yaw.</summary>
        static void ArkChairAt(Transform parent, Vector3 anchor, float yaw)
        {
            var mesh = DartCockpitMesh("Seat");
            if (mesh == null) return;
            var c = Child(parent, "Chair", anchor, Quaternion.Euler(0f, yaw, 0f));
            // the chair mesh is in Dart coordinates; the Dart's seat anchor is (0, -1.2, 3.63)
            Visual(c.transform, "Mesh", mesh, ChairMaterial()).transform.localPosition = new Vector3(0f, 1.2f, -3.63f);
        }

        /// <summary>
        /// The Ark's deck shell (VW_HullInterior panel detail): floor, walls, ceilings and bulkheads that close the walkable
        /// volume from the hull (seen from inside the hull is culled), structural U-frames, pipe runs and light panels,
        /// the airlock tunnel with its hazard-striped boarding ramp, the bridge with the raised pilot platform and the
        /// walls under the window, consoles for the engineer and turret stations, fabricator and cargo lockers.
        /// </summary>
        static Mesh ArkDeckMesh()
        {
            var mb = Hero(0.03f);
            var floorCol = new Color(0.3f, 0.33f, 0.38f);
            float hallMid = (ArkHallZ0 + ArkHallZ1) * 0.5f, hallLen = ArkHallZ1 - ArkHallZ0, hallH = ArkHallTop - ArkFloor;
            float bMid = (ArkHallZ1 + ArkBridgeZ1) * 0.5f, bLen = ArkBridgeZ1 - ArkHallZ1;
            float wallMid = (ArkFloor + ArkHallTop) * 0.5f;

            // ---- corridor: floor with runner + gutters, walls with wainscot and a light strip, ceiling, stern wall
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, ArkFloor - 0.06f, hallMid), new Vector3(ArkHallX * 2f, 0.12f, hallLen), floorCol));
            With(mb, Satin, 0.2f, 0.4f, () => mb.AddBox(T(0, ArkFloor + 0.005f, hallMid), new Vector3(1.0f, 0.01f, hallLen - 0.6f), Slate));
            Glow(mb, () => mb.AddBox(T(0, ArkFloor + 0.012f, hallMid), new Vector3(0.05f, 0.01f, hallLen - 1.0f), Cyan), 0.6f);
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                    for (float z = ArkHallZ0 + 0.4f; z < ArkHallZ1 - 0.2f; z += 0.5f)
                        mb.AddBox(T(s * (ArkHallX - 0.25f), ArkFloor + 0.005f, z), new Vector3(0.35f, 0.012f, 0.1f), Rubber);
            });
            float lowH = ArkHallCorner - ArkFloor, lowMid = (ArkFloor + ArkHallCorner) * 0.5f;
            var corner = new Vector2(ArkHallX - ArkHallCeilX, ArkHallTop - ArkHallCorner);   // chamfer run (x, y)
            float cornerLen = corner.magnitude, cornerAngle = Mathf.Atan2(corner.y, corner.x) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (ArkHallX + 0.04f);
                if (s < 0)
                {
                    // left wall: openings for the airlock (the door top reaches into the chamfer: a header there)
                    float l0 = ArkDoorZ0 - ArkHallZ0, l1 = ArkHallZ1 - ArkDoorZ1;
                    With(mb, Paint, 0.2f, 1f, () =>
                    {
                        mb.AddBox(T(x, lowMid, ArkHallZ0 + l0 * 0.5f), new Vector3(0.08f, lowH, l0), ArkPanel);
                        mb.AddBox(T(x, lowMid, ArkDoorZ1 + l1 * 0.5f), new Vector3(0.08f, lowH, l1), ArkPanel);
                    });
                }
                else With(mb, Paint, 0.2f, 1f, () => mb.AddBox(T(x, lowMid, hallMid), new Vector3(0.08f, lowH, hallLen), ArkPanel));
                // chamfer panel from the wall top to the ceiling edge
                int side = s;
                With(mb, Paint, 0.2f, 1f, () => mb.AddBox(Matrix4x4.TRS(new Vector3(side * ((ArkHallX + ArkHallCeilX) * 0.5f + 0.03f), (ArkHallCorner + ArkHallTop) * 0.5f + 0.03f, hallMid),
                    Quaternion.Euler(0f, 0f, side * -cornerAngle), Vector3.one), new Vector3(cornerLen + 0.05f, 0.08f, hallLen), ArkPanel));
                // wainscot and light strip (left: broken by the airlock)
                foreach (var (z0, z1) in s < 0 ? new[] { (ArkHallZ0, ArkDoorZ0 - 0.15f), (ArkDoorZ1 + 0.15f, ArkHallZ1) } : new[] { (ArkHallZ0, ArkHallZ1) })
                {
                    With(mb, Satin, 0.2f, 0.8f, () => mb.AddBox(T(s * (ArkHallX - 0.01f), ArkFloor + 0.45f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.9f, z1 - z0 - 0.05f), Slate));
                    Glow(mb, () => mb.AddBox(T(s * (ArkHallX - 0.03f), ArkFloor + 0.92f, (z0 + z1) * 0.5f), new Vector3(0.02f, 0.03f, z1 - z0 - 0.2f), Cyan), 0.8f);
                }
            }
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, ArkHallTop + 0.04f, hallMid), new Vector3(ArkHallCeilX * 2f + 0.1f, 0.08f, hallLen), ArkPanel));
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, wallMid, ArkHallZ0 - 0.05f), new Vector3(ArkHallX * 2f + 0.1f, hallH + 0.1f, 0.1f), ArkPanel));

            // structural U-frames (clear of the airlock) and ceiling light panels between them
            float[] ribs = { -7.4f, -5.0f, -3.0f, 0.9f, 3.4f, 5.9f };
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                foreach (float z in ribs)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        mb.AddBox(T(s * (ArkHallX - 0.06f), lowMid, z), new Vector3(0.14f, lowH, 0.26f), Slate);
                        mb.AddBox(Matrix4x4.TRS(new Vector3(s * ((ArkHallX + ArkHallCeilX) * 0.5f - 0.06f), (ArkHallCorner + ArkHallTop) * 0.5f - 0.05f, z),
                            Quaternion.Euler(0f, 0f, s * -cornerAngle), Vector3.one), new Vector3(cornerLen, 0.14f, 0.26f), Slate);
                    }
                    mb.AddBox(T(0, ArkHallTop - 0.06f, z), new Vector3(ArkHallCeilX * 2f, 0.14f, 0.26f), Slate);
                }
            });
            for (int i = 0; i + 1 < ribs.Length; i++)
            {
                float zc = (ribs[i] + ribs[i + 1]) * 0.5f, len = (ribs[i + 1] - ribs[i]) * 0.5f;
                With(mb, Satin, 0.3f, 0f, () => mb.AddBox(T(0, ArkHallTop - 0.015f, zc), new Vector3(1.3f, 0.03f, len + 0.15f), Graphite));
                Glow(mb, () => mb.AddBox(T(0, ArkHallTop - 0.032f, zc), new Vector3(1.1f, 0.01f, len), new Color(0.85f, 0.93f, 1f)), 0.75f);
            }
            // pipe runs along the ceiling edges with brackets
            for (int s = -1; s <= 1; s += 2)
            {
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * (ArkHallX - 0.2f), ArkHallCorner - 0.12f, ArkHallZ0 + 0.1f), new Vector3(s * (ArkHallX - 0.2f), ArkHallCorner - 0.12f, ArkHallZ1 - 0.1f), 0.05f, 0.05f, 10, Gunmetal);
                    mb.AddTube(new Vector3(s * (ArkHallX - 0.1f), ArkHallCorner - 0.04f, ArkHallZ0 + 0.1f), new Vector3(s * (ArkHallX - 0.1f), ArkHallCorner - 0.04f, ArkHallZ1 - 0.1f), 0.03f, 0.03f, 8, Gold);
                });
            }

            // ---- airlock: tunnel through the flank, hazard frame, warning lamps, the boarding ramp outside
            float dMid = (ArkDoorZ0 + ArkDoorZ1) * 0.5f, dLen = ArkDoorZ1 - ArkDoorZ0, tunX = -(ArkHallX + ArkSkinX) * 0.5f, tunW = ArkSkinX - ArkHallX + 0.05f;
            With(mb, Satin, 0.6f, 0.6f, () =>
            {
                mb.AddBox(T(tunX, ArkFloor - 0.06f, dMid), new Vector3(tunW, 0.12f, dLen), Graphite);
                mb.AddBox(T(tunX, ArkDoorTop + 0.05f, dMid), new Vector3(tunW, 0.1f, dLen), Graphite);
                mb.AddBox(T(tunX, (ArkFloor + ArkDoorTop) * 0.5f, ArkDoorZ0 - 0.05f), new Vector3(tunW, ArkDoorTop - ArkFloor, 0.1f), Graphite);
                mb.AddBox(T(tunX, (ArkFloor + ArkDoorTop) * 0.5f, ArkDoorZ1 + 0.05f), new Vector3(tunW, ArkDoorTop - ArkFloor, 0.1f), Graphite);
            });
            With(mb, 0.7f, 0.85f, 0f, () =>
            {
                mb.AddBox(T(-ArkHallX + 0.02f, (ArkFloor + ArkDoorTop) * 0.5f, ArkDoorZ0 - 0.08f), new Vector3(0.1f, ArkDoorTop - ArkFloor + 0.1f, 0.12f), Gold);
                mb.AddBox(T(-ArkHallX + 0.02f, (ArkFloor + ArkDoorTop) * 0.5f, ArkDoorZ1 + 0.08f), new Vector3(0.1f, ArkDoorTop - ArkFloor + 0.1f, 0.12f), Gold);
                mb.AddBox(T(-ArkHallX + 0.02f, ArkDoorTop + 0.06f, dMid), new Vector3(0.1f, 0.12f, dLen + 0.28f), Gold);
            });
            Hazard(mb, T(tunX, ArkFloor + 0.004f, dMid), tunW, 0.3f, 6, 0.01f);
            for (int s = -1; s <= 1; s += 2)
                Glow(mb, () => mb.AddBox(T(-ArkHallX + 0.09f, ArkDoorTop - 0.1f, dMid + s * (dLen * 0.5f + 0.2f)), new Vector3(0.04f, 0.14f, 0.1f), new Color(1f, 0.55f, 0.15f)), 1.5f);
            // hull access panel on the flank beside the airlock (HullTerminal)
            With(mb, Satin, 0.5f, 0.5f, () => mb.AddBox(T(-ArkSkinX - 0.03f, ArkFloor + 0.3f, ArkDoorZ1 + 1.1f), new Vector3(0.08f, 0.9f, 0.7f), Graphite));
            Glow(mb, () => mb.AddBox(T(-ArkSkinX - 0.075f, ArkFloor + 0.4f, ArkDoorZ1 + 1.1f), new Vector3(0.01f, 0.5f, 0.5f), Cyan), 0.7f);
            Glow(mb, () => mb.AddBox(T(-ArkSkinX - 0.075f, ArkFloor - 0.08f, ArkDoorZ1 + 1.1f), new Vector3(0.01f, 0.06f, 0.5f), new Color(1f, 0.55f, 0.15f)), 1.2f);
            var rampFrom = new Vector3(-ArkSkinX, ArkFloor, dMid);
            var rampTo = new Vector3(-5.5f, -3.15f, dMid);
            var rampDir = rampFrom - rampTo;
            float rampAngle = Mathf.Atan2(rampDir.y, rampDir.x) * Mathf.Rad2Deg;
            var rampM = Matrix4x4.TRS((rampFrom + rampTo) * 0.5f + new Vector3(0, -0.06f, 0), Quaternion.Euler(0f, 0f, rampAngle), Vector3.one);
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(rampM, new Vector3(rampDir.magnitude, 0.12f, dLen), Graphite));
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (float u = -rampDir.magnitude * 0.5f + 0.3f; u < rampDir.magnitude * 0.5f - 0.2f; u += 0.32f)
                    mb.AddBox(rampM * T(u, 0.065f, 0f), new Vector3(0.08f, 0.015f, dLen - 0.3f), Rubber);
            });
            Hazard(mb, rampM * T(-rampDir.magnitude * 0.5f + 0.15f, 0.07f, 0f) * Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0)), dLen, 0.25f, 6, 0.01f);
            for (int s = -1; s <= 1; s += 2)
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    float rz = dMid + s * (dLen * 0.5f - 0.05f);
                    mb.AddTube(new Vector3(rampTo.x + 0.1f, rampTo.y + 0.9f, rz), new Vector3(rampFrom.x - 0.05f, rampFrom.y + 0.9f, rz), 0.03f, 0.03f, 8, Gunmetal);
                    mb.AddTube(new Vector3(rampTo.x + 0.1f, rampTo.y, rz), new Vector3(rampTo.x + 0.1f, rampTo.y + 0.9f, rz), 0.03f, 0.03f, 8, Gunmetal);
                    mb.AddTube(new Vector3((rampTo.x + rampFrom.x) * 0.5f, (rampTo.y + rampFrom.y) * 0.5f, rz), new Vector3((rampTo.x + rampFrom.x) * 0.5f, (rampTo.y + rampFrom.y) * 0.5f + 0.9f, rz), 0.03f, 0.03f, 8, Gunmetal);
                });

            // ---- bulkhead between corridor and bridge: header over the opening, jambs where the cab narrows
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                mb.AddBox(T(0, (ArkHallTop + ArkBridgeTop) * 0.5f, ArkHallZ1), new Vector3(ArkBridgeX * 2f, ArkBridgeTop - ArkHallTop, 0.16f), Slate);
                for (int s = -1; s <= 1; s += 2)
                    mb.AddBox(T(s * (ArkBridgeX + ArkHallX) * 0.5f, wallMid, ArkHallZ1), new Vector3(ArkHallX - ArkBridgeX + 0.1f, hallH, 0.16f), Slate);
                mb.AddBox(T(0, (ArkHallCorner + ArkHallTop) * 0.5f, ArkHallZ1 - 0.02f), new Vector3(ArkBridgeX * 2f, ArkHallTop - ArkHallCorner, 0.12f), Slate);
            });
            Hazard(mb, TR(0, ArkHallTop - 0.08f, ArkHallZ1 - 0.085f, 90, 0, 0), ArkBridgeX * 2f, 0.12f, 10, 0.01f);

            // ---- bridge: floor, walls, ceiling, the walls under the slanted window, raised pilot platform, front console
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, ArkFloor - 0.06f, bMid), new Vector3(ArkBridgeX * 2f, 0.12f, bLen), floorCol));
            float wTopZ = ArkWindowTop.x;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (ArkBridgeX + 0.05f);
                With(mb, Paint, 0.2f, 1f, () =>
                {
                    mb.AddBox(T(x, (ArkFloor + ArkBridgeTop) * 0.5f, (ArkHallZ1 + wTopZ) * 0.5f), new Vector3(0.1f, ArkBridgeTop - ArkFloor, wTopZ - ArkHallZ1), ArkPanel);
                    mb.AddBox(T(x, (ArkFloor + ArkWindowBottom.y) * 0.5f, (wTopZ + ArkBridgeZ1) * 0.5f), new Vector3(0.1f, ArkWindowBottom.y - ArkFloor, ArkBridgeZ1 - wTopZ), ArkPanel);
                    // the triangle under the window's side edge: outline (-y, z) in the slab's local XZ, thickness along x
                    mb.AddSlab(Matrix4x4.TRS(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one), new[]
                    {
                        new Vector2(-ArkWindowBottom.y, wTopZ), new Vector2(-ArkWindowBottom.y, ArkWindowBottom.x - 0.05f), new Vector2(-ArkWindowTop.y, wTopZ),
                    }, 0.1f, 0.01f, ArkPanel);
                });
                With(mb, Satin, 0.2f, 0.8f, () => mb.AddBox(T(s * (ArkBridgeX - 0.01f), ArkFloor + 0.45f, (ArkHallZ1 + ArkPlatformZ) * 0.5f), new Vector3(0.05f, 0.9f, ArkPlatformZ - ArkHallZ1 - 0.2f), Slate));
                Glow(mb, () => mb.AddBox(T(s * (ArkBridgeX - 0.03f), ArkFloor + 0.92f, (ArkHallZ1 + ArkPlatformZ) * 0.5f), new Vector3(0.02f, 0.03f, ArkPlatformZ - ArkHallZ1 - 0.4f), Cyan), 0.8f);
            }
            With(mb, Paint, 0f, 1f, () =>
            {
                mb.AddBox(T(0, ArkBridgeTop + 0.05f, (ArkHallZ1 + wTopZ) * 0.5f), new Vector3(ArkBridgeX * 2f + 0.2f, 0.1f, wTopZ - ArkHallZ1), ArkPanel);
                mb.AddBox(T(0, (ArkWindowTop.y + ArkBridgeTop) * 0.5f + 0.05f, wTopZ - 0.04f), new Vector3(ArkBridgeX * 2f + 0.2f, ArkBridgeTop - ArkWindowTop.y + 0.1f, 0.08f), ArkPanel);
                mb.AddBox(T(0, (ArkFloor + ArkWindowBottom.y) * 0.5f, ArkBridgeZ1 + 0.05f), new Vector3(ArkBridgeX * 2f + 0.2f, ArkWindowBottom.y - ArkFloor, 0.1f), ArkPanel);
            });
            for (int k = 0; k < 2; k++)
            {
                float zc = 8.6f + k * 3.8f;
                With(mb, Satin, 0.3f, 0f, () => mb.AddBox(T(0, ArkBridgeTop - 0.015f, zc), new Vector3(1.4f, 0.03f, 1.6f), Graphite));
                Glow(mb, () => mb.AddBox(T(0, ArkBridgeTop - 0.032f, zc), new Vector3(1.2f, 0.01f, 1.4f), new Color(0.85f, 0.93f, 1f)), 0.75f);
            }
            // platform: deck plate with a lit step edge and two steps up
            float pMid = (ArkPlatformZ + ArkBridgeZ1) * 0.5f, pLen = ArkBridgeZ1 - ArkPlatformZ;
            With(mb, Satin, 0.6f, 0.6f, () =>
            {
                mb.AddBox(T(0, (ArkFloor + ArkPlatform) * 0.5f, pMid), new Vector3(ArkBridgeX * 2f, ArkPlatform - ArkFloor, pLen), Graphite);
                mb.AddBox(T(0, ArkFloor + (ArkPlatform - ArkFloor) * 0.25f, ArkPlatformZ - 0.2f), new Vector3(1.4f, (ArkPlatform - ArkFloor) * 0.5f, 0.4f), Graphite);
            });
            Glow(mb, () => mb.AddBox(T(0, ArkPlatform - 0.01f, ArkPlatformZ + 0.02f), new Vector3(ArkBridgeX * 2f - 0.1f, 0.02f, 0.04f), new Color(1f, 0.55f, 0.15f)), 1.1f);
            With(mb, 0.2f, 0f, 0f, () => mb.AddBox(T(0, ArkPlatform + 0.005f, pMid - 0.3f), new Vector3(0.7f, 0.01f, 1.4f), Rubber));
            // front console under the window (between the Dart dashboard and the window's bottom edge)
            float fz0 = ArkEye.z + 1.2f;
            With(mb, Satin, 0.45f, 0.8f, () => mb.AddBox(T(0, (ArkPlatform + ArkWindowBottom.y - 0.1f) * 0.5f, (fz0 + ArkBridgeZ1) * 0.5f), new Vector3(ArkBridgeX * 2f, ArkWindowBottom.y - 0.1f - ArkPlatform, ArkBridgeZ1 - fz0), Graphite));
            Glow(mb, () => mb.AddBox(T(0, ArkWindowBottom.y - 0.09f, fz0 + 0.04f), new Vector3(ArkBridgeX * 2f - 0.2f, 0.015f, 0.03f), Cyan), 1.0f);

            // ---- stations: engineer console (stern, left wall), turret screens, fabricator, cargo lockers
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(-ArkHallX + 0.25f, ArkFloor + 0.45f, -7.3f), new Vector3(0.5f, 0.9f, 1.8f), Navy);
                mb.AddBox(TR(-ArkHallX + 0.3f, ArkFloor + 0.95f, -7.3f, 0, 0, -25), new Vector3(0.45f, 0.1f, 1.7f), Graphite);
                mb.AddBox(T(-ArkHallX + 0.06f, ArkFloor + 1.55f, -7.3f), new Vector3(0.08f, 0.9f, 1.7f), Graphite);
            });
            Glow(mb, () => mb.AddBox(TR(-ArkHallX + 0.33f, ArkFloor + 1.01f, -7.3f, 0, 0, -25), new Vector3(0.3f, 0.015f, 1.5f), Cyan), 0.9f);
            Glow(mb, () => mb.AddBox(T(-ArkHallX + 0.11f, ArkFloor + 1.55f, -7.3f), new Vector3(0.015f, 0.72f, 1.5f), new Color(0.3f, 0.6f, 1f)), 0.6f);
            foreach (var (x, z, col) in new[] { (-ArkBridgeX, 8.4f, Red), (ArkHallX, -7.9f, Cyan) })
            {
                float s = Mathf.Sign(x);
                With(mb, Satin, 0.3f, 0.3f, () => mb.AddBox(T(x - s * 0.06f, ArkFloor + 1.5f, z), new Vector3(0.08f, 0.7f, 1.0f), Graphite));
                Glow(mb, () => mb.AddBox(T(x - s * 0.105f, ArkFloor + 1.5f, z), new Vector3(0.01f, 0.56f, 0.86f), col), 0.6f);
            }
            float fx = ArkHallX - 0.35f;
            With(mb, Paint, 0.2f, 0.7f, () => mb.AddBox(T(fx + 0.05f, ArkFloor + 1.0f, -4.2f), new Vector3(0.6f, 2.0f, 1.7f), ArkPanel));
            With(mb, 1f, 0.2f, 0f, () => mb.AddBox(T(fx - 0.26f, ArkFloor + 1.15f, -4.2f), new Vector3(0.03f, 0.9f, 1.1f), new Color(0.1f, 0.25f, 0.35f)));
            Glow(mb, () => mb.AddBox(T(fx - 0.2f, ArkFloor + 1.15f, -4.2f), new Vector3(0.01f, 0.6f, 0.8f), Cyan), 0.5f);
            With(mb, 0.55f, 1f, 0f, () => mb.AddTube(new Vector3(fx - 0.15f, ArkFloor + 1.7f, -4.2f), new Vector3(fx - 0.15f, ArkFloor + 1.4f, -4.2f), 0.04f, 0.02f, 8, Gunmetal));
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(fx + 0.05f, ArkFloor + 1.0f, 2.2f), new Vector3(0.6f, 2.0f, 2.1f), Graphite);
                for (int d = 0; d < 3; d++) mb.AddBox(T(fx - 0.26f, ArkFloor + 0.4f + d * 0.6f, 2.2f), new Vector3(0.03f, 0.5f, 1.9f), Slate);
            });
            for (int d = 0; d < 3; d++) Glow(mb, () => mb.AddBox(T(fx - 0.28f, ArkFloor + 0.55f + d * 0.6f, 2.95f), new Vector3(0.01f, 0.06f, 0.06f), d == 0 ? Green : Gold), 1.4f);
            return SaveMesh(mb, "Ship_Ark_Deck");
        }

        static GameObject BuildArk()
        {
            var cargoHull = ArkCargoMesh();
            if (cargoHull != null) return BuildCargoArk(cargoHull);
            var go = ShipRoot("Ship_Ark", ShipClass.Ark, 20000f, 8, 4);
            var core = go.GetComponent<ShipCore>();
            var hc = go.GetComponent<HealthComponent>();
            hc.MaxHealth = 600f;

            // Interior: floor y=0, 8 wide (x), 26 long (z), 4 high. Open doorway at the rear.
            var mb = Hero(0.12f);
            ArkInterior(mb);
            ArkExterior(mb);
            Visual(go.transform, "Hull", SaveMesh(mb, "Ship_Ark"), MatHull);
            var glass = new MeshBuilder();
            glass.AddBox(T(0, 2.6f, 13.45f), new Vector3(6f, 1.6f, 0.1f), Color.white);
            Visual(go.transform, "Windshield", SaveMesh(glass, "Ship_Ark_Glass"), MatGlass);

            // Turret heads
            var baseMesh = ArkTurretBaseMesh();
            var headMesh = ArkTurretHeadMesh();

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
            Box(go, new Vector3(0, 0f, 16.8f), new Vector3(9.6f, 3.4f, 6.2f));
            Box(go, new Vector3(-2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f));
            Box(go, new Vector3(2.75f, 2f, -13.2f), new Vector3(3.3f, 4.4f, 0.4f));
            Box(go, new Vector3(0, 3.7f, -13.2f), new Vector3(2.2f, 1.4f, 0.4f));
            for (int s = -1; s <= 1; s += 2)
            {
                Box(go, new Vector3(s * 7.1f, 0.6f, -7.9f), new Vector3(3.8f, 0.7f, 8.4f));   // outrigger pylon
                Box(go, new Vector3(s * 10f, 0.6f, -10.6f), new Vector3(3.4f, 3.4f, 14.6f));  // engine nacelle
            }
            Box(go, new Vector3(-3.1f, 1.8f, -10.2f), new Vector3(1.4f, 3.6f, 1.4f));         // hyperdrive coil
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
                var tb = Visual(go.transform, "TurretBase" + i, baseMesh, MatHull);
                tb.transform.localPosition = new Vector3(side * 3.5f, 4.5f, 4f);
                var yaw = Child(tb.transform, "Yaw", new Vector3(0, 0.9f, 0));
                var pitch = Child(yaw.transform, "Pitch", Vector3.zero);
                Visual(pitch.transform, "Head", headMesh, MatHull);
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
            fx.Thrusters = Thrusters(go.transform, new Vector3(-10f, 0.6f, -18.1f), new Vector3(10f, 0.6f, -18.1f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(4.2f, 4.2f, 4.2f);
            ShipLighting(go, new Vector3(0, -3f, 0), 24f, new Vector3(-2.0f, -0.55f, 18.9f), new Vector3(2.0f, -0.55f, 18.9f));
            // Interior: the hull blocks the sun, so the cabin gets its own cool ceiling lights.
            for (int i = 0; i < 3; i++) AddLight(go.transform, "CabinLight", new Vector3(0, 3.6f, -8f + i * 8f), new Color(0.75f, 0.88f, 1f), 9f, 9f);
            // Cabin reflections: one box-projected probe rendered when the ship spawns.
            var probeGo = Child(go.transform, "CabinProbe", new Vector3(0, 2f, 0));
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(8f, 4.2f, 26.4f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.importance = 2;
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

        // Faction palettes (DESIGN.md "Enemy Visual Identity")
        static readonly Color Obsidian = new Color(0.11f, 0.11f, 0.125f);
        static readonly Color Rust = new Color(0.47f, 0.21f, 0.06f);
        static readonly Color SensorRed = new Color(0.94f, 0.27f, 0.27f);
        static readonly Color GlowRed = new Color(1f, 0.1f, 0.06f); // emissive red: saturated so bloom keeps it red, not pink
        static readonly Color AcidLime = new Color(0.52f, 0.8f, 0.09f);
        static readonly Color Chitin = new Color(0.35f, 0.11f, 0.53f);
        static readonly Color BioLime = new Color(0.64f, 0.9f, 0.21f);
        static readonly Color Basalt = new Color(0.06f, 0.09f, 0.16f);
        static readonly Color VoidViolet = new Color(0.87f, 0.84f, 1f);

        /// <summary>Organic builder for the alien faction: smooth glossy chitin, no panel seams.</summary>
        static MeshBuilder Organic() => new MeshBuilder { Bevel = 0.2f, SmoothAngle = 60f, Panel = 0f, Gloss = 0.7f };

        /// <summary>Salvage look: repaints noise-selected faces since `from` in rust (patchwork armour).</summary>
        static void RustPatches(MeshBuilder mb, int from, float scale, float threshold, int seed)
        {
            mb.Paint(from, p => Mathf.PerlinNoise(p.x * scale + seed * 7.3f, p.z * scale + p.y * 0.7f * scale) > threshold, Rust, 0.25f, 0.4f);
        }

        /// <summary>Engine bell pointing -Z at `rear` (exit plane) with a glowing throat in `glow`.</summary>
        static void Bell(MeshBuilder mb, Vector3 attach, float r, float len, Color glow, Color body)
        {
            var bell = Matrix4x4.TRS(attach, Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one);
            With(mb, 0.55f, 1f, 0.2f, () => mb.AddLathe(bell, new[]
            {
                new Vector2(r * 0.8f, -0.02f), new Vector2(r * 0.88f, len * 0.3f), new Vector2(r, len * 0.95f), new Vector2(r * 0.96f, len),
                new Vector2(r * 0.86f, len * 0.9f), new Vector2(r * 0.66f, len * 0.3f), new Vector2(r * 0.55f, len * 0.12f),
            }, 20, body));
            Glow(mb, () => mb.AddLathe(bell, new[] { new Vector2(r * 0.55f, len * 0.12f), new Vector2(r * 0.3f, len * 0.08f) }, 20, glow), 1.4f);
            With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(bell, new[] { new Vector2(r * 0.3f, 0f), new Vector2(r * 0.25f, len * 0.25f), new Vector2(0f, len * 0.35f) }, 14, Slate));
        }

        /// <summary>Two-segment insect leg from hip to foot through a raised knee, with a joint ball and a claw tip.</summary>
        static void InsectLeg(MeshBuilder mb, Vector3 hip, Vector3 knee, Vector3 foot, float r, Color c, Color joint)
        {
            Limb(mb, hip, knee, r, r * 0.8f, c, 1f);
            Limb(mb, knee, foot, r * 0.8f, r * 0.35f, c, 1f);
            With(mb, 0.8f, 0f, 0f, () => mb.AddSphere(Matrix4x4.TRS(knee, Quaternion.identity, Vector3.one), r * 0.95f, 2, joint));
        }

        static Mesh PirateFighterMesh()
        {
            var mb = Hero(0.18f);
            mb.Gloss = 0.3f;
            int hull = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-2.25f, 0.5f, 0.42f, 3f), new MeshBuilder.Section(-1.0f, 0.62f, 0.5f, 3.2f),
                new MeshBuilder.Section(1.0f, 0.46f, 0.38f, 2.8f, -0.05f), new MeshBuilder.Section(2.3f, 0.16f, 0.14f, 2.4f, -0.1f),
                new MeshBuilder.Section(2.75f, 0f, 0f, 2f, -0.12f),
            }, 20, Obsidian, smoothing: 2);
            RustPatches(mb, hull, 1.6f, 0.6f, 1);
            // cockpit hump with a glaring red sensor slit
            With(mb, 0.9f, 0.3f, 0f, () => mb.AddLoft(T(0, 0.38f, 0), new[]
            {
                new MeshBuilder.Section(-0.9f, 0.1f, 0.05f, 2.5f), new MeshBuilder.Section(-0.5f, 0.32f, 0.22f, 2.5f),
                new MeshBuilder.Section(0.6f, 0.3f, 0.18f, 2.5f), new MeshBuilder.Section(1.2f, 0.08f, 0.04f, 2.5f),
            }, 16, Rubber, smoothing: 2));
            Glow(mb, () => mb.AddBox(T(0, 0.5f, 0.75f), new Vector3(0.42f, 0.05f, 0.14f), GlowRed), 1.1f);
            for (int s = -1; s <= 1; s += 2)
            {
                var M = Matrix4x4.Scale(new Vector3(s, 1f, 1f));
                var W = M * TR(0, -0.05f, 0, 0, 0, -18);
                int wing = mb.VertexCount;
                mb.AddSlab(W, new[] { new Vector2(0.4f, 0.6f), new Vector2(2.7f, -0.5f), new Vector2(2.8f, -1.3f), new Vector2(0.4f, -1.7f) }, 0.2f, 0.03f, Obsidian, new[] { 0.24f, 0.08f, 0.08f, 0.22f });
                RustPatches(mb, wing, 2.2f, 0.55f, 2 + s);
                mb.AddSlab(W * T(0, 0.02f, 0), new[] { new Vector2(1.0f, 0.1f), new Vector2(2.0f, -0.45f), new Vector2(2.0f, -0.95f), new Vector2(1.0f, -0.75f) }, 0.2f, 0.02f, Rust, new[] { 0.24f, 0.16f, 0.16f, 0.24f });
                mb.AddSlab(W * TR(2.78f, -0.25f, 0, 0, 0, 90), new[] { new Vector2(-0.35f, -0.5f), new Vector2(0.35f, -0.9f), new Vector2(0.35f, -1.4f), new Vector2(-0.35f, -1.35f) }, 0.08f, 0.02f, Obsidian);
                Glow(mb, () => mb.AddBox(W * T(2.83f, -0.1f, -1.0f), new Vector3(0.03f, 0.05f, 0.35f), GlowRed), 1.1f);
                // twin nose blasters
                With(mb, 0.5f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 0.34f, -0.22f, 0.9f), new Vector3(s * 0.34f, -0.22f, 2.95f), 0.06f, 0.05f, 10, Gunmetal);
                    mb.AddPrism(TR(s * 0.34f, -0.22f, 1.3f, 90, 0, 0), 0.11f, 0.11f, 0.5f, 6, Slate);
                });
                Glow(mb, () => mb.AddPrism(TR(s * 0.34f, -0.22f, 2.96f, 90, 0, 0), 0.035f, 0.035f, 0.01f, 10, GlowRed), 1.1f);
                // exposed heat sinks
                With(mb, 0.35f, 1f, 0f, () => { for (int f = 0; f < 5; f++) mb.AddBox(T(s * 0.42f, 0.42f, -0.9f - f * 0.22f), new Vector3(0.16f, 0.3f, 0.05f), Gunmetal); });
            }
            Bell(mb, new Vector3(0, 0, -2.2f), 0.42f, 0.55f, GlowRed, Gunmetal);
            return SaveMesh(mb, "Enemy_PirateFighter");
        }

        static Mesh DroneMesh()
        {
            var mb = Hero(0.2f);
            mb.SmoothAngle = 30f; mb.Gloss = 0.35f;
            // faceted octahedral shell split around the eye, gunmetal equator ring with antenna spikes
            int shell = mb.VertexCount;
            mb.AddPrism(T(0, 0.42f, 0), 0.85f, 0f, 0.85f, 4, Obsidian);
            mb.AddPrism(TR(0, -0.42f, 0, 180, 0, 0), 0.85f, 0f, 0.85f, 4, Obsidian);
            RustPatches(mb, shell, 3.5f, 0.7f, 5);
            With(mb, 0.5f, 1f, 0f, () =>
            {
                // equator ring (tilted so it never crosses the eye) with four antenna spikes
                var ring = Matrix4x4.Rotate(Quaternion.Euler(-20f, 0f, 0f));
                mb.AddLathe(ring, new[] { new Vector2(0.92f, -0.06f), new Vector2(1.02f, -0.03f), new Vector2(1.02f, 0.03f), new Vector2(0.92f, 0.06f), new Vector2(0.92f, -0.06f) }, 24, Gunmetal);
                for (int i = 0; i < 4; i++)
                {
                    var q = Quaternion.Euler(-20f, i * 90f + 45f, 0f);
                    mb.AddTube(q * new Vector3(0, 0, 1.0f), q * new Vector3(0, -0.05f, 1.45f), 0.05f, 0.015f, 6, Gunmetal);
                }
            });
            // crimson optical core: iris ring + glowing lens
            With(mb, 0.6f, 1f, 0f, () => mb.AddLathe(TR(0, 0, 0.62f, 90, 0, 0), new[] { new Vector2(0.38f, -0.06f), new Vector2(0.4f, 0.06f), new Vector2(0.3f, 0.12f), new Vector2(0.24f, 0.04f) }, 20, Slate));
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0, 0.7f), Quaternion.identity, new Vector3(1f, 1f, 0.6f)), 0.24f, 2, GlowRed), 1.3f);
            Glow(mb, () => { for (int i = 0; i < 4; i++) mb.AddBox(Matrix4x4.TRS(Quaternion.Euler(0, 0, i * 90f) * new Vector3(0, 0.55f, -0.55f), Quaternion.Euler(0, 0, i * 90f), Vector3.one), new Vector3(0.18f, 0.04f, 0.04f), SensorRed); }, 1.6f);
            return SaveMesh(mb, "Enemy_DroneSwarm");
        }

        static Mesh GunshipMesh()
        {
            var mb = Hero(0.2f);
            mb.Gloss = 0.3f;
            int hull = mb.VertexCount;
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-8.1f, 2.2f, 1.3f, 4f), new MeshBuilder.Section(-6.5f, 2.6f, 1.55f, 4.5f),
                new MeshBuilder.Section(5.5f, 2.6f, 1.55f, 4.5f), new MeshBuilder.Section(8.6f, 1.8f, 1.1f, 4f, -0.2f),
                new MeshBuilder.Section(9.6f, 1.0f, 0.6f, 3f, -0.4f),
            }, 28, Obsidian, smoothing: 2);
            RustPatches(mb, hull, 0.45f, 0.58f, 11);
            for (int s = -1; s <= 1; s += 2)
            {
                int pod = mb.VertexCount;
                mb.AddLoft(T(s * 3.7f, -0.5f, 0), new[]
                {
                    new MeshBuilder.Section(-5.6f, 0.9f, 0.85f, 4f), new MeshBuilder.Section(-4.8f, 1.15f, 1.05f, 4.5f),
                    new MeshBuilder.Section(2.6f, 1.15f, 1.05f, 4.5f), new MeshBuilder.Section(3.6f, 0.7f, 0.6f, 4f),
                }, 20, Rust, smoothing: 2);
                mb.Paint(pod, p => p.y < -1.0f, Obsidian, 0.3f);
                // bolted armour plates and heat sink fins
                for (int k = 0; k < 3; k++)
                    mb.AddSlab(TR(s * 2.62f, 0.4f, -3.5f + k * 3.2f, 0, 0, s * 90), new[] { new Vector2(-0.8f, -1.3f), new Vector2(0.9f, -1.3f), new Vector2(0.9f, 1.2f), new Vector2(-0.6f, 1.3f) }, 0.12f, 0.03f, k == 1 ? Rust : Obsidian);
                With(mb, 0.35f, 1f, 0f, () => { for (int f = 0; f < 8; f++) mb.AddBox(T(s * 4.9f, -0.3f, -3.6f + f * 0.5f), new Vector3(0.2f, 1.1f, 0.08f), Gunmetal); });
                Glow(mb, () => mb.AddBox(T(s * 2.64f, 0.9f, 4.2f), new Vector3(0.04f, 0.14f, 2.2f), GlowRed), 1.1f);
            }
            // cow-catcher ram: V of raked blades under the nose
            for (int b = -3; b <= 3; b++)
                With(mb, 0.4f, 0.8f, 0.3f, () => mb.AddSlab(TR(b * 0.62f, -1.25f, 9.3f - Mathf.Abs(b) * 0.45f, -35, b * -14f, 0),
                    new[] { new Vector2(-0.12f, -0.9f), new Vector2(0.12f, -0.9f), new Vector2(0.12f, 0.7f), new Vector2(-0.12f, 1.0f) }, 0.12f, 0.03f, b % 2 == 0 ? Rust : Gunmetal));
            With(mb, 0.4f, 1f, 0f, () => mb.AddTube(new Vector3(-2.2f, -1.0f, 8.0f), new Vector3(2.2f, -1.0f, 8.0f), 0.14f, 0.14f, 10, Gunmetal));
            // top turret: armoured base, heavy twin cannon (muzzle at (0, 2.5, 4.6))
            With(mb, 0.4f, 0.6f, 0.5f, () => mb.AddLathe(T(0, 1.45f, 2f), new[] { new Vector2(1.5f, 0f), new Vector2(1.45f, 0.3f), new Vector2(1.1f, 0.55f), new Vector2(0f, 0.6f) }, 20, Obsidian));
            int head = mb.VertexCount;
            mb.AddLoft(T(0, 2.4f, 2f), new[]
            {
                new MeshBuilder.Section(-1.3f, 0.9f, 0.4f, 3.5f), new MeshBuilder.Section(-0.9f, 1.1f, 0.55f, 4f),
                new MeshBuilder.Section(0.9f, 1.0f, 0.5f, 4f), new MeshBuilder.Section(1.3f, 0.7f, 0.35f, 3.5f),
            }, 20, Rust, smoothing: 2);
            Glow(mb, () => mb.AddBox(T(0, 2.62f, 3.3f), new Vector3(0.9f, 0.08f, 0.05f), GlowRed), 1.1f);
            for (int s = -1; s <= 1; s += 2)
                With(mb, 0.5f, 1f, 0f, () =>
                {
                    mb.AddTube(new Vector3(s * 0.32f, 2.5f, 3.0f), new Vector3(s * 0.32f, 2.5f, 4.6f), 0.13f, 0.12f, 12, Gunmetal);
                    mb.AddPrism(TR(s * 0.32f, 2.5f, 4.45f, 90, 0, 0), 0.2f, 0.18f, 0.3f, 8, Slate);
                });
            for (int i = 0; i < 3; i++) Bell(mb, new Vector3(-2f + i * 2f, 0, -8.1f), 0.8f, 1.1f, GlowRed, Gunmetal);
            return SaveMesh(mb, "Enemy_PirateGunship");
        }

        static Mesh CrawlerMesh()
        {
            var mb = Organic();
            // segmented body: abdomen with glowing sac, thorax, head with mandibles and eyes
            mb.AddLoft(T(0, 0.62f, 0), new[]
            {
                new MeshBuilder.Section(-0.95f, 0.1f, 0.08f, 2.2f, 0.05f), new MeshBuilder.Section(-0.75f, 0.42f, 0.34f, 2.3f, 0.08f),
                new MeshBuilder.Section(-0.25f, 0.46f, 0.36f, 2.3f, 0.05f), new MeshBuilder.Section(0.05f, 0.3f, 0.24f, 2.2f),
                new MeshBuilder.Section(0.2f, 0.38f, 0.28f, 2.4f, 0.02f), new MeshBuilder.Section(0.55f, 0.34f, 0.24f, 2.4f, 0.05f),
                new MeshBuilder.Section(0.68f, 0.2f, 0.15f, 2.2f, 0.06f),
            }, 20, Chitin, smoothing: 3);
            for (int k = 0; k < 3; k++)
                mb.AddLathe(Matrix4x4.TRS(new Vector3(0, 0.7f, -0.65f + k * 0.22f), Quaternion.Euler(90, 0, 0), new Vector3(1.05f, 1f, 0.85f)),
                    new[] { new Vector2(0.38f, -0.04f), new Vector2(0.44f, 0f), new Vector2(0.38f, 0.04f) }, 20, AcidLime);
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.98f, -0.45f), Quaternion.identity, new Vector3(0.9f, 0.6f, 1.1f)), 0.24f, 2, BioLime), 1.3f);
            mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 0.7f, 0.82f), Quaternion.identity, new Vector3(1f, 0.75f, 0.9f)), 0.24f, 2, AcidLime);
            for (int s = -1; s <= 1; s += 2)
            {
                Glow(mb, () => { mb.AddSphere(T(s * 0.1f, 0.8f, 1.0f), 0.05f, 1, Gold); mb.AddSphere(T(s * 0.17f, 0.74f, 0.96f), 0.035f, 1, Gold); }, 1.8f);
                Limb(mb, new Vector3(s * 0.1f, 0.6f, 0.95f), new Vector3(s * 0.05f, 0.48f, 1.22f), 0.05f, 0.015f, Chitin, 1f);           // mandible
                // forelegs raised as scythes, then two walking pairs
                InsectLeg(mb, new Vector3(s * 0.25f, 0.68f, 0.45f), new Vector3(s * 0.45f, 1.05f, 0.75f), new Vector3(s * 0.32f, 0.55f, 1.15f), 0.07f, Chitin, AcidLime);
                InsectLeg(mb, new Vector3(s * 0.3f, 0.6f, 0.25f), new Vector3(s * 0.8f, 0.85f, 0.45f), new Vector3(s * 1.0f, 0.02f, 0.65f), 0.07f, Chitin, AcidLime);
                InsectLeg(mb, new Vector3(s * 0.32f, 0.6f, 0.05f), new Vector3(s * 0.85f, 0.88f, 0.0f), new Vector3(s * 1.1f, 0.02f, -0.05f), 0.07f, Chitin, AcidLime);
                InsectLeg(mb, new Vector3(s * 0.3f, 0.6f, -0.2f), new Vector3(s * 0.8f, 0.85f, -0.5f), new Vector3(s * 0.95f, 0.02f, -0.8f), 0.07f, Chitin, AcidLime);
            }
            return SaveMesh(mb, "Enemy_Crawler");
        }

        static Mesh SpitterMesh()
        {
            var mb = Organic();
            // tall bulbous body (loft along y), banded chitin, inflatable glowing throat sac, mortar mouth tube
            mb.AddLoft(TR(0, 0, 0, -90, 0, 0), new[]
            {
                new MeshBuilder.Section(0.55f, 0.2f, 0.2f, 2.2f), new MeshBuilder.Section(0.8f, 0.5f, 0.45f, 2.3f),
                new MeshBuilder.Section(1.25f, 0.6f, 0.55f, 2.3f), new MeshBuilder.Section(1.7f, 0.42f, 0.4f, 2.2f, 0.05f),
                new MeshBuilder.Section(1.95f, 0.26f, 0.26f, 2.2f, 0.12f), new MeshBuilder.Section(2.1f, 0f, 0f, 2f, 0.12f),
            }, 22, AcidLime, smoothing: 3);
            for (int k = 0; k < 4; k++)
                mb.AddLathe(T(0, 0.85f + k * 0.22f, 0), new[] { new Vector2(0.48f + (k == 1 || k == 2 ? 0.1f : 0f), -0.04f), new Vector2(0.55f + (k == 1 || k == 2 ? 0.1f : 0f), 0f), new Vector2(0.48f + (k == 1 || k == 2 ? 0.1f : 0f), 0.04f) }, 22, Chitin);
            Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 1.35f, 0.42f), Quaternion.identity, new Vector3(1f, 0.9f, 0.8f)), 0.36f, 2, BioLime), 1.1f);
            Limb(mb, new Vector3(0, 1.7f, 0.25f), new Vector3(0, 1.62f, 0.9f), 0.2f, 0.15f, Chitin, 1f);
            Glow(mb, () => mb.AddLathe(TR(0, 1.62f, 0.9f, 90, 0, 0), new[] { new Vector2(0.13f, 0f), new Vector2(0.05f, 0.02f), new Vector2(0f, 0.025f) }, 14, BioLime), 1.8f);
            for (int s = -1; s <= 1; s += 2) Glow(mb, () => mb.AddSphere(T(s * 0.16f, 1.92f, 0.22f), 0.05f, 1, Gold), 1.8f);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 60f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                InsectLeg(mb, dir * 0.3f + Vector3.up * 0.75f, dir * 0.75f + Vector3.up * 1.0f, dir * 0.9f, 0.1f, Chitin, AcidLime);
            }
            return SaveMesh(mb, "Enemy_Spitter");
        }

        static Mesh CritterMesh()
        {
            var mb = Organic();
            // manta: flattened body, membrane wings with glowing veins, whip tail
            mb.AddLoft(Matrix4x4.identity, new[]
            {
                new MeshBuilder.Section(-0.6f, 0.15f, 0.08f, 2.2f), new MeshBuilder.Section(-0.3f, 0.36f, 0.16f, 2.4f),
                new MeshBuilder.Section(0.35f, 0.34f, 0.17f, 2.4f), new MeshBuilder.Section(0.7f, 0.12f, 0.08f, 2.2f, -0.02f), new MeshBuilder.Section(0.8f, 0f, 0f, 2f, -0.02f),
            }, 18, Pink, smoothing: 2);
            for (int s = -1; s <= 1; s += 2)
            {
                var M = Matrix4x4.Scale(new Vector3(s, 1f, 1f));
                mb.AddSlab(M * TR(0, 0.04f, 0, 0, 0, 10), new[] { new Vector2(0.25f, 0.4f), new Vector2(1.0f, 0.15f), new Vector2(1.6f, -0.35f), new Vector2(1.2f, -0.45f), new Vector2(0.25f, -0.4f) },
                    0.05f, 0.015f, Chitin, new[] { 0.08f, 0.04f, 0.02f, 0.03f, 0.08f });
                for (int v = 0; v < 3; v++)
                    Glow(mb, () => mb.AddTube(M.MultiplyPoint3x4(new Vector3(0.3f, 0.07f, 0.1f - v * 0.18f)), M.MultiplyPoint3x4(new Vector3(1.1f + v * 0.15f, 0.21f + v * 0.02f, -0.1f - v * 0.12f)), 0.015f, 0.008f, 5, BioLime), 1.2f);
                Glow(mb, () => mb.AddSphere(T(s * 0.12f, 0.1f, 0.62f), 0.04f, 1, Gold), 1.8f);
            }
            Limb(mb, new Vector3(0, 0, -0.55f), new Vector3(0, 0.05f, -1.4f), 0.06f, 0.015f, Chitin, 1f);
            return SaveMesh(mb, "Enemy_FlyingCritter");
        }

        static Mesh GuardianMesh()
        {
            var mb = Organic();
            // armoured beast: heavy body, layered frontal plates, carapace ridge, glowing rear egg sacs, four stout legs
            mb.AddLoft(T(0, 2.0f, 0), new[]
            {
                new MeshBuilder.Section(-1.4f, 0.6f, 0.6f, 2.3f), new MeshBuilder.Section(-0.9f, 1.25f, 1.05f, 2.5f),
                new MeshBuilder.Section(0.4f, 1.3f, 1.1f, 2.6f, 0.1f), new MeshBuilder.Section(1.1f, 0.9f, 0.85f, 2.4f, 0.4f),
                new MeshBuilder.Section(1.35f, 0.4f, 0.4f, 2.2f, 0.6f),
            }, 24, Chitin, smoothing: 3);
            for (int k = 0; k < 4; k++)
                mb.AddLathe(Matrix4x4.TRS(new Vector3(0, 2.25f + k * 0.02f, -0.8f + k * 0.55f), Quaternion.Euler(90, 0, 0), new Vector3(1.15f, 1f, 1f)),
                    new[] { new Vector2(1.05f, -0.12f), new Vector2(1.2f, 0f), new Vector2(1.05f, 0.12f) }, 24, AcidLime, 200f, 140f);
            for (int p = 0; p < 3; p++)
                mb.AddSlab(TR(0, 2.1f + p * 0.42f, 1.3f - p * 0.08f, -70 + p * 10, 0, 0), new[] { new Vector2(-0.9f + p * 0.15f, -0.3f), new Vector2(0.9f - p * 0.15f, -0.3f), new Vector2(0.7f - p * 0.15f, 0.35f), new Vector2(-0.7f + p * 0.15f, 0.35f) },
                    0.18f, 0.05f, p == 1 ? AcidLime : Chitin);
            mb.AddSphere(Matrix4x4.TRS(new Vector3(0, 3.3f, 1.05f), Quaternion.identity, new Vector3(1f, 0.8f, 1f)), 0.45f, 2, AcidLime);
            for (int s = -1; s <= 1; s += 2)
            {
                Limb(mb, new Vector3(s * 0.22f, 3.55f, 1.1f), new Vector3(s * 0.45f, 4.1f, 1.45f), 0.12f, 0.03f, Chitin, 1f);              // horn
                Glow(mb, () => mb.AddSphere(T(s * 0.2f, 3.4f, 1.45f), 0.08f, 1, Gold), 1.8f);
                InsectLeg(mb, new Vector3(s * 0.9f, 1.8f, 0.7f), new Vector3(s * 1.6f, 2.0f, 1.1f), new Vector3(s * 1.5f, 0.02f, 1.3f), 0.25f, Chitin, AcidLime);
                InsectLeg(mb, new Vector3(s * 0.9f, 1.8f, -0.6f), new Vector3(s * 1.6f, 2.0f, -0.9f), new Vector3(s * 1.45f, 0.02f, -1.2f), 0.25f, Chitin, AcidLime);
            }
            for (int e = 0; e < 5; e++)
            {
                float a = e * 1.26f;
                Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.45f, 2.0f + Mathf.Sin(a) * 0.35f, -1.5f - (e % 2) * 0.12f), Quaternion.identity, new Vector3(1f, 1f, 1.2f)), 0.28f, 2, BioLime), 1.2f);
            }
            return SaveMesh(mb, "Enemy_NestGuardian");
        }

        static GameObject BuildEnemy(EnemyKind k)
        {
            GameObject go;
            Mesh mesh;
            Vector3 muzzle = new Vector3(0, 0, 2f);
            switch (k)
            {
                case EnemyKind.PirateFighter:
                    go = EnemyRoot(k, Team.Pirates, 45);
                    mesh = PirateFighterMesh();
                    Box(go, Vector3.zero, new Vector3(5f, 1.6f, 4.6f));
                    muzzle = new Vector3(0, 0, 3f);
                    break;
                case EnemyKind.DroneSwarm:
                    go = EnemyRoot(k, Team.Pirates, 18);
                    mesh = DroneMesh();
                    go.AddComponent<SphereCollider>().radius = 1.3f;
                    muzzle = new Vector3(0, 0, 1.3f);
                    break;
                case EnemyKind.PirateGunship:
                    go = EnemyRoot(k, Team.Pirates, 260);
                    mesh = GunshipMesh();
                    Box(go, new Vector3(0, 0, 0.5f), new Vector3(9.5f, 3.4f, 17f));
                    muzzle = new Vector3(0, 2.5f, 4.6f);
                    break;
                case EnemyKind.Crawler:
                    go = EnemyRoot(k, Team.Aliens, 35);
                    mesh = Prefer(ImportedMesh(ModelDir + "/Aliens/Enemy_Crawler.fbx", "Enemy_Crawler"), CrawlerMesh);
                    Box(go, new Vector3(0, 0.6f, 0), new Vector3(1.4f, 1f, 1.8f));
                    muzzle = new Vector3(0, 0.7f, 0.9f);
                    break;
                case EnemyKind.Spitter:
                    go = EnemyRoot(k, Team.Aliens, 30);
                    mesh = Prefer(ImportedMesh(ModelDir + "/Aliens/Enemy_Spitter.fbx", "Enemy_Spitter"), SpitterMesh);
                    Box(go, new Vector3(0, 1.1f, 0), new Vector3(1.4f, 2.2f, 1.4f));
                    muzzle = new Vector3(0, 1.6f, 0.9f);
                    break;
                case EnemyKind.FlyingCritter:
                    go = EnemyRoot(k, Team.Aliens, 22);
                    mesh = Prefer(ImportedMesh(ModelDir + "/Aliens/Enemy_FlyingCritter.fbx", "Enemy_FlyingCritter"), CritterMesh);
                    go.AddComponent<SphereCollider>().radius = 0.9f;
                    go.GetComponent<EnemyBrain>().HoverHeight = 5f;
                    muzzle = new Vector3(0, 0, 0.8f);
                    break;
                default: // NestGuardian
                    go = EnemyRoot(k, Team.Aliens, 220);
                    mesh = Prefer(ImportedMesh(ModelDir + "/Aliens/Enemy_NestGuardian.fbx", "Enemy_NestGuardian"), GuardianMesh);
                    Box(go, new Vector3(0, 2f, 0), new Vector3(3.4f, 4f, 2.6f));
                    muzzle = new Vector3(0, 2f, 1.6f);
                    break;
            }
            var brain = go.GetComponent<EnemyBrain>();
            var vis = Visual(go.transform, "Visual", mesh, MatHull);
            brain.Visual = vis.transform;
            brain.Muzzle = Child(go.transform, "Muzzle", muzzle).transform;
            return SavePrefab(go);
        }

        /// <summary>Elongated crystal shard along local +Z: hexagonal prism with pointed ends (faceted).</summary>
        static void Shard(MeshBuilder mb, Matrix4x4 m, float len, float r, int sides, Color c)
        {
            mb.AddLoft(m, new[]
            {
                new MeshBuilder.Section(-len * 0.5f, 0f, 0f, 2f), new MeshBuilder.Section(-len * 0.32f, r, r, 2f),
                new MeshBuilder.Section(len * 0.3f, r * 0.92f, r * 0.92f, 2f), new MeshBuilder.Section(len * 0.5f, 0f, 0f, 2f),
            }, sides, c);
        }

        /// <summary>
        /// Void Mothership (DESIGN.md): fractured basalt monolith of interlocking crystal shards around a central spine,
        /// violet-white seams and vents, cradles for the three reactor pods. Faceted on purpose (SmoothAngle low).
        /// </summary>
        static Mesh BossMesh()
        {
            var mb = Hero(0.1f);
            mb.SmoothAngle = 20f; mb.Panel = 0.6f; mb.Gloss = 0.75f; mb.Metal = 0.3f;
            var rng = new System.Random(707);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // central spine and the big slabs of the hull mass (stay inside the gameplay colliders)
            Shard(mb, Matrix4x4.identity, 96f, 9f, 6, Basalt);
            Shard(mb, Matrix4x4.TRS(new Vector3(0, 8f, -5f), Quaternion.Euler(-4, 0, 30), Vector3.one), 56f, 4.5f, 5, Basalt);
            Shard(mb, Matrix4x4.TRS(new Vector3(0, -6f, 10f), Quaternion.Euler(6, 0, 0), new Vector3(1.4f, 0.7f, 1f)), 60f, 6f, 6, Basalt);
            for (int s = -1; s <= 1; s += 2)
            {
                Shard(mb, Matrix4x4.TRS(new Vector3(s * 16f, -2f, -10f), Quaternion.Euler(0, s * 6f, s * 15f), new Vector3(1.6f, 0.45f, 1f)), 34f, 6f, 6, Basalt);
                Shard(mb, Matrix4x4.TRS(new Vector3(s * 10f, 3f, 14f), Quaternion.Euler(-6, s * -8f, s * 25f), Vector3.one), 44f, 4.5f, 5, Basalt);
                Shard(mb, Matrix4x4.TRS(new Vector3(s * 9f, -1f, 36f), Quaternion.Euler(0, s * -10f, 0), Vector3.one), 26f, 3.5f, 6, Basalt);
                // reactor cradles: claws around each side pod at (+-21, 2, 0)
                for (int c = 0; c < 4; c++)
                {
                    float a = c * 90f + 45f;
                    var dir = Quaternion.Euler(0, 0, a) * Vector3.up;
                    Shard(mb, Matrix4x4.TRS(new Vector3(s * 21f, 2f, 0f) + dir * 4.6f, Quaternion.LookRotation(Vector3.forward + dir * 0.4f, dir), Vector3.one), 9f, 0.9f, 5, Basalt);
                }
            }
            for (int c = 0; c < 4; c++)
            {
                float a = c * 90f;
                var dir = Quaternion.Euler(0, 0, a) * Vector3.right;
                Shard(mb, Matrix4x4.TRS(new Vector3(0f, 14f, 18f) + dir * 4.6f, Quaternion.LookRotation(Vector3.forward + dir * 0.4f, dir), Vector3.one), 9f, 0.9f, 5, Basalt);
            }
            // smaller orbiting fragments, kept close to the hull
            for (int i = 0; i < 26; i++)
            {
                var pos = new Vector3(R(-14f, 14f), R(-7f, 9f), R(-38f, 42f));
                var rot = Quaternion.Euler(R(-25f, 25f), R(-30f, 30f), R(0f, 180f));
                Shard(mb, Matrix4x4.TRS(pos, rot, Vector3.one), R(6f, 14f), R(0.8f, 2.2f), rng.Next(4, 7), Basalt);
            }
            // violet seams, vents and the forward weapon prongs (muzzles at z 50)
            With(mb, 0.9f, 0f, 0f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.AddTube(new Vector3(s * 7.6f, 1.5f, -30f), new Vector3(s * 7.6f, 1.5f, 32f), 0.35f, 0.35f, 6, VoidViolet);
                    mb.AddTube(new Vector3(s * 3.5f, 6.8f, -20f), new Vector3(s * 3.5f, 6.8f, 22f), 0.25f, 0.25f, 6, VoidViolet);
                }
            }, 1.6f);
            for (int s = -1; s <= 1; s += 2)
            {
                Shard(mb, Matrix4x4.TRS(new Vector3(s * 10f, 0f, 44f), Quaternion.identity, Vector3.one), 14f, 1.6f, 6, Basalt);
                Glow(mb, () => mb.AddSphere(T(s * 10f, 0f, 50.2f), 0.9f, 2, VoidViolet), 2f);
            }
            for (int i = 0; i < 4; i++)
            {
                var at = new Vector3(-9f + i * 6f, 0f, -38f);
                Glow(mb, () => mb.AddLathe(Matrix4x4.TRS(at, Quaternion.FromToRotation(Vector3.up, Vector3.back), Vector3.one),
                    new[] { new Vector2(2.6f, 0f), new Vector2(2.2f, 0.4f), new Vector2(0f, 0.6f) }, 6, VoidViolet), 1.8f);
            }
            return SaveMesh(mb, "Boss_Warden");
        }

        static GameObject BuildBoss()
        {
            var go = NetRoot("Boss_Warden", true);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var hc = go.AddComponent<HealthComponent>();
            hc.MaxHealth = 3000f;
            hc.ImmuneHint = "The Warden's hull is shielded - destroy the 3 glowing core pods first!";
            var so = new SerializedObject(hc);
            so.FindProperty("team").enumValueIndex = (int)Team.Pirates;
            so.ApplyModifiedPropertiesWithoutUndo();
            var boss = go.AddComponent<BossBrain>();

            Visual(go.transform, "Hull", BossMesh(), MatHull);
            Box(go, new Vector3(0, 0, 5f), new Vector3(26f, 14f, 90f));
            Box(go, new Vector3(0, -2f, -10f), new Vector3(54f, 4f, 30f));
            Box(go, new Vector3(0, 10f, -5f), new Vector3(8f, 6f, 50f));

            var podMesh = new MeshBuilder();
            podMesh.AddRock(Matrix4x4.identity, 4.4f, 33, Color.white); // big enough to read (and hit) from the stand-off range
            var podM = SaveMesh(podMesh, "Boss_Pod");
            var podMat = Mat("VW_BossPod", "VOIDWARD/Unlit");
            podMat.SetColor("_BaseColor", new Color(2.6f, 2.2f, 3.8f)); // violet-white weak points (DESIGN.md)
            Vector3[] podPos = { new Vector3(-21f, 2f, 0f), new Vector3(21f, 2f, 0f), new Vector3(0f, 14f, 18f) };
            var pods = new HealthComponent[3];
            for (int i = 0; i < 3; i++)
            {
                var p = Child(go.transform, "CorePod" + i, podPos[i]);
                Visual(p.transform, "Glow", podM, podMat);
                p.AddComponent<SphereCollider>().radius = 5.2f;
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

        /// <summary>Rock builder: smooth-ish boulders (no panel seams) for nodes, ruins and debris.</summary>
        static MeshBuilder Stone() => new MeshBuilder { Bevel = 0.15f, SmoothAngle = 38f, Panel = 0f, Gloss = 0.12f };

        static GameObject BuildPickup()
        {
            var go = NetRoot("WorldPickup", false);
            var p = go.AddComponent<WorldPickup>();
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true; sc.radius = 0.7f; sc.center = new Vector3(0, 0.5f, 0);
            // Item capsule: tinted glossy core (WorldPickup tints _BaseColor) inside a gunmetal cage with end caps.
            var mb = Hero(0.2f);
            With(mb, 0.85f, 0.2f, 0f, () => mb.AddLathe(Matrix4x4.identity, new[]
            {
                new Vector2(0f, -0.22f), new Vector2(0.1f, -0.2f), new Vector2(0.15f, -0.12f), new Vector2(0.15f, 0.12f), new Vector2(0.1f, 0.2f), new Vector2(0f, 0.22f),
            }, 16, Color.white), 0.5f);
            With(mb, 0.6f, 1f, 0f, () =>
            {
                mb.AddLathe(T(0, -0.2f, 0), new[] { new Vector2(0f, -0.05f), new Vector2(0.17f, -0.05f), new Vector2(0.19f, 0f), new Vector2(0.17f, 0.04f), new Vector2(0.12f, 0.05f) }, 16, new Color(0.75f, 0.77f, 0.82f));
                mb.AddLathe(T(0, 0.2f, 0), new[] { new Vector2(0.12f, -0.05f), new Vector2(0.17f, -0.04f), new Vector2(0.19f, 0f), new Vector2(0.17f, 0.05f), new Vector2(0f, 0.05f) }, 16, new Color(0.75f, 0.77f, 0.82f));
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 120f * Mathf.Deg2Rad;
                    mb.AddTube(new Vector3(Mathf.Cos(a) * 0.17f, -0.18f, Mathf.Sin(a) * 0.17f), new Vector3(Mathf.Cos(a) * 0.17f, 0.18f, Mathf.Sin(a) * 0.17f), 0.018f, 0.018f, 6, new Color(0.75f, 0.77f, 0.82f));
                }
            });
            var vis = Child(go.transform, "Visual", new Vector3(0, 0.6f, 0));
            Visual(vis.transform, "Mesh", SaveMesh(mb, "Pickup"), MatHull);
            Set(p, "visual", vis.transform);
            return SavePrefab(go);
        }

        static GameObject BuildCrate()
        {
            var go = NetRoot("LootCrate", false);
            var c = go.AddComponent<LootCrate>();
            // Rugged supply crate: blue body with white corner frames, rubber bumpers, side handles, gold latch, status light.
            var mb = Hero(0.12f);
            mb.AddBox(T(0, 0.37f, 0), new Vector3(1.36f, 0.66f, 0.86f), Blue);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.AddBox(T(s * 0.6f, 0.37f, 0), new Vector3(0.18f, 0.72f, 0.92f), Chalk);
                With(mb, 0.15f, 0f, 0f, () => { mb.Bevel = 0.3f; mb.AddBox(T(s * 0.62f, 0.06f, 0), new Vector3(0.24f, 0.12f, 0.96f), Rubber); mb.Bevel = 0.12f; });
                With(mb, 0.5f, 1f, 0f, () => Polyline(mb, new[] { new Vector3(s * 0.7f, 0.42f, -0.2f), new Vector3(s * 0.76f, 0.42f, -0.15f), new Vector3(s * 0.76f, 0.42f, 0.15f), new Vector3(s * 0.7f, 0.42f, 0.2f) }, 0.022f, 6, Gunmetal));
            }
            With(mb, 0.25f, 0.2f, 0.4f, () => mb.AddBox(T(0, 0.37f, 0.44f), new Vector3(0.9f, 0.4f, 0.04f), Navy));
            Hazard(mb, TR(0, 0.12f, 0.45f, 90, 0, 0), 1.0f, 0.1f, 8, 0.015f);
            With(mb, 0.75f, 0.9f, 0f, () => mb.AddBox(T(0, 0.66f, 0.45f), new Vector3(0.22f, 0.1f, 0.06f), Gold));
            Glow(mb, () => mb.AddBox(T(0, 0.5f, 0.465f), new Vector3(0.36f, 0.05f, 0.02f), Cyan), 1.2f);
            Visual(go.transform, "Body", SaveMesh(mb, "Crate_Body"), MatHull);
            AddLight(go.transform, "Glow", new Vector3(0, 0.9f, 0.7f), Cyan, 2.5f, 4f);
            var lid = Child(go.transform, "LidPivot", new Vector3(0, 0.7f, -0.45f));
            var lm = Hero(0.12f);
            lm.AddBox(T(0, 0.08f, 0.45f), new Vector3(1.44f, 0.16f, 0.94f), Chalk);
            With(lm, Paint, 0f, 0.6f, () => lm.AddBox(T(0, 0.17f, 0.45f), new Vector3(0.9f, 0.05f, 0.6f), Blue));
            With(lm, 0.4f, 1f, 0f, () => { for (int r = 0; r < 4; r++) lm.AddBox(T(-0.27f + r * 0.18f, 0.205f, 0.45f), new Vector3(0.06f, 0.03f, 0.5f), Gunmetal); });
            Visual(lid.transform, "Lid", SaveMesh(lm, "Crate_Lid"), MatHull);
            Box(go, new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 1f));
            Set(c, "lid", lid.transform);
            return SavePrefab(go);
        }

        static GameObject BuildNode()
        {
            var go = NetRoot("ResourceNode", false);
            var n = go.AddComponent<ResourceNode>();
            Color stone = new Color(0.42f, 0.44f, 0.52f), oreCol = new Color(0.92f, 0.43f, 0.15f);

            // Ore: weathered boulder with glossy copper veins and chunks
            var ore = Stone();
            ore.AddRock(Matrix4x4.TRS(new Vector3(0, 0.6f, 0), Quaternion.Euler(0, 20, 0), new Vector3(1.2f, 0.85f, 1f)), 1f, 41, stone);
            ore.AddRock(Matrix4x4.TRS(new Vector3(0.55f, 0.25f, -0.55f), Quaternion.identity, Vector3.one), 0.55f, 46, stone * 0.9f);
            With(ore, 0.85f, 1f, 0f, () =>
            {
                ore.AddRock(Matrix4x4.TRS(new Vector3(0.7f, 0.5f, 0.45f), Quaternion.identity, Vector3.one), 0.4f, 42, oreCol);
                ore.AddRock(Matrix4x4.TRS(new Vector3(-0.65f, 0.75f, -0.25f), Quaternion.identity, Vector3.one), 0.34f, 43, oreCol);
                ore.AddRock(Matrix4x4.TRS(new Vector3(0.05f, 1.25f, 0.2f), Quaternion.identity, Vector3.one), 0.28f, 44, oreCol);
                ore.AddRock(Matrix4x4.TRS(new Vector3(-0.2f, 0.45f, 0.85f), Quaternion.identity, Vector3.one), 0.22f, 47, oreCol);
            });
            Glow(ore, () =>
            {
                ore.AddTube(new Vector3(0.62f, 0.62f, 0.5f), new Vector3(0.2f, 1.1f, 0.62f), 0.035f, 0.02f, 5, new Color(1f, 0.55f, 0.2f));
                ore.AddTube(new Vector3(-0.55f, 0.8f, -0.2f), new Vector3(-0.3f, 1.2f, 0.35f), 0.03f, 0.015f, 5, new Color(1f, 0.55f, 0.2f));
            }, 0.9f);

            // Crystal: cluster of faceted prisms with glowing cores growing from a rock base
            var crystal = Stone();
            crystal.AddRock(Matrix4x4.TRS(new Vector3(0, 0.15f, 0), Quaternion.identity, new Vector3(1.3f, 0.45f, 1.3f)), 0.6f, 45, stone);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + (i % 2) * 17f;
                var m = TR(Mathf.Cos(a * Mathf.Deg2Rad) * 0.38f, 0.55f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.38f, Mathf.Sin(a) * 22f, a, 20f);
                Color col = i % 2 == 0 ? Cyan : new Color(0.55f, 0.45f, 1f);
                With(crystal, 0.95f, 0.1f, 0f, () => Shard(crystal, m * TR(0, 0, 0, -90, 0, 0), 1.4f + (i % 3) * 0.5f, 0.2f, 6, col), 0.35f);
            }
            With(crystal, 0.95f, 0.1f, 0f, () => Shard(crystal, TR(0, 1.0f, 0, -90, 0, 0), 2.2f, 0.3f, 6, new Color(0.6f, 0.95f, 1f)), 0.5f);

            // Scrap: pile of hull plates, a torn pipe and a small tank
            var scrap = Hero(0.12f);
            int sStart = scrap.VertexCount;
            scrap.AddSlab(TR(0, 0.18f, 0, 0, 20, 8), new[] { new Vector2(-0.8f, -0.5f), new Vector2(0.75f, -0.6f), new Vector2(0.85f, 0.4f), new Vector2(-0.6f, 0.55f) }, 0.12f, 0.03f, Gunmetal);
            scrap.AddSlab(TR(0.25f, 0.45f, 0.1f, 25, -15, 30), new[] { new Vector2(-0.5f, -0.35f), new Vector2(0.45f, -0.3f), new Vector2(0.4f, 0.35f), new Vector2(-0.45f, 0.3f) }, 0.08f, 0.02f, Blue);
            scrap.AddSlab(TR(-0.35f, 0.55f, 0.25f, -20, 60, -35), new[] { new Vector2(-0.35f, -0.25f), new Vector2(0.35f, -0.3f), new Vector2(0.3f, 0.3f), new Vector2(-0.3f, 0.25f) }, 0.07f, 0.02f, Chalk);
            With(scrap, 0.5f, 1f, 0f, () =>
            {
                scrap.AddTube(new Vector3(-0.6f, 0.2f, -0.3f), new Vector3(0.1f, 0.95f, -0.45f), 0.09f, 0.09f, 10, Gunmetal);
                scrap.AddLathe(TR(0.55f, 0.35f, -0.25f, 0, 0, 80), new[] { new Vector2(0f, -0.3f), new Vector2(0.17f, -0.27f), new Vector2(0.2f, -0.18f), new Vector2(0.2f, 0.18f), new Vector2(0.17f, 0.27f), new Vector2(0f, 0.3f) }, 12, Gold);
            });
            scrap.Paint(sStart, p => Mathf.PerlinNoise(p.x * 3f + 5f, p.z * 3f + p.y * 2f) > 0.62f, Rust, 0.15f, 0.3f);

            // Fuel: pressurised canister with bands, valve, hazard stripe and gauge
            var fuel = Hero(0.15f);
            With(fuel, 0.65f, 0.3f, 0.3f, () => fuel.AddLathe(T(0, 0.65f, 0), new[]
            {
                new Vector2(0f, -0.6f), new Vector2(0.32f, -0.58f), new Vector2(0.42f, -0.48f), new Vector2(0.44f, -0.35f), new Vector2(0.44f, 0.35f),
                new Vector2(0.42f, 0.48f), new Vector2(0.32f, 0.58f), new Vector2(0.14f, 0.62f), new Vector2(0f, 0.62f),
            }, 20, Gold));
            With(fuel, Paint, 0f, 0.4f, () =>
            {
                fuel.AddLathe(T(0, 0.35f, 0), new[] { new Vector2(0.44f, -0.06f), new Vector2(0.47f, -0.04f), new Vector2(0.47f, 0.04f), new Vector2(0.44f, 0.06f) }, 20, Chalk);
                fuel.AddLathe(T(0, 0.95f, 0), new[] { new Vector2(0.44f, -0.06f), new Vector2(0.47f, -0.04f), new Vector2(0.47f, 0.04f), new Vector2(0.44f, 0.06f) }, 20, Chalk);
            });
            With(fuel, 0.5f, 1f, 0f, () =>
            {
                fuel.AddTube(new Vector3(0, 1.25f, 0), new Vector3(0, 1.42f, 0), 0.07f, 0.07f, 10, Gunmetal);
                fuel.AddTube(new Vector3(-0.14f, 1.4f, 0), new Vector3(0.14f, 1.4f, 0), 0.03f, 0.03f, 8, Red);
                fuel.AddLathe(T(0, 0.04f, 0), new[] { new Vector2(0.38f, -0.04f), new Vector2(0.48f, -0.04f), new Vector2(0.5f, 0.04f), new Vector2(0.4f, 0.06f) }, 20, Rubber);
            });
            Hazard(fuel, TR(0, 0.65f, 0.445f, 90, 0, 0), 0.4f, 0.12f, 4, 0.015f);
            Glow(fuel, () => fuel.AddBox(T(0.2f, 0.8f, 0.41f), new Vector3(0.1f, 0.24f, 0.04f), Cyan), 1.2f);

            var variants = new List<Transform>();
            foreach (var (name, mb) in new[] { ("Ore", ore), ("Crystal", crystal), ("Scrap", scrap), ("Fuel", fuel) })
            {
                var v = Child(go.transform, name, Vector3.zero);
                Visual(v.transform, "Mesh", SaveMesh(mb, "Node_" + name), MatHull);
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
            // Hive mound: ribbed chitin dome with a glowing maw, clustered egg sacs and curved spines.
            var mb = Organic();
            mb.AddLathe(Matrix4x4.identity, new[]
            {
                new Vector2(3.0f, 0f), new Vector2(2.9f, 0.5f), new Vector2(2.5f, 1.3f), new Vector2(1.8f, 2.0f), new Vector2(1.1f, 2.4f), new Vector2(0.7f, 2.5f),
                new Vector2(0.55f, 2.35f), new Vector2(0.5f, 2.0f),
            }, 28, Chitin);
            for (int r = 0; r < 8; r++)
            {
                float a = r * 45f + 10f;
                var q = Quaternion.Euler(0, a, 0);
                Limb(mb, q * new Vector3(0, 0.1f, 2.9f), q * new Vector3(0, 1.9f, 1.75f), 0.22f, 0.14f, AcidLime, 0.8f);
            }
            Glow(mb, () => mb.AddLathe(T(0, 2.0f, 0), new[] { new Vector2(0.5f, 0f), new Vector2(0.3f, -0.1f), new Vector2(0f, -0.15f) }, 20, Pink), 1.2f);
            for (int e = 0; e < 9; e++)
            {
                float a = e * 40f * Mathf.Deg2Rad, rr = 3.0f + (e % 3) * 0.35f;
                Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * rr, 0.35f + (e % 2) * 0.15f, Mathf.Sin(a) * rr), Quaternion.Euler(0, e * 40f, 0), new Vector3(1f, 1.25f, 1f)), 0.38f - (e % 3) * 0.06f, 2, BioLime), 0.8f);
            }
            for (int sp = 0; sp < 5; sp++)
            {
                float a = sp * 72f * Mathf.Deg2Rad;
                var basePos = new Vector3(Mathf.Cos(a) * 1.6f, 1.9f, Mathf.Sin(a) * 1.6f);
                Polyline(mb, new[] { basePos, basePos + new Vector3(Mathf.Cos(a) * 0.5f, 1.0f, Mathf.Sin(a) * 0.5f), basePos + new Vector3(Mathf.Cos(a) * 1.3f, 1.4f, Mathf.Sin(a) * 1.3f) }, 0.1f, 8, Chitin);
            }
            Visual(go.transform, "Mound", SaveMesh(mb, "Nest"), MatHull);
            AddLight(go.transform, "Glow", new Vector3(0, 3.6f, 0), Pink, 12f, 12f);
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 2.6f; sc.center = new Vector3(0, 1f, 0);
            return SavePrefab(go);
        }

        static GameObject BuildRuin()
        {
            var go = NetRoot("Ruin", false);
            // Precursor shrine: tiled plaza, six carved obelisks with glowing runes, a fallen lintel, central altar crystal.
            var mb = Stone();
            Color stone = new Color(0.78f, 0.8f, 0.88f), dark = new Color(0.55f, 0.58f, 0.66f);
            mb.Panel = 0.5f;
            mb.AddBox(T(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f), dark);
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                    if ((x + z) % 2 == 0 && Mathf.Abs(x) + Mathf.Abs(z) < 6) mb.AddBox(T(x * 1.1f, 0.32f, z * 1.1f), new Vector3(1.0f, 0.06f, 1.0f), stone);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float h = 2.5f + (i % 3) * 1.4f;
                var m = TR(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f, 0, -i * 60f + 90f, (i % 2) * 6f);
                mb.AddSlab(m * TR(0, h * 0.5f, 0, 90, 0, 0), new[] { new Vector2(-0.5f, h * 0.5f), new Vector2(0.5f, h * 0.5f), new Vector2(0.38f, -h * 0.5f), new Vector2(0f, -h * 0.5f - 0.35f), new Vector2(-0.38f, -h * 0.5f) },
                    1f, 0.08f, stone, new[] { 1f, 1f, 0.8f, 0.7f, 0.8f });
                for (int r = 0; r < 3; r++)
                    Glow(mb, () => mb.AddBox(m * T(0, 0.8f + r * 0.6f, -0.52f), new Vector3(0.12f + (r % 2) * 0.18f, 0.08f, 0.04f), Cyan), 1.1f);
                Box(go, new Vector3(Mathf.Cos(a) * 6f, h * 0.5f, Mathf.Sin(a) * 6f), new Vector3(1f, h, 1f));
            }
            mb.AddBox(TR(3f, 4.2f, 3f, 0, 45, 4), new Vector3(7f, 0.6f, 1f), stone);
            mb.AddBox(TR(-4.4f, 0.55f, -1.6f, 6, 20, 70), new Vector3(2.2f, 0.7f, 0.9f), dark);
            mb.AddRock(Matrix4x4.TRS(new Vector3(-2.8f, 0.4f, 3.6f), Quaternion.identity, Vector3.one), 0.5f, 81, dark);
            mb.AddRock(Matrix4x4.TRS(new Vector3(3.4f, 0.35f, -3.2f), Quaternion.identity, Vector3.one), 0.4f, 82, dark);
            mb.AddLathe(T(0, 0.3f, 0), new[] { new Vector2(1.3f, 0f), new Vector2(1.3f, 0.3f), new Vector2(1.0f, 0.35f), new Vector2(0.9f, 0.7f), new Vector2(0.6f, 0.75f), new Vector2(0f, 0.75f) }, 8, stone);
            With(mb, 0.95f, 0.2f, 0f, () => Shard(mb, TR(0, 2.0f, 0, -90, 0, 0), 2.4f, 0.45f, 6, Cyan), 0.9f);
            Glow(mb, () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    mb.AddBox(TR(Mathf.Cos(a) * 1.2f, 0.62f, Mathf.Sin(a) * 1.2f, 0, -i * 45f, 0), new Vector3(0.05f, 0.04f, 0.4f), Cyan);
                }
            }, 1.1f);
            Box(go, new Vector3(0, 0.15f, 0), new Vector3(8f, 0.3f, 8f));
            Visual(go.transform, "Mesh", SaveMesh(mb, "Ruin"), MatHull);
            AddLight(go.transform, "Glow", new Vector3(0, 2.6f, 0), Cyan, 20f, 15f);
            return SavePrefab(go);
        }

        static GameObject BuildCrashed()
        {
            var go = NetRoot("CrashedShip", false);
            // A downed Dart: the hero hull (scorched, left wing torn off) nosed into the ground, wing and debris nearby.
            var hull = Child(go.transform, "Hull", new Vector3(0, 0.9f, 0), Quaternion.Euler(8f, 15f, 12f));
            Visual(hull.transform, "Mesh", DartHullMesh("CrashedShip", true), MatHull);
            var debris = Hero(0.12f);
            int dStart = debris.VertexCount;
            debris.AddSlab(TR(-4.2f, 0.25f, -1.8f, 0, 30, -18), new[] { new Vector2(1.0f, 1.2f), new Vector2(3.4f, -0.9f), new Vector2(3.5f, -1.7f), new Vector2(1.0f, -2.4f) }, 0.3f, 0.04f, Chalk, new[] { 0.34f, 0.12f, 0.12f, 0.3f });
            debris.AddSlab(TR(3.8f, 0.15f, 2.4f, 10, -20, 30), new[] { new Vector2(-0.6f, -0.4f), new Vector2(0.7f, -0.5f), new Vector2(0.6f, 0.5f), new Vector2(-0.5f, 0.4f) }, 0.08f, 0.02f, Blue);
            debris.AddSlab(TR(1.8f, 0.12f, -3.9f, -8, 50, 14), new[] { new Vector2(-0.4f, -0.3f), new Vector2(0.5f, -0.3f), new Vector2(0.45f, 0.35f), new Vector2(-0.35f, 0.3f) }, 0.06f, 0.02f, Gunmetal);
            debris.Weather(dStart, p => Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(p.x * 0.7f + 2f, p.z * 0.7f) - 0.35f) * 2.5f), new Color(0.1f, 0.09f, 0.09f));
            var rocks = Stone();
            for (int i = 0; i < 7; i++)
                rocks.AddRock(Matrix4x4.TRS(new Vector3(-1.5f + Mathf.Sin(i * 2.1f) * 3f, 0.05f, 4.3f + Mathf.Cos(i * 1.7f) * 1.6f), Quaternion.identity, new Vector3(1f, 0.6f, 1f)), 0.35f + (i % 3) * 0.15f, 90 + i, new Color(0.35f, 0.33f, 0.32f));
            Visual(go.transform, "Debris", SaveMesh(debris, "CrashedShip_Debris"), MatHull);
            Visual(go.transform, "Rubble", SaveMesh(rocks, "CrashedShip_Rubble"), MatHull);
            AddLight(go.transform, "Warning", new Vector3(0.6f, 2.6f, 1f), Red, 8f, 10f);
            Box(go, new Vector3(0, 0.8f, 0), new Vector3(3.4f, 2.2f, 9f));
            return SavePrefab(go);
        }

        static GameObject BuildWreck()
        {
            var go = NetRoot("Wreck", false);
            // Derelict freighter section drifting in space: torn hull with exposed ribs, peeled plates, scorch and debris.
            var mb = Hero(0.15f);
            int hull = mb.VertexCount;
            mb.AddLoft(TR(0, 0, 0, 0, 0, 12), new[]
            {
                new MeshBuilder.Section(-11f, 3.2f, 2.6f, 4f), new MeshBuilder.Section(-9.5f, 3.9f, 3.0f, 4.5f),
                new MeshBuilder.Section(6f, 3.9f, 3.0f, 4.5f), new MeshBuilder.Section(7.5f, 3.6f, 2.8f, 4.5f, 0.2f),
            }, 28, new Color(0.5f, 0.53f, 0.6f), true, false, 2);
            mb.Paint(hull, p => Mathf.Abs(p.y) < 0.6f, Navy);
            mb.Weather(hull, p => Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(p.x * 0.22f + 4f, p.z * 0.22f + p.y * 0.2f) - 0.4f) * 2.4f) + Mathf.Clamp01((p.z - 4f) * 0.25f), new Color(0.1f, 0.09f, 0.09f));
            // exposed ribs and stringers at the torn front
            With(mb, 0.4f, 1f, 0f, () =>
            {
                var m = TR(0, 0, 0, 0, 0, 12);
                for (int r = 0; r < 3; r++)
                {
                    float z = 8.5f + r * 1.6f;
                    var pts = new List<Vector3>();
                    for (int k = 0; k <= 10 - r * 2; k++)
                    {
                        float a = (200f + k * 16f) * Mathf.Deg2Rad;
                        pts.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 3.6f, Mathf.Sin(a) * 2.7f, z)));
                    }
                    Polyline(mb, pts, 0.16f, 6, Gunmetal);
                }
                for (int k = 0; k < 5; k++)
                {
                    float a = (215f + k * 30f) * Mathf.Deg2Rad;
                    mb.AddTube(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 3.6f, Mathf.Sin(a) * 2.7f, 7f)), m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 3.5f, Mathf.Sin(a) * 2.6f, 10.5f - k * 0.5f)), 0.12f, 0.08f, 6, Gunmetal);
                }
            });
            mb.AddSlab(TR(-6f, 2f, -4f, 20, 10, 40), new[] { new Vector2(-3f, -5f), new Vector2(3f, -4.5f), new Vector2(2.6f, 5f), new Vector2(-2.4f, 4.6f) }, 0.3f, 0.06f, Chalk);
            mb.AddSlab(TR(7f, -3f, 6f, -30, -20, 10), new[] { new Vector2(-2.5f, -4f), new Vector2(2.4f, -3.6f), new Vector2(2.2f, 4f), new Vector2(-2f, 3.6f) }, 0.25f, 0.05f, Blue);
            Bell(mb, new Vector3(0, -0.5f, -11f), 2.4f, 3f, new Color(0.25f, 0.15f, 0.1f), Gunmetal);
            var rocks = Stone();
            for (int i = 0; i < 8; i++)
                rocks.AddRock(Matrix4x4.TRS(new Vector3(-8f + i * 2.4f, -6f + (i % 2) * 12f, -8f + i * 2.6f), Quaternion.Euler(i * 30, i * 50, 0), Vector3.one), 0.8f + (i % 3) * 0.4f, 70 + i, new Color(0.45f, 0.47f, 0.52f));
            Visual(go.transform, "Mesh", SaveMesh(mb, "Wreck"), MatHull);
            Visual(go.transform, "Debris", SaveMesh(rocks, "Wreck_Debris"), MatHull);
            Box(go, Vector3.zero, new Vector3(8f, 6f, 22f));
            return SavePrefab(go);
        }

        static GameObject BuildStation()
        {
            var go = NetRoot("TraderStation", false);
            var ts = go.AddComponent<TraderStation>();
            // Hub: banded lathe drum with window rows, docking collars, antenna crown and a gold beacon.
            var mb = Hero(0.15f);
            mb.AddLathe(Matrix4x4.identity, new[]
            {
                new Vector2(0f, -16.4f), new Vector2(3f, -16.2f), new Vector2(7f, -14f), new Vector2(8.6f, -12f), new Vector2(10f, -11.5f), new Vector2(10f, 11.5f),
                new Vector2(8.6f, 12f), new Vector2(7f, 14f), new Vector2(3f, 16.2f), new Vector2(0f, 16.4f),
            }, 40, Chalk);
            With(mb, Paint, 0f, 0.6f, () =>
            {
                foreach (float y in new[] { -8f, -4f, 4f, 8f })
                    mb.AddLathe(T(0, y, 0), new[] { new Vector2(10f, -0.8f), new Vector2(10.25f, -0.6f), new Vector2(10.25f, 0.6f), new Vector2(10f, 0.8f) }, 40, Blue);
            });
            for (int i = 0; i < 20; i++)
            {
                float a = i * 18f * Mathf.Deg2Rad;
                foreach (float y in new[] { -6f, 6f })
                    With(mb, 1f, 0.2f, 0f, () => mb.AddBox(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 10.02f, y, Mathf.Sin(a) * 10.02f), Quaternion.Euler(0, -i * 18f, 0), Vector3.one), new Vector3(0.1f, 1.3f, 1.4f), new Color(0.2f, 0.45f, 0.6f)), 0.5f);
            }
            Glow(mb, () => mb.AddLathe(Matrix4x4.identity, new[] { new Vector2(10.1f, -0.3f), new Vector2(10.2f, 0f), new Vector2(10.1f, 0.3f) }, 40, Cyan), 1f);
            With(mb, 0.5f, 1f, 0.2f, () =>
            {
                mb.AddTube(new Vector3(0, 16f, 0), new Vector3(0, 21f, 0), 0.3f, 0.15f, 10, Gunmetal);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 120f * Mathf.Deg2Rad;
                    mb.AddTube(new Vector3(Mathf.Cos(a) * 2f, 15.5f, Mathf.Sin(a) * 2f), new Vector3(Mathf.Cos(a) * 3f, 19f, Mathf.Sin(a) * 3f), 0.12f, 0.06f, 8, Gunmetal);
                }
                mb.AddLathe(Matrix4x4.TRS(new Vector3(-3f, 16f, 2f), Quaternion.Euler(30, 0, 20), Vector3.one), new[] { new Vector2(0f, 0f), new Vector2(1.2f, 0.15f), new Vector2(2.2f, 0.6f), new Vector2(2.1f, 0.7f), new Vector2(1.1f, 0.3f), new Vector2(0f, 0.15f) }, 20, Chalk);
            });
            With(mb, 0.75f, 0.85f, 0f, () => mb.AddLathe(T(0, 21f, 0), new[] { new Vector2(0f, 0f), new Vector2(0.6f, 0.1f), new Vector2(0.4f, 0.8f), new Vector2(0f, 1.0f) }, 10, Gold));
            Glow(mb, () => mb.AddSphere(T(0, 22.1f, 0), 0.3f, 1, Gold), 1.6f);
            // pad arm (collider 3 x 3 x 16 at z 18) and the landing pad (30 x 1 x 30 at z 34)
            With(mb, 0.4f, 0.8f, 0.6f, () => mb.AddBox(T(0, 0, 18f), new Vector3(3f, 3f, 16f), Gunmetal));
            With(mb, 0.45f, 1f, 0f, () => { for (int k = 0; k < 7; k++) mb.AddBox(T(0, 1.55f, 11f + k * 2.2f), new Vector3(3.2f, 0.12f, 0.3f), Slate); });
            With(mb, Satin, 0.3f, 0.8f, () => mb.AddBox(T(0, -1.5f, 34f), new Vector3(30f, 1f, 30f), Navy));
            With(mb, Paint, 0f, 0f, () =>
            {
                mb.AddBox(T(0, -0.99f, 34f), new Vector3(0.8f, 0.02f, 9f), Chalk);                       // H marking
                mb.AddBox(T(-3.6f, -0.99f, 34f), new Vector3(0.8f, 0.02f, 9f), Chalk);
                mb.AddBox(T(3.6f, -0.99f, 34f), new Vector3(0.8f, 0.02f, 9f), Chalk);
                mb.AddBox(T(0, -0.99f, 34f), new Vector3(7.2f, 0.02f, 0.8f), Chalk);
            });
            for (int side = 0; side < 4; side++)
            {
                var q = Quaternion.Euler(0, side * 90f, 0);
                Hazard(mb, Matrix4x4.TRS(new Vector3(0, -0.99f, 34f) + q * new Vector3(0, 0, 14.4f), q, Vector3.one), 28f, 0.8f, 28, 0.02f);
            }
            // trader kiosk (console collider 3 x 3 x 1.2 at (0, 0.5, 22.5))
            With(mb, Satin, 0.4f, 0.6f, () => mb.AddBox(T(0, 0.5f, 22.5f), new Vector3(3f, 3f, 1.2f), Graphite));
            With(mb, Paint, 0f, 0.4f, () => mb.AddSlab(T(0, 2.3f, 23.2f), new[] { new Vector2(-1.8f, -1.2f), new Vector2(1.8f, -1.2f), new Vector2(1.6f, 1.0f), new Vector2(-1.6f, 1.0f) }, 0.15f, 0.04f, Chalk));
            With(mb, 0.75f, 0.85f, 0f, () => mb.AddBox(T(0, -0.85f, 23.1f), new Vector3(3.1f, 0.2f, 0.1f), Gold));
            Glow(mb, () => mb.AddBox(T(0, 1.2f, 23.12f), new Vector3(2f, 0.9f, 0.04f), Cyan), 0.9f);
            for (int i = 0; i < 4; i++)
            {
                var lp = new Vector3(-14f + (i % 2) * 28f, -1f, 20f + (i / 2) * 28f);
                With(mb, 0.5f, 1f, 0f, () => mb.AddTube(lp, lp + Vector3.up * 3.6f, 0.2f, 0.14f, 8, Gunmetal));
                Glow(mb, () => mb.AddSphere(Matrix4x4.TRS(lp + Vector3.up * 3.8f, Quaternion.identity, new Vector3(1f, 0.7f, 1f)), 0.35f, 2, Cyan), 1.4f);
            }
            Visual(go.transform, "Hub", SaveMesh(mb, "Station_Hub"), MatHull);
            for (int i = 0; i < 4; i++) AddLight(go.transform, "PadLight", new Vector3(-14f + (i % 2) * 28f, 3.5f, 20f + (i / 2) * 28f), Cyan, 40f, 22f);
            AddLight(go.transform, "ConsoleLight", new Vector3(0, 3.5f, 26f), LampWhite, 30f, 14f);

            // spinning habitat ring: 8 lofted modules with windows, joined by trusses, on 4 spokes
            var ring = Hero(0.15f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var q = Quaternion.Euler(0, a, 0);
                var m = Matrix4x4.TRS(q * new Vector3(0, 0, 24f), q * Quaternion.Euler(0, 90, 0), Vector3.one);
                ring.AddLoft(m, new[]
                {
                    new MeshBuilder.Section(-4.6f, 1.0f, 1.0f, 3f), new MeshBuilder.Section(-4.2f, 1.5f, 1.4f, 3.5f),
                    new MeshBuilder.Section(4.2f, 1.5f, 1.4f, 3.5f), new MeshBuilder.Section(4.6f, 1.0f, 1.0f, 3f),
                }, 20, i % 2 == 0 ? Chalk : Blue, smoothing: 2);
                for (int w = 0; w < 5; w++)
                    With(ring, 1f, 0.2f, 0f, () => ring.AddBox(m * T(1.52f, 0.3f, -3f + w * 1.5f), new Vector3(0.06f, 0.5f, 0.8f), new Color(0.2f, 0.45f, 0.6f)), 0.6f);
                var q2 = Quaternion.Euler(0, a + 22.5f, 0);
                With(ring, 0.45f, 1f, 0f, () =>
                {
                    ring.AddTube(q2 * new Vector3(0, 0.6f, 23.4f) + q2 * Vector3.right * -1.2f, q2 * new Vector3(0, 0.6f, 23.4f) + q2 * Vector3.right * 1.2f, 0.25f, 0.25f, 8, Gunmetal);
                    ring.AddTube(q2 * new Vector3(0, -0.6f, 23.4f) + q2 * Vector3.right * -1.2f, q2 * new Vector3(0, -0.6f, 23.4f) + q2 * Vector3.right * 1.2f, 0.25f, 0.25f, 8, Gunmetal);
                });
                if (i % 2 == 0)
                    With(ring, 0.45f, 1f, 0.3f, () => ring.AddTube(q * new Vector3(0, 0, 10.2f), q * new Vector3(0, 0, 22.5f), 0.45f, 0.45f, 10, Gunmetal));
            }
            var spinner = Child(go.transform, "Ring", new Vector3(0, 6f, 0));
            Visual(spinner.transform, "RingMesh", SaveMesh(ring, "Station_Ring"), MatHull);
            Set(ts, "spinner", spinner.transform);

            var hubCol = go.AddComponent<CapsuleCollider>();
            hubCol.radius = 10f; hubCol.height = 34f;
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

        static GameObject BuildDirector()
        {
            var go = NetRoot("GameDirector", false);
            go.AddComponent<RunManager>();
            go.AddComponent<WarpDrive>();
            go.AddComponent<WarpFlight>();
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
