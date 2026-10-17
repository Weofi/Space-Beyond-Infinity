# Game Design: VOIDWARD

VOIDWARD is a 1-4 player co-op space roguelite vertical slice. A routine moon mission is already on its way back to Earth - the families watch the return live on the news - when an anomaly (a wormhole) swallows the ship; the crew wakes up in the Triangulum Galaxy (M33) and has to find its way home through the Local Group: explore and loot alien planets, upgrade the ship and its warp drive, and fly galaxy by galaxy back to the Milky Way. Death sends the crew back to the moment they entered the current system in a time loop, memories intact. This document defines the gameplan and the design specifications across UI Design, Asset Design, and Game Feedback.

**Source of truth for gameplay decisions:** GitHub issue #72 (gameplan), with #46 (run structure & galaxies), #73 (warp drive & warp flight), #74 (prologue, loop, homecoming; story intro #109), #49 (home ship), #108 (seamless warps through jump zones), #128-#132 (missions, expeditions, own ships in warp, drive variants, system loop). Where a spec below still describes the old loop (Collapse chase through 3 sectors, Jump Gates, Warden boss, pirates), it is marked *(legacy)* and will be replaced by the issue that migrates it.

Key dependencies between domains:
- UI accent colors and HUD styling derive directly from galaxy color palettes defined in Asset Design.
- Diegetic cockpit dashboards and holographic station consoles bridge physical Asset Design and tactile UI interaction.
- The Home ETA is the central progress readout: every fitted part lowers it visibly (HUD, galaxy map, home ship dashboard).
- System pressure comes from the alien alert level (HUD meter, music threat, spawns); the Rift (former Collapse) only drives the finale.
- Every element is tagged as either `(core)` for the lean playable vertical slice or `(optional)` for secondary expansion.

---

## Gameplan: The Journey Home (#72)

