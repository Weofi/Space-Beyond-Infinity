# Voidward – Agent Handoff

Co-op space roguelite vertical slice (1–4 players). Player docs: `README.md`. This file = dev context for AI agents.
User speaks German – answer in German.

## Stack
- Unity 6000.6.5f1, URP (`Assets/SourceFiles/Settings/PC_RPAsset`, Forward+), Input System only, TMP (ugui 2.x)
- Netcode for GameObjects 2.13.3 + Unity Transport; Facepunch.Steamworks (`Assets/Plugins/Steamworks`, define `VOIDWARD_STEAM`, AppID 480)
- Cinemachine 3 (`Unity.Cinemachine`), Multiplayer Play Mode 3.0
- Only macOS + WebGL build modules installed (no Windows build on this machine)

## Layout (`Assets/_Project`)
- `Scripts/<Module>` with one asmdef each. Dependency order: Core ← Data ← Combat/Inventory/Planet ← Ship ← Player ← AI/World ← Progression/UI; Networking separate.
- `Editor/ContentBuilder.cs` (materials, all ScriptableObject data, balance values), `PrefabBuilder.cs` (all procedural meshes + prefabs + network prefab list), `SceneBuilder.cs` (scene `Scenes/Voidward.unity`, lighting, volume, project settings), `BuildMenu.cs`.
- `Shaders/`: VW_ToonFlat (main lit), VW_NebulaSkybox, VW_Atmosphere, VW_Collapse, VW_Unlit, VW_AdditiveParticle, VW_Glass.
- **All content is generated.** Change the builder code, then run menu `Voidward/Build All Content`. Manual edits to generated prefabs/scene/data are overwritten.

## Architecture
- Session: `SessionManager` (host/join/transport switch, Steam → UTP fallback), `TransportConfig` (CLI: `-transport=utp|steam -autohost -autojoin=IP -autostart -players=N`).
- `DirectorSpawner` spawns the GameDirector prefab (`RunManager` + `CombatNet`) on `SessionManager.ServerStarted`.
- `RunManager` (big file): lobby/run/end phases, sector generation, player setup, respawns, Collapse, gate/jump, boss, events, enemy spawning, cheats (`ExecuteCheat(int)`).
- `GameState` (Core/Contracts.cs): static shared client state (UIOpen, Phase, SectorIndex, Objective…).
- Authority: pawns + ships use owner-authoritative NetworkTransform; pilot seat transfers ship ownership. Projectiles are server-simulated in `CombatNet` with cosmetic client copies. Planets/asteroids are seed-synced, not replicated.
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
- Art style (#3): stylized low-poly, white/blue hard surfaces, navy, gold accents, cyan glow. `MeshBuilder.Bevel` chamfers boxes; `Surface(gloss, glow)` writes UV0 (gloss = specular, glow = emission) read by VW_ToonFlat. Vertex colors are authored in sRGB (shader converts to linear). Palette constants live at the top of `PrefabBuilder`.
- Editor ↔ release build = "NetworkConfig mismatch". Use a Development build (`Voidward/Build/macOS (Development)`) or MPPM.
- Owning a ship in its spawn frame breaks positioning for remote clients → `RunManager.SeatLater` delays it by 0.6 s.

## Issue workflow (mandatory)
Repo: `Weofi/Space-Beyond-Infinity`, default branch `main`. When the user hands over an issue:
1. Read the issue (incl. images/mockups) via GitHub MCP.
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
- Run menu items (`Voidward/Build All Content`, `Voidward/Build/...`) via ManageMenuItem, not RunCommand.
- Quick play test via RunCommand: `SessionManager.Instance.HostGame()`, then `SessionManager.Instance.StartRun()`. Screenshot: `ScreenCapture.CaptureScreenshot("/tmp/x.png")`.
- MP test: Editor host + `Builds/macOS-Dev/Voidward.app/Contents/MacOS/Voidward -transport=utp -autojoin=127.0.0.1`; afterwards `pkill -f Voidward.app`.
- Target: zero console errors/warnings from Voidward code. Known foreign noise: AI Inference tensor warnings, PreviewRenderUtility leak.
- Unity skills (`.agents/skills`, from `Unity-Technologies/skills`, lock: `skills-lock.json`): use them as domain knowledge only. Implement their recommendations in the builder code (`ContentBuilder`/`PrefabBuilder`/`SceneBuilder`) or in runtime code, never as direct edits to generated scenes/prefabs/assets. Update: `npx skills update -p`.

## Status (2026-10-11)
Full loop playable and tested: lobby → 3 sectors → boss (3 phases) → results/Stardust → restart; 2-player MP (Editor ↔ dev build) tested. Steam is untested with 2 real accounts. Release + dev macOS builds exist in `Builds/` (git-ignored).

Known limitations: placeholder procedural art/audio, planet LOD only 2 levels (whole planet), no terrain deformation, owner authority without prediction, Ark can only be claimed (not built).

## Next priorities (user, 2026-10-11)
User feedback: **menu, graphics and gameplay need major improvement.** Clarify concrete goals/references with the user before large changes.
- Menu/UI: currently plain code-built uGUI panels (UIKit). Candidates: proper layout/styling, transitions, sounds, more readable HUD.
- Graphics: flat toon shader + procedural meshes. Candidates: better ship/planet models, materials/textures, lighting/post, VFX, planet LOD/chunking.
- Gameplay: game feel (flight, weapons, hit feedback), balance (`ContentBuilder` values), enemy variety/AI, pacing (spawns in `RunManager`), onboarding/tutorial.
