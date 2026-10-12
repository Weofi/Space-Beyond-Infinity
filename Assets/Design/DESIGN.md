# Game Design: VOIDWARD

VOIDWARD is a 1-4 player co-op space roguelite vertical slice. Players flee an encroaching cosmic collapse, navigate three distinct sectors, touch down on low-poly spherical mini-planets to scavenge and mine, engage hostile ships and creatures, upgrade ship systems, and breach the final sector gate. This document defines the design specifications across UI Design, Asset Design, and Game Feedback.

Key dependencies between domains:
- UI accent colors and HUD styling derive directly from sector color palettes defined in Asset Design.
- Diegetic cockpit dashboards and holographic station consoles bridge physical Asset Design and tactile UI interaction.
- The Collapse threat pacing integrates the HUD distance meter, screen-space chromatic distortion, environmental lighting degradation, and escalating multi-tier audio alarms.
- Every element is tagged as either `(core)` for the lean playable vertical slice or `(optional)` for secondary expansion.

---

## UI Design

### Screen Inventory & Flows

```
[Main Menu] ---> [Lobby] ---> [Hangar] ---> [Sector 1..3 Gameplay HUD] ---> [Game Over]
      |                                              |                            |
  [Settings]                                     [Pause]                  [Demo Complete]
```

- **Main Menu (core):** Title banner, Host Game, Join Game (Direct IP / Steam Session Code), Settings, Quit. Clean centered stack with a live backdrop of a sector nebula and rotating low-poly asteroid.
- **Lobby (core):** Player roster card (1-4 slots with avatar color, ready state, selected starting role: Pilot, Turret, Engineer), ship selection carousel (Dart, Mule, Ark), sector route preview, Launch button (unlocked when all players ready).
- **Hangar (core):** Inter-run meta-progression hub. Stardust balance indicator, ship frame unlock tree, starting module loadout sockets, "Initiate Run" airlock trigger.
- **HUD (core):** Persistent contextual gameplay overlay supporting both on-foot and ship flight contexts.
- **Pause Menu (core):** Modal overlay pausing single-player or providing an in-game options sheet in co-op. Resume, Settings, Abandon Run (with confirmation dialogue).
- **Settings (core):** Tabbed overlay (Audio, Video, Controls). Sliders for Master/Music/SFX; toggles for Fullscreen, V-Sync, Bloom; slider for FOV (70-100); mouse sensitivity slider with Invert Y toggle; full input rebind table.
- **Game Over (core):** Post-run debrief panel. Failure cause banner ("Consumed by Collapse", "Hull Breached", "Suffocated"), run statistics (distance fled, sectors traversed, enemies eliminated, loot collected), calculated Stardust reward, "Return to Hangar" button.
- **Demo Complete (core):** Victory debrief displayed upon defeating the Sector 3 Boss Gate. Run summary stats, bonus Stardust allocation, celebratory palette glow, "Return to Hangar" button.

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
- **The Collapse Threat Meter (Top-Center: Anchor (Center, 1036px)) (core):**
  - Primary tension anchor: 440 x 28px horizontal meter framed in `#0E1420EE`.
  - Background track: Dark void purple `#180D2E`.
  - Threat fill: Deep gradient from Menacing Violet `#7C3AED` to Void Crimson `#DC2626`.
  - Pips: Left pip represents the advancing Collapse event horizon; middle pip represents the player fleet ship; right pip represents the sector Jump Gate.
  - Distance readout: 20pt bold tabular numerals centered directly below the bar (e.g., `COLLAPSE: 1,840m`).
  - Distance threshold tinting: Safe (>1500m) muted teal `#2DD4BF`, Caution (800-1500m) amber `#FBBF24`, Danger (<800m) flashing red `#EF4444` at 2.5 Hz.
- **Compass & Waypoint Strip (Top-Center: Anchor (Center, 984px)) (core):**
  - Horizontal bearing strip: 500 x 22px. Subtle degree ticks with cardinal points (N, E, S, W) in 12pt bold `#94A3B8`.
  - Embedded dynamic markers: Jump Gate (Golden Ring icon `#F59E0B`), active player pings (colored chevrons), landed ship beacon (Cyan ship icon `#38BDF8`).
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
  - Stardust Currency (Meta): `#C084FC` (pastel neon violet)
  - Scrap / Mineral Credits: `#FCD34D` (soft golden pyrite)
