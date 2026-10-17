using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Voidward.Core;
using Voidward.Data;
using Voidward.Ship;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Low-poly ships (#122, style #120): Quaternius hulls from Tools/Blender/lowpoly_ships.py and Kenney Station Kit parts
    /// from Tools/Blender/lowpoly_kit.py, both recoloured to the shared palette (Art/Palette.json) in the vw vertex contract.
    /// Material: VW_LowPoly (VW_ToonFlat, flat normals, vertex colour). The PBR builds stay as fallback when the FBX is missing.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string LowPolyShipDir = ShipModelDir + "/LowPoly";
        const string KitDir = ModelDir + "/Kit";

        /// <summary>True while PreviewLowPoly builds a ship: SavePrefab hands the object back instead of saving it.</summary>
        static bool previewOnly;

        static Mesh LowPolyMesh(string name) => ImportedMesh(LowPolyShipDir + "/" + name + ".fbx", name);

        /// <summary>A Kenney Station Kit part (Kit_&lt;name&gt;.fbx); Kenney units: a wall tile is 1 x 1.</summary>
        static Mesh KitMesh(string name) => ImportedMesh(KitDir + "/Kit_" + name + ".fbx", "Kit_" + name);

        /// <summary>Flat-shaded palette material for every low-poly model: hard facets, vertex colour, soft light ramp, low rim.</summary>
        static Material LowPolyMaterial()
        {
            var m = Mat("VW_LowPoly", "VOIDWARD/ToonFlat");
            if (MatToon != null) m.CopyPropertiesFromMaterial(MatToon);
            m.shaderKeywords = new[] { "_USEVERTEXCOLOR_ON", "_FLATSHADING_ON" };
            m.SetFloat("_UseVertexColor", 1f);
            m.SetFloat("_FlatShading", 1f);
            m.SetFloat("_SurfaceDetail", 0f);
            m.SetFloat("_Steps", 6f);
            m.SetFloat("_Softness", 1f);
            m.SetFloat("_AmbientBoost", 0.6f);
            m.SetFloat("_RimStrength", 0.08f);
            m.SetFloat("_SpecStrength", 0.6f);
            m.SetFloat("_ReflStrength", 0.3f);
            m.SetFloat("_GlowIntensity", 3f);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Same palette material for interiors seen from inside: no rim (it reads as glow in a cockpit).</summary>
        static Material LowPolyInteriorMaterial()
        {
            var m = Mat("VW_LowPolyInterior", "VOIDWARD/ToonFlat");
            var src = LowPolyMaterial();
            m.CopyPropertiesFromMaterial(src);
            m.shaderKeywords = src.shaderKeywords;
            m.SetFloat("_RimStrength", 0f);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Places a Kenney part: position/rotation in ship space, uniform scale (Kenney units to metres).</summary>
        static GameObject KitPart(Transform parent, string part, Vector3 pos, float yaw, float scale, Material mat = null)
        {
            var mesh = KitMesh(part);
            if (mesh == null) return null;
            var go = Visual(parent, part, mesh, mat != null ? mat : LowPolyInteriorMaterial());
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        // ================================================================ Dart (Quaternius "Omen", 15 m)

        /// <summary>Seated eye of the low-poly Dart's pilot: under the glass roof of the cockpit block (roof y 1.33-1.42, windscreen at z -2).</summary>
        static readonly Vector3 DartLPEye = new Vector3(0f, 0.95f, -3.75f);

        /// <summary>
        /// Dart, low poly (#122): the Quaternius Omen at 15 m: nose cannon +Z, delta wing widening to the stern (15.2 m),
        /// the cockpit block in front of the stern cowl with a glass roof + windscreen (lowpoly_ships.py glass_box).
        /// The pilot sits in a tub sunk into the hull under that roof, with the Kenney chair, a Kenney console as dashboard,
        /// live screens, and side stick + throttle as own objects (#40).
        /// </summary>
        static GameObject BuildLowPolyDart(Mesh hull)
        {
            var go = ShipRoot("Ship_Dart", ShipClass.Dart, 2400f, 4, 2);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", hull, LowPolyMaterial());
            var canopy = LowPolyMesh("Ship_Dart_LP_Canopy");
            if (canopy != null)
            {
                Visual(go.transform, "Canopy", canopy, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Visual(go.transform, "CanopyInner", canopy, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var interior = LowPolyCockpit(go.transform, DartLPEye, 0.92f, -4.75f, -2.1f, 1.3f);

            Box(go, new Vector3(0f, -0.2f, 3.5f), new Vector3(2.6f, 2.4f, 8f));      // nose + cannon
            Box(go, new Vector3(0f, -0.4f, -1.5f), new Vector3(6f, 2.4f, 4f));       // mid body
            Box(go, new Vector3(0f, -0.5f, -5.5f), new Vector3(15f, 1.6f, 4f));      // stern + wings

            // [F] trigger around the cockpit block; exit onto the wing beside it
            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, DartLPEye - new Vector3(0f, 1.2f, -0.04f), DartLPEye, new Vector3(-3.4f, 0.4f, -3.6f),
                new Vector3(0f, 0.8f, -3.5f), new Vector3(2.4f, 1.8f, 3.4f));
            SeatGrips(pilot, interior.Find("Stick/GripIK"), interior.Find("Throttle/GripIK"));
            var term = Child(go.transform, "CargoTerminal", new Vector3(0f, 0.2f, -6.2f));
            Box(term, Vector3.zero, new Vector3(4f, 3f, 3f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-0.55f, -0.2f, 7.6f)).transform, Child(go.transform, "MuzzleR", new Vector3(0.55f, -0.2f, 7.6f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleC", new Vector3(0f, -0.2f, 7.7f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-3.4f, 0.4f, -3.6f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            // the stern cowl behind the cockpit is the engine: one big exhaust in its mouth
            fx.Thrusters = Thrusters(go.transform, new Vector3(0f, 0.4f, -5.2f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(2.6f, 2.6f, 2.4f);
            ShipLighting(go, new Vector3(0f, -2.2f, 0f), 14f, new Vector3(-0.6f, -0.5f, 7.3f), new Vector3(0.6f, -0.5f, 7.3f));
            return SavePrefab(go);
        }

        // ================================================================ Mule (Quaternius "Spitfire", 13 m)

        /// <summary>Seated eye of the low-poly Mule's pilot: under the cockpit wedge on the body's front (glass z -3.4..0.5, roof ~y 2.2).</summary>
        static readonly Vector3 MuleLPEye = new Vector3(0f, 1.75f, -1.6f);

        /// <summary>
        /// Mule, low poly (#122): the Quaternius Spitfire at 13 m: a tall body at the stern, a long nose boom (+Z), stub wings
        /// out to two stacked engine pods per side (18.2 m across). The pilot sits under the glass wedge on the body's front
        /// in the same tub as the Dart; cargo terminal at the stern.
        /// </summary>
        static GameObject BuildLowPolyMule(Mesh hull)
        {
            var go = ShipRoot("Ship_Mule", ShipClass.Mule, 3500f, 6, 3);
            var core = go.GetComponent<ShipCore>();
            Visual(go.transform, "Hull", hull, LowPolyMaterial());
            var canopy = LowPolyMesh("Ship_Mule_LP_Canopy");
            if (canopy != null)
            {
                Visual(go.transform, "Canopy", canopy, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Visual(go.transform, "CanopyInner", canopy, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var interior = LowPolyCockpit(go.transform, MuleLPEye, 1.1f, -3.0f, 0.2f, 1.3f);

            Box(go, new Vector3(0f, 1.2f, -4.0f), new Vector3(5f, 3f, 6.2f));        // body
            Box(go, new Vector3(0f, 0.5f, 2.8f), new Vector3(2.8f, 1.2f, 7.4f));     // nose boom
            Box(go, new Vector3(0f, 0.55f, -2.7f), new Vector3(13f, 0.8f, 3.8f));    // stub wings
            Box(go, new Vector3(-7.77f, -0.1f, -3.07f), new Vector3(2.7f, 5.1f, 3.9f)); // engine pods
            Box(go, new Vector3(7.77f, -0.1f, -3.07f), new Vector3(2.7f, 5.1f, 3.9f));

            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, MuleLPEye - new Vector3(0f, 1.2f, -0.04f), MuleLPEye, new Vector3(-3.8f, 1.4f, -2.6f),
                new Vector3(0f, 1.6f, -1.4f), new Vector3(2.8f, 1.8f, 4f));
            SeatGrips(pilot, interior.Find("Stick/GripIK"), interior.Find("Throttle/GripIK"));
            var term = Child(go.transform, "CargoTerminal", new Vector3(0f, 0.6f, -7.2f));
            Box(term, Vector3.zero, new Vector3(3.5f, 3f, 2.5f), true);
            term.AddComponent<ShipTerminal>().TerminalMode = ShipTerminal.Mode.ShipPanel;

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-0.45f, 0.5f, 6.6f)).transform, Child(go.transform, "MuzzleR", new Vector3(0.45f, 0.5f, 6.6f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleC", new Vector3(0f, 0.2f, 6.7f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(-3.8f, 1.4f, -2.6f)).transform;
            core.FabricatorPoint = go.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-7.77f, 1.22f, -5.0f), new Vector3(-7.77f, -1.32f, -5.0f), new Vector3(7.77f, 1.22f, -5.0f), new Vector3(7.77f, -1.32f, -5.0f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(1.7f, 1.7f, 1.8f);
            ShipLighting(go, new Vector3(0f, -1.5f, -3f), 14f, new Vector3(-0.5f, 0.3f, 6.5f), new Vector3(0.5f, 0.3f, 6.5f));
            return SavePrefab(go);
        }

        /// <summary>
        /// A low-poly cockpit tub under `ship` ("Interior"), seat facing +Z: floor 1.3 m under the eye, side walls at +-halfWidth,
        /// rear wall at zBack, open towards the windscreen at zFront, a hull-coloured ledge up to `sill` around the glass.
        /// Fittings: Kenney chair, Kenney wide console as the dashboard with six live screens, side stick (right) and throttle
        /// (left) with hand IK goals, cockpit lights. A LODGroup culls it once the ship is small on screen.
        /// </summary>
        static Transform LowPolyCockpit(Transform ship, Vector3 eye, float halfWidth, float zBack, float zFront, float sill)
        {
            var root = Child(ship, "Interior", Vector3.zero).transform;
            float floor = eye.y - 1.3f;
            var mb = new MeshBuilder();
            Color wall = Pal("hull_mid"), dark = Pal("hull_dark"), trim = Pal("trim"), accent = Pal("accent"), glow = Pal("glow_cyan");
            float len = zFront - zBack, zc = (zBack + zFront) * 0.5f, h = sill - floor;
            // thin slabs: their inner faces close the tub (the hull's own back faces are culled)
            mb.AddBox(T(0f, floor - 0.03f, zc), new Vector3(halfWidth * 2f, 0.06f, len), dark);
            mb.AddBox(T(-halfWidth, floor + h * 0.5f, zc), new Vector3(0.06f, h, len), wall);
            mb.AddBox(T(halfWidth, floor + h * 0.5f, zc), new Vector3(0.06f, h, len), wall);
            mb.AddBox(T(0f, floor + h * 0.5f, zBack), new Vector3(halfWidth * 2f, h, 0.06f), wall);
            mb.AddBox(T(0f, floor + 0.35f, zFront), new Vector3(halfWidth * 2f, 0.7f, 0.06f), wall);
            // a raised floor plate and accent stripes along the sills
            mb.AddBox(T(0f, floor + 0.02f, zc), new Vector3(halfWidth * 1.1f, 0.03f, len * 0.7f), trim);
            foreach (float sx in new[] { -1f, 1f })
            {
                mb.AddBox(T(sx * (halfWidth - 0.04f), floor + h * 0.62f, zc), new Vector3(0.03f, 0.05f, len * 0.9f), accent);
                mb.Glow = 1f;
                mb.AddBox(T(sx * (halfWidth - 0.04f), floor + h * 0.45f, zc + 0.2f), new Vector3(0.02f, 0.02f, len * 0.5f), glow);
                mb.Glow = 0f;
            }
            Visual(root, "Tub", SaveMesh(mb, ship.name + "_LP_CockpitTub"), LowPolyInteriorMaterial());

            // Kenney fittings (1 Kenney unit ~ 1.6 m for furniture)
            KitPart(root, "chair-armrest-headrest", new Vector3(0f, floor, eye.z - 0.12f), 0f, 1.6f);
            KitPart(root, "computer-wide", new Vector3(0f, floor, zFront - 0.42f), 180f, 1.5f);
            KitPart(root, "display-wall", new Vector3(-halfWidth + 0.02f, floor + 0.55f, eye.z + 0.6f), 90f, 1.2f);
            KitPart(root, "display-wall", new Vector3(halfWidth - 0.02f, floor + 0.55f, eye.z + 0.6f), -90f, 1.2f);

            // side stick (right) and throttle (left) as own objects, pivot at the base (#40), hand IK goals on the grips (#95)
            var stickMesh = LeverMesh("Ship_LP_Stick", 0.42f, Pal("trim"), Pal("glow_warn"));
            var throttleMesh = LeverMesh("Ship_LP_Throttle", 0.32f, Pal("trim"), Pal("accent"));
            var stick = Child(root, "Stick", new Vector3(0.36f, floor + 0.38f, eye.z + 0.42f)).transform;
            var throttle = Child(root, "Throttle", new Vector3(-0.38f, floor + 0.42f, eye.z + 0.36f)).transform;
            Visual(stick, "Grip", stickMesh, LowPolyInteriorMaterial());
            Visual(throttle, "Lever", throttleMesh, LowPolyInteriorMaterial());
            Child(stick, "GripIK", GripPoint(stickMesh.bounds, 0.11f, 1f), Quaternion.Euler(0f, 0f, -90f));
            Child(throttle, "GripIK", GripPoint(throttleMesh.bounds, 0.06f, -1f), Quaternion.Euler(0f, 0f, 90f));

            // live screens on the console's slanted top: flight / radar / nav, hull / fuel / drive below
            var screens = Child(root, "Screens", Vector3.zero).transform;
            float sy = floor + 0.62f, sz = zFront - 0.55f;
            var look = new Vector3(0f, -0.55f, 0.84f);
            CockpitScreenAt(screens, ScreenKind.Flight, new Vector3(0f, sy, sz), look, new Vector2(0.36f, 0.2f));
            CockpitScreenAt(screens, ScreenKind.Radar, new Vector3(-0.42f, sy, sz + 0.02f), look, new Vector2(0.28f, 0.2f));
            CockpitScreenAt(screens, ScreenKind.Nav, new Vector3(0.42f, sy, sz + 0.02f), look, new Vector2(0.28f, 0.2f));
            CockpitScreenAt(screens, ScreenKind.Hull, new Vector3(-0.3f, sy - 0.17f, sz - 0.12f), look, new Vector2(0.16f, 0.12f));
            CockpitScreenAt(screens, ScreenKind.Fuel, new Vector3(0f, sy - 0.17f, sz - 0.12f), look, new Vector2(0.2f, 0.09f));
            CockpitScreenAt(screens, ScreenKind.Drive, new Vector3(0.3f, sy - 0.17f, sz - 0.12f), look, new Vector2(0.16f, 0.12f));

            AddLight(root, "CockpitLight", eye + new Vector3(0f, 0.3f, 0.1f), new Color(0.75f, 0.85f, 1f), 0.35f, 2.2f);
            AddLight(root, "ScreenGlow", new Vector3(0f, sy, sz - 0.3f), new Color(0.35f, 0.85f, 1f), 0.45f, 1.2f);
            AddLight(root, "FootwellGlow", new Vector3(0f, floor + 0.2f, zFront - 0.6f), new Color(1f, 0.55f, 0.2f), 0.3f, 1.4f);
            var lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.035f, root.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
            return root;
        }

        /// <summary>A low-poly lever (base plate, shaft, grip, a coloured button), pivot at the base.</summary>
        static Mesh LeverMesh(string name, float height, Color body, Color button)
        {
            var mb = new MeshBuilder();
            mb.AddBox(T(0f, 0.02f, 0f), new Vector3(0.12f, 0.04f, 0.12f), Pal("hull_dark"));
            mb.AddBox(T(0f, height * 0.45f, 0f), new Vector3(0.035f, height * 0.9f, 0.035f), Pal("hull_mid"));
            mb.AddBox(T(0f, height * 0.92f, 0.01f), new Vector3(0.06f, height * 0.25f, 0.07f), body);
            mb.Glow = 1f;
            mb.AddBox(T(0f, height * 1.05f + 0.01f, 0.02f), new Vector3(0.025f, 0.012f, 0.025f), button);
            return SaveMesh(mb, name);
        }

        static Dictionary<string, Color> palette;

        /// <summary>A shared palette colour (Art/Palette.json, #121), sRGB.</summary>
        static Color Pal(string name)
        {
            if (palette == null)
            {
                palette = new Dictionary<string, Color>();
                var json = System.IO.File.ReadAllText(ArtDir + "/Palette.json");
                foreach (Match m in Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*\"#([0-9A-Fa-f]{6})\""))
                    if (ColorUtility.TryParseHtmlString("#" + m.Groups[2].Value, out var c)) palette[m.Groups[1].Value] = c;
            }
            return palette.TryGetValue(name, out var col) ? col : Color.magenta;
        }

        // ================================================================ dev preview

        /// <summary>
        /// Dev (#122): builds one low-poly ship without saving it and renders `shots` views (camera/target in ship space) to
        /// Temp/HeroShots/&lt;name&gt;_&lt;i&gt;.png. Edit Mode only; nothing in the project changes except mesh/material assets.
        /// </summary>
        public static void PreviewLowPoly(string ship, Vector3[] cams, Vector3[] targets, float fov = 40f, string name = null)
        {
            if (!OpenStudio()) return;
            if (MatToon == null) MatToon = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/VW_Toon.mat");
            if (MatPlume == null) MatPlume = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/VW_Plume.mat");
            previewOnly = true;
            GameObject go = null;
            try
            {
                if (ship == "Dart") go = BuildLowPolyDart(LowPolyMesh("Ship_Dart_LP"));
                else if (ship == "Mule") go = BuildLowPolyMule(LowPolyMesh("Ship_Mule_LP"));
            }
            finally { previewOnly = false; }
            if (go == null) { Debug.LogWarning("[Voidward] PreviewLowPoly: nothing built for " + ship); return; }
            studioSun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            for (int i = 0; i < cams.Length; i++)
                StudioShot((name ?? ship + "_LP") + "_" + i, go.transform.TransformPoint(cams[i]), go.transform.TransformPoint(targets[i]), fov, true);
            Object.DestroyImmediate(go);
            // back to the game scene: an untitled studio scene left open starts an empty run on Play
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[Voidward] PreviewLowPoly " + ship + " done");
        }
    }
}
