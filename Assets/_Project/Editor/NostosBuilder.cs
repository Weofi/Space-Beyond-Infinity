using System.Collections.Generic;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Ship;

namespace Voidward.EditorTools
{
    /// <summary>
    /// The home ship Nostos (#49): the Ark hull at 1.5 x (Tools/Blender/nostos.py) with a walkable deck from stern to
    /// bow - engine room (warp core, engineer, aft turret), cargo hall (the corridor kit, airlock with boarding ramp,
    /// fabricator, cargo terminal, hatches to the docking clamps), cryo bay (four pods, one per player slot) and the
    /// bridge (pilot on a raised platform behind the window, the Dart's fittings; forward turret). Four docking clamps
    /// on the wings carry the crew's Darts (DockClamp). Coordinates are ship-local Unity metres (nose +Z).
    /// </summary>
    public static partial class ContentBuilder
    {
        // ---- the walkable volume (nostos.py ROOMS / DOOR / HALL_Z) ----
        const float NFloor = -2.325f;                                        // deck (Ark -1.55 x 1.5)
        const float NHallX = 2.43f, NHallTop = 1.14f;                        // fuselage inner half width, roof
        const float NEngZ0 = -16f, NEngTop = 0.85f;                          // engine room: stern wall, lower roof (tail)
        const float NHallZ = -1.5f, NHallLen = 15.85f;                       // the corridor kit (Nostos_Hall.fbx), centre + length
        const float NHallZ0 = NHallZ - NHallLen * 0.5f, NHallZ1 = NHallZ + NHallLen * 0.5f;
        const float NKitHeight = 3.2f, NKitWall = 1.9f;                      // kit ceiling above the deck, walkable half width
        const float NBridgeZ0 = 9.9f, NBridgeX = 2.04f, NBridgeTop = 2.4f, NBridgeZ1 = 25.28f;
        const float NDoorZ0 = -3.75f, NDoorZ1 = -0.75f, NDoorTop = 0.825f, NSkinX = 2.865f;
        const float NPlatform = -1.0f, NPlatformZ = 19.4f, NRampZ = 16.9f;   // raised pilot platform and its ramp
        static readonly Vector3 NEye = new Vector3(0f, NPlatform + 1.36f, 22.0f);
        static readonly Vector2 NWindowTop = new Vector2(23.07f, 2.01f), NWindowBottom = new Vector2(25.38f, -0.71f);
        // docking clamps: Dart root poses on the wings (index = player slot): front left/right, rear left/right
        static readonly Vector3[] NClamps = { new Vector3(-12f, 1.2f, 12f), new Vector3(12f, 1.2f, 12f), new Vector3(-12f, 2.8f, -17f), new Vector3(12f, 2.8f, -17f) };
        static readonly float[] NClampWing = { -2.6f, -2.6f, -0.75f, -0.75f };          // wing top under each clamp
        static readonly float[] NHatchZ = { 4.2f, 4.2f, -7.4f, -7.4f };                 // hatch to each clamp in the hall (left / right wall)
        // cryo pods (index = player slot): left/right wall of the cryo bay, facing inward
        static readonly Vector3[] NPods = { new Vector3(-1.84f, NFloor, 7.25f), new Vector3(1.84f, NFloor, 7.25f), new Vector3(-1.84f, NFloor, 8.6f), new Vector3(1.84f, NFloor, 8.6f) };
        static readonly Vector3 NCore = new Vector3(0f, NFloor, -13.6f);