- **Sector UI Accent Triads:**
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

### Visual Identity & Low-Poly Modeling Conventions

- **Art Style:** Flat-shaded, faceted low-poly aesthetic inspired by "Astroneer". Chunky, readable silhouettes optimized for instant recognition at distance.
- **Geometry Rules:** Hard polygon edges without smooth normal averaging. Geometry is kept clean and chunky. No micro-bevels or noisy tessellation.
- **Texturing & Surface:** Zero texture maps. All surface albedo is driven strictly by vertex color palettes and flat material tinting.
- **Material & Lighting (URP):** Stylized custom lighting model using soft ambient fill, low material smoothness (0.05-0.15) to preserve crisp polygonal facets without glossy specularity, and generous post-processing bloom on emissive elements.
- **Atmospheric Palette:** Deep cosmic nebulae rendered in saturated pastel gradients. Fog is tinted to the sector palette to provide depth layering between planets, ships, and debris.

### Ship Design Language

- **Dart (Agile Starter Fighter) (core):**
  - Silhouette: Sharp delta tri-wing configuration with a bulbous panoramic cockpit bubble.
  - Scale: 8.5m length, 6.2m wingspan, 2.8m height.
  - Visual Features: Twin oversized rear thruster bells, twin forward wing-root blaster barrels, retractable tripod landing skids.
  - Palette: Clean chalk white hull `#E2E8F0`, sector accent wing highlights (Mint `#34D399` or Coral `#FB923C`), dark slate internal chassis `#1E293B`.
- **Mule (Cargo Hauler) (core):**
  - Silhouette: Asymmetrical industrial block-form with forward-offset cab and an exposed rear container superstructure.
  - Scale: 15.0m length, 9.4m width, 5.8m height.
  - Visual Features: Quad swiveling external engine pods, external roll-cage trusses, magnetic cargo clamps carrying faceted freight containers.
  - Palette: Heavy-machinery ochre yellow `#F59E0B`, weathered graphite structure `#334155`, hazard diagonal accents on airlock and clamps.
- **Ark (Multi-Station Co-op Frigate) (core):**
  - Silhouette: Heavy catamaran command ship with wide forward observation bridge and twin outrigger engine pylons.
  - Scale: 32.0m length, 20.5m width, 9.2m height.
  - Walkable Interior Architecture (Gravity Volume):
    1. *Bridge / Cockpit (Forward):* Seated pilot station, wrap-around low-poly glass canopy, central navigation holographic projector.
    2. *Dorsal Turret Well (Mid Upper):* Ladder access to an elevated 360-degree glass bubble containing the gunner seat and dual heavy pulse turrets.
    3. *Engineering Core (Aft Center):* Recessed bay containing the glowing hyperdrive jump coil, shield emitter generator, and resource refinement hopper.
    4. *Cargo Bay & Airlock (Lower Deck):* Rear ramp extending to planet terrain, 6 cargo magnetic lock pads, oxygen replenishment shower booth.
  - Palette: Deep navy plating `#1E293B`, off-white dorsal armor slabs `#F1F5F9`, luminescent cyan conduit lines `#38BDF8`.
- **Cockpit Dashboard Conventions (core):**
  - Diegetic instrumentation: Low-poly angled dashboard consoles positioned within player field of view.
  - Glow screens: Emissive screens displaying pitch/horizon ladder, analog throttle bar, fuel cylinder, and 3D wireframe radar sphere.
  - Physical switchgear: Chunky polygonal toggles and buttons with colored indicator LEDs that physically flip on system activation.

### Enemy Visual Identity & Silhouettes

- **Pirate Faction (Mechanical, Angular, Hostile):**
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
- **Void Mothership Boss (Sector 3 Climax) (core):**
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
  - *Trader Station (core):* 140m rotating toroidal ring station stationed at the mid-point of Sectors 1 and 2. Features external docking clamp beacons and an illuminated interior landing bay with friendly teal beacon arrays `#2DD4BF`.
  - *Jump Gate (core):* Massive 85m polygonal ring positioned at the sector terminus. Composed of dark alloy pylons surrounding a swirling, opaque hyperspace energy membrane. Features 4 socket pedestals for inserting gathered Energy Cells.
  - *Precursor Monolith (optional):* 15m solitary geometric spire etched with glowing cyan runic grooves, granting sector map reveals or meta currency.

