# Voidward – Agent Handoff

Co-op space roguelite vertical slice (1–4 players). Player docs: `README.md`. This file = dev context for AI agents.

## Gameplan (2026-10-13, issue #72)
The game is now a **journey home**: a moon mission (*SELENE-4*) falls into a wormhole, the crew wakes in M33 and flies through 5 Local Group galaxies (M33 -> M31 -> NGC 6822 -> Leo I -> LMC) home to Sol, upgrading the **warp drive** (Mk I-V, Home Drive) to shrink the **Home ETA**. Travel = flown warp flights (#73). Death = time loop back to M33 with meta kept (#74). Enemies: nature + aliens only (no pirates); Wardens = alien guardian species (#42). Collapse -> the Rift (finale only). Credits -> Salvage, Stardust -> Memory fragments.
- **Gameplay decisions follow #72 and its child issues, not the legacy code.** The code still runs the old loop (3 sectors, Collapse, Jump Gates, Warden boss) until #46 (run structure & galaxies), #73, #74, #49 land.
- Design + glossary (DE design term / EN UI term / code name) + names: `Assets/Design/DESIGN.md` "Gameplan: The Journey Home". Old code names stay until the issue that migrates a system renames it; new code uses the glossary names (`GalaxyDef`, `HomeEta`, `AlertLevel`, `Salvage`, `ExoticMatter`, `Rift`, `Memories` ...). UI language stays English.
User speaks German – answer in German.

## Stack
- Unity 6000.6.5f1, URP (`Assets/SourceFiles/Settings/PC_RPAsset`, Forward+), Input System only, TMP (ugui 2.x)
- Netcode for GameObjects 2.13.3 + Unity Transport; Facepunch.Steamworks (`Assets/Plugins/Steamworks`, define `VOIDWARD_STEAM`, AppID 480)
- Cinemachine 3 (`Unity.Cinemachine`), Multiplayer Play Mode 3.0
- Blender 5.2 LTS (optional DCC tool, headless via CLI, no MCP). See "Blender (asset scripts)".
- Developed on two machines: Mac (macOS + WebGL build modules) and Windows PC (Windows build module). See "Cross-platform (Mac + Windows)".

## Layout (`Assets/_Project`)
- `Scripts/<Module>` with one asmdef each. Dependency order: Core ← Data ← Combat/Inventory/Planet ← Ship ← Player ← AI/World ← Progression/UI; Networking separate.
- `Editor/ContentBuilder.cs` (materials, all ScriptableObject data, balance values), `PrefabBuilder.cs` (all procedural meshes + prefabs + network prefab list), `SceneBuilder.cs` (scene `Scenes/Voidward.unity`, lighting, volume, project settings), `PlanetBuilder.cs` (planet presets `Data/Planets`, planet/ocean materials, detail textures, scene `Scenes/PlanetPreview.unity`), `BuildMenu.cs`.
- `Shaders/`: VW_ToonFlat (main lit), VW_Planet (triplanar terrain, elevation/slope/region ramp, planet-centered via `_PlanetCenter`), VW_Ocean (depth/opaque-texture water), VW_NebulaSkybox, VW_Atmosphere (depth-based ray-marched scattering shell, Cull Front + ZTest Always), VW_Clouds (procedural cloud shell), VW_Collapse, VW_Unlit, VW_AdditiveParticle, VW_Glass.
- **All content is generated.** Change the builder code, then run menu `Voidward/Build All Content`. Manual edits to generated prefabs/scene/data are overwritten.

## Architecture
- Session: `SessionManager` (host/join/transport switch, Steam → UTP fallback), `TransportConfig` (CLI: `-transport=utp|steam -autohost -autojoin=IP -autostart -players=N`).
- `DirectorSpawner` spawns the GameDirector prefab (`RunManager` + `CombatNet`) on `SessionManager.ServerStarted`.
- `RunManager` (big file): lobby/run/end phases, sector generation, player setup, respawns, Collapse, gate/jump, boss, events, enemy spawning, cheats (`ExecuteCheat(int)`). Legacy loop; #46 splits it into `GalaxyGenerator` (deterministic data), `SystemDirector` (live system: spawns, alert level, events) and `RunState` + `SaveSystem` (suspend save), and removes gates/Collapse.
- `GameState` (Core/Contracts.cs): static shared client state (UIOpen, Phase, SectorIndex, Objective…).
- Authority: pawns + ships use owner-authoritative NetworkTransform; pilot seat transfers ship ownership. Projectiles are server-simulated in `CombatNet` with cosmetic client copies. Planets/asteroids are seed-synced, not replicated.
- Planets (#17, #21): radius 1.0-1.9 km (`SectorLayout.Min/MaxPlanetRadius`, 2-3 per sector on alternating corridor sides). `BiomeDef.Shape` -> `PlanetShapeDef` (noise layers, landforms = volcano/mesa/spire/basin/dome, canyon network incl. river/lava channels, craters, ocean, region colors, sites, atmosphere, clouds, decor style, particles, weather). `PlanetTerrain` = deterministic, thread-safe height field (single source of truth for chunks, colliders, decor, sites, `PlanetBody` queries, `Raycast`). `PlanetVisuals` = 6 quadtrees of chunks (`PlanetMesh.BuildChunk` on worker threads, tangent-adjusted cube mapping, skirts, horizon culling), collider tiles streamed around `TerrainAnchor`s (pawn + ship prefabs), per-chunk props (`PlanetDecor`), set pieces (`PlanetLandmarks`), ocean/atmosphere/cloud shells. `PlanetAmbience` (client) = particles, weather, haze/ambient override, wind audio. `PlanetBody.SiteKinds` (deterministic) drive POI clusters in `RunManager.PopulatePlanet` and HUD site markers. `GravitySource.GroundRadiusAt` makes altitude checks terrain-aware. Far terrain has no colliders: use `PlanetBody.Raycast/SurfaceWorldPoint` or `GravitySource.GroundPoint`, not Physics, for spawn/placement. Dev preview: menu `Voidward/Open Planet Preview`, then Play. Planet look (#70): VW_Planet = soft wrap lighting (no toon bands), procedural macro noise (never fades), cavity AO from chunk UV1.x (mesh Laplacian), cliff strata; VW_Ocean = depth absorption, reflects the planet's own sky (`_SkyColor`/`_HorizonColor`), distance-dependent GGX glint, shore foam, `PlanetShapeDef.Lava` switches to the crust/crack lava mode; VW_Clouds = soft edges + sun-offset self shadow.
- Seats don't reparent: the pawn follows `ShipSeat.SeatOf` in LateUpdate; seated capsules are disabled on all peers.
- Floating origin: each sector is generated around the origin; on jump, ships and pawns are rebased via `NetTeleport` (sent to the transform owner).
- UI is fully code-built (`UIKit`, `UIRoot`, `MenuScreens`, `HudView`, `InventoryView`, `TradeView`). Namespace `Voidward.UI` has enum `Screen` → write `UnityEngine.Screen` explicitly.
- Meta progression: `Progression/Meta.cs` (Stardust, perks, local save; `Meta.ResetAll()`).

## Conventions / gotchas (learned the hard way)
- One MonoBehaviour/NetworkBehaviour per file, file name = class name (otherwise "missing script" on prefabs).
- Server RPCs: `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]` + `RpcParams rp` → `rp.Receive.SenderClientId`.
- Never write NetworkVariables before spawn: store them in pending fields, then apply them in `OnNetworkSpawn`.
- Don't use `??` / `?.` on UnityEngine.Object.
- TMP: ASCII glyphs only (no `…`, arrows, emoji) → otherwise missing-glyph warnings.
- PrefabBuilder deletes prefabs before re-saving them (keeps `GlobalObjectIdHash` valid). Network prefab list is rebuilt from scratch.
- Skybox shader must handle reversed-Z (otherwise it draws over everything).
- Art style (#3, refined: see `Assets/Design/DESIGN.md` "Visual Identity"): **polished, high-end stylized sci-fi**, not abstract. "Low-poly" means clean chunky shape language, NOT low detail/finish: assets need secondary detail (bevels, panels, greebles, decals), material definition and good lighting; a screenshot must look like a finished game. Palette: white/blue hard surfaces, navy, gold accents, cyan glow. Current procedural assets are still too abstract (#26). `MeshBuilder.Bevel` chamfers boxes; `Surface(gloss, glow, metal)` writes UV0 (gloss, glow, metal, edge) and `Panel` writes UV1.x, all read by VW_ToonFlat. Vertex colors are authored in sRGB (shader converts to linear). Palette constants live at the top of `PrefabBuilder`.
- Hero hard-surface pipeline (#26, reference: Dart + astronaut): build with `Hero()` (weighted normals via `SmoothAngle`, panel detail) and the parametric parts `AddLoft` (fuselages, limbs), `AddLathe` (nozzles, rings, tanks), `AddSlab` (wings, plates), `AddTube`/`Polyline`, liveries via quad-aware `Paint`. Render with `MatHull` (VW_ToonFlat `_SURFACEDETAIL_ON`: triplanar panel seams/rivets/grime from `SurfaceDetailBuilder`, edge wear on chamfers, GGX + probe reflections). Primitives choose winding from geometry, so mirrored matrices (`Scale(-1,1,1)`) are fine. Iterate with menu `Voidward/Dev/Hero Preview Shots` (renders to `Temp/HeroShots`, no full rebuild needed); after Build All Content, `ContentBuilder.PreviewPrefabs("<name filter>")` / `PreviewShot(...)` (callable from RunCommand) render built prefabs. Ship exhaust and projectiles = `VW_Plume` (additive fresnel). Soot/burns: `MeshBuilder.Weather`; contact shading: `Grade`. Planet decor, landmarks, asteroids and the menu ground use `VW_Terrain` with a rock detail mask (enable via `MeshBuilder.Panel = 1`). Per-sector color grading lives in `PaletteDef` (runtime volume in `SectorVisuals`).
- Editor ↔ release build = "NetworkConfig mismatch". Use a Development build (`Voidward/Build/macOS (Development)` or `Windows (x64, Development)`) or MPPM.
- Owning a ship in its spawn frame breaks positioning for remote clients → `RunManager.SeatLater` delays it by 0.6 s.

## Issue workflow (mandatory)
Repo: `Weofi/Space-Beyond-Infinity`, default branch `main`. When the user hands over an issue:
1. Read the issue (incl. images/mockups) via `gh issue view <nr> --comments`.
2. `git checkout main && git pull`, then create a dedicated branch `issue-<nr>-<short-slug>` (never work on `main`).
3. Push the branch early and open a **draft PR** against `main` with `Closes #<nr>` in the body (check for a PR template first).
4. Implement on that branch with small, descriptive commits (`#<nr>: ...`), push regularly.
5. When done: compile check + test per the "Testing playbook" (cheapest level first, not a full run), update the PR description (changes, test notes, screenshots), mark it ready for review.
6. If everything works and the issue is fully done: merge the PR into `main` yourself (user-approved), then `git checkout main && git pull`. If anything is broken or open, don't merge; report back instead.
One issue = one branch = one PR. Don't mix issues.

## Agent workflow (Unity MCP)
- Don't edit C# while Unity is in Play Mode (a domain reload breaks statics).
- Compile check: read `Logs/Editor.log` after the last "Requested script compilation" and grep `): error CS`.
- `Unity_RunCommand` returns "Unity not detected" while compiling/reloading → just retry. System.Reflection is not allowed there.
- Run menu items (`Voidward/Build All Content`, `Voidward/Build/...`) via ManageMenuItem, not RunCommand. Exception: dev builds, see the testing playbook.
- Never call `AssetDatabase.Refresh`/`ImportAsset` in Play Mode: it can trigger a script recompile + domain reload that breaks the session (UIRoot NREs). Shader tweaks: exit Play Mode first, or check them in Edit Mode (render a quad with the material).
- Target: zero console errors/warnings from Voidward code. Known foreign noise: AI Inference tensor warnings, PreviewRenderUtility leak.
- Unity skills (`.agents/skills`, from `Unity-Technologies/skills`, lock: `skills-lock.json`): use them as domain knowledge only. Implement their recommendations in the builder code (`ContentBuilder`/`PrefabBuilder`/`SceneBuilder`) or in runtime code, never as direct edits to generated scenes/prefabs/assets. Update: `npx skills update -p`.

## Testing playbook (dev harness, #65)
Goal: test the situation, not a whole run, and keep tool output small. Harness = `Scripts/Dev` (`Voidward.Dev`, editor + development builds only).
- **Pick the cheapest level**: shader/particle look → Edit Mode (render a quad / `DevShots.Shot(name, pose)` works outside Play Mode) or the Lab; gameplay in context → `QuickStart` + a few shots; MP only when RPCs/NetworkVariables/authority changed.
- **Start** (one RunCommand line from Edit Mode, survives the domain reload): `Voidward.Dev.DevTest.Play(Voidward.Dev.DevScenario.Lab)`; then `bash Tools/Dev/dev-wait.sh quickstart` (no sleeps). Scenarios: `Space` (pilot seat), `Planet` (on foot, sunlit dry ground, blaster, god mode), `Lab` (5 test walls Metal/Rock/Dirt/Organic/Shield next to the trader station, same on every peer). Already playing: `DevTest.QuickStart(...)`. Humans: menu `Voidward/Dev/Play: ...`.
- **Act**: `DevTest.Fire(kind, from, to)` (server, dmg 0), `Hitscan(from, to)`, `PawnShoot(target, rifle)` + `GroundAhead(dist, side)`, `SpawnEnemy(kind, pos, lookAt)`, `Teleport`, `God`, `ClearDecals`, `DecalCount`; damage taken: `HurtPawn(amount, fromDir)` / `HurtShip(amount, fromDir)` (server hit from a world direction, ignores god mode; ship = shield first), `SetVitals(health01, shield01)`; lab: `LabTargets[i]`, `LabGun(i)`, `LabCam`, `LabClose(i)`.
- **Loot / music / meta (#43)**: `DropLoot(id, from, count)` (server; pops out of `from` like an enemy drop), `DropRarityRow(from)` (one item per rarity, ids in `RarityItems`), `LootCount`, `LootMagnetized`, `ClearLoot()`; music: `SetThreat(level01, boss, seconds)` (client override of `Threat`, -1 clears), `MusicState` / `LogMusic(tag)` (threat + combat/boss stem mix); meta: `BackupMeta()` -> `AddStardust(n)` -> ... -> `RestoreMeta()` (the meta save is the real local save). Perk unlock UI: push `UIRoot.Instance.Push(Screen.Hangar)` from the main menu and invoke an `Unlock (..)` button's onClick inside a `Sequence`.
- **Planets (#70)**: `DevTest.PlanetSurvey(name, shapes, views, radius, seed)` (play mode, any phase) builds each `PlanetShapeDef` as a temporary planet far outside the sector, drives the main camera through the `PlanetView` flags (Orbit, Limb, Flight 300 m, Ground, Shore) so LOD/decor/ambience react like in game, and writes ONE sheet (row = shape, column = view, no HUD). Shapes: `DevTest.CatalogShapes()` or load `Data/Planets/Planet_<id>.asset` via AssetDatabase. Use it as before/after for any planet shader or preset change.
- **Look**: `DevTest.Sequence("name", seconds, (frame, t) => {...}, new[] { 0.1f, 0.5f, 2f }, DevTest.LabCam)` runs the step every frame and writes ONE contact sheet (640x360 tiles; pose = free camera, omit it for the player's screen incl. HUD) to `%TEMP%/VoidwardDev/name.jpg`; wait with `dev-wait.sh name`. Read sheets instead of many full screenshots. `DevTest.Screen(name)` = single downscaled screen grab.
- **Log protocol**: every call logs `[Dev] begin <tag>` synchronously and `[Dev] end <tag> ...` (or `FAILED`) when done; `dev-wait.sh <tag> [timeout]` prints the end line (`VW_LOG=<file>` for a client log).
- **Dev build without log flood**: RunCommand `Voidward.EditorTools.DevBuild.WindowsDevQuiet()` (Mac: `MacDevQuiet()`), returns one line; the menu build via RunCommand returns ~150k chars of foreign shader warnings. `EditorApplication.delayCall` does not fire while the editor is in the background, don't rely on it.
- **MP**: `DevTest.Play(DevScenario.Lab, 2)` (host waits for 2 players), then `bash Tools/Dev/mp-client.sh lab 3` (dev build with `-autojoin -skipintro -devscenario=lab -devshots=3`; it renders the lab camera every 3 s and logs `[Dev] client1_<t> decals N` into `%TEMP%/VoidwardDev/client.log`). Compare host/client via log lines, look at one client image at most. Stop: `Stop-Process -Name Voidward` / `pkill -f Voidward.app`.
- Write a test step as a small RunCommand using the harness; if a helper is missing, add it to `DevTest` instead of pasting long ad-hoc scripts.
- **Keep the harness growing**: when a feature brings its own typical test steps (e.g. spawn a boss phase, open a crate, dock at a station), add them as `DevTest` helpers or a new `DevScenario` in the same PR, so later issues reuse them. Document every new helper, scenario or flag here (one line is enough) - this section is how the next session learns about it.

## Cross-platform (Mac + Windows)
- Same Unity version on both machines (`ProjectSettings/ProjectVersion.txt`). Line endings are pinned to LF via `.gitattributes`.
- Before switching machines: commit + push. On the other machine: `git pull` first, then open Unity.
- `.mcp.json` is git-ignored (machine-specific Unity relay path); each machine keeps its own.
- GitHub: use the GitHub CLI (`gh`, logged in via `gh auth login --web`) for issues, PRs and merges. No GitHub MCP server (its OAuth needs dynamic client registration, which Claude Code can't do with GitHub). On Windows, `gh` lives at `C:\Program Files\GitHub CLI\gh.exe` (call it by full path if PATH isn't refreshed yet).
- Blender: Windows `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`; Mac `/Applications/Blender.app/Contents/MacOS/Blender` (install the same 5.2 version there).

## Blender (asset scripts)
- When to use: procedural C# (`MeshBuilder`/`PrefabBuilder`) stays the default and already reaches the target look for hard-surface assets. Use Blender only where C# gets painful: organic forms (boss, characters, alien stuff), blended surfaces, baked AO/normal maps for close-up detail.
- Keep it reproducible: Blender content is a Python script in the repo (e.g. `Tools/Blender/<asset>.py`), never a hand-edited `.blend` as the only source. Exported FBX goes to `Assets/_Project/Art/Models/` and is committed (Unity must build without Blender); `PrefabBuilder` references it.
- Run headless: `blender -b --factory-startup -P script.py -- <args>`. In 5.2, FBX export applies modifiers via `use_mesh_modifiers=True` (not `apply_modifiers`). Unity is Y-up / Blender Z-up: export with `axis_forward='-Z', axis_up='Y'` and check scale (1 unit = 1 m).
- Shared helpers: `Tools/Blender/vw_blender.py` (skin skeletons, warp, displacement, Cycles AO bake, export). Vertex data contract: colour attribute `Col` (sRGB) = base colour, UV map `surf` = (gloss, glow), UV map `detail` = (metal, baked AO); model faces Blender -Y (becomes Unity +Z; Blender +X becomes Unity -X). `PrefabBuilder.ImportedMesh` converts such an FBX to the MeshBuilder layout (C# meshes stay as fallback). Example: `Tools/Blender/aliens.py`.
- After a first open/reimport on a machine, check `git status` before committing: reimport noise (package `.meta` files, emptied `m_List` in `UniversalRenderPipelineGlobalSettings.asset`) must not be committed.

## Status (2026-10-13)
Legacy loop fully playable and tested: lobby -> 3 sectors -> boss (3 phases) -> results/Stardust -> restart; 2-player MP (Editor <-> dev build) tested. Steam is untested with 2 real accounts. Release + dev macOS builds exist in `Builds/` (git-ignored). Game feel pass #43 (hit feedback, damage taken, loot juice, dynamic music) done. Docs switched to the new gameplan (#72); the code migration starts with #46.

Known limitations: placeholder procedural art/audio, no terrain deformation, LOD without geomorphing, no swimming (pawns walk on the seabed), owner authority without prediction, Ark can only be claimed (not built).

## Next priorities (user, 2026-10-13)
Roadmap: #53. Order of the foundation:
1. **#46** run structure: 5 galaxies x 4-6 systems from the seed, galaxy map, remove Jump Gates/Energy Cells/Collapse chase, run timer, suspend save.
2. **#73** warp drive tiers + flown warp flight (warp throttle, obstacles, Home ETA in real flight minutes, exotic matter as fuel, the Rift in the finale).
3. **#74** prologue (moon mission -> wormhole), time loop on death, homecoming ending; **#49** home ship (Nostos, from the Ark).
4. Then the build-up loop (#50, #55, #44, #54), enemies (#41, #51 pirates -> aliens, #42 Warden enclaves), systems/expeditions (#45 alert level, #57, #52, #56), living world (#38-#40), meta (#47 Logbook, #59, #58, #48).
Older feedback still applies: menu, graphics and gameplay need major improvement; clarify concrete goals/references with the user before large changes.
