# VOIDWARD — co-op space roguelite (vertical-slice demo)

> *Find the way home. Together.*

A first-person co-op space roguelite for 1–4 players. A routine **moon mission** falls into a
**wormhole** and the crew wakes up in the **Triangulum Galaxy (M33)** — 2.73 million light-years from
home. Explore alien planets, mine, loot and fight for drive parts, exotic matter and alien tech,
upgrade the ship and above all its **warp drive**, and fly galaxy by galaxy through the Local Group
back to the Milky Way. The **Home ETA** always shows how long the flight home would take with the
current drive. Die, and the wormhole's **time loop** wakes you at its exit again — with your memories.

Route: M33 → Andromeda (M31) → NGC 6822 → Leo I → Large Magellanic Cloud → home flight to Sol.
Two endings only: **Homecoming** or **death** (= the loop resets). Target run length ~2.5–3 h.

> **Transition note:** the design above (issue #72) replaced the old loop ("outrun the Collapse
> through 3 sectors, power Jump Gates, defeat the Warden"). The run structure (#46: 5 galaxies, systems,
> galaxy map, suspend save) and the crew warp drive with Home ETA (#78) are playable; still to come are the flown
> warp (#79-#81, parts of #73), prologue/loop/homecoming
> (#74) and the home ship (#49). Section 3 describes both: the target design and what is playable today.

Built with Unity 6 (URP), Netcode for GameObjects, Unity Transport + Facepunch.Steamworks,
the Input System, Cinemachine 3 and TextMeshPro. All art, audio and music are procedural
(no external assets required), in a polished, high-end stylized sci-fi look: clean chunky
shapes in the spirit of Astroneer, but with real surface detail, lighting and VFX - not abstract placeholder art.

---

## 1. Running the game

1. Open the project in **Unity 6000.6.x**.
2. Run **Voidward ▸ Build All Content** once (it regenerates every ScriptableObject, mesh, material,
   prefab, the network prefab list and the scene `Assets/_Project/Scenes/Voidward.unity`).
   The repository already contains the generated output; rerun after changing the builder code.
3. Open `Assets/_Project/Scenes/Voidward.unity` and press **Play**.
4. Main menu → **Host Game** → **Start Run**.

### Standalone build
*File ▸ Build Profiles* → Windows (or macOS) → Build. `steam_appid.txt` (AppID 480) is copied
next to the executable automatically by `SteamAppIdBuildStep`.

> The machine this demo was produced on only has the **macOS** build module installed, so the build
> was verified as a macOS standalone. Install *Windows Build Support (Mono/IL2CPP)* in Unity Hub to
> produce the Windows build — no code changes are needed (Facepunch Win64 binaries are included).

---

## 2. Transports & multiplayer testing

| Transport | When | How to connect |
|-----------|------|----------------|
| **UTP** (Unity Transport) | default in the Editor | Host, others join by IP (default `127.0.0.1:7777`) |
| **Steam** (Facepunch, AppID 480) | default in builds | Steam lobby: invite via overlay (Shift+Tab) / *Invite Friends*, or *Join ▸ Refresh* lobby list; traffic via Steam relay/P2P |

* Switch in the main menu (**Transport: … ▸ Switch**) or with the command line:
  `-transport=utp|steam`, `-ip=1.2.3.4`, `-port=7777`.
* If Steam is not running or fails to initialize the game **falls back to UTP** automatically
  (notification shown, no crash).
* Automation flags (useful for local tests): `-autohost`, `-autojoin=127.0.0.1`,
  `-autostart -players=2` (host starts the run once N players are connected).

### Local test with Multiplayer Play Mode (UTP)
1. *Window ▸ Multiplayer ▸ Multiplayer Play Mode* → enable **Player 2…4** (up to 4 virtual players).
2. Press Play. In the main editor window click **Host Game**.
3. In each virtual player window click **Join Game ▸ Join IP** (`127.0.0.1`).
4. Host clicks **Start Run**.

### Local test with two builds / build + editor
```
Voidward.app/Contents/MacOS/Voidward -transport=utp -autohost -autostart -players=2
Voidward.app/Contents/MacOS/Voidward -autojoin=127.0.0.1
```
*Voidward ▸ Build ▸ macOS / Windows* produce release builds, *… (Development)* produce development
builds. **Editor ↔ build** sessions require a *Development* build (Netcode's config hash differs between
the Editor and release players); build ↔ build and Editor ↔ Multiplayer Play Mode work with any build type.

### Steam test (two PCs, two Steam accounts)
1. Both PCs run Steam with different accounts that are friends.
2. Build the game (Steam is the default transport in builds; `steam_appid.txt` = 480).
3. PC 1: **Host Game** → a Steam lobby is created → **Invite Friends** (or Shift+Tab overlay).
4. PC 2: accept the invite (the game joins automatically) or **Join Game ▸ Refresh ▸ Join**.
5. Host: **Start Run**.

Late joiners are dropped into the running system with a fresh ship. If a client disconnects, its
ship stays in the world. If the host quits, clients return to the main menu with a notification.

---

## 3. How to play

### Target design (gameplan #72)

**Loop per galaxy:** arrive by warp flight → galaxy map with 4–6 systems → short warp to a system,
scan from orbit → **expedition**: land, explore, mine, find drive parts, exotic matter and blueprints,
fight fauna and aliens while the system's **alert level** rises → back on board: fit parts, craft,
upgrade → the Home ETA drops → keep searching or warp on to the next galaxy (one-way).

* **Travel is gameplay:** no portals, no cutscenes. Warp flights are flown by the pilot with a warp
  throttle, through long empty stretches and obstacles (asteroids, debris, anomalies). A better drive
  is really faster; landing and combat handle the same at every tier (#73).
* **Drive tiers:** Mk I (mission ship) → Mk II (M33) → Mk III (M31) → Mk IV (NGC 6822) → Mk V (Leo I)
  → Home Drive (LMC, ~5 min home). One drive realistically reaches the next galaxy; skipping one costs
  much more flight time and exotic matter.
* **Enemies:** nature and aliens only (no pirates): fauna, an alien ecosystem per galaxy and the
  **Wardens**, an ancient biomechanical species that guards warp cores in its enclaves.
* **Death:** a downed player is revived by the crew or wakes in a cryo pod on board. Crew wipe or the
  home ship destroyed → the time loop resets the run to M33. Meta progression = the **Logbook of the
  Loop** (memory fragments, codex, unlocks).
* **The Rift** (formerly the Collapse) only appears in the finale: the flight home is an escape from it.

### Currently playable build

**Loop:** wake up at the *Wormhole Exit* in M33 → explore the system (scan/ping, land, mine, loot, fight)
→ back to the ship, upgrade/craft → open the **galaxy map (M)** → as pilot, warp to another system of the
galaxy or one-way to the next galaxy → … → from the Large Magellanic Cloud: **fly home** (homecoming).

* **Galaxies & systems:** 5 galaxies (M33, M31, NGC 6822, Leo I, LMC), each with 4-6 systems generated
  from the run seed: planetary systems, asteroid & wreck fields, anomalies (frequent flares/meteors),
  nomad meeting points (trader), empty space (distress signals) and one **Warden enclave** per galaxy.
  Enemies, loot tier and events get harder from galaxy to galaxy. The sky shows the journey: the next
  galaxy and the Milky Way grow along the route.
* **Warp drive (#78):** the crew shares one warp drive with four components - **warp core** (top speed,
  Mk I - Mk V, Home Drive), **nav computer** (how much of that speed is safe in fields), **cooling** (how long
  full throttle holds) and **Exotic tank** (capacity). Swap parts and load **Exotic Matter** (warp fuel) in the
  *Warp Drive* tab (Tab, or the button on the galaxy map) while at a ship. The **Home ETA** (HUD under the
  compass, cockpit NAV, galaxy map) shows the real flight time home and drops with every better part; each
  backpack part shows its ETA change. Warden enclaves guard the best core of their galaxy; crates, wrecks,
  anomalies, crystal veins, alien nests and Nomad traders give Exotic Matter, nav/cooling/tank parts.
* **Warping (placeholder until the flown warp of #79/#80):** the pilot sets a course on the galaxy map, after
  a 5 s countdown the whole crew warps. Systems of a galaxy can be revisited (their crates stay looted).
  The warp to the next galaxy is one-way and burns Exotic Matter; **skipping a galaxy** costs much more
  (and needs a bigger tank). There are no mandatory objectives.
* **Warden enclave:** fly to the far end of the enclave system to wake its Warden: destroy its three
  glowing core pods (weak points) to drop its shield, then the hull. It drops a hoard of top-tier crates.
* **Oxygen** drains outside ships and outside breathable atmospheres (Ice and Crystal worlds are
  breathable). Ships, the Ark interior and station pads refill it.
* **Team respawns**: 4 per run (+1 per new galaxy). Respawn happens at your ship (or any ship).
  Lose your ship and you get a replacement Dart after 20 s. If the whole crew is lost, the loop resets.
* **The Ark** — a derelict capital ship near the wormhole exit. Bring 8 Scrap and restore its
  power. It has Pilot, 2 Turret (flak/laser) and Engineer stations, a fabricator, a big hold,
  big shields and a walkable interior with its own gravity. Players can walk around inside it while
  it flies. (Becomes the home ship in the new design, #49.)
* **Trader stations** (wormhole exit and nomad systems): buy modules/gear/cells/fuel, sell resources,
  repair & refuel (60 cr).
* **Events**: meteor showers, distress signals (salvage + hostiles), solar flares (shields
  offline, radiation in the open).
* **Suspend save:** the host's run is saved when entering a galaxy and when leaving the session; *Continue
  Run* in the lobby resumes it (players are matched by name). The save is deleted when the run ends.
* **Meta progression**: every run awards *Stardust* (distance, kills, galaxies, homecoming).
  Spend it in the **Hangar** on perks, the Mule ship and starting modules (saved locally).
  (Becomes memory fragments / the Logbook, #47.)

### Controls (rebindable in Settings)

| Action | On foot | Piloting |
|--------|---------|----------|
| Move / thrust | WASD | WASD (forward/strafe) |
| Up / down | Space (jump / jetpack) · Ctrl | Space / Ctrl |
| Look / steer | Mouse | Mouse (pitch/yaw) |
| Roll | Q / E (zero-g) | Q / E |
| Sprint / boost | Shift | Shift |
| Brake | X (EVA) | X |
| Fire / alt-fire | LMB / RMB | weapon 1 / weapon 2 |
| Interact / enter-exit seat | F | F (leave seat) |
| Tools | 1 Drill · 2 Blaster · 3 Rifle · 4 Medkit | – |
| Inventory / ship / crafting | Tab | Tab |
| Galaxy map (warp course) | M | M |
| Ping (enemy / loot / look) | T or MMB | T |
| Helmet flashlight on/off | L | – (ship headlights turn on automatically) |
| Pause | Esc | Esc |
| Debug cheats | F1 | F1 |

**F1 cheats** (host-authoritative): god mode, give resources + credits, warp to the next system, spawn enemy,
warp to the next galaxy, wake a Warden nearby, trigger an event, refill ship. FPS counter included.

---

## 4. Content overview

* **Ships:** Dart (agile starter), Mule (hauler, unlock), **Ark** (co-op capital ship, 4 stations).
* **Planet biomes:** Ice, Desert, Toxic Jungle, Crystal, Volcanic — procedural miniature worlds
  (radius 1.0–1.9 km) with their own geology (terraced canyon networks, buttes and hoodoos;
  jagged peaks, glacier domes and frozen chasms; river valleys and karst towers; crystal spire
  mountains and glowing lakes; calderas, basalt plateaus and lava channels), scattering
  atmospheres, cloud layers, weather (sandstorms, blizzards, ash fall, spore blooms), biome props
  and landmark set pieces (arches, giant crystals, elder trees, monolith rings, colossal bones...).
  Each planet has 6-8 **sites** - flattened landing clearings next to landforms holding ruins,
  crash sites, hives, resource fields or supply caches. The HUD labels sites on approach.
* **Enemies (7 + boss):** Pirate Fighter, Swarm Drone, Pirate Gunship (Andromeda+), Crawler,
  Spitter, Skyskate (flying critter), Nest Guardian, **The Warden** (3-phase boss with weak points).
  The pirate ships are being rebuilt as alien enemies (#51).
* **Modules (23):** Engine Mk I–III (+Ark drive), Laser Mk I–III, Missile Pod Mk I–II,
  Flak Mk I–II, Shield Mk I–III, Hull Mk I–III, Cargo Mk I–II, Scanner Mk I–II, Fuel Tank Mk I–II.
* **Gear (9):** Suit Mk II/III, Oxygen Tank Mk II/III, Jetpack Mk II/III, Drill Mk II/III, Pulse Rifle.
* **Resources:** Ore, Crystal, Scrap, Fuel, Exotic Matter, Credits. 17 crafting recipes.
* **Warp drive parts (24):** Warp Core / Nav Computer / Cooling / Exotic Tank, each Mk I-V plus tier 6 (Home Drive core, Warden parts).
* **Galaxies:** Triangulum (M33) → Andromeda (M31) → Barnard's Galaxy (NGC 6822) → Leo I → Large
  Magellanic Cloud → home, each with its own palette, biome pool, enemy mix and difficulty/loot tier,
  and 4–6 seeded systems.

---

## 5. Architecture

```
Assets/_Project/
  Scripts/
    Core/         service locator (G), EventBus, input (VwInput), gravity, math/noise, mesh builder,
                  audio (procedural AudioManager), VFX (VfxManager), save system, cheats, NetTeleport
    Data/         ScriptableObject definitions + ContentCatalog (Resources)
    Networking/   SessionManager (host/join/leave, transport switch + Steam fallback),
                  SteamTransport (Facepunch P2P), SteamLobbyService, TransportConfig, NetworkPlayer
    Combat/       HealthComponent (server-authoritative), CombatNet (projectile system), TargetTag
    Inventory/    NetInventory (grid, NetworkList), WorldPickup, LootCrate, ResourceNode, PlayerGear/crafting
    Planet/       PlanetBody (seed-synced planet, sites), PlanetTerrain (height field), PlanetVisuals
                  (chunked quadtree LOD + collider streaming), PlanetDecor, PlanetLandmarks, PlanetAmbience
    Ship/         ShipCore (stats/modules/shields/fuel/weapons), ShipFlight, ShipSeat, terminals, dashboard
    Player/       PlayerPawn (FPS controller, EVA, seats, tools, oxygen, interaction, pings)
    AI/           EnemyBrain (state machine), AlienNest, BossBrain, EnemyFactory
    World/        RunManager (run flow, galaxies/systems, warp, suspend save, stats), GalaxyGenerator (seeded
                  galaxy plans), SystemDirector (system content, spawns, events, Warden enclave), SystemLayout,
                  SystemVisuals (client-local asteroids/palette/journey sky), RunState/RunSave, TraderStation
    Progression/  Meta (Stardust, perks, loadout, local save)
    UI/           code-built uGUI/TMP screens (menu, lobby, hangar, settings, HUD, inventory, trade…)
  Editor/         ContentBuilder (data, materials), PrefabBuilder (meshes, prefabs), SceneBuilder
  Shaders/        ToonFlat (flat/toon), Planet, Ocean, Atmosphere (scattering), Clouds, NebulaSkybox,
                  Collapse (kept for the Rift, #73), Glass, Unlit, AdditiveParticle
```

Assembly definitions keep the graph acyclic (Core ← Data ← Combat/Inventory/Planet ← Ship ← Player
← World ← UI; Editor references everything).

**Networking model**
* Server-authoritative gameplay: damage, loot, inventory, crafting, trading, AI, spawning, oxygen,
  respawns, warps and the suspend save all run on the host. Clients send requests via RPCs.
* Movement: player pawns and piloted ships use **owner-authoritative NetworkTransforms** — the
  owner simulates locally (instant, prediction-like response) and everyone else interpolates.
  Taking the pilot seat transfers ship ownership to that client; leaving returns it to the server.
* Projectiles are not network objects: one RPC spawns identical cosmetic copies on all clients,
  the server resolves hits and reports impacts.
* Procedural content is **seeded**: only seeds and a few parameters are synced; planets
  (geometry + colliders), asteroid fields and trader stock are generated identically on every peer.
* **Floating origin:** every system is generated around the world origin and all ships/players are
  re-based on each warp (`NetTeleport`, routed to whoever owns the transform), so coordinates stay
  within ~8 km (including the kilometre-scale planets) and precise in multiplayer.
* Seats do not re-parent network objects: pawns poll the seat registry and follow the seat
  locally; players walking inside the moving Ark are carried by a moving-platform step.

**Performance:** planets are six quadtrees of terrain chunks built on worker threads (finer near
the camera, horizon-culled, skirts hide LOD seams); colliders are streamed only around pawns and
ships; props are generated per chunk. Asteroid fields are batched into a few meshes,
VFX/projectile visuals/audio sources are pooled, and only the active system's content exists at any time.

---

## 6. Known limitations

* Windows standalone build not produced on the development Mac (module not installed);
  verified via macOS build and Play Mode.
* Terrain deformation is not implemented; LOD chunks switch without geomorphing (skirts hide gaps).
* No real client-side prediction/reconciliation — owner authority is used instead (cheat-prone
  but responsive; fine for co-op).
* Players riding inside the moving Ark may see slight jitter of *other* players.
* Docking is "land on the pad/next to the ship"; there is no magnetic docking clamp.
* Large co-op ship is only found as a derelict (not buildable).
* All visuals/audio are procedural placeholders in the intended style; no Asset Store packages.
* Steam lobby list uses Spacewar (AppID 480), so you may see other developers' test lobbies.
