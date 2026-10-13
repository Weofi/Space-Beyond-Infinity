# Voidward – Agent Handoff

Co-op space roguelite vertical slice (1–4 players). Player docs: `README.md`. This file = dev context for AI agents.
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
- `RunManager` (big file): lobby/run/end phases, sector generation, player setup, respawns, Collapse, gate/jump, boss, events, enemy spawning, cheats (`ExecuteCheat(int)`).
- `GameState` (Core/Contracts.cs): static shared client state (UIOpen, Phase, SectorIndex, Objective…).
- Authority: pawns + ships use owner-authoritative NetworkTransform; pilot seat transfers ship ownership. Projectiles are server-simulated in `CombatNet` with cosmetic client copies. Planets/asteroids are seed-synced, not replicated.
- Planets (#17, #21): radius 1.0-1.9 km (`SectorLayout.Min/MaxPlanetRadius`, 2-3 per sector on alternating corridor sides). `BiomeDef.Shape` -> `PlanetShapeDef` (noise layers, landforms = volcano/mesa/spire/basin/dome, canyon network incl. river/lava channels, craters, ocean, region colors, sites, atmosphere, clouds, decor style, particles, weather). `PlanetTerrain` = deterministic, thread-safe height field (single source of truth for chunks, colliders, decor, sites, `PlanetBody` queries, `Raycast`). `PlanetVisuals` = 6 quadtrees of chunks (`PlanetMesh.BuildChunk` on worker threads, tangent-adjusted cube mapping, skirts, horizon culling), collider tiles streamed around `TerrainAnchor`s (pawn + ship prefabs), per-chunk props (`PlanetDecor`), set pieces (`PlanetLandmarks`), ocean/atmosphere/cloud shells. `PlanetAmbience` (client) = particles, weather, haze/ambient override, wind audio. `PlanetBody.SiteKinds` (deterministic) drive POI clusters in `RunManager.PopulatePlanet` and HUD site markers. `GravitySource.GroundRadiusAt` makes altitude checks terrain-aware. Far terrain has no colliders: use `PlanetBody.Raycast/SurfaceWorldPoint` or `GravitySource.GroundPoint`, not Physics, for spawn/placement. Dev preview: menu `Voidward/Open Planet Preview`, then Play.
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
5. When done: compile check + play test, update the PR description (changes, test notes, screenshots), mark it ready for review.
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
- **Act**: `DevTest.Fire(kind, from, to)` (server, dmg 0), `Hitscan(from, to)`, `PawnShoot(target, rifle)` + `GroundAhead(dist, side)`, `SpawnEnemy(kind, pos, lookAt)`, `Teleport`, `God`, `ClearDecals`, `DecalCount`; lab: `LabTargets[i]`, `LabGun(i)`, `LabCam`, `LabClose(i)`.
- **Look**: `DevTest.Sequence("name", seconds, (frame, t) => {...}, new[] { 0.1f, 0.5f, 2f }, DevTest.LabCam)` runs the step every frame and writes ONE contact sheet (640x360 tiles; pose = free camera, omit it for the player's screen incl. HUD) to `%TEMP%/VoidwardDev/name.jpg`; wait with `dev-wait.sh name`. Read sheets instead of many full screenshots. `DevTest.Screen(name)` = single downscaled screen grab.
- **Log protocol**: every call logs `[Dev] begin <tag>` synchronously and `[Dev] end <tag> ...` (or `FAILED`) when done; `dev-wait.sh <tag> [timeout]` prints the end line (`VW_LOG=<file>` for a client log).
- **Dev build without log flood**: RunCommand `Voidward.EditorTools.DevBuild.WindowsDevQuiet()` (Mac: `MacDevQuiet()`), returns one line; the menu build via RunCommand returns ~150k chars of foreign shader warnings. `EditorApplication.delayCall` does not fire while the editor is in the background, don't rely on it.
- **MP**: `DevTest.Play(DevScenario.Lab, 2)` (host waits for 2 players), then `bash Tools/Dev/mp-client.sh lab 3` (dev build with `-autojoin -skipintro -devscenario=lab -devshots=3`; it renders the lab camera every 3 s and logs `[Dev] client1_<t> decals N` into `%TEMP%/VoidwardDev/client.log`). Compare host/client via log lines, look at one client image at most. Stop: `Stop-Process -Name Voidward` / `pkill -f Voidward.app`.
- Write a test step as a small RunCommand using the harness; if a helper is missing, add it to `DevTest` instead of pasting long ad-hoc scripts.

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

## Status (2026-10-11)
Full loop playable and tested: lobby → 3 sectors → boss (3 phases) → results/Stardust → restart; 2-player MP (Editor ↔ dev build) tested. Steam is untested with 2 real accounts. Release + dev macOS builds exist in `Builds/` (git-ignored).

Known limitations: placeholder procedural art/audio, no terrain deformation, LOD without geomorphing, no swimming (pawns walk on the seabed), owner authority without prediction, Ark can only be claimed (not built).

## Next priorities (user, 2026-10-11)
User feedback: **menu, graphics and gameplay need major improvement.** Clarify concrete goals/references with the user before large changes.
- Menu/UI: currently plain code-built uGUI panels (UIKit). Candidates: proper layout/styling, transitions, sounds, more readable HUD.
- Graphics: flat toon shader + procedural meshes. Candidates: better ship/planet models, materials/textures, lighting/post, VFX, planet LOD/chunking.
- Gameplay: game feel (flight, weapons, hit feedback), balance (`ContentBuilder` values), enemy variety/AI, pacing (spawns in `RunManager`), onboarding/tutorial.