### Pillars
1. **The way home is always visible:** the Home ETA shows how long the flight home takes with the current drive. Every new part lowers it measurably.
2. **Explore for tech:** planets are expeditions. What the crew needs is down there, and it is guarded.
3. **Travel is gameplay:** no portals, no travel cutscenes, no warp button: every warp starts seamlessly when the crew flies out of a jump zone (#108). Reaching the next galaxy is a physical warp flight with long empty stretches and obstacles (asteroids, debris, anomalies). The pilot controls the pace (warp throttle); a better drive is really faster, but high speed has to be mastered. Landing and combat handle the same at every tier (#73).
4. **Roguelite:** a crew wipe loops the current system back to the moment of arrival; scans, coordinates and unlocks stay (see "Endings, death and the loop").
5. **Co-op crew:** everyone flies their own ship by default; multi-seat ships split roles (pilot, gunner, engineer). Land, carry and fight together; revive each other.
6. **Every galaxy is a mission:** clear goals (fuel for the jump, the galaxy's drive core, optional signals) instead of aimless wandering; each goal is an expedition with a structure.

### Route (fixed order, content per loop from the seed)
| # | Galaxy | Distance to Sol | Character | Best drive found there |
|---|---|---|---|---|
| 1 | Triangulum (M33) | 2.73 M ly | wormhole exit, temperate, calm start | Mk II |
| 2 | Andromeda (M31) | 2.54 M ly | huge, dense, many biomes, swarm aliens | Mk III |
| 3 | NGC 6822 (Barnard's Galaxy) | 1.6 M ly | dwarf galaxy, nebulae and star birth, crystal beings | Mk IV |
| 4 | Leo I | 0.82 M ly | ancient, star-poor, dark, void creatures | Mk V |
| 5 | Large Magellanic Cloud | 0.16 M ly | Milky Way fills the sky, home of the Wardens, hardest enemies | Home Drive |
| Goal | Milky Way -> Sol | 0 | finale: flight home (~5 min) and landing | - |

Target run length ~2.5-3 h (per galaxy ~20-25 min exploring + ~8-12 min warp flight). Suspend save on entering each galaxy (one run slot); a system checkpoint on every system entry is the loop's restart point (see "Endings, death and the loop").

### Home ETA & drive tiers (real flight minutes, balancing in #73)
| Drive | Found in | -> next galaxy | skip one galaxy | straight home |
|---|---|---|---|---|
| Mk I (mission ship) | start | ~8-12 min | ~45 min | ~40 h |
| Mk II | M33 | ~8-12 min | ~45 min | ~10 h |
| Mk III | M31 | ~8-12 min | ~45 min | ~2.5 h |
| Mk IV | NGC 6822 | ~8-12 min | ~45 min | ~50 min |
| Mk V | Leo I | ~8-12 min | - | ~20 min |
| Home Drive | LMC | - | - | ~5 min |

Balance as implemented in #78 (`WarpDriveDef` + `GalaxyDef.LegLength/LegFuel`, balanced build): leg time = leg length / core speed (Mk I 1, II 3, III 10, IV 25, V 60, Home 240); next galaxy 10 min with the core found there, skipping ~35-43 min, home: Mk I ~36 h, Mk II ~12 h, Mk III ~3.6 h, Mk IV ~80 min, Mk V ~30 min, Home Drive 5 min. Exotic Matter per leg 8/10/12/14/16, +25 % per skipped galaxy; tanks hold 12/20/30/42/56/75. Times assume a fitting build and good flying; poor navigation/cooling forces braking (intended grind). Skipping a galaxy costs much more flight time and exotic matter. Any drive can theoretically fly home (Mk I ~40 h, achievement "The Long Way").

### Core loop per galaxy
1. **Arrival** by warp flight at the galaxy's edge -> galaxy map with 4-6 systems and unknown **signals** ("?").
2. **Approach a system:** fly out of the jump zone at the system's edge towards a neighbouring system -> short in-galaxy hop (#108); **orbital scan** turns signals into known sites (type, threat, reward).
3. **Expedition:** land at a site and play its objective (see "Galaxy missions"). The **alert level** rises the longer and louder the crew stays on a planet and resets on lift-off.
4. **Back on board:** fit parts, craft, choose upgrades -> the ETA drops.
5. **Decision:** keep searching (better core, more fuel, assault the Warden enclave) or warp to the next galaxy by flying out through the **Galactic Rim** of the galaxy's outermost system (or skip one through the skip zone next to it, with lots of fuel). Galaxy changes are one-way; systems inside a galaxy can be revisited.

### Galaxy missions (#128) and expeditions (#129)
Every galaxy gives the crew three kinds of goals, shown in the galaxy map and the HUD objective:

| Goal | Kind | Reward |
|---|---|---|
| **Fuel for the jump** | required | Exotic Matter for the Galactic Rim (`GalaxyDef.LegFuel`) |
| **The galaxy's drive core** | main goal | the next core tier (Mk II, III ...), big ETA drop |
| **Side signals** (wrecks, ruins, nomads, events) | optional | drive parts with variants, weapons, blueprints, Salvage |

- **Drive core hunt:** the core's location is unknown on arrival. 2-3 **coordinate fragments** from different expeditions triangulate it (galaxy map: the search area shrinks per fragment); the last step leads into the Warden enclave or a guarded site (#42).
- **Signals and scans:** every system holds 1-4 signals. A scan from orbit (or flying close) reveals type, threat and reward; the crew picks where to land instead of wandering.
- **Expedition types** (each with start, objective, extraction):
  - **Drill:** place a drill on an Exotic Matter vein, defend it 2-3 min against waves, collect the canister.
  - **Salvage:** cut a wreck open, carry the heavy core/part back to a ship (carrier is slowed and cannot shoot, the others cover; #50).
  - **Ruin:** activate pylons/solve a short sequence, a guardian wakes up; reward = coordinate fragment or blueprint.
  - **Nest / event / nomads:** shorter optional sites (#52, #39).
- **Alert level (per planet):** rises with time, noise (shots, drilling, explosions) and failed objectives -> more and tougher spawns, finally an elite. Lift-off resets it (#45).

### Ships and crew roles (#130)
- **Default: every player flies their own ship** (Dart, one seat). Landing, combat and warp work solo or as a wing.
- **Multi-seat ships** (Mule, Ark, later Nostos #49): one pilot, the others take turrets (gunner) or the engineer station (heat, shields, repairs).
- **Warp flight:** the lead pilot sets the pace (throttle, cruise, drop-outs); every other pilot **flies their own ship inside the bubble** (dodging obstacles, shooting rocks and interceptors) instead of sitting in a fixed formation slot. Gunners in multi-seat ships shoot from their turrets. Leaving the bubble's corridor pulls the ship back (`WarpBubble.Pull`).

### Drive parts with trade-offs (#131)
Each slot (Core, Navigation, Cooling, Tank) keeps its **tier** (Mk I-V, Home) and gets an optional **variant** with an upside and a downside, plus a rarity. Examples:

| Slot | Variant | Upside | Downside |
|---|---|---|---|
| Core | Overdrive | +25 % speed | much more heat |
| Core | Ghost | interceptor patrols ignore the bubble | -15 % speed |
| Core | Siphon | refuels Exotic Matter in gas clouds | weaker base speed |
| Nav | Long Scan | sees obstacles much earlier | narrower dodge cone |
| Cooling | Cryo | heat drops fast | burns Exotic Matter |
| Tank | Volatile | huge tank | explodes on hull hits during warp |

Matching variants give small **set bonuses**. Variants come from side signals, Warden hoards and nomads; the build is a real choice, not only "higher Mk is better" (#44).

### Jump zones (#108)
Every warp starts without a button, countdown or cutscene: the lead pilot flies out of a jump zone and the flight turns into the warp bubble seamlessly. Each system has a jump zone at its edge per neighbouring system (pointing towards it, no Exotic Matter). Only the outermost system has the **Galactic Rim** (towards the next galaxy) and the skip zone (further out, towards the galaxy after next); the LMC's rim is the flight home. In cruise the border triggers at once; in normal flight only after a few seconds of lining up. A zone only triggers with every living player aboard; missing fuel, gravity/combat lock or interdiction hold the ship softly at the border with a clear HUD message. Arrivals are seamless too (cruise in at the target system's edge).

System types: planetary system, asteroid/wreck field, anomaly, Warden enclave (guardian boss + best warp core, #42), nomad meeting point (trade, optional), empty space with an event.

### Endings, death and the loop
- **Homecoming:** flight home done, Sol reached, Earth approached and landed at the mission's landing site, all flown by the crew; closing shot: the same family and news show as the intro, "The crew of SELENE-4 is back" (#88). Score = run time.
- **Death - the system loop (#132):**
  - **Down, not dead:** a player at 0 health goes down with a bleed-out window (~45 s); a crewmate revives them by holding [F] (~4 s).
  - **Solo:** one **emergency cryo** charge per system: the first down wakes the player in a cryo pod on their ship; the second down is a wipe.
  - **Crew wipe** (everyone down at once, or caught by the Rift) -> the collapse plays and the **current system loops back to the moment the crew arrived** (checkpoint on every system entry: drive, inventory, cargo, Exotic Matter, ships). Everything gained in this system is lost; the crew starts at the arrival jump zone.
  - **Same layout, new dice:** after a loop the system keeps its seed (planets, ruins, wrecks, signal sites in the same places), but enemies and loot are rerolled; the alert level starts at 0.
  - **What survives a loop:** scanned signals, coordinate fragments, logbook entries and unlocked blueprints, so the second attempt is faster (knowledge counts, like Outer Wilds).
  - **Cost:** every loop costs something small (e.g. a bit of Exotic Matter or a cryo charge) so dying never becomes free. The loop counter keeps driving deja-vu radio and lore.
  - The Rift in the finale works the same: caught = the flight home starts again from the LMC's outermost system.
- **Story intro (#109, #87, first loop only, skippable):** a family at home watches the evening news: "Today the crew of the moon mission SELENE-4 returns to Earth." The camera moves into the live picture on the TV and lands in the cockpit on the return flight (Earth in the window, radio with Mission Control, short playable flight tutorial). An anomaly opens and pulls the ship in; short cut back: the TV shows "SIGNAL LOST". Waking in M33: "Distance to Earth: 2.73 million light-years", the Home ETA appears for the first time.
- The wormhole is a natural phenomenon, not a villain. A loop keeps the system's layout (same seed) and rerolls enemies and loot; a new journey (new game) reshuffles the galaxies. Lore: crew logbook, radio echoes from earlier loops, alien ruins, codex entries.
- **The Rift** (former Collapse): the wormhole pulls at the crew. No permanent chaser per system; it is the climax of the finale (flight home = escape from the Rift) and a rare hazard.

### Enemies: nature and aliens only
No pirates, no humans. Fauna (#39) is neutral to predatory and guards nests and resources. Each galaxy has its own alien ecosystem (#51: swarm, crystal beings, void creatures ...). The **Wardens** are an ancient biomechanical species hoarding warp cores and tech; one enclave per galaxy guards the best drive core (#42). Optional trade with neutral alien nomads for salvage.

### Meta: Logbook of the Loop (#47)
Codex, memories (unlocks for the pools), start options, flat perks, skill tree (#59), after the first homecoming **wormhole tiers** (difficulty modifiers like the Heat system in Hades) and leaderboard (#48).

### Glossary (UI and code)
German terms are the design language (issues, discussion), English terms are used in the UI and in code. Existing code keeps its old names until the issue that migrates the system renames it; new code uses the new names.

| Design (DE) | UI (EN) | Code | Replaces |
|---|---|---|---|
| Heimflug-ETA | Home ETA | `HomeEta` | Collapse distance meter |
| Warp-Flug | Warp flight | `WarpFlight` | Jump Gate jump |
| Warp-Antrieb (Mk I-V, Heimantrieb) | Warp drive (Mk I-V, Home Drive) | `WarpDriveDef`, `DriveTier` | Engine modules for travel |
| Warp-Kern | Warp core | `WarpCore` | - |
| Galaxie | Galaxy | `GalaxyDef`, `GalaxyIndex` | Sector (`SectorDef`) |
| System | System | `StarSystemDef`, `SystemIndex` | Sector layout |
| Galaxie-Karte | Galaxy map | `GalaxyMapView` | Sector route preview |
| Expedition | Expedition | `Expedition` | - |
| Signal | Signal | `Signal` | - |
| Koordinaten-Fragment | Coordinate fragment | `CoreFragment` | - |
| Antriebs-Variante | Drive variant | `DriveVariant` | - |
| Notfall-Kryo | Emergency cryo | `EmergencyCryo` | - |
| System-Checkpoint | System checkpoint | `SystemCheckpoint` | Galaxy-entry suspend save |
| Alarmstufe | Alert level | `AlertLevel` | Collapse pressure |
| Bergungsgut | Salvage | `Salvage` | Credits |
| Exotische Materie | Exotic matter | `ExoticMatter` | Energy Cells (as travel resource) |
| Waechter | Wardens | `Warden` | The Warden (single boss) |
| Riss | The Rift | `Rift` | The Collapse |
| Schleife | Loop | `Loop`, `LoopIndex` | Run (meta view) |
| Erinnerungsfragmente | Memory fragments | `Memories` | Stardust |
| Logbuch der Schleife | Logbook | `Logbook` | Hangar |
| Heimatschiff | Home ship | `HomeShip` (`ShipClass.HomeShip`) | Ark |
| Heimkehr | Homecoming | `Homecoming` | Demo Complete / victory |
| Kryo-Kapsel | Cryo pod | `CryoPod` | Team respawns |

Note for UI text: TMP is ASCII-only, so on-screen German would need "ae/oe/ue"; the UI language stays English.

### Names
- **Mission:** *SELENE-4*, a routine lunar supply and survey mission.
- **Home ship:** *Nostos* (Greek for "homecoming"), the mission's long-range transport; built from the Ark hull (#49).
- **Crew callsigns** (player slots 1-4, matching ping colors): *Vega* (P1, blue), *Lyra* (P2, green), *Altair* (P3, yellow), *Deneb* (P4, magenta) - stars of the Summer Triangle and its neighbourhood, visible from home. Steam/player names stay visible next to them.

---

## UI Design

### Screen Inventory & Flows

```
[Main Menu] ---> [Lobby] ---> [Logbook] ---> [Prologue] ---> [Galaxy 1..5 HUD / Galaxy Map] ---> [Loop Reset]
      |                                                               |                            |
  [Settings]                                                      [Pause]                     [Homecoming]
```

- **Main Menu (core):** Title banner, Host Game, Join Game (Direct IP / Steam Session Code), Settings, Quit. Clean centered stack with a live backdrop of a sector nebula and rotating low-poly asteroid.
- **Lobby (core):** Player roster card (1-4 slots with avatar color, callsign, ready state, selected starting role: Pilot, Gunner, Engineer), route preview (M33 -> Sol), loop counter, Launch button (unlocked when all players ready). Continue Run when a suspend save exists (host).
- **Logbook (core, formerly Hangar):** Inter-loop meta-progression hub (#47). Memory fragment balance, codex, unlocks, start options, perks.
- **Galaxy Map (core):** Systems of the current galaxy with type, threat and scan info, Home ETA, exotic matter and range; shows the jump zones and the Galactic Rim for navigation (warps start by flying out of a zone, #108).
- **HUD (core):** Persistent contextual gameplay overlay supporting both on-foot and ship flight contexts.
- **Pause Menu (core):** Modal overlay pausing single-player or providing an in-game options sheet in co-op. Resume, Settings, Abandon Run (with confirmation dialogue).
- **Settings (core):** Tabbed overlay (Audio, Video, Controls). Sliders for Master/Music/SFX; toggles for Fullscreen, V-Sync, Bloom; slider for FOV (70-100); mouse sensitivity slider with Invert Y toggle; full input rebind table.
- **Loop Reset (core, formerly Game Over):** Debrief when the crew dies. Cause banner ("Crew lost", "Nostos destroyed"), run statistics (galaxies reached, best drive, run time, enemies, loot), memory fragments gained, transition back to the wormhole exit.
- **Homecoming (core, formerly Demo Complete):** Shown after landing on Earth, after the closing news scene. Run time (score), summary stats, bonus memory fragments, wormhole tier unlock.

### HUD Layout Specifications (1080p Reference)

- **Vitals & Gauges (Bottom-Left: Anchor (48px, 48px)) (core):**
  - **Shield Bar:** Position (0, 48px relative to anchor), size 280 x 14px. Color: Sky Blue `#38BDF8`. Depletion triggers a 100ms white `#FFFFFF` flash and border flicker.
  - **Health Bar:** Position (0, 24px relative to anchor), size 280 x 20px. Color: Mint Green `#4EED8C`. Critical threshold (<25%): pulses at 2 Hz between `#4EED8C` and Crimson `#FF4A5A`.
  - **Oxygen Bar (On-foot):** Position (0, 0px relative to anchor), size 220 x 12px. Color: Cyan `#06B6D4`. Threshold (<20%): alternates between Cyan and Danger Red at 1.5 Hz with an O2 warning icon.
  - **Fuel Bar (Ship Flight):** Replaces Oxygen in cockpit mode. Size 220 x 12px. Color: Amber `#FBBF24`. Threshold (<15%): flashes amber at 2 Hz.
  - Surface plate backing for all bars: Translucent space navy `#0E1420CC` with 1px border `#33415580`.
- **Hotbar (Bottom-Center: Anchor (Center, 36px)) (core):**
  - 5 slots in a horizontal row, overall dimensions 312 x 56px (56 x 56px per slot, 8px gap).
  - Resting state: Dark slate `#0E1420E6`, 1px border `#334155`.
  - Active selection state: Border expands to 2px with bright Cyan glow `#38BDF8`, slot background lifts to `#1E293B`.
  - Slot numerals: 12pt bold tabular digit pinned to top-left corner (`#94A3B8`). Tool/item icon centered (32 x 32px). Quantity badge pinned to bottom-right (14pt bold `#F8FAFC`).
- **Home ETA & Alert Level (Top-Center) (core):** Replaces the Collapse meter. Home ETA readout (e.g. `HOME ETA 2H 31M`, tabular, ticks down with a gold flash whenever a fitted part lowers it), current galaxy + system name, and the system alert level as a 5-step meter (teal -> amber -> red). The Rift meter below is only shown in the finale.
- **The Collapse Threat Meter *(legacy, becomes the Rift meter in the finale)* (Top-Center: Anchor (Center, 1036px)) (core):**
  - Primary tension anchor: 440 x 28px horizontal meter framed in `#0E1420EE`.
  - Background track: Dark void purple `#180D2E`.
  - Threat fill: Deep gradient from Menacing Violet `#7C3AED` to Void Crimson `#DC2626`.
  - Pips: Left pip represents the advancing Collapse event horizon; middle pip represents the player fleet ship; right pip represents the sector Jump Gate.
  - Distance readout: 20pt bold tabular numerals centered directly below the bar (e.g., `COLLAPSE: 1,840m`).
  - Distance threshold tinting: Safe (>1500m) muted teal `#2DD4BF`, Caution (800-1500m) amber `#FBBF24`, Danger (<800m) flashing red `#EF4444` at 2.5 Hz.
- **Compass & Waypoint Strip (Top-Center: Anchor (Center, 984px)) (core):**
  - Horizontal bearing strip: 500 x 22px. Subtle degree ticks with cardinal points (N, E, S, W) in 12pt bold `#94A3B8`.
  - Embedded dynamic markers: warp target / objective (Golden Ring icon `#F59E0B`), active player pings (colored chevrons), landed ship beacon (Cyan ship icon `#38BDF8`).
- **Crosshair & Reticle (Screen Center: 960 x 540px) (core):**
  - On-foot / flight reticle: Minimal 24 x 24px ring `#E2E8F066` with 2px static center pip.
  - Hit confirmation marker: 4 diagonal ticks (`#F87171`) blooming outward from 12px to 24px radius over 80ms before fading. Critical/weak-point hits bloom in Gold `#FBBF24`.
  - Interaction Prompt: Floats 52px directly below crosshair. Pill container 160 x 32px with keycap icon `[F]` in `#38BDF8` and action string in 16pt body text (e.g., `[F] MINE ORE`, `[F] PILOT SEAT`).
- **Boss Health Bar (Top-Center: Anchor (Center, 920px)) (core):**
  - 640 x 24px segmented into 3 sub-bars representing the 3 mothership weak-point power cores.
  - Active segment fill: Menacing Magenta `#E11D48`. Depleted segment: Slotted dark background `#37131D`. Boss title header: 18pt bold uppercase `#FDA4AF`.
- **Screen-Space Alerts & Overlays (core):**
  - Damage Vignette (core): Radial perimeter gradient pulsing Crimson `#DC262699` on hit, decaying smoothly over 220ms.
  - Low-Oxygen Suffocation Pulse (core): Rhythmic radial constriction vignette `#0891B255` expanding and relaxing at 1.0 Hz in sync with heartbeat audio.
  - Notification Toasts (core): Stack positioned at Top-Right (Anchor (1872px, 980px)), expanding downwards. Max 3 active cards (300 x 44px). Background `#0E1420F0`, left accent bar 4px with event color. 4.0s linger duration before sliding right off-screen.
  - World-Space Co-op Pings (core): Clamped screen-space beacons displaying 20 x 20px glyph, player nameplate, and tabular distance meter (e.g., `142m`).

### Typography System

- **Display & Screen Titles (core):** 52pt bold, all-caps, tracking +5% (letter spacing 1.2px), color `#F8FAFC`. Used for Main Menu headers, Game Over banners, and Sector Transition splashes.
- **Section & Modal Headers (core):** 28pt semi-bold, all-caps, tracking +4%, color `#CBD5E1`. Used for settings categories, lobby headings, and vendor titles.
- **Subheaders & Card Titles (core):** 20pt medium, tracking +2%, color `#E2E8F0`. Used for item names, ship archetypes, and perk headers.
- **Body Text (core):** 16pt regular, line-height 130%, color `#94A3B8`. Used for lore snippets, item descriptions, and rebind notices.
- **HUD Tabular Numerals (core):** 18pt-24pt bold, monospace/tabular digit formatting, color `#F8FAFC`. Ensures health numbers, distance values, and ammo counters do not jitter on change.
- **Keycap Badges & Micro Labels (core):** 12pt bold, all-caps, background `#1E293B`, rounded 4px pad (4px horizontal), color `#38BDF8`.

### Color System

- **Surface Hierarchy:**
  - Base Backdrop Overlay: `#0A0E17DD` (deep cosmos navy, 87% opacity).
  - Primary Panel Surface: `#0E1420EE` (structural surface, 93% opacity).
  - Raised Card / Container: `#162032F2` (tactile card tone).
  - Inset / Input Well: `#0A0F1A` (sunken background for progress tracks and text fields).
  - Border Stroke: 1px subtle divider `#33415555`. No high-contrast garish outlines.
- **Semantic Colors:**
  - Health: `#4EED8C` (mint green) | Depleted: `#14291ECC`
  - Shield: `#38BDF8` (celestial cyan-blue) | Depleted: `#0C2033CC`
  - Oxygen: `#06B6D4` (pure cyan) | Depleted: `#08232ACC`
  - Fuel / Thrust: `#FBBF24` (warm solar amber) | Depleted: `#2B1F05CC`
  - Danger / Alert / Hostile: `#EF4444` (bright coral red) | Critical: `#7F1D1D`
  - Memory Fragments (Meta, formerly Stardust): `#C084FC` (pastel neon violet)
  - Salvage (Run, formerly Credits): `#FCD34D` (soft golden pyrite)
  - Exotic Matter (warp fuel): `#C4A5FF` / item tint violet (#78; teal is already the HUD meter accent)
- **Sector UI Accent Triads *(legacy, to be redefined per galaxy in #46)*:**
  - **Sector 1 (Verdant Drift):** Primary Mint `#34D399`, Secondary Teal `#0F766E`, Accent Sunlight `#FBBF24`.
  - **Sector 2 (Ember Reach):** Primary Coral `#FB923C`, Secondary Terracotta `#9A3412`, Accent Flare Yellow `#FDE047`.
  - **Sector 3 (Umbral Deep):** Primary Violet `#A855F7`, Secondary Deep Indigo `#4338CA`, Accent Electric Cyan `#22D3EE`.

### Component Specifications

- **Buttons (core):** 240 x 48px standard (160 x 40px compact). Corner radius 6px.
  - Rest State: Surface `#1E293B`, bottom lip shadow 3px `#0F172A`, label `#E2E8F0`.
  - Hover State: Surface `#334155`, top highlight 1px `#64748B`, label `#FFFFFF`, cursor changes.
  - Pressed State: Surface `#0F172A`, 0px lip shadow (2px downward translation), label `#94A3B8`.
  - Disabled State: Surface `#0F172A66`, border `#1E293B44`, label `#475569`.
- **Sliders (core):** Track 220 x 8px, background `#0A0F1A`, rounded 4px. Filled portion uses sector primary accent `#38BDF8`. Thumb: 18 x 18px circle, color `#F8FAFC`, with 2px border `#0E1420`. Hover scales thumb by 15%.
- **Toggles (core):** 46 x 24px rounded pill. Inactive: track `#1E293B`, thumb 16px `#64748B` aligned left. Active: track `#38BDF8`, thumb 16px `#FFFFFF` aligned right. Transition duration 120ms.
- **Inventory Grid Cells (core):** 60 x 60px squares, 6px spacing. Inset background `#111827`, border 1px `#1F2937`. Quality/tier border indicator (Tier 1 Grey `#64748B`, Tier 2 Cyan `#38BDF8`, Tier 3 Purple `#A855F7`). Drag-over highlight: bright white border tint.
- **Resource Bars (core):** 16px height, rounded 3px. Dual fill rendering: instantaneous front fill bar, paired with a trailing white/red ghost fill that catches up over 350ms to communicate damage or drain volume clearly.
- **Tooltips (core):** 240px fixed width, floating card. Background `#0E1420F8`, corner radius 6px, border 1px in matching item tier color. Header 16pt bold, body 13pt regular, stat comparisons highlighted in green `#4EED8C` or red `#EF4444`.

---

## Asset Design

### Visual Identity & Modeling Conventions

Direction since 2026-10-17 (#120, rules #121): **low poly, flat-shaded with a shared colour palette** ("variant A"). It replaces the detailed PBR direction of #91. Comparison of the variants and the audit of the old assets: `Docs/screenshots/lowpoly-audit/` (`04_stilvarianten.jpg`). Assets imported under #91-#100 (Fab/Sketchfab PBR models, photoscans, photo textures) are legacy and get replaced by #122-#127 and #133; the runtime code built around them stays.

- **Art Style:** clear silhouettes from few, large, readable faces. Hard facets everywhere (geometric flat normals, no smoothing groups), one colour per face from the shared palette, slight per-face brightness jitter (+-5 %) allowed for life. Readable at a glance first, detail second. A screenshot must look like a finished stylised game, not like a prototype.
- **No:** photo or scan textures, normal/height/detail maps, baked AO textures, greebles and panel lines in texture, PBR metal/roughness maps, decimated high-poly scans (they read as noisy facets). Small palette atlases (<= 256 px, flat swatches) that some CC0 packs ship with are converted to vertex colours on import.
- **Light and glow:** emissives carry the sci-fi look: cyan and orange glow (`surf.y` glow in the vertex contract) plus bloom. Lighting, fog, ACES tonemapping and the per-galaxy grade (`PaletteDef`) stay and do the mood; materials stay simple.
- **Shader:** `VW_ToonFlat` with `_FLATSHADING_ON` + `_USEVERTEXCOLOR_ON` (steps 6, softness 1, low rim) is the standard material for models. Glass: dark palette colour with high gloss (opaque or `VW_Glass`). Planet terrain follows #124 (faceted ground with a fixed facet size if the prototype works, else smooth ground with faceted objects).
- **Budgets (triangles):** small props < 300, props/decor < 1k, hand tools < 800, crew body 1.5-3k, creatures 1-3k, bosses <= 8k, small ships 1.5-4k exterior + <= 3k cockpit, Nostos <= 8k hull + <= 10k walkable interior, station <= 12k. Textures: none (vertex colour only).
- **Where assets come from:** free low-poly packs first (Quaternius, Kenney, Poly Pizza; Sketchfab/Fab only with a true flat low-poly look; CC0/CC-BY, commercial use allowed), recoloured to the palette; building it ourselves with a Blender script is equally fine because the style is simple. Every third-party asset gets a line in `Assets/_Project/Art/CREDITS.md`.
- **Consistency:** the palette is the glue: assets from different authors are recoloured to it, no asset keeps its own colour scheme. Similar facet size for objects of the same size class (a 2 m rock and a 2 m crate have a similar number of faces).
- **Atmospheric Palette:** deep cosmic nebulae in saturated gradients with layered depth (dust, stars, distant bodies). Fog is tinted to the sector palette for depth between planets, ships and debris. The photographic sky (NASA star map, galaxy photos) is checked against the style in #133.
- **Pipeline:** reproducible as before: sources in the repo, a `Tools/Blender/<asset>.py` script normalizes (metres, nose +Z, origin) and writes the vw vertex contract (`Col` = palette colour, `surf` = gloss/glow), `PrefabBuilder.ImportedMesh` builds the mesh, gameplay parts (colliders, seats, muzzles, thrusters, lights) are placed in `PrefabBuilder`.

#### Shared palette (sRGB, design values)
| Group | Colours |
|---|---|
| Human tech hulls (Dart, Mule, Nostos, station) | hull light `#C9CED4`, hull mid `#8A9199`, hull dark `#424850`, trim `#262A30`, accent orange `#E07A2E`, warning yellow `#E8B53A` |
| Glass and screens | canopy glass `#1B2532`, screen base `#0F1A22` |
| Emission | cyan `#4FD8FF`, engine orange `#FF9A3C`, warning red `#FF4D4D` |
| Crew | suit `#D8DBDF`, joints/gear `#3A3F46`, visor `#162030` + crew colour on the visor rim (#60) |
| Wardens / precursors | ivory `#E8E2D0`, ivory shade `#B8B09A`, warden cyan `#5FE3E0` |
| Aliens (default faction, #51 adds one accent per galaxy) | chitin purple `#581C87`, acid green `#84CC16`, lime glow `#A3E635` |
| Rock and ground (neutral) | rock light `#A39A91`, rock mid `#7F766F`, rock dark `#5A534E`; biome colours: "Planet Biome Identities" below |
| Resources | ore `#C46A3A`, crystal violet `#8B5CF6`, crystal cyan `#5BD6E8`, exotic matter `#C084FC` (glow) |

The palette becomes data (a palette asset the builders read) with the first asset issue (#122); until then these values are the reference.

### Ship Design Language

- **Dart (Agile Starter Fighter) (core):** low poly since #122: Quaternius "Omen" (CC0) recoloured to the palette (`lowpoly_ships.py`); the Fab "SpaceShip" (#91) stays only as the builder's fallback. Placeholder look: the ships get another pass later.
  - Silhouette: long nose cannon, a delta wing widening to the stern, short tail fins, the cockpit block in front of the stern cowl.
  - Scale: 15.0m length, 15.2m span, 3.7m height.
  - Cockpit: glass roof + windscreen on the cockpit block; the pilot sits in a tub sunk into the hull (Kenney chair, Kenney console as dashboard with the six live screens, side stick + throttle as own objects).
  - Visual Features: one big exhaust in the stern cowl behind the cockpit, nose cannon mouth.
  - Palette: hull greys with orange accent panels (palette `accent`), cyan/orange glow.
- **Mule (Cargo Hauler) (core):** low poly since #122: Quaternius "Spitfire" (CC0) recoloured; the Fab "DROPSHIP R35" (#97) stays as fallback.
  - Silhouette: tall body at the stern, a long flat nose boom, stub wings out to two stacked engine pods per side.
  - Scale: 13.0m length, 18.2m width (engine pods), 5.3m height.
  - Cockpit: the raised glass wedge on the body's front, the same low-poly tub and fittings as the Dart. Cargo terminal at the stern.
  - Visual Features: four exhausts in the pods, guns at the boom tip.
  - Palette: hull greys, large orange pod and body panels.
- **Nostos (home ship, multi-station co-op transport) (core, #49):** today the Ark hull of #97 (Sketchfab "SPACESHIP - CARGO" by Kerem Kavalci) at 1.5 x, interior from the "Sci-Fi Corridor - Revisited 2019" kit (Mesh Masters), cryo pods "Cryo LOD0" (Nikitos & 3130), all legacy PBR. A low-poly hull (Quaternius "Imperial") is prepared, but the interior, airlock and clamps are fitted to the Ark's narrow winged hull, so the swap waits for the ship rework. The crew's ship from the first minute: it carries the warp drive (it leads every warp; parts and Exotic Matter are handled aboard), the cryo pods and the Darts; destroyed = the loop resets.
  - Silhouette: Long hauler with a tall forward cab (bridge), a row of cargo racks on the spine, four engine nacelles on front and rear wings, a Dart docked on each wing.
  - Scale: 54.0m length, 37.3m width (wings), 12.0m height.
  - Walkable Interior Architecture (one deck at y -2.33, Gravity Volumes), stern to bow:
    1. *Engine room:* the warp core (drive terminal: parts + Exotic Matter), engineer console (left), aft gunner station (right); laser turret on the tail.
    2. *Cargo hall (corridor kit, 4.4 m wide):* airlock in the left flank with a boarding ramp, fabricator and cargo terminal (right), four hatches up to the docking clamps.
    3. *Cryo bay:* four pods (one per player slot) - a fallen crew member who is not revived wakes here (costs a cryo charge).
    4. *Bridge (cab):* raised pilot platform with a ramp behind the wide front window (Dart fittings, live screens), forward gunner station at the rear left; flak turret on the cab roof.
  - Docking clamps: four, on the wing tops. Darts dock for every warp flight (automatically under the bubble's veil) or when a pilot eases into a free clamp (< 16 m, < 14 m/s); holding thrust launches them. Leaving the seat of a docked Dart puts the pilot at its hatch inside; the hatch boards it again.
  - Landing: heavy - the descent is capped (6 m/s, easing to a crawl) and it only sets down on ground flatter than 14 deg (it hovers with a warning above steeper ground). Hardpoints for #50 are data on its `ShipDef`.
  - Derelict look (unused in the loop): the same ship, scorched and dark (no glow), red flickering emergency light inside.
  - Palette: Gunmetal plating with tan trim, cyan glow; slate interior panels with cyan light strips and orange warning lights.
- **Cockpit Dashboard Conventions (core):**
  - Diegetic instrumentation: Angled, detailed dashboard consoles positioned within player field of view.
  - Glow screens: Emissive screens displaying pitch/horizon ladder, analog throttle bar, fuel cylinder, and 3D wireframe radar sphere.
  - Physical switchgear: Chunky polygonal toggles and buttons with colored indicator LEDs that physically flip on system activation.

### Enemy Visual Identity & Silhouettes

New direction (#72, #51): only nature and aliens. The pirate faction below is *(legacy)*: its archetypes are rebuilt as alien craft (biomechanical hulls instead of salvage metal). Each galaxy gets its own alien ecosystem; the Wardens are the ancient biomechanical guardian species.


- **Pirate Faction *(legacy, replaced by alien craft in #51)* (Mechanical, Angular, Hostile):**
  - Design Motif: Aggressive wedge forms, exposed heat sinks, jagged armor plates, rusted industrial salvage look.
  - Palette: Matte obsidian steel `#18181B`, dark rust brown `#78350F`, glaring red sensor slits and thruster plumes `#EF4444`.
  - Archetypes:
    - *Pirate Fighter (core):* 7m dagger hull, dual raked wings, twin nose blasters. Highly agile pursuit craft.
    - *Drone Swarm (core):* 1.2m faceted octahedrons flying in loose clusters of 3-5 units, rotating around a central crimson optical core.
    - *Pirate Gunship (optional):* 18m lumbering armored gun platform with a top-mounted swiveling heavy turret and forward cow-catcher ram.
- **Alien Faction (Organic, Biomorphic, Primal):**
  - Design Motif: Segmented chitin carapaces, bulbous glowing sacs, multi-legged arachnid and mantis postures, fluid organic curves composed of chunky polygon facets.
  - Palette: Acid yellow-green `#84CC16`, deep chitin purple `#581C87`, bioluminescent lime accents `#A3E635`.
  - Archetypes:
    - *Crawler (core):* 1.4m six-legged faceted insectoid. Scuttles across planetary terrain with rapid gait; lunges forward to slash with twin forelegs.
    - *Spitter (core):* 2.4m tall tripod alien with an inflatable throat sac. Rears back and launches glowing parabolic acid mortar orbs.
    - *Flying Critter (optional):* 2.0m wingspan manta-form gliding through planetary atmospheres, swooping on grounded players.
    - *Nest Guardian (optional):* 5.2m hulking armored carapace beast that guards planetary hives. Features heavy frontal armor plates and vulnerable glowing rear egg sacs.
- **Void Mothership Boss *(legacy: becomes the Warden enclave guardian per galaxy, #42)* (core):**
  - Silhouette: Colossal 110m fractured geometric monolith floating in zero gravity directly before the final Jump Gate.
  - Architecture: Asymmetrical crystalline dreadnought constructed from interlocking obsidian fragments orbiting a central core.
  - Weak-Point Power Cores: 3 distinct glowing spherical reactors (Port Generator, Starboard Generator, Central Core) measuring 6m in diameter.
  - Visual Vulnerability: Weak points glow with brilliant celestial violet-white light `#DDD6FE` and shed crystalline particle flakes; non-weak-point armor is impervious dark basalt `#0F172A`.

### Planet Biome Identities (Mini-Planets ~150-280m Radius)

- **1. Ice Biome (core):**
  - Lowlands: Pale periwinkle ice sheets `#93C5FD`.
  - Highlands: Glacial crisp white `#F8FAFC`.
  - Landmark Features: Hexagonal translucent ice spires, frozen crevasses, smooth skating ridges.
  - Atmosphere: Whispering pale blue ring haze `#60A5FA33`. Breathable: Yes.
- **2. Desert Biome (core):**
  - Lowlands: Terracotta clay flats `#C2410C`.
  - Highlands: Golden windswept dunes `#F59E0B`.
  - Landmark Features: Gigantic natural rock arches, layered polygonal mesas, desiccated canyon networks.
  - Atmosphere: Warm amber dust suspension `#FDE68A25`. Breathable: No (continuous O2 consumption).
- **3. Toxic Jungle Biome (core):**
  - Lowlands: Murky dark teal peat `#115E59`.
  - Highlands: Fluorescent lime moss terraces `#65A30D`.
  - Landmark Features: Enormous bulbous mushroom flora, hollow petrified root tunnels, bubbling geysers.
  - Atmosphere: Thick lime-tinted vapor fog `#A7F3D040`. Breathable: No (rapid O2 drain).
- **4. Crystal Biome (optional):**
  - Lowlands: Deep amethyst obsidian `#312E81`.
  - Highlands: Pale lilac crystal plateaus `#C084FC`.
  - Landmark Features: Clusters of towering faceted crystal prisms sprouting from craters, floating geometric crystal clusters.
  - Atmosphere: Shimmering iridescent violet haze `#E9D5FF25`. Breathable: Yes.
- **5. Volcanic Biome (optional):**
  - Lowlands: Basalt charcoal rock `#18181B`.
  - Highlands: Ash grey crags `#475569`.
  - Landmark Features: Glowing molten orange lava fissures `#EA580C`, smoking caldera vents, jagged obsidian spires.
  - Atmosphere: Dark ash particulate haze with rising ember sparks `#F9731635`. Breathable: No.

### Props & Points of Interest (POIs)

- **Resource Nodes (core):**
  - *Ore Node:* 2.2m cluster of jagged copper-rust rock facets `#EA580C`. Fractures into 3-5 bouncy tetrahedral ore chunks.
  - *Crystal Node:* 2.6m cluster of upright violet crystal columns `#A855F7`. Emits a faint ambient chime and glow.
  - *Scrap Node:* 2.0m pile of crumpled industrial plating and trusses `#64748B` with yellow caution stripes.
  - *Fuel Node:* 1.8m cluster of organic amber resin orbs `#FBBF24` protruding from planetary crust.
- **Loot Containers & Hazards (core):**
  - *Loot Crate:* 1.2 x 1.2 x 1.0m chamfered container. Dark grey body `#1E293B`, glowing teal latch `#2DD4BF`. Pops open like a clam shell when interacted with.
  - *Alien Nest:* 4m bulbous fleshy pod `#4C1D95` embedded in soil, surrounded by 3-4 destroyable eggs that hatch Crawlers if disturbed.
  - *Crashed Scout Ship:* Fractured Dart fuselage partially embedded in a crater, smoking engine bay, salvageable cockpit crate.
- **Stations & Megastructures (core):**
  - *Trader Station (core, becomes the alien nomad meeting point):* 140m rotating toroidal ring station stationed at the mid-point of Sectors 1 and 2. Features external docking clamp beacons and an illuminated interior landing bay with friendly teal beacon arrays `#2DD4BF`.
  - *Jump Gate (legacy, removed in #46 - travel is by own warp drive):* Massive 85m polygonal ring positioned at the sector terminus. Composed of dark alloy pylons surrounding a swirling, opaque hyperspace energy membrane. Features 4 socket pedestals for inserting gathered Energy Cells.
  - *Precursor Monolith (optional):* 15m solitary geometric spire etched with glowing cyan runic grooves, granting sector map reveals or meta currency.

### Visual Effects (VFX) Direction

- **Purity Principle:** No noisy photorealistic smoke, alpha-blended fire sheets, or procedural noise blobs. Every effect is built from crisp polygonal meshes, flat-shaded debris particles, or glowing geometric billboards.
- **Engine Thrusters (core):** Diamond-shaped particle puffs expelled along the thrust vector, shrinking rapidly over 250ms while shifting from white-hot core to sector pastel accent.
- **Weapon Lasers (core):** Elongated faceted diamond geometry (length 1.5m, radius 0.08m) with an intensely emissive core and a saturated outer bloom envelope.
- **Explosions (core):** Instantaneous flash of a 24-faceted icosahedron sphere (0-60ms), instantly popping into 12-16 spinning flat triangular fragments that decelerate with air resistance while fading.
- **Shield Impacts (core):** Hexagonal wireframe grid dome illuminating momentarily at the point of hit, rippling outward over a 1.2m radius and fading within 180ms.
- **Atmospheric Re-entry (core):** Sweeping pastel plasma compression ribbons wrapping around the forward contours of the ship hull when entering a planet's gravity envelope at speed.
- **Warp Flight (core):** Warp tunnel streaks scaling with warp throttle, hull vibration and heat shimmer at high warp, obstacle silhouettes readable far ahead (#73).
- **Hyperspace Jump (legacy, becomes the warp entry/exit flash):** Full-screen brilliant white-lavender flash (`#EDE9FE`, 120ms duration) that snaps into radial warp velocity streaks before dropping players into the next sector.

---

## Game Feedback

### Genre Profile: High-Energy Survival Hybrid

VOIDWARD marries punchy, arcade-responsive action controls (snappy mining, impactful weapon discharge, tight zero-g boost mechanics) with expedition suspense (a rising alien alert level, depleting oxygen supplies, long warp flights that must be mastered, and high-stakes co-op multi-station management) and the steady reward of a Home ETA that keeps shrinking. Feedback is immediate, high-contrast, and strictly informative.

### Interaction Map

| Interaction | Tier | Importance | Camera | Time | Transform | Visual | Audio | Input | Rationale |
|---|---|---|---|---|---|---|---|---|---|
| **Hand Mining Strike** | core | Medium | Light impulse 0.04m along impact normal, decay 100ms | — | Node squashes 8% along hit axis, recovers over 140ms | Rock fracture sparks (6-8 pastel chips), node facet flash white (60ms) | Crisp stone crack + resonant crystal ping (±12% pitch) | 80ms vibration burst on gamepad | Confirms solid tool contact and communicates material resistance |
| **Node Shatter / Harvest** | core | Heavy | Directional impulse 0.12m, exponential decay 250ms | 0.03s hitstop freeze frame | Node expands 20% then collapses into fragments | 3-5 bouncy polygonal resource nuggets pop out in an upward arc | Deep structural crumble + metallic pop | Heavy 180ms rumble | Delivers the core reward payoff for finishing a resource deposit |
| **Resource Magnet Pickup** | core | Light | — | — | Nugget accelerates toward player with parabolic curve | Small pastel sparkle burst upon reaching inventory socket | Light high-register chime (e.g. C6, +3 semitones on combo) | — | Effortless loot acquisition feedback without visual clutter |
| **Player Weapon Fire** | core | Medium | Recoil kick: 1.5 deg pitch up, 0.4 deg yaw kick, settle 120ms | — | Weapon barrel recoils backward 0.05m, springs back | Sharp diamond muzzle flash (40ms) + forward light burst | Snappy high-energy zap with low-end transient thump | 60ms crisp trigger kick | Makes small-arms combat feel snappy and mechanically responsive |
| **Laser Hit Confirmation** | core | Light | — | — | Enemy squashes 5% on impact vector | 4-tick red crosshair bloom (80ms), enemy mesh flashes white (70ms) | Crisp metallic chirp / wet alien squish (±10% pitch) | — | Instant hit validation across vast space and ground distances |
| **Weak-Point / Crit Hit** | core | Heavy | Sharp 0.08m camera jolt | 0.04s hitstop | Target staggers or expands 12% | Golden crosshair tick bloom, bright starry particle explosion | Resonant glass shatter + deep harmonic gong | Sharp 120ms pulse | Tells player unmistakably that they struck a critical vulnerability |
| **Ship Primary Cannon** | core | Medium | Ship camera dips 0.08m back, settles over 180ms | — | Cannon mount slides backward, returns via spring | Heavy muzzle blast + dual dynamic light flashes | Deep mechanical clunk + resonant plasma boom | Sustained low vibration | Gives ship weaponry substantial tonnage and ballistic authority |
| **Ship Boost Activation** | core | Medium | FOV expands +12 deg over 300ms, settles at +8 deg | — | Ship body stretches 6% along forward axis | Exhaust flame extends 3x, screen-edge speed streak lines | High-pitch turbine whine surging into powerful roar | Ramp-up haptic rumble | Conveys sudden explosive acceleration and speed breaking |
| **Shield Depletion** | core | Heavy | Omnidirectional camera shudder 0.1m, decay 200ms | — | — | Full shield dome shatters into fading hexagonal glass shards | High-voltage electric crackle + sudden muffled low-pass filter | Sustained double buzz | High-priority danger signal warning player that hull is exposed |
| **Hull Damage Taken** | core | Heavy | Hard directional shake toward incoming fire, decay 300ms | — | — | Screen-space crimson vignette flash, cockpit warning sirens flash red | Metallic hull tear + urgent two-tone alarm klaxon | Heavy jolt | Instantly directs player spatial awareness to the threat vector |
| **Low Oxygen (<20%)** | core | Critical | Subtle rhythmic camera pulse synced to heartbeat | — | — | Tunnel-vision black/cyan vignette constricting at 1.0 Hz | Deep muffled biological heartbeat + heavy labored breathing | Throbbing low-frequency pulse | Evokes claustrophobia and urgent survival panic |
| **Rift Proximity (finale; legacy: Collapse <800m)**| core | Critical | Persistent low-amplitude micro-tremor, intensity scales | — | — | Horizon blackens with violet electrical tendrils, screen edges warp | Deep subterranean 40Hz void rumble + cosmic static hiss | Continuous low hum | Relentless physical pressure during the escape home |
| **Ship Planetary Landing** | core | Medium | Vertical bounce 0.15m downward, rebound settle 350ms | — | Landing struts compress 30%, slowly expand | Radial ring of surface dust particles billows outward | Deep cushioned pneumatic thud + gravel crunch | Solid low thud | Communicates the physical mass and touchdown stability of the vessel |
| **Warp Engage (legacy: Jump Gate activation)** | core | Critical | Violent camera shake ramping over 4s to maximum | 0.05s freeze frame on transition | Ship vibrates violently, then streaks forward | Warp field flares to pure white lavender | Rising turbine crescendo snapping into absolute silence | Maximum rumble ramping to sudden cut | Climactic departure for the next galaxy |
| **Station Seat Enter/Exit** | core | Light | Smooth camera blend into cockpit perspective (250ms) | — | Player hands grip controls | Diegetic console boots up, glowing vector readouts ignite | Heavy pneumatic chair latch + cockpit boot chime | Light click | Establishes transition between player pawn and ship pilot agency |
| **Co-op Ping Placed** | core | Light | — | — | — | 3D colored beacon pillars drop from sky, minimap pip pulses | Crisp spatialized two-tone chirp (1200Hz -> 1600Hz) | Subtle pip | Effortless non-verbal team coordination |
| **Home ETA Drops** | core | Medium | - | - | ETA readout pops 12% and ticks down | Gold flash on the ETA, drive core glows on the dashboard | Rising two-note chime + low engine purr | Light pulse | The central progress reward: every part brings home closer |
| **Crafting / Upgrade Done** | core | Medium | — | — | UI card pops 10% overshoot, settles over 150ms | Golden starburst particle shower over module socket | Harmonious major chord chime + mechanical latch | — | Satisfying reward confirmation for progression investment |

### Multi-Step Event Sequences

- **Mining Node Shatter Sequence:**
  1. *Impact Strike (0ms):* Tool connects. Node flashes white (60ms). 0.04m camera impulse along strike normal. Rock strike SFX plays.
  2. *Fracture & Hitstop (60ms):* Hitstop freezes animation for 30ms. Structural crack fissures ignite with interior light.
  3. *Explosion & Debris (90ms):* Node model disables; 10-14 rock chunks spray outward with exponential velocity decay.
  4. *Nugget Spawn (120ms):* 3-5 resource nuggets pop upward in a parabolic fountain, bouncing on terrain.
  5. *Magnet Pull (600ms):* Nuggets activate homing trajectory toward player vacuum radius.
  6. *Inventory Slotting (850ms):* Nuggets reach player; soft chime plays, hotbar icon pulses 15% scale with green flash.

- **Jump Gate Charge & Sector Warp Sequence *(legacy, replaced by warp flight #73)*:**
  1. *Cell Insertion (0-2s):* Player slots required Energy Cells into gate pylons. Pylon conduits illuminate sequentially with ascending chimes.
  2. *Membrane Ignition (2.0s):* Center ring ignites into a swirling pastel violet energy vortex. Ambient music drops to a muffled low-pass filter.
  3. *Charge Crescendo (2.0s - 4.8s):* Gate generates a massive gravitational pull. Ship engine pitch rises exponentially. Camera shake intensifies linearly from 0.05m to 0.25m amplitude.
  4. *The Snap (4.85s):* 0.05s total freeze-frame. All audio instantly cuts to total vacuum silence.
  5. *Hyperspace Breach (4.9s):* Brilliant lavender flash blooms across entire display (`#EDE9FE`). Screen streaks into radial warp lines.
  6. *Sector Arrival (5.4s):* Warp lines collapse; ship drops into new sector space with a thunderous bass boom; sector title banner slides in.

- **Boss Weak-Point Core Destruction Sequence:**
  1. *Core Overload (0ms):* Lethal damage applied. Core locks into place; hitstop for 0.04s.
  2. *Thermal Swell (40ms):* Core mesh scales up 25% over 180ms while shifting to incandescent white.
  3. *Rupture Blast (220ms):* Core explodes into 20 glowing obsidian shards and an expanding flat circular shock ring. Directional 0.15m camera shake.
  4. *Structural Feedback (350ms):* Mothership groans with a deep metallic roar; adjacent hull segments spark with secondary localized electrical explosions.
  5. *Boss Health Retraction (500ms):* Associated segment on HUD Boss Bar drains with an animated red ghost trail; next phase music layer engages.

### Audio System Direction & Alarm Taxonomy

- **Galaxy Music & Environmental Ambience (sector moods below are legacy, to be mapped onto the 5 galaxies):**
  - *Sector 1 (Verdant Drift):* Bright, exploratory synth bed in C Major. Shimmering high-register arpeggios, gentle crystalline wind chimes, peaceful low sub-bass drone. Communicates fresh beginnings and accessible scavenging.
  - *Sector 2 (Ember Reach):* Warm, tense analog pads in A Minor. Mid-tempo industrial percussion pulse, low drone of stellar solar winds, strained harmonic resonance. Communicates industrial urgency and rising pressure.
  - *Sector 3 (Umbral Deep):* Cold, ominous void drone in D Minor. Irregular metallic groans, dissonant high-frequency shimmer, deep rhythmic heartbeat rhythm. Communicates lethal cosmic hostility and impending climax.
- **Alarm Taxonomy (Strict Sensory Separation):**
  - *Low Oxygen Alarm:* Rhythmic biological double-thud heartbeat (1.0 Hz, 80Hz center) coupled with player breathing and a gentle high-register vital monitor blip. Non-mechanical, purely physiological.
  - *Hull Critical Alarm (<25% HP):* High-urgency mechanical two-tone klaxon (780Hz alternating with 960Hz at 3.0 Hz cadence). Plays directly through cockpit speakers with an assertive stereo pan.
  - *Rift Proximity Warning (finale; legacy Collapse <800m):* Ominous low-frequency seismic hum (35-55Hz) that vibrates continuously, punctuated by intermittent electrical crackle and rising metallic groaning.
  - *Shield Depleted Alert:* Single descending electrical zap (1200Hz down to 200Hz over 180ms) followed by an urgent buzz, signalling immediate vulnerability.
- **Weapon & Impact Sound Design:**
  - *Mining Laser / Pick:* High-clarity ceramic "clink" with subtle harmonic ring. Randomized pitch variance (±12%) on every stroke to prevent acoustic fatigue.
  - *Light Plasma Blaster:* Snappy whip-like electric zap (850Hz transient, decaying to 180Hz over 90ms).
  - *Heavy Ship Cannon:* Deep mechanical chamber strike followed by a concussive low-frequency boom (120Hz punch).
  - *Target Hit Confirmation:* Soft metallic ping on armored hulls; wet, squishy pop on alien biomass.
- **UI & Interaction Sound Design:**
  - *Button Hover:* Subtly muted 12ms tick (1800Hz, -18dB) giving immediate cursor feedback without irritation.
  - *Button Confirm / Engage:* Crisp tactile switch thud with a slight warm harmonic chime (520Hz, 45ms).
  - *Cancel / Backout:* Descending muted wood-click (480Hz down to 240Hz, 30ms).
  - *Loot Collect / Cha-Ching:* Two-note bright bell arpeggio (C6 to G6, 80ms) evoking positive arcade gratification.
  - *Memory Fragment Accrual (formerly Stardust):* Ascending multi-tonal crystal cascade (3 notes, 120ms total).

### Co-op Communication Affordances (Ping System)

- **Ping Taxonomy, Colors & Glyphs:**
  - **Look / Move Marker (core):** Slate White `#E2E8F0`. Four-point diamond pip. Audio: Neutral two-tone blip (900Hz). Used for general coordination and navigation waypoints.
  - **Enemy / Danger Threat (core):** Aggressive Red `#EF4444`. Inverted triangle with center exclamation pip. Audio: Sharp urgent double-beep (1100Hz / 1400Hz). Placed on hostile craft, spitters, or alien ambushes.
  - **Loot / Resource Deposit (core):** Luminous Cyan `#38BDF8`. Hexagonal container glyph. Audio: Cheerful high-register chime (1300Hz). Placed on unmined ore nodes, loot crates, or dropped modules.
  - **Objective / Warp Target (core):** Solar Amber `#F59E0B`. Circular ring glyph with four ray pips. Audio: Resonant brassy ping (600Hz). Placed on a warp core, an enclave or the nomad dock.
- **In-World Affordances:**
  - Placing a ping drops a vertical translucent light beacon column from the sky (0.5m radius, 12m height) lasting 6.0 seconds.
  - Screen-edge clamping: When a ping target is off-camera, the glyph clamps to the screen perimeter with an orienting directional arrow and live distance readout in meters.
  - Co-op player differentiation: Pings display the issuing player's color dot and initials above the glyph (P1 Blue, P2 Green, P3 Yellow, P4 Magenta).

---

## Technical Design References & Implementation Bounds

- **Reference Display:** 1920 x 1080 resolution, 16:9 aspect ratio. All pixel dimensions scale uniformly using reference-resolution canvas scaling.
- **Render Pipeline Scope:** Universal Render Pipeline (URP). Flat-shaded low-poly materials (`VW_ToonFlat`: vertex colour, geometric flat normals, soft light ramp, readable specular, low rim; no normal/detail maps), see "Visual Identity". Post-processing: Bloom, Tonemapping (ACES), SSAO, color grading per sector, plus Chromatic Aberration/Vignette overrides for damage and threat states.
- **Network Awareness:** All gameplay feedback (weapon hit flashes, mining node shattering, shield break bursts) executes predictively on the local client with authoritative health confirmation from the server to guarantee instantaneous input feel under latency.