### Visual Effects (VFX) Direction

- **Purity Principle:** No noisy photorealistic smoke, alpha-blended fire sheets, or procedural noise blobs. Every effect is built from crisp polygonal meshes, flat-shaded debris particles, or glowing geometric billboards.
- **Engine Thrusters (core):** Diamond-shaped particle puffs expelled along the thrust vector, shrinking rapidly over 250ms while shifting from white-hot core to sector pastel accent.
- **Weapon Lasers (core):** Elongated faceted diamond geometry (length 1.5m, radius 0.08m) with an intensely emissive core and a saturated outer bloom envelope.
- **Explosions (core):** Instantaneous flash of a 24-faceted icosahedron sphere (0-60ms), instantly popping into 12-16 spinning flat triangular fragments that decelerate with air resistance while fading.
- **Shield Impacts (core):** Hexagonal wireframe grid dome illuminating momentarily at the point of hit, rippling outward over a 1.2m radius and fading within 180ms.
- **Atmospheric Re-entry (core):** Sweeping pastel plasma compression ribbons wrapping around the forward contours of the ship hull when entering a planet's gravity envelope at speed.
- **Hyperspace Jump (core):** Full-screen brilliant white-lavender flash (`#EDE9FE`, 120ms duration) that snaps into radial warp velocity streaks before dropping players into the next sector.

---

## Game Feedback

### Genre Profile: High-Energy Survival Hybrid

VOIDWARD marries punchy, arcade-responsive action controls (snappy mining, impactful weapon discharge, tight zero-g boost mechanics) with crushing environmental suspense (the encroaching Collapse, depleting oxygen supplies, and high-stakes co-op multi-station management). Feedback is immediate, high-contrast, and strictly informative.

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
| **Collapse Proximity (<800m)**| core | Critical | Persistent low-amplitude micro-tremor, intensity scales | — | — | Horizon blackens with violet electrical tendrils, screen edges warp | Deep subterranean 40Hz void rumble + cosmic static hiss | Continuous low hum | Relentless physical pressure driving players toward the Jump Gate |
| **Ship Planetary Landing** | core | Medium | Vertical bounce 0.15m downward, rebound settle 350ms | — | Landing struts compress 30%, slowly expand | Radial ring of surface dust particles billows outward | Deep cushioned pneumatic thud + gravel crunch | Solid low thud | Communicates the physical mass and touchdown stability of the vessel |
| **Jump Gate Warp Activation** | core | Critical | Violent camera shake ramping over 4s to maximum | 0.05s freeze frame on transition | Ship vibrates violently, then streaks forward | Gate energy membrane flares to pure white lavender | Rising turbine crescendo snapping into absolute silence | Maximum rumble ramping to sudden cut | Climactic sector finale signaling escape from the Collapse |
| **Station Seat Enter/Exit** | core | Light | Smooth camera blend into cockpit perspective (250ms) | — | Player hands grip controls | Diegetic console boots up, glowing vector readouts ignite | Heavy pneumatic chair latch + cockpit boot chime | Light click | Establishes transition between player pawn and ship pilot agency |
| **Co-op Ping Placed** | core | Light | — | — | — | 3D colored beacon pillars drop from sky, minimap pip pulses | Crisp spatialized two-tone chirp (1200Hz -> 1600Hz) | Subtle pip | Effortless non-verbal team coordination |
| **Crafting / Upgrade Done** | core | Medium | — | — | UI card pops 10% overshoot, settles over 150ms | Golden starburst particle shower over module socket | Harmonious major chord chime + mechanical latch | — | Satisfying reward confirmation for progression investment |

### Multi-Step Event Sequences

