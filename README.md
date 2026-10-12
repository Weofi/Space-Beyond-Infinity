# VOIDWARD — co-op space roguelite (vertical-slice demo)

> *Outrun the Collapse. Together.*

A first-person co-op space roguelite for 1–4 players: fly your own ship along a three-sector route,
land on kilometre-scale miniature planets, mine, loot and fight, upgrade ship and gear, crew a big co-op ship,
and keep ahead of **The Collapse** — an expanding black hole that consumes everything behind you.
Power the Jump Gates, cross three sectors and defeat **The Warden** to escape.

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

Late joiners are dropped into the running sector with a fresh ship. If a client disconnects, its
ship stays in the world. If the host quits, clients return to the main menu with a notification.

---

## 3. How to play

**Loop:** travel → scan/ping → land or dock → explore, mine & loot → fight → back to the ship →
upgrade/craft → stay ahead of the Collapse → insert Energy Cells into the Jump Gate → jump →
… → sector 3 boss.

* The **Collapse** advances along the route (HUD top-right shows its distance). Anything it
  reaches takes heavy damage; players killed by it are *lost* for the rest of the run.
* Each sector's **Jump Gate** needs Energy Cells (2 / 3). Fly close with cells in your backpack or
  ship cargo and they are inserted automatically (or use the socket under the gate on foot).
  Then everyone alive must gather near the gate → 6 s countdown → jump.
* **Oxygen** drains outside ships and outside breathable atmospheres (Ice and Crystal worlds are
  breathable). Ships, the Ark interior and station pads refill it.
* **Team respawns**: 4 per run (+1 per jump). Respawn happens at your ship (or any ship).
  Lose your ship and you get a replacement Dart after 20 s.
* **The Ark** — a derelict capital ship near the start of sector 1. Bring 8 Scrap and restore its
  power. It has Pilot, 2 Turret (flak/laser) and Engineer stations, a fabricator, a big hold,
  big shields and a walkable interior with its own gravity. Players can walk around inside it while
  it flies.
* **Trader stations**: buy modules/gear/cells/fuel, sell resources, repair & refuel (60 cr).
* **Events**: meteor showers, distress signals (salvage + pirates), solar flares (shields
  offline, radiation in the open).
* **Boss — The Warden**: destroy its three glowing core pods (weak points) to drop its shield,
  then destroy the hull. It spawns drone waves and enrages below 40 %. HP scales with player count.
* **Meta progression**: every run awards *Stardust* (distance, kills, sectors, victory).
  Spend it in the **Hangar** on perks, the Mule ship and starting modules (saved locally).

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
| Ping (enemy / loot / look) | T or MMB | T |
| Helmet flashlight on/off | L | – (ship headlights turn on automatically) |
| Pause | Esc | Esc |
| Debug cheats | F1 | F1 |

**F1 cheats** (host-authoritative): god mode, give resources + credits, skip sector, spawn enemy,
push the Collapse back, spawn boss (sector 3), trigger an event, refill ship. FPS counter included.

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
* **Enemies (7 + boss):** Pirate Fighter, Swarm Drone, Pirate Gunship (sector 2+), Crawler,
  Spitter, Skyskate (flying critter), Nest Guardian, **The Warden** (3-phase boss with weak points).
* **Modules (23):** Engine Mk I–III (+Ark drive), Laser Mk I–III, Missile Pod Mk I–II,
  Flak Mk I–II, Shield Mk I–III, Hull Mk I–III, Cargo Mk I–II, Scanner Mk I–II, Fuel Tank Mk I–II.
* **Gear (9):** Suit Mk II/III, Oxygen Tank Mk II/III, Jetpack Mk II/III, Drill Mk II/III, Pulse Rifle.
* **Resources:** Ore, Crystal, Scrap, Fuel, Energy Cells, Credits. 17 crafting recipes.
* **Sectors:** Pastel Drift → Verdigris Expanse → Ember Maw (+ boss), each with its own palette,
  hazards, enemy mix, Collapse speed and difficulty tier.

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
    World/        RunManager (run/sector flow, Collapse, gate, events, spawns, stats), SectorLayout,
                  SectorVisuals (client-local asteroids/palette), JumpGate, TraderStation
    Progression/  Meta (Stardust, perks, loadout, local save)
    UI/           code-built uGUI/TMP screens (menu, lobby, hangar, settings, HUD, inventory, trade…)
  Editor/         ContentBuilder (data, materials), PrefabBuilder (meshes, prefabs), SceneBuilder
  Shaders/        ToonFlat (flat/toon), Planet, Ocean, Atmosphere (scattering), Clouds, NebulaSkybox,
                  Collapse, Glass, Unlit, AdditiveParticle
```

Assembly definitions keep the graph acyclic (Core ← Data ← Combat/Inventory/Planet ← Ship ← Player
← World ← UI; Editor references everything).

**Networking model**
* Server-authoritative gameplay: damage, loot, inventory, crafting, trading, AI, spawning, oxygen,
  respawns, Collapse and jumps all run on the host. Clients send requests via RPCs.
* Movement: player pawns and piloted ships use **owner-authoritative NetworkTransforms** — the
  owner simulates locally (instant, prediction-like response) and everyone else interpolates.
  Taking the pilot seat transfers ship ownership to that client; leaving returns it to the server.
* Projectiles are not network objects: one RPC spawns identical cosmetic copies on all clients,
  the server resolves hits and reports impacts.
* Procedural content is **seeded**: only seeds and a few parameters are synced; planets
  (geometry + colliders), asteroid fields and trader stock are generated identically on every peer.
* **Floating origin:** every sector is generated around the world origin and all ships/players are
  re-based on each jump (`NetTeleport`, routed to whoever owns the transform), so coordinates stay
  within ~8 km (including the kilometre-scale planets) and precise in multiplayer.
* Seats do not re-parent network objects: pawns poll the seat registry and follow the seat
  locally; players walking inside the moving Ark are carried by a moving-platform step.

**Performance:** planets are six quadtrees of terrain chunks built on worker threads (finer near
the camera, horizon-culled, skirts hide LOD seams); colliders are streamed only around pawns and
ships; props are generated per chunk. Asteroid fields are batched into a few meshes,
VFX/projectile visuals/audio sources are pooled, and only active sector content exists at any time.

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