        static GameObject ShipKitModel(Transform parent, string asset, string name, Vector3 pos, Quaternion rot)
        {
            var src = GltfModelSource(ShipModelDir + "/" + asset + ".fbx", ShipModelDir + "/" + asset, asset);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src, parent, false);
            go.name = name;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static GameObject BuildNostos(Mesh hull)
        {
            var go = ShipRoot("Ship_Nostos", ShipClass.HomeShip, 30000f, 8, 5);
            var core = go.GetComponent<ShipCore>();
            go.GetComponent<HealthComponent>().MaxHealth = 900f;
            go.GetComponent<TerrainAnchor>().Radius = 90f;
            go.GetComponent<ShipFlight>().ImpactSafeSpeed = 16f;
            var hullR = Visual(go.transform, "Hull", hull, NostosHullMaterial()).GetComponent<MeshRenderer>();
            var glass = NostosGlassMesh();
            if (glass != null)
            {
                Visual(go.transform, "Window", glass, FabShipGlassMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Visual(go.transform, "WindowInner", glass, FabShipGlassInnerMaterial()).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // ---- interior: the corridor kit as the cargo hall, the deck shell around it, the Dart's fittings on the bridge
            var interior = Child(go.transform, "Interior", Vector3.zero);
            ShipKitModel(interior.transform, "Nostos_Hall", "CargoHall", new Vector3(0f, NFloor, NHallZ), Quaternion.identity);
            var deckR = Visual(interior.transform, "Deck", NostosDeckMesh(), InteriorMaterial()).GetComponent<MeshRenderer>();
            var fit = Child(interior.transform, "Fittings", NEye - new Vector3(0f, 0f, 3.67f));
            bool fitted = DartFittings(fit.transform, false, true);
            if (!fitted) Dashboard(interior.transform, NEye + new Vector3(0f, -0.55f, 0.85f), 1.4f);
            Visual(go.transform, "Clamps", NostosClampMesh(), MatHull);

            NostosColliders(go);

            // ---- interior gravity (moves with the ship; pawns ride along via platform carry): deck, bridge, airlock
            var grav = Child(go.transform, "Interior Gravity", Vector3.zero);
            var deckG = Child(grav.transform, "Deck", new Vector3(0, (NFloor + NHallTop) * 0.5f, (NEngZ0 + NBridgeZ0) * 0.5f));
            Box(deckG, Vector3.zero, new Vector3(NHallX * 2f, NHallTop - NFloor + 0.3f, NBridgeZ0 - NEngZ0), true);
            core.Interior = deckG.AddComponent<GravityVolume>();
            var bridgeG = Child(grav.transform, "Bridge", new Vector3(0, (NFloor + NBridgeTop) * 0.5f, (NBridgeZ0 + NBridgeZ1) * 0.5f));
            Box(bridgeG, Vector3.zero, new Vector3(NBridgeX * 2f, NBridgeTop - NFloor + 0.3f, NBridgeZ1 - NBridgeZ0), true);
            bridgeG.AddComponent<GravityVolume>();
            var lockG = Child(grav.transform, "Airlock", new Vector3(-(NHallX + NSkinX) * 0.5f, (NFloor + NDoorTop) * 0.5f, (NDoorZ0 + NDoorZ1) * 0.5f));
            Box(lockG, Vector3.zero, new Vector3(NSkinX - NHallX + 0.1f, NDoorTop - NFloor + 0.3f, NDoorZ1 - NDoorZ0), true);
            lockG.AddComponent<GravityVolume>();

            // ---- stations: pilot (bridge), two turrets (cab roof, tail), engineer (engine room)
            var pilot = Seat(go.transform, "PilotSeat", StationRole.Pilot, NEye + new Vector3(0f, -1.2f, -0.04f), NEye, new Vector3(0.9f, NPlatform + 0.1f, 20.4f),
                NEye + new Vector3(0f, -0.5f, 0.2f), new Vector3(1.6f, 1.8f, 1.6f));
            if (fitted) SeatGrips(pilot, fit.transform.Find("Stick/GripIK"), fit.transform.Find("Throttle/GripIK"));

            var baseMesh = ArkTurretBaseMesh();
            var headMesh = ArkTurretHeadMesh();
            var turrets = new[]
            {
                (top: new Vector3(0f, 3.62f, 12.9f), anchor: new Vector3(-1.35f, NFloor + 0.16f, 12.2f), exit: new Vector3(-0.3f, NFloor + 0.1f, 13.4f), yaw: 0f, weapon: WeaponKind.Flak),
                (top: new Vector3(0f, 0.95f, -18.9f), anchor: new Vector3(1.55f, NFloor + 0.16f, -11.6f), exit: new Vector3(0.5f, NFloor + 0.1f, -10.6f), yaw: 90f, weapon: WeaponKind.Laser),
            };
            for (int i = 0; i < turrets.Length; i++)
            {
                var tb = Visual(go.transform, "TurretBase" + i, baseMesh, MatHull);
                tb.transform.localPosition = turrets[i].top;
                tb.transform.localScale = Vector3.one * 1.5f;
                var yaw = Child(tb.transform, "Yaw", new Vector3(0, 0.9f, 0));
                var pitch = Child(yaw.transform, "Pitch", Vector3.zero);
                Visual(pitch.transform, "Head", headMesh, MatHull);
                ArkChairAt(interior.transform, turrets[i].anchor, turrets[i].yaw);
                var seat = Seat(go.transform, "TurretSeat" + i, StationRole.Turret, turrets[i].anchor, Vector3.zero, turrets[i].exit,
                    turrets[i].anchor + new Vector3(0f, 0.8f, 0.1f), new Vector3(1.2f, 1.8f, 1.3f));
                var vp = seat.transform.Find("ViewPoint");
                vp.SetParent(pitch.transform, false);
                vp.localPosition = new Vector3(0, 1.7f, -1.8f);
                vp.localRotation = Quaternion.identity;
                seat.TurretYaw = yaw.transform;
                seat.TurretPitch = pitch.transform;
                seat.Muzzle = Child(pitch.transform, "Muzzle", new Vector3(0, 0, 2f)).transform;
                seat.TurretWeapon = turrets[i].weapon;
                seat.TurretIndex = i;
            }
            // engineer: engine room, left wall console beside the warp core
            var engAnchor = new Vector3(-1.45f, NFloor + 0.16f, -11.6f);
            var engRot = Quaternion.Euler(0f, -90f, 0f);
            ArkChairAt(interior.transform, engAnchor, -90f);
            Seat(go.transform, "EngineerSeat", StationRole.Engineer, engAnchor, engAnchor + engRot * new Vector3(0f, 1.2f, 0.04f), new Vector3(-0.4f, NFloor + 0.1f, -10.6f),
                engAnchor + new Vector3(-0.2f, 0.8f, 0f), new Vector3(1.4f, 1.8f, 1.3f), Quaternion.Euler(10f, -90f, 0f));

            // ---- terminals: warp drive at the core, fabricator + cargo in the hall, the hull panel beside the ramp
            Terminal(go, "WarpDriveTerminal", NCore + new Vector3(0f, 1.3f, 0f), new Vector3(1.8f, 2.6f, 1.8f), ShipTerminal.Mode.WarpDrive);
            var fab = Terminal(go, "Fabricator", new Vector3(NKitWall - 0.25f, NFloor + 1.0f, -2.6f), new Vector3(0.9f, 2f, 1.8f), ShipTerminal.Mode.Fabricator);
            Terminal(go, "CargoTerminal", new Vector3(NKitWall - 0.25f, NFloor + 1.0f, 1.0f), new Vector3(0.9f, 2f, 2.2f), ShipTerminal.Mode.ShipPanel);
            Terminal(go, "HullTerminal", new Vector3(-NSkinX - 0.2f, NFloor + 0.3f, NDoorZ1 + 1.4f), new Vector3(0.7f, 2.0f, 1.4f), ShipTerminal.Mode.ShipPanel);

            // ---- docking clamps (#49): the Dart's root pose on each, its hatch in the hall
            var clamps = Child(go.transform, "DockClamps", Vector3.zero);
            for (int i = 0; i < NClamps.Length; i++)
            {
                var c = Child(clamps.transform, "Clamp" + i, NClamps[i]);
                var dc = c.AddComponent<DockClamp>();
                dc.Index = i;
                float side = i % 2 == 0 ? -1f : 1f;
                var hatch = Child(go.transform, "Hatch" + i, new Vector3(side * (NKitWall - 0.55f), NFloor + 0.1f, NHatchZ[i]), Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f));
                dc.Hatch = hatch.transform;
                Box(hatch, new Vector3(0f, 1.1f, -0.35f), new Vector3(1.0f, 2.0f, 0.5f), true);
                hatch.AddComponent<DockHatch>().Clamp = dc;
            }

            // ---- cryo bay (#49): one pod per player slot; a fallen crew member wakes in front of theirs
            var bay = Child(interior.transform, "CryoBay", Vector3.zero);
            for (int i = 0; i < NPods.Length; i++)
            {
                float side = NPods[i].x < 0f ? -1f : 1f;
                var rot = Quaternion.Euler(0f, side < 0f ? 90f : -90f, 0f);    // the pod's front (+Z) faces the aisle
                var pod = Child(bay.transform, "CryoPod" + i, NPods[i], rot);
                var model = ShipKitModel(pod.transform, "Nostos_CryoPod", "Model", Vector3.zero, Quaternion.identity);
                if (model == null) Visual(pod.transform, "Model", CryoPodFallbackMesh(), MatHull);
                var cp = pod.AddComponent<CryoPod>();
                cp.Index = i;
                cp.Exit = Child(pod.transform, "Exit", new Vector3(0f, 0.15f, 1.0f)).transform;
                cp.Glow = AddLight(pod.transform, "PodLight", new Vector3(0f, 1.6f, 0.55f), new Color(0.55f, 0.85f, 1f), 1.2f, 2.2f);
            }

            core.MuzzlesA = new[] { Child(go.transform, "MuzzleL", new Vector3(-5.33f, -2.22f, 21.6f)).transform, Child(go.transform, "MuzzleR", new Vector3(5.33f, -2.22f, 21.6f)).transform };
            core.MuzzlesB = new[] { Child(go.transform, "MuzzleMissile", new Vector3(0, 4.05f, 9.6f)).transform };
            core.RespawnPoint = Child(go.transform, "Respawn", new Vector3(0, NFloor + 0.3f, 0f)).transform;
            core.FabricatorPoint = fab.transform;
            var fx = go.AddComponent<ShipEngineFx>();
            fx.Thrusters = Thrusters(go.transform, new Vector3(-5.33f, -0.3f, -26.0f), new Vector3(5.33f, -0.3f, -26.0f), new Vector3(-5.33f, -2.22f, 4.1f), new Vector3(5.33f, -2.22f, 4.1f));
            foreach (var t in fx.Thrusters) t.localScale = new Vector3(1.65f, 1.65f, 3f);
            ShipLighting(go, new Vector3(0, -5.4f, 3f), 36f, new Vector3(-1.35f, -2.4f, 25.9f), new Vector3(1.35f, -2.4f, 25.9f));

            // ---- lights (the hull blocks the sun) and one box-projected reflection probe rendered when the ship spawns
            var cabin = new List<Light>();
            foreach (float z in new[] { -8f, -4.5f, -1f, 2.5f, 5.6f }) cabin.Add(AddLight(interior.transform, "HallLight", new Vector3(0, NFloor + 2.85f, z), new Color(0.78f, 0.9f, 1f), 2.6f, 6f));
            cabin.Add(AddLight(interior.transform, "CoreLight", NCore + new Vector3(0f, 1.6f, 0f), new Color(0.4f, 0.85f, 1f), 3.2f, 6.5f));
            cabin.Add(AddLight(interior.transform, "EngineLight", new Vector3(0, NEngTop - 0.2f, -11f), new Color(1f, 0.62f, 0.3f), 1.6f, 5f));
            cabin.Add(AddLight(interior.transform, "CryoLight", new Vector3(0, NHallTop - 0.2f, 7.9f), new Color(0.6f, 0.85f, 1f), 1.8f, 4.5f));
            foreach (float z in new[] { 12f, 16f, 20.5f }) cabin.Add(AddLight(interior.transform, "BridgeLight", new Vector3(0, NBridgeTop - 0.2f, z), new Color(0.78f, 0.9f, 1f), 2.4f, 6.5f));
            cabin.Add(AddLight(interior.transform, "AirlockLight", new Vector3(-NHallX + 0.2f, NDoorTop - 0.1f, (NDoorZ0 + NDoorZ1) * 0.5f), new Color(1f, 0.6f, 0.25f), 1.4f, 4f));
            var probeGo = Child(go.transform, "CabinProbe", new Vector3(0, 0f, 5f));
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(NHallX * 2f, 5f, NBridgeZ1 - NEngZ0);
            probe.center = new Vector3(0, 0f, (NEngZ0 + NBridgeZ1) * 0.5f - 5f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.importance = 2;

            var lod = interior.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.02f, interior.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();

            var look = go.AddComponent<ShipDerelictLook>();
            look.Renderers = new Renderer[] { hullR, deckR };
            look.Derelict = new[] { NostosDerelictMaterial(), InteriorDerelictMaterial() };
            cabin.AddRange(fit.GetComponentsInChildren<Light>());
            look.Lights = cabin.ToArray();
            return SavePrefab(go);
        }

        static GameObject Terminal(GameObject ship, string name, Vector3 pos, Vector3 size, ShipTerminal.Mode mode)
        {
            var t = Child(ship.transform, name, pos);
            Box(t, Vector3.zero, size, true);
            t.AddComponent<ShipTerminal>().TerminalMode = mode;
            return t;
        }

        /// <summary>
        /// Solid colliders: a hollow shell around the walkable deck (floor, roofs, walls with the airlock gap), the bridge
        /// with its platform, ramp and slanted window, and solid boxes for the tail, nacelles, wings and the spine racks.
        /// </summary>
        static void NostosColliders(GameObject go)
        {
            float hallMid = (-10.2f + NBridgeZ0) * 0.5f, hallLen = NBridgeZ0 + 10.2f;
            float engMid = (NEngZ0 - 10.2f) * 0.5f, engLen = -10.2f - NEngZ0;
            float deckMid = (NEngZ0 + NBridgeZ0) * 0.5f, deckLen = NBridgeZ0 - NEngZ0;
            // under the deck: deep belly under the hall, the shallower tail under the engine room
            Box(go, new Vector3(0, (NFloor - 4.2f) * 0.5f, hallMid), new Vector3(NHallX * 2f + 0.8f, NFloor + 4.2f + 0.01f, hallLen));
            Box(go, new Vector3(0, (NFloor - 2.8f) * 0.5f, engMid), new Vector3(NHallX * 2f + 0.8f, NFloor + 2.8f + 0.01f, engLen));
            // roofs
            Box(go, new Vector3(0, NHallTop + 0.15f, hallMid), new Vector3(NHallX * 2f + 0.8f, 0.3f, hallLen));
            Box(go, new Vector3(0, NEngTop + 0.12f, engMid), new Vector3(NHallX * 2f + 0.8f, 0.24f, engLen));
            Box(go, new Vector3(0, NFloor + NKitHeight + 0.1f, NHallZ), new Vector3(NHallX * 2f, 0.2f, NHallLen));        // the kit's ceiling
            Box(go, new Vector3(0, 2.25f, -0.75f), new Vector3(5.1f, 1.65f, 18f));                                          // racks on the spine
            // walls: right full length, left around the airlock; in the hall the kit's pipe trenches keep players on the walkway
            float wallH = NHallTop - NFloor, wallMid = (NFloor + NHallTop) * 0.5f;
            Box(go, new Vector3(NHallX + 0.2f, wallMid, deckMid), new Vector3(0.4f, wallH, deckLen));
            Box(go, new Vector3(-NHallX - 0.2f, wallMid, (NEngZ0 + NDoorZ0) * 0.5f), new Vector3(0.4f, wallH, NDoorZ0 - NEngZ0));
            Box(go, new Vector3(-NHallX - 0.2f, wallMid, (NDoorZ1 + NBridgeZ0) * 0.5f), new Vector3(0.4f, wallH, NBridgeZ0 - NDoorZ1));
            Box(go, new Vector3(-NHallX - 0.2f, (NDoorTop + NHallTop) * 0.5f, (NDoorZ0 + NDoorZ1) * 0.5f), new Vector3(0.4f, NHallTop - NDoorTop, NDoorZ1 - NDoorZ0));
            for (int s = -1; s <= 1; s += 2)
            {
                if (s < 0)
                {
                    Box(go, new Vector3(-NKitWall - 0.1f, NFloor + 1.6f, (NHallZ0 + NDoorZ0) * 0.5f), new Vector3(0.2f, 3.2f, NDoorZ0 - NHallZ0));
                    Box(go, new Vector3(-NKitWall - 0.1f, NFloor + 1.6f, (NDoorZ1 + NHallZ1) * 0.5f), new Vector3(0.2f, 3.2f, NHallZ1 - NDoorZ1));
                }
                else Box(go, new Vector3(NKitWall + 0.1f, NFloor + 1.6f, NHallZ), new Vector3(0.2f, 3.2f, NHallLen));
            }
            Box(go, new Vector3(0, NFloor + 1.6f, NEngZ0 - 0.15f), new Vector3(NHallX * 2f, 3.4f, 0.3f));                // stern wall
            Box(go, new Vector3(0, -1.35f, -20.95f), new Vector3(5.7f, 4.65f, 9.5f));                                     // tail
            // warp core (solid column)
            var core = Child(go.transform, "CoreCollider", NCore + new Vector3(0f, 1.6f, 0f));
            var cc = core.AddComponent<CapsuleCollider>();
            cc.radius = 0.62f; cc.height = 3.2f;
            // bridge: under the floor, walls up to the window, roof, header over the door, jambs where the cab narrows
            float bMid = (NBridgeZ0 + NBridgeZ1) * 0.5f, bLen = NBridgeZ1 - NBridgeZ0;
            Box(go, new Vector3(0, (NFloor - 4.4f) * 0.5f, bMid), new Vector3(NBridgeX * 2f + 0.4f, NFloor + 4.4f, bLen));
            float wz = (NBridgeZ0 + NWindowTop.x) * 0.5f, wl = NWindowTop.x - NBridgeZ0;
            Box(go, new Vector3(-NBridgeX - 0.25f, 0.2f, wz), new Vector3(0.5f, 5.2f, wl));
            Box(go, new Vector3(NBridgeX + 0.25f, 0.2f, wz), new Vector3(0.5f, 5.2f, wl));
            Box(go, new Vector3(0, NBridgeTop + 0.4f, wz), new Vector3(NBridgeX * 2f + 0.6f, 0.8f, wl));
            Box(go, new Vector3(0, 3.1f, 13.5f), new Vector3(5.4f, 1.3f, 7f));                                             // cab top under the turret
            Box(go, new Vector3(0, (NHallTop + NBridgeTop) * 0.5f, NBridgeZ0), new Vector3(NBridgeX * 2f, NBridgeTop - NHallTop, 0.2f));
            for (int s = -1; s <= 1; s += 2)
                Box(go, new Vector3(s * (NBridgeX + NHallX) * 0.5f, wallMid, NBridgeZ0), new Vector3(NHallX - NBridgeX, wallH, 0.2f));
            Box(go, new Vector3(0, (NWindowBottom.y - 4.6f) * 0.5f, 25.6f), new Vector3(NBridgeX * 2f + 0.4f, NWindowBottom.y + 4.6f, 0.6f)); // nose under the window
            // platform + its ramp
            Box(go, new Vector3(0, (NFloor + NPlatform) * 0.5f, (NPlatformZ + NBridgeZ1) * 0.5f), new Vector3(NBridgeX * 2f, NPlatform - NFloor, NBridgeZ1 - NPlatformZ));
            var rampFrom = new Vector3(0f, NFloor, NRampZ);
            var rampTo = new Vector3(0f, NPlatform, NPlatformZ);
            var rampDir = rampTo - rampFrom;
            var ramp = Child(go.transform, "PlatformRamp", (rampFrom + rampTo) * 0.5f + new Vector3(0, -0.06f, 0), Quaternion.LookRotation(rampDir, Vector3.up));
            Box(ramp, Vector3.zero, new Vector3(1.6f, 0.12f, rampDir.magnitude + 0.05f));
            // the slanted window
            var wTop = new Vector3(0, NWindowTop.y, NWindowTop.x);
            var wBot = new Vector3(0, NWindowBottom.y, NWindowBottom.x);
            var wUp = (wTop - wBot).normalized;
            var wn = Vector3.Cross(Vector3.right, wUp);
            var wc = Child(go.transform, "WindowCollider", (wTop + wBot) * 0.5f + wn * 0.06f, Quaternion.LookRotation(wn, wUp));
            Box(wc, Vector3.zero, new Vector3(4.4f, (wTop - wBot).magnitude + 0.1f, 0.12f));
            // nacelles and wings (measured from nostos.py: front wings slope down outwards, rear wings sit high)
            for (int s = -1; s <= 1; s += 2)
            {
                Box(go, new Vector3(s * 5.33f, -2.22f, 12.6f), new Vector3(3.3f, 3.3f, 17.1f));     // front nacelles
                Box(go, new Vector3(s * 5.33f, -0.3f, -17.55f), new Vector3(3.3f, 3.3f, 17.1f));    // rear nacelles
                Box(go, new Vector3(s * 7.3f, -2.15f, 12f), new Vector3(5.4f, 0.7f, 15f));          // front wings (root, tip)
                Box(go, new Vector3(s * 12.4f, -2.75f, 10f), new Vector3(4.8f, 0.6f, 10f));
                Box(go, new Vector3(s * 8.3f, -0.35f, -18f), new Vector3(3.6f, 0.7f, 12f));         // rear wings
                Box(go, new Vector3(s * 12.4f, -0.95f, -20f), new Vector3(4.8f, 0.5f, 8f));
            }
            // airlock floor (the kit's pipe trench is cut there) and the boarding ramp down to the ground
            Box(go, new Vector3(-(NKitWall + NSkinX) * 0.5f + 0.4f, NFloor - 0.1f, (NDoorZ0 + NDoorZ1) * 0.5f), new Vector3(NSkinX - NKitWall + 0.9f, 0.2f, NDoorZ1 - NDoorZ0));
            var rFrom = new Vector3(-NSkinX, NFloor, 0f);
            var rTo = new Vector3(-8.25f, -4.725f, 0f);
            var rDir = rFrom - rTo;
            var boarding = Child(go.transform, "RampCollider", (rFrom + rTo) * 0.5f + new Vector3(0, -0.06f, (NDoorZ0 + NDoorZ1) * 0.5f),
                Quaternion.Euler(0f, 0f, Mathf.Atan2(rDir.y, rDir.x) * Mathf.Rad2Deg));
            Box(boarding, Vector3.zero, new Vector3(rDir.magnitude + 0.1f, 0.12f, NDoorZ1 - NDoorZ0));
        }

        /// <summary>
        /// The deck shell around the corridor kit (VW_HullInterior panel detail): the engine room with the warp core and the
        /// engineer console, the cryo bay, the bridge with its raised pilot platform and the walls under the window, the
        /// airlock tunnel with the boarding ramp, hatches up to the docking clamps, fabricator and cargo lockers.
        /// </summary>
        static Mesh NostosDeckMesh()
        {
            var mb = Hero(0.03f);
            var floorCol = new Color(0.3f, 0.33f, 0.38f);
            var panel = new Color(0.33f, 0.37f, 0.43f);
            float wallH = NHallTop - NFloor, wallMid = (NFloor + NHallTop) * 0.5f;

            // ---- engine room: floor, walls, low roof, stern wall
            float eMid = (NEngZ0 + NHallZ0) * 0.5f, eLen = NHallZ0 - NEngZ0;
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, NFloor - 0.06f, eMid), new Vector3(NHallX * 2f, 0.12f, eLen), floorCol));
            Hazard(mb, T(0, NFloor + 0.004f, NCore.z), 2.4f, 0.3f, 8, 0.01f);
            for (int s = -1; s <= 1; s += 2)
            {
                int side = s;
                With(mb, Paint, 0.2f, 1f, () => mb.AddBox(T(side * (NHallX + 0.04f), (NFloor + NEngTop) * 0.5f, eMid), new Vector3(0.08f, NEngTop - NFloor, eLen), panel));
                With(mb, Satin, 0.2f, 0.8f, () => mb.AddBox(T(side * (NHallX - 0.01f), NFloor + 0.5f, eMid), new Vector3(0.05f, 1.0f, eLen - 0.1f), Slate));
                Glow(mb, () => mb.AddBox(T(side * (NHallX - 0.03f), NFloor + 1.02f, eMid), new Vector3(0.02f, 0.03f, eLen - 0.3f), new Color(1f, 0.55f, 0.15f)), 0.9f);
            }
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, NEngTop + 0.04f, eMid), new Vector3(NHallX * 2f + 0.1f, 0.08f, eLen), panel));
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, (NFloor + NEngTop) * 0.5f, NEngZ0 - 0.05f), new Vector3(NHallX * 2f + 0.1f, NEngTop - NFloor + 0.1f, 0.1f), panel));
            // bulkhead around the kit's aft doorway (the fuselage is wider and taller than the kit)
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                    mb.AddBox(T(s * (NHallX + 2.2f) * 0.5f, (NFloor + NEngTop) * 0.5f, NHallZ0), new Vector3(NHallX - 2.2f + 0.1f, NEngTop - NFloor, 0.14f), Slate);
            });
            // the warp core: a ringed column, glowing cyan through its windows, on a hazard-striped plinth
            With(mb, 0.6f, 1f, 0.3f, () =>
            {
                mb.AddLathe(T(NCore.x, NFloor, NCore.z), new[] { new Vector2(1.0f, 0f), new Vector2(1.0f, 0.25f), new Vector2(0.75f, 0.32f), new Vector2(0.75f, 0.35f) }, 32, Graphite);
                mb.AddLathe(T(NCore.x, NEngTop - 0.36f, NCore.z), new[] { new Vector2(0.75f, 0f), new Vector2(0.75f, 0.03f), new Vector2(1.0f, 0.1f), new Vector2(1.0f, 0.36f) }, 32, Graphite);
            });
            Glow(mb, () => mb.AddTube(new Vector3(NCore.x, NFloor + 0.3f, NCore.z), new Vector3(NCore.x, NEngTop - 0.3f, NCore.z), 0.5f, 0.5f, 24, new Color(0.35f, 0.85f, 1f)), 1.6f);
            With(mb, 0.55f, 1f, 0f, () =>
            {
                for (int k = 0; k < 5; k++)
                {
                    float y = NFloor + 0.6f + k * 0.55f;
                    mb.AddLathe(T(NCore.x, y, NCore.z), new[] { new Vector2(0.62f, -0.06f), new Vector2(0.66f, 0f), new Vector2(0.62f, 0.06f) }, 28, Gunmetal);
                }
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    var o = new Vector3(Mathf.Cos(a) * 0.6f, 0f, Mathf.Sin(a) * 0.6f);
                    mb.AddTube(NCore + o + Vector3.up * 0.32f, NCore + o + Vector3.up * (NEngTop - NFloor - 0.32f), 0.04f, 0.04f, 8, Gunmetal);
                }
            });
            // engineer console (left wall) and the aft gunner's screen (right wall)
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(-NHallX + 0.3f, NFloor + 0.45f, -11.6f), new Vector3(0.6f, 0.9f, 1.8f), Navy);
                mb.AddBox(TR(-NHallX + 0.35f, NFloor + 0.95f, -11.6f, 0, 0, -25), new Vector3(0.5f, 0.1f, 1.7f), Graphite);
                mb.AddBox(T(-NHallX + 0.06f, NFloor + 1.6f, -11.6f), new Vector3(0.08f, 0.9f, 1.7f), Graphite);
                mb.AddBox(T(NHallX - 0.06f, NFloor + 1.5f, -11.6f), new Vector3(0.08f, 0.7f, 1.0f), Graphite);
            });
            Glow(mb, () => mb.AddBox(TR(-NHallX + 0.38f, NFloor + 1.01f, -11.6f, 0, 0, -25), new Vector3(0.34f, 0.015f, 1.5f), Cyan), 0.9f);
            Glow(mb, () => mb.AddBox(T(-NHallX + 0.11f, NFloor + 1.6f, -11.6f), new Vector3(0.015f, 0.72f, 1.5f), new Color(0.3f, 0.6f, 1f)), 0.6f);
            Glow(mb, () => mb.AddBox(T(NHallX - 0.105f, NFloor + 1.5f, -11.6f), new Vector3(0.01f, 0.56f, 0.86f), Cyan), 0.6f);

            // ---- around the kit hall: plating between the kit's walls and the fuselage at both doorways
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                for (int s = -1; s <= 1; s += 2)
                    mb.AddBox(T(s * (NHallX + 2.2f) * 0.5f, wallMid, NHallZ1), new Vector3(NHallX - 2.2f + 0.1f, wallH, 0.14f), Slate);
                mb.AddBox(T(0, (NFloor + NKitHeight + 0.2f + NHallTop) * 0.5f, NHallZ1), new Vector3(NHallX * 2f, NHallTop - NFloor - NKitHeight - 0.2f, 0.14f), Slate);
            });
            // hatches up to the docking clamps: framed panel with a lit ring and a ladder
            for (int i = 0; i < NHatchZ.Length; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (NKitWall + 0.02f), z = NHatchZ[i];
                With(mb, 0.7f, 0.85f, 0f, () => mb.AddBox(T(x, NFloor + 1.1f, z), new Vector3(0.1f, 2.1f, 1.1f), Gold));
                With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(x - side * 0.04f, NFloor + 1.1f, z), new Vector3(0.06f, 1.9f, 0.9f), Graphite));
                Glow(mb, () => mb.AddBox(T(x - side * 0.075f, NFloor + 1.9f, z), new Vector3(0.01f, 0.06f, 0.7f), Cyan), 1.2f);
                Glow(mb, () => mb.AddBox(T(x - side * 0.075f, NFloor + 0.3f, z), new Vector3(0.01f, 0.06f, 0.7f), Cyan), 1.2f);
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    for (int r = 0; r < 6; r++) mb.AddTube(new Vector3(x - side * 0.12f, NFloor + 0.4f + r * 0.3f, z - 0.3f), new Vector3(x - side * 0.12f, NFloor + 0.4f + r * 0.3f, z + 0.3f), 0.02f, 0.02f, 6, Gunmetal);
                });
            }
            // fabricator and cargo lockers on the right wall
            float fx = NKitWall - 0.3f;
            With(mb, Paint, 0.2f, 0.7f, () => mb.AddBox(T(fx + 0.05f, NFloor + 1.0f, -2.6f), new Vector3(0.6f, 2.0f, 1.7f), panel));
            With(mb, 1f, 0.2f, 0f, () => mb.AddBox(T(fx - 0.26f, NFloor + 1.15f, -2.6f), new Vector3(0.03f, 0.9f, 1.1f), new Color(0.1f, 0.25f, 0.35f)));
            Glow(mb, () => mb.AddBox(T(fx - 0.2f, NFloor + 1.15f, -2.6f), new Vector3(0.01f, 0.6f, 0.8f), Cyan), 0.5f);
            With(mb, Satin, 0.3f, 0.6f, () =>
            {
                mb.AddBox(T(fx + 0.05f, NFloor + 1.0f, 1.0f), new Vector3(0.6f, 2.0f, 2.1f), Graphite);
                for (int d = 0; d < 3; d++) mb.AddBox(T(fx - 0.26f, NFloor + 0.4f + d * 0.6f, 1.0f), new Vector3(0.03f, 0.5f, 1.9f), Slate);
            });
            for (int d = 0; d < 3; d++) Glow(mb, () => mb.AddBox(T(fx - 0.28f, NFloor + 0.55f + d * 0.6f, 1.75f), new Vector3(0.01f, 0.06f, 0.06f), d == 0 ? Green : Gold), 1.4f);

            // ---- airlock: floor plate over the cut trench, tunnel through the flank, hazard frame, the ramp outside
            float dMid = (NDoorZ0 + NDoorZ1) * 0.5f, dLen = NDoorZ1 - NDoorZ0, tunX = -(NKitWall + NSkinX) * 0.5f + 0.4f, tunW = NSkinX - NKitWall + 0.9f;
            With(mb, Satin, 0.6f, 0.6f, () =>
            {
                mb.AddBox(T(tunX, NFloor - 0.06f, dMid), new Vector3(tunW, 0.12f, dLen), Graphite);
                mb.AddBox(T(-(NHallX + NSkinX) * 0.5f, NDoorTop + 0.05f, dMid), new Vector3(NSkinX - NHallX + 0.05f, 0.1f, dLen), Graphite);
                mb.AddBox(T(-(NKitWall + NSkinX) * 0.5f, (NFloor + NDoorTop) * 0.5f, NDoorZ0 - 0.05f), new Vector3(NSkinX - NKitWall, NDoorTop - NFloor, 0.1f), Graphite);
                mb.AddBox(T(-(NKitWall + NSkinX) * 0.5f, (NFloor + NDoorTop) * 0.5f, NDoorZ1 + 0.05f), new Vector3(NSkinX - NKitWall, NDoorTop - NFloor, 0.1f), Graphite);
            });
            With(mb, 0.7f, 0.85f, 0f, () =>
            {
                mb.AddBox(T(-NKitWall, (NFloor + NDoorTop) * 0.5f, NDoorZ0 - 0.08f), new Vector3(0.12f, NDoorTop - NFloor + 0.1f, 0.14f), Gold);
                mb.AddBox(T(-NKitWall, (NFloor + NDoorTop) * 0.5f, NDoorZ1 + 0.08f), new Vector3(0.12f, NDoorTop - NFloor + 0.1f, 0.14f), Gold);
            });
            Hazard(mb, T(tunX, NFloor + 0.004f, dMid), tunW, 0.3f, 8, 0.01f);
            for (int s = -1; s <= 1; s += 2)
                Glow(mb, () => mb.AddBox(T(-NKitWall - 0.1f, NDoorTop - 0.15f, dMid + s * (dLen * 0.5f + 0.25f)), new Vector3(0.04f, 0.16f, 0.12f), new Color(1f, 0.55f, 0.15f)), 1.5f);
            With(mb, Satin, 0.5f, 0.5f, () => mb.AddBox(T(-NSkinX - 0.03f, NFloor + 0.3f, NDoorZ1 + 1.4f), new Vector3(0.08f, 0.9f, 0.7f), Graphite));
            Glow(mb, () => mb.AddBox(T(-NSkinX - 0.075f, NFloor + 0.4f, NDoorZ1 + 1.4f), new Vector3(0.01f, 0.5f, 0.5f), Cyan), 0.7f);
            var rampFrom = new Vector3(-NSkinX, NFloor, dMid);
            var rampTo = new Vector3(-8.25f, -4.725f, dMid);
            var rampDir = rampFrom - rampTo;
            float rampAngle = Mathf.Atan2(rampDir.y, rampDir.x) * Mathf.Rad2Deg;
            var rampM = Matrix4x4.TRS((rampFrom + rampTo) * 0.5f + new Vector3(0, -0.06f, 0), Quaternion.Euler(0f, 0f, rampAngle), Vector3.one);
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(rampM, new Vector3(rampDir.magnitude, 0.12f, dLen), Graphite));
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (float u = -rampDir.magnitude * 0.5f + 0.3f; u < rampDir.magnitude * 0.5f - 0.2f; u += 0.32f)
                    mb.AddBox(rampM * T(u, 0.065f, 0f), new Vector3(0.08f, 0.015f, dLen - 0.3f), Rubber);
            });
            Hazard(mb, rampM * T(-rampDir.magnitude * 0.5f + 0.15f, 0.07f, 0f) * Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0)), dLen, 0.25f, 8, 0.01f);
            for (int s = -1; s <= 1; s += 2)
                With(mb, 0.55f, 1f, 0f, () =>
                {
                    float rz = dMid + s * (dLen * 0.5f - 0.05f);
                    mb.AddTube(new Vector3(rampTo.x + 0.1f, rampTo.y + 0.9f, rz), new Vector3(rampFrom.x - 0.05f, rampFrom.y + 0.9f, rz), 0.03f, 0.03f, 8, Gunmetal);
                    mb.AddTube(new Vector3(rampTo.x + 0.1f, rampTo.y, rz), new Vector3(rampTo.x + 0.1f, rampTo.y + 0.9f, rz), 0.03f, 0.03f, 8, Gunmetal);
                });

            // ---- cryo bay: floor, walls, ceiling, frost-blue strips
            float cMid = (NHallZ1 + NBridgeZ0) * 0.5f, cLen = NBridgeZ0 - NHallZ1;
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, NFloor - 0.06f, cMid), new Vector3(NHallX * 2f, 0.12f, cLen), floorCol));
            for (int s = -1; s <= 1; s += 2)
            {
                int side = s;
                With(mb, Paint, 0.2f, 1f, () => mb.AddBox(T(side * (NHallX + 0.04f), wallMid, cMid), new Vector3(0.08f, wallH, cLen), panel));
                Glow(mb, () => mb.AddBox(T(side * (NHallX - 0.03f), NHallTop - 0.25f, cMid), new Vector3(0.02f, 0.03f, cLen - 0.3f), new Color(0.6f, 0.9f, 1f)), 0.9f);
            }
            With(mb, Paint, 0f, 1f, () => mb.AddBox(T(0, NHallTop + 0.04f, cMid), new Vector3(NHallX * 2f + 0.1f, 0.08f, cLen), panel));
            With(mb, 0.2f, 0f, 0f, () => mb.AddBox(T(0, NFloor + 0.005f, cMid), new Vector3(1.2f, 0.01f, cLen - 0.4f), Rubber));

            // ---- bulkhead between cryo bay and bridge: header over the opening, jambs where the cab narrows
            With(mb, Satin, 0.5f, 0.6f, () =>
            {
                mb.AddBox(T(0, (NHallTop + NBridgeTop) * 0.5f, NBridgeZ0), new Vector3(NBridgeX * 2f, NBridgeTop - NHallTop, 0.16f), Slate);
                for (int s = -1; s <= 1; s += 2)
                    mb.AddBox(T(s * (NBridgeX + NHallX) * 0.5f, wallMid, NBridgeZ0), new Vector3(NHallX - NBridgeX + 0.1f, wallH, 0.16f), Slate);
            });
            Hazard(mb, TR(0, NHallTop - 0.08f, NBridgeZ0 - 0.085f, 90, 0, 0), NBridgeX * 2f, 0.12f, 12, 0.01f);

            // ---- bridge: floor, walls, ceiling, the walls under the slanted window, raised pilot platform with a ramp, front console
            float bMid = (NBridgeZ0 + NBridgeZ1) * 0.5f, bLen = NBridgeZ1 - NBridgeZ0;
            With(mb, 0.45f, 0.5f, 1f, () => mb.AddBox(T(0, NFloor - 0.06f, bMid), new Vector3(NBridgeX * 2f, 0.12f, bLen), floorCol));
            float wTopZ = NWindowTop.x;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (NBridgeX + 0.05f);
                With(mb, Paint, 0.2f, 1f, () =>
                {
                    mb.AddBox(T(x, (NFloor + NBridgeTop) * 0.5f, (NBridgeZ0 + wTopZ) * 0.5f), new Vector3(0.1f, NBridgeTop - NFloor, wTopZ - NBridgeZ0), panel);
                    mb.AddBox(T(x, (NFloor + NWindowBottom.y) * 0.5f, (wTopZ + NBridgeZ1) * 0.5f), new Vector3(0.1f, NWindowBottom.y - NFloor, NBridgeZ1 - wTopZ), panel);
                    mb.AddSlab(Matrix4x4.TRS(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one), new[]
                    {
                        new Vector2(-NWindowBottom.y, wTopZ), new Vector2(-NWindowBottom.y, NWindowBottom.x - 0.05f), new Vector2(-NWindowTop.y, wTopZ),
                    }, 0.1f, 0.01f, panel);
                });
                With(mb, Satin, 0.2f, 0.8f, () => mb.AddBox(T(s * (NBridgeX - 0.01f), NFloor + 0.5f, (NBridgeZ0 + NRampZ) * 0.5f), new Vector3(0.05f, 1.0f, NRampZ - NBridgeZ0 - 0.2f), Slate));
                Glow(mb, () => mb.AddBox(T(s * (NBridgeX - 0.03f), NFloor + 1.02f, (NBridgeZ0 + NRampZ) * 0.5f), new Vector3(0.02f, 0.03f, NRampZ - NBridgeZ0 - 0.4f), Cyan), 0.8f);
            }
            With(mb, Paint, 0f, 1f, () =>
            {
                mb.AddBox(T(0, NBridgeTop + 0.05f, (NBridgeZ0 + wTopZ) * 0.5f), new Vector3(NBridgeX * 2f + 0.2f, 0.1f, wTopZ - NBridgeZ0), panel);
                mb.AddBox(T(0, (NWindowTop.y + NBridgeTop) * 0.5f + 0.05f, wTopZ - 0.04f), new Vector3(NBridgeX * 2f + 0.2f, NBridgeTop - NWindowTop.y + 0.1f, 0.08f), panel);
                mb.AddBox(T(0, (NFloor + NWindowBottom.y) * 0.5f, NBridgeZ1 + 0.05f), new Vector3(NBridgeX * 2f + 0.2f, NWindowBottom.y - NFloor, 0.1f), panel);
            });
            for (int k = 0; k < 3; k++)
            {
                float zc = 12f + k * 4.3f;
                With(mb, Satin, 0.3f, 0f, () => mb.AddBox(T(0, NBridgeTop - 0.015f, zc), new Vector3(1.6f, 0.03f, 1.8f), Graphite));
                Glow(mb, () => mb.AddBox(T(0, NBridgeTop - 0.032f, zc), new Vector3(1.4f, 0.01f, 1.6f), new Color(0.85f, 0.93f, 1f)), 0.75f);
            }
            // platform: deck block with a lit edge, a ramp up with grip strips and rails
            float pMid = (NPlatformZ + NBridgeZ1) * 0.5f, pLen = NBridgeZ1 - NPlatformZ;
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(T(0, (NFloor + NPlatform) * 0.5f, pMid), new Vector3(NBridgeX * 2f, NPlatform - NFloor, pLen), Graphite));
            Glow(mb, () => mb.AddBox(T(0, NPlatform - 0.01f, NPlatformZ + 0.02f), new Vector3(NBridgeX * 2f - 0.1f, 0.02f, 0.04f), new Color(1f, 0.55f, 0.15f)), 1.1f);
            var pr0 = new Vector3(0f, NFloor, NRampZ);
            var pr1 = new Vector3(0f, NPlatform, NPlatformZ);
            var prM = Matrix4x4.TRS((pr0 + pr1) * 0.5f + new Vector3(0, -0.06f, 0), Quaternion.LookRotation(pr1 - pr0, Vector3.up), Vector3.one);
            float prLen = (pr1 - pr0).magnitude;
            With(mb, Satin, 0.6f, 0.6f, () => mb.AddBox(prM, new Vector3(1.6f, 0.12f, prLen), Graphite));
            With(mb, 0.3f, 1f, 0f, () =>
            {
                for (float u = -prLen * 0.5f + 0.25f; u < prLen * 0.5f - 0.1f; u += 0.3f) mb.AddBox(prM * T(0f, 0.065f, u), new Vector3(1.4f, 0.015f, 0.08f), Rubber);
            });
            for (int s = -1; s <= 1; s += 2)
                With(mb, 0.55f, 1f, 0f, () => mb.AddTube(pr0 + new Vector3(s * 0.85f, 0.95f, 0f), pr1 + new Vector3(s * 0.85f, 0.95f, 0f), 0.03f, 0.03f, 8, Gunmetal));
            With(mb, 0.2f, 0f, 0f, () => mb.AddBox(T(0, NPlatform + 0.005f, pMid - 0.6f), new Vector3(0.8f, 0.01f, 1.8f), Rubber));
            // front console under the window (between the Dart dashboard and the window's bottom edge)
            float fz0 = NEye.z + 1.2f;
            With(mb, Satin, 0.45f, 0.8f, () => mb.AddBox(T(0, (NPlatform + NWindowBottom.y - 0.1f) * 0.5f, (fz0 + NBridgeZ1) * 0.5f), new Vector3(NBridgeX * 2f, NWindowBottom.y - 0.1f - NPlatform, NBridgeZ1 - fz0), Graphite));
            Glow(mb, () => mb.AddBox(T(0, NWindowBottom.y - 0.09f, fz0 + 0.04f), new Vector3(NBridgeX * 2f - 0.2f, 0.015f, 0.03f), Cyan), 1.0f);
            // forward gunner's screen (bridge, left wall)
            With(mb, Satin, 0.3f, 0.3f, () => mb.AddBox(T(-NBridgeX + 0.06f, NFloor + 1.6f, 12.2f), new Vector3(0.08f, 0.7f, 1.0f), Graphite));
            Glow(mb, () => mb.AddBox(T(-NBridgeX + 0.105f, NFloor + 1.6f, 12.2f), new Vector3(0.01f, 0.56f, 0.86f), Red), 0.6f);
            return SaveMesh(mb, "Ship_Nostos_Deck");
        }

        /// <summary>Docking clamps on the wings: a pylon from the wing up to the Dart's belly, two cradle arms, a lit rim.</summary>
        static Mesh NostosClampMesh()
        {
            var mb = Hero(0.06f);
            for (int i = 0; i < NClamps.Length; i++)
            {
                var c = NClamps[i];
                float belly = c.y - 1.75f, wing = NClampWing[i];
                float h = belly - wing;
                With(mb, Satin, 0.7f, 0.5f, () =>
                {
                    mb.AddBox(T(c.x, wing + h * 0.5f, c.z), new Vector3(0.9f, h, 3.2f), Graphite);
                    mb.AddBox(T(c.x, wing + 0.1f, c.z), new Vector3(1.6f, 0.2f, 4.2f), Gunmetal);
                    mb.AddBox(T(c.x, belly - 0.08f, c.z), new Vector3(2.6f, 0.16f, 1.2f), Gunmetal);
                    for (int s = -1; s <= 1; s += 2)
                        mb.AddBox(TR(c.x + s * 1.2f, belly + 0.1f, c.z, 0, 0, s * 35f), new Vector3(0.18f, 0.6f, 1.0f), Graphite);
                });
                Glow(mb, () => mb.AddBox(T(c.x, belly - 0.18f, c.z), new Vector3(2.4f, 0.03f, 1.0f), Cyan), 1.2f);
                Hazard(mb, T(c.x, wing + 0.205f, c.z), 1.4f, 0.3f, 6, 0.01f);
            }
            return SaveMesh(mb, "Ship_Nostos_Clamps");
        }

        /// <summary>Procedural stand-in for the cryo pod model: a capsule shell with a frosted window, front = +Z.</summary>
        static Mesh CryoPodFallbackMesh()
        {
            var mb = Hero(0.05f);
            With(mb, Satin, 0.4f, 0.5f, () => mb.AddBox(T(0, 1.15f, -0.1f), new Vector3(0.95f, 2.3f, 0.8f), new Color(0.82f, 0.86f, 0.9f)));
            Glow(mb, () => mb.AddBox(T(0, 1.3f, 0.31f), new Vector3(0.6f, 1.5f, 0.02f), new Color(0.55f, 0.85f, 1f)), 0.6f);
            return SaveMesh(mb, "Ship_Nostos_CryoPod");
        }
    }
}