- **Mining Node Shatter Sequence:**
  1. *Impact Strike (0ms):* Tool connects. Node flashes white (60ms). 0.04m camera impulse along strike normal. Rock strike SFX plays.
  2. *Fracture & Hitstop (60ms):* Hitstop freezes animation for 30ms. Structural crack fissures ignite with interior light.
  3. *Explosion & Debris (90ms):* Node model disables; 10-14 low-poly rock chunks spray outward with exponential velocity decay.
  4. *Nugget Spawn (120ms):* 3-5 resource nuggets pop upward in a parabolic fountain, bouncing on terrain.
  5. *Magnet Pull (600ms):* Nuggets activate homing trajectory toward player vacuum radius.
  6. *Inventory Slotting (850ms):* Nuggets reach player; soft chime plays, hotbar icon pulses 15% scale with green flash.

- **Jump Gate Charge & Sector Warp Sequence:**
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

- **Sector Music & Environmental Ambience:**
  - *Sector 1 (Verdant Drift):* Bright, exploratory synth bed in C Major. Shimmering high-register arpeggios, gentle crystalline wind chimes, peaceful low sub-bass drone. Communicates fresh beginnings and accessible scavenging.
  - *Sector 2 (Ember Reach):* Warm, tense analog pads in A Minor. Mid-tempo industrial percussion pulse, low drone of stellar solar winds, strained harmonic resonance. Communicates industrial urgency and rising pressure.
  - *Sector 3 (Umbral Deep):* Cold, ominous void drone in D Minor. Irregular metallic groans, dissonant high-frequency shimmer, deep rhythmic heartbeat rhythm. Communicates lethal cosmic hostility and impending climax.
- **Alarm Taxonomy (Strict Sensory Separation):**
  - *Low Oxygen Alarm:* Rhythmic biological double-thud heartbeat (1.0 Hz, 80Hz center) coupled with player breathing and a gentle high-register vital monitor blip. Non-mechanical, purely physiological.
  - *Hull Critical Alarm (<25% HP):* High-urgency mechanical two-tone klaxon (780Hz alternating with 960Hz at 3.0 Hz cadence). Plays directly through cockpit speakers with an assertive stereo pan.
  - *Collapse Proximity Warning (<800m):* Ominous low-frequency seismic hum (35-55Hz) that vibrates continuously, punctuated by intermittent electrical crackle and rising metallic groaning.
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
  - *Stardust Meta Accrual:* Ascending multi-tonal crystal cascade (3 notes, 120ms total).

### Co-op Communication Affordances (Ping System)

- **Ping Taxonomy, Colors & Glyphs:**
  - **Look / Move Marker (core):** Slate White `#E2E8F0`. Four-point diamond pip. Audio: Neutral two-tone blip (900Hz). Used for general coordination and navigation waypoints.
  - **Enemy / Danger Threat (core):** Aggressive Red `#EF4444`. Inverted triangle with center exclamation pip. Audio: Sharp urgent double-beep (1100Hz / 1400Hz). Placed on hostile ships, spitters, or pirate ambushes.
  - **Loot / Resource Deposit (core):** Luminous Cyan `#38BDF8`. Hexagonal container glyph. Audio: Cheerful high-register chime (1300Hz). Placed on unmined ore nodes, loot crates, or dropped modules.
  - **Jump Gate / Mission Objective (core):** Solar Amber `#F59E0B`. Circular ring glyph with four ray pips. Audio: Resonant brassy ping (600Hz). Placed on the gate terminal or trader dock.
- **In-World Affordances:**
  - Placing a ping drops a vertical translucent light beacon column from the sky (0.5m radius, 12m height) lasting 6.0 seconds.
  - Screen-edge clamping: When a ping target is off-camera, the glyph clamps to the screen perimeter with an orienting directional arrow and live distance readout in meters.
  - Co-op player differentiation: Pings display the issuing player's color dot and initials above the glyph (P1 Blue, P2 Green, P3 Yellow, P4 Magenta).

---

## Technical Design References & Implementation Bounds

- **Reference Display:** 1920 x 1080 resolution, 16:9 aspect ratio. All pixel dimensions scale uniformly using reference-resolution canvas scaling.
- **Render Pipeline Scope:** Universal Render Pipeline (URP). Flat unlit or stylized low-specular PBR shaders. Post-processing stack strictly leverages Bloom, Tonemapping (ACES), and Chromatic Aberration/Vignette overrides for damage and threat states.
- **Network Awareness:** All gameplay feedback (weapon hit flashes, mining node shattering, shield break bursts) executes predictively on the local client with authoritative health confirmation from the server to guarantee instantaneous input feel under latency.
