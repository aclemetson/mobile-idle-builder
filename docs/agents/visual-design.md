# Visual Design — Structure Art Direction & Procedural Meshes

> Verified against: `feature/field-wire-mesh`, 2026-06-25.
> Port holes + Atom Generator structure + per-building drafts added 2026-06-27.
> Element tile icons added 2026-07-02.
> Power Relay "broadcast pylon" structure + placement radius ring/highlight added 2026-07-03.
> Belt Splitter structure + selection-only tile highlight added 2026-11-08; tutorial buildings redesigned ("the physics made visible") 2026-11-08.

How buildings and field structures are rendered, and the art direction they should follow.
Read this when touching structure visuals, procedural geometry, or shaders.

## Art direction

**A building's shape shows the process it performs** ("the physics made visible"): a Fusion Reactor is a star
in a magnetic bottle, Maxwell's Demon is the thought experiment itself. Structures should feel **fluid and woven into the fabric of the universe — not alien, not
mechanical boxes**. The visual language is **round bodies that taper to sharp points
(singularities)**. Geometry is organic and continuous rather than faceted or hard-edged;
motion is slow and alive (drifting, breathing, glowing) rather than mechanical.

Concretely:
- Prefer **surfaces of revolution** (a 2D silhouette curve revolved around the vertical
  axis) for primary structures — they give naturally round forms that can ease to a fine point.
- A glowing apex/tip reads as the "singularity" and is the focal accent.
- Reserve hard cubes/primitives for placeholders only.

## Rendering model (how the game draws things)

All world geometry is **built in code at runtime** — there are no authored `.fbx` models or
building prefabs. Two patterns:

1. **Primitive composition** — `GameObject.CreatePrimitive` cubes/quads/spheres, scaled and
   colored. Used by grid tiles (`GridRenderer`), placeholder building cubes
   (`BuildingVisualizer`), conveyor item spheres (`ConveyorVisualizer`).
2. **Procedural mesh builders** — pure static classes that emit a `Mesh` from generated
   vertices/indices, paired with a thin MonoBehaviour and a custom URP shader. This is the
   house pattern for distinctive structures.

### Materials & Android safety
- Custom in-code materials are created from `Shader.Find("MobileIdleBuilder/<Name>")`.
- Every custom shader MUST be added to `ProjectSettings/GraphicsSettings.asset`
  `m_AlwaysIncludedShaders`, or variant stripping renders it **magenta on Android**.
- Shared URP materials for primitives come from `RenderingMaterials.Instance.Opaque/.Transparent`
  (`Assets/Scripts/Bootstrap/RenderingMaterials.cs`).
- Per-instance color goes through a `MaterialPropertyBlock` on `_BaseColor` (never
  `enableInstancing = true` on a material — it caused magenta stripping; see
  `fix-mobile-particle-magenta.md`).

## Item tile icons (2D menu sprites)

Every `ItemSO` has a **periodic-table tile sprite** used in the HUD menus (not the 3D
scene). These are 2D UI Toolkit sprites — no custom shader, so the Android magenta rules
above do not apply.

- **Generator:** `MobileIdleBuilder/Generate Element Icons` (`Assets/Scripts/Editor/ElementIconGenerator.cs`)
  renders one 256x256 PNG per item to `Assets/Art/Icons/Elements/{id}.png` and imports it as
  a Sprite. It renders TMP 3D text (`LiberationSans SDF`) over a procedurally-drawn rounded-rect
  backdrop via a temp orthographic camera + `RenderPipeline.SubmitRenderRequest` (the URP-correct
  on-demand render path in Unity 6).
- **Assignment:** `GameDataImporter` assigns the sprite to `ItemSO.icon` via a **convention
  fallback** — when an item's `icon_path` is still `"TODO"` it loads
  `Assets/Art/Icons/Elements/{id}.png` if present. So the JSON stays untouched and the icon
  survives re-imports. Run `MobileIdleBuilder/Import Game Data` after generating.
- **Layout:** atomic number (top-left, small) · symbol (centre, large bold) · atomic mass
  (below, small). Mass/number are shown only for elements/isotopes (`atomicNumber > 0`); a mass
  line distinguishes isotopes from their parent element. Particles/molecules/alloys/components
  show the symbol only.
- **Colour = nuclear creation regime**, mirroring `docs/gameplay_loop_data.js` `nuclear.regimes`
  so tiles match the docs periodic table. The border is the full regime colour; the interior is a
  muted tint (`Lerp(#0d1117, regime, 0.35)`) with white text for contrast:
  genesis `#d2a8ff` (Z1) · fusion `#3fb950` (Z2-26) · fission `#f0883e` (Z27-80) ·
  field `#58a6ff` (Z81-92) · breeding `#db61a2` (Z93-100) · synthesis `#e3b341` (Z101-118) ·
  non-element `#6e7681`.
- **Where they surface:** the Assembler recipe picker (`HUDBuildingInspectorSubController`), the
  manual recipe list + ingredient chips and the codex (`HUDController`). Bind via
  `HUDController.MakeItemIcon(sprite, ussClass)`; USS classes `.item-icon` / `.recipe-input-icon`
  / `.codex-icon` live in `Assets/UI/components.uss`. A null icon is skipped, never fatal.

### Blender-rendered molecule icons
Molecules built only from H, C, N and O use a 3D ball-and-stick render on the same grey tile instead of
a text tile. `scripts/molecule_icons/` holds the pipeline; re-run it to add or change one (add the item
to `molecules.json` first):
1. `fetch_structures.py` downloads PubChem 3D structures into `structures.json` and checks each formula.
2. `render_blender.py` runs inside Blender (5.2 tested) and writes `renders/<id>.png` (gitignored). It
   replaces all meshes in the open scene. It picks the camera angle with the least atom overlap.
3. `compose_icons.py` installs `Assets/Art/Icons/Elements/<id>.png`, lists the id in
   `Assets/Art/Icons/rendered_icons.txt`, and writes `renders/contact_sheet.png`. **Check the sheet at
   the UI sizes** (48/28/18): things that look fine at 256 can fail there.

- **`ElementIconGenerator` skips ids in `rendered_icons.txt`**, so regenerating can't overwrite them.
  `ItemIconTests` checks each listed id is a real item with a PNG.
- **Atom colours** match the element tiles: H `#d2a8ff` (genesis), C `#3fb950` (fusion). N `#2dd4bf` and
  O `#d9e45c` are deliberate departures, because fusion green covers all three of C, N and O.
- **Large molecules** set `hide_h` with thicker `chunk`/`bond_chunk`; long chains also set `diagonal`.
- **Representative structures** for generic items: `amino_acid` = glycine (the recipe's exact atoms),
  `polymer_precursor` = ethylene, `fatty_acid` = octanoic acid (C8; the recipe implies C16, which is a
  hairline at icon size).

## Reference implementations

### Conveyor "river" channel (belts)
Conveyors are flowing **water** on a flat dark **bed** (no rails/walls) — direction reads from the moving
current, not an arrow. Two stacked procedural layers per cell, both from one ribbon builder:
- `Assets/Scripts/Grid/ConveyorFlowMeshBuilder.cs` — pure static ribbon builder in cell-local XZ.
  `uv.y` runs 0 (inbound edge) → 1 (outbound edge) along the flow; `uv.x` runs 0→1 across the channel.
  Shapes from one primitive: **straight** (quad strip), **bend** (quadratic Bézier, centre as control),
  and **merge** (each inbound is drawn as a *full* edge→exit ribbon with `uv.y` 0→1 — the straight-through
  input a complete straight piece, every turning input a complete bend piece — all sharing the exit half,
  so they overlay and blend into one seamless outflow). A `widthFrac` arg builds the **floor**
  (`FloorWidthFrac` == `WaterWidthFrac`, at `FloorY`) under the inner **water** (`WaterWidthFrac`, `FlowY`).
- `Assets/Shaders/ConveyorFlow.shader` — URP Unlit **translucent** water (`Transparent+50`, above the
  transparent grid tiles or they overdraw it at some camera angles). Lengthwise streaks (functions of
  `uv.x`) drift downstream; every `uv.y` wave uses a whole number of cycles/cell so the flow is
  continuous across collinear edges. Soft (alpha-faded) banks.
- `Assets/Shaders/ConveyorChannel.shader` — **opaque**, flat dark bed (drawn at `Geometry`, writes depth so
  it sits over the grid tiles). Same footprint as the water; it only gives the translucent ribbon a base
  and occludes the grid tiles beneath (no rails or bolts).
- Both custom shaders are registered in `GraphicsSettings.asset` `m_AlwaysIncludedShaders` (else magenta
  on Android).
- `Assets/Scripts/Grid/ConveyorVisualizer.cs` — spawns a `Channel` (floor) + `Flow` (water) child per
  segment, sharing one material each (fall back to the opaque primitive material if a shader is missing).
  `RefreshAll()` rebuilds every belt after placement/removal (links and merge geometry can change across
  the whole graph); item spheres still float on the water. Inbound edges per cell come from neighbours
  whose `ExitDir` points back at the cell — the same geometry `ConveyorPlacer.RelinkAll` uses.
- `Assets/Scripts/Tests/ConveyorFlowMeshBuilderTests.cs` — EditMode invariants (straight/bend vertex
  counts, merge arms reaching each inbound + exit edge, monotonic flow UVs, flat low profile).

### Field wire-mesh (tappable resource fields)
- `Assets/Scripts/Gameplay/FieldWireMeshBuilder.cs` — line-topology grid mesh.
- `Assets/Scripts/Gameplay/FieldWireMesh.cs` — MonoBehaviour; tap bounce.
- `Assets/Scripts/Gameplay/WireBounce.cs` — pure exponential-decay envelope (reusable).
- `Assets/Shaders/FieldWireMesh.shader` — URP Unlit, `_Time`-driven noise + radial bounce.

### Field-collector "field drawn to a point" structure (the Harvester building)
A low cone with a long concave taper to a sharp tip. Rings on the ground contract towards it, a particle condenses at
the tip as the harvest timer fills, and each harvest flares it and fires a burst from a black doorway on the cone's flat
front. Used by **any building carrying `CollectorData`** (data-driven, no hardcoded id).

- `Assets/Scripts/Gameplay/CollectorMeshBuilder.cs`: surface-of-revolution builder, called with `CollectorStructure.ConeProfile`.
  The top collapses to one shared apex vertex; the **front (+Z) is sliced flat** at `FrontFlatZ(p)`.
- `Assets/Scripts/Gameplay/ApertureMeshBuilder.cs`: `BuildArchDoor(width, height, segs)`, the black doorway on the flat front.
- `Assets/Scripts/Gameplay/CollectorStructure.cs`: builds the cone, door, particle jet, `GroundRipples` and bead, tints them
  to the field colour, and orients the front to `OutputDirectionData.Direction` (enum x 90 degrees on Y; defaults to South).
  `CollectorSystem` subtracts the interval on each deposit, so a frame-over-frame **drop** in `CollectorData.Timer` means an
  item was produced. Pulse motion is on the host transform, not the shader, so the door and particles stay on the surface.
- `Assets/Shaders/CollectorStructure.shader`: URP Unlit, shading only (body emission + tip glow by `uv.y`, `_PulseAmp` flare).
- `Assets/Scripts/Tests/CollectorMeshBuilderTests.cs`: EditMode invariants.

Wiring: `BuildingVisualizer.Refresh()` Pass 2 detects collectors via `em.HasComponent<CollectorData>(entity)`. The base colour is
the field colour (`FieldGenerator.GetFieldAt(x,y).fieldColor`), tracked per cell in `_baseColors` so tints restore to it. On
**load** a collector can be created before its field has spawned, so such cells wait in `_pendingFieldColor` until
`ResolvePendingFieldColors()` (from `Update`) applies the colour, giving up after 10s.

### Nucleon Harvester "two wells, one nucleon" structure (the 2x1 collector)
The 2x1 harvester declares `structure_kind: NucleonHarvester`, which Pass 2 checks before the generic collector branch, so its
`CollectorData` still drives it. A cone on each cell draws on its field, and arcs from both tips feed a cluster of quarks between them; each
harvest flies a nucleon to the output door. It has no door of its own: Pass 3 draws one from the placed port (a low hole fixture), and
`NucleonHarvesterStructure.PortEdgeLocal` aims the flight at the same spot, so both follow rotation. The 1x1 door is centred because
its only cell is its centre; **any multi-cell collector must use its port, not its centre.** `NucleonHarvesterStructureTests` pins this.

### Entropy-sink "the thought experiment" structure (Maxwell's Demon)
Maxwell's thought experiment made literal: a gold ring holds two chambers split by a wall, with fast hot particles on one side
and slow cold ones on the other. A small demon rides a gate in the wall, which lifts on a timer to let one particle through,
warming it up. Used by **any building carrying `EntropySinkTag`** (data-driven, no hardcoded id).

- `Assets/Scripts/Gameplay/EntropySinkStructure.cs`: the ring (`TorusMeshBuilder`) is the host's mesh so it keeps the hover tint.
  Floors are `RevolveMeshBuilder.BuildSector` halves; the wall, gate, demon and particles are primitives moved on the CPU. It is
  also the **random pattern swapper** for the ring: it holds a pattern for 3-6 s, then crossfades `_Blend` 0 to 1. The gate is
  time-driven, not tied to real deposits.
- `Assets/Shaders/EntropySinkTorus.shader`: URP Unlit, `_BaseColor` (left for `PresenceReceiver`) + `_AccentColor` tracing one of
  **5 procedural patterns** selected by `_PatternA`/`_PatternB`, crossfaded by `_Blend`, scrolled by `_SwirlSpeed`. No textures.
  The Fusion Reactor reuses it for plasma.

Wiring: `BuildingVisualizer.Refresh()` Pass 2 detects sinks via `em.HasComponent<EntropySinkTag>(entity)` and scales the host to the
footprint, so one local unit spans it. **No port arrows:** Pass 3 skips them for the sink (the input ports still exist in ECS
for conveyor deposits); bespoke input visuals are planned later.

### Belt Splitter "river meeting a rock" structure
The belt's water forks around a teardrop rock with its point upstream; a 3-way uses two smaller rocks with a
channel between them. Selected by `BuildingStructureKind.BeltSplitter`. The water shows the flow, so Pass 3
draws no holes for it, though unconnected ports still get ground arrows.

- `Assets/Scripts/Grid/ConveyorFlowMeshBuilder.cs` `BuildFork`: each exit takes its own slice of the inbound
  edge and widens to a full belt by its exit, so branches never overlap. Overlapping full ribbons, like a merge,
  looked wrong here: the translucent water doubled up and the streaks crossed. Both ends match the neighbouring
  belts' width and `uv.x`.
- `Assets/Scripts/Gameplay/TeardropMeshBuilder.cs`: rock(s) as one mesh, `uv.y` = 1 at the tip and high along the crown.
- `Assets/Scripts/Gameplay/SplitterStructure.cs`: reuses the registered `CollectorStructure` shader and the belts'
  own materials (`ConveyorVisualizer.GetFlowMaterial` / `GetChannelMaterial`), so no new always-included shader. The
  tip flares when `SplitterState.NextFacing` changes. Host rotation points the tip upstream; the `River` child
  undoes it, because belt meshes are built in world directions and units.
- `Assets/Scripts/Tests/TeardropMeshBuilderTests.cs`: rock shape and fork coverage, slices, and side order.

### Atom Generator "an atom being built" structure (the Atom Generator building)
A nucleus you can see is made of separate protons and neutrons, clustered around a slim glowing core, circled by tilted electron
orbit rings that each carry a bright electron. Holes are separate fixtures at the port edges (see "Port holes" below). Selected
**data-drivenly** by `BuildingStructureKind.AtomGenerator` (see "Choosing a structure" below), not a hardcoded id.

- `Assets/Scripts/Gameplay/AtomNucleusMeshBuilder.cs`: surface-of-revolution builder for the core; the top collapses to one
  shared apex vertex; `uv.y` runs 0 (base) to 1 (apex).
- `Assets/Scripts/Gameplay/AtomGeneratorStructure.cs`: builds the core (`CoreProfile`), 14 nucleon spheres spread evenly over a
  sphere, and 3 tilted orbits (thin `TorusMeshBuilder` rings; each pivot spins about its normal so the ring looks fixed while its
  electron orbits). A **drop** in `RecipeProcessData.Progress` means an atom was assembled: it fires a decaying `_PulseAmp` flare
  (`WireBounce`) and bursts the cluster outward.
- `Assets/Shaders/AtomGenerator.shader`: URP Unlit, shading only; `_BaseColor` is left for `PresenceReceiver`.
- `Assets/Scripts/Tests/AtomNucleusMeshBuilderTests.cs`: EditMode invariants.

## Port arrows (on the ground)

`Assets/Scripts/Grid/PortGroundArrow.cs`: two flat chevrons on the tile in front of a port, gold pointing away for an output, blue
pointing in for an input, with the far chevron larger and dimmer so an input reads as a funnel and an output as a spray. Built in code
(`ChevronMeshBuilder`) with meshes shared across arrows, drawn just above belt water, with a dark outline for contrast on any
tile. The placement ghost uses the same arrows.

An arrow means **"this port still needs a belt"**. `PortArrowRules.ShouldShow` mirrors `ConveyorSystem`: an output feeds a belt that
**starts** on the front tile, an input is fed by a belt that **ends** there, and a belt only passing by connects nothing, so its
arrow stays on top of it. An arrow also hides when the tile is off the grid or holds a building. `BuildingVisualizer.RefreshPortArrows`
re-evaluates after every `ConveyorVisualizer.BeltsChanged` and `Refresh`, so deleting a belt brings its arrow back.
Maxwell's Demon has no arrows (Pass 3 skips it).

## Port holes (building input/output apertures)

Buildings show their I/O as physical holes placed at their **port edges**, so item flow reads at a
glance. Two distinct shapes (the grammar):

- **Output hole** = a lit, convex emission aperture — `ApertureMeshBuilder.BuildArchDoor` (the arched
  doorway the harvester already uses), tinted bright/warm so it reads as glowing.
- **Input hole** = a recessed, concave intake mouth — `ApertureMeshBuilder.BuildIntakeMouth` (a funnel
  receding to a dark throat), tinted dark/cool so it reads as a hole items are fed INTO.

**Where a hole sits (must match the conveyor contract in `ConveyorSystem.HasMatchingPort`):**
- **Output port:** hole on the `Facing` edge, opening outward (`Facing` = the direction it ejects).
- **Input port:** hole on the edge **opposite** `Facing`, opening outward toward the belt. `Input.Facing`
  is the *travel direction of incoming items*, so a south-facing input is fed from the building's north
  edge. (When authoring `ports` in `game_data.json`, to put an intake on the **East** edge use
  `local_facing: "West"`, etc. See the `_note_ports` on `atomic_assembler`.) The Atom Generator is 2x1 since #275 (four intakes, one output); `BuildingVisualizer` centres the nucleus on the footprint and grows it 25% per extra tile, since a round body can't stretch.

**Mechanism:** `BuildingVisualizer.Refresh()` Pass 3 already iterates `PlacedPortData` per building to
draw the ground arrows; for any building carrying `BuildingVisualStyle` it also spawns a hole fixture per
port (`CreateHoleFixture`) at the resolved edge, rotated so the fixture's local +Z faces outward. The
arch-door / intake meshes and their materials are **shared singletons** built once at the cell size
(`EnsureHoleAssets`) so holes are allocation-free; they're tracked in `_spawnedPortArrows` for cleanup.

**Future polish (not yet done):** holes are tinted by a generic output-warm / input-cool scheme.
`PlacedPortData` does not carry which input *slot* (item) a port feeds, so per-item tinting (proton =
gold, neutron = grey, electron = blue, as in the drafts below) needs a port→slot mapping first.

## Selection highlight

A building tints its grid tiles teal (`GridRenderer.occupiedColor`) **only while selected**: `BuildingVisualizer.SelectBuilding` / `DeselectBuilding`, driven by `BuildingInspectorController`. `GridRenderer.RestoreCell` paints teal from its selected set, not from `GridOccupancy`, so ghosts and power previews can't bring it back. The inspector's `Hidden` event (X button, back navigation, building gone) reaches the controller through `HUDController.BuildingInspectorHidden`, so the highlight can't stick.

## Choosing a structure (data-driven, no hardcoded ids)

`BuildingSO.structureKind` (enum `BuildingStructureKind`, sourced from `structure_kind` in
`game_data.json`) declares a building's bespoke form. `BuildingPlacer` copies it onto the entity as
`BuildingVisualStyle` at placement — and therefore on **load**, which re-runs `PlaceBuilding`.
`BuildingVisualizer` Pass 2 switches on it. Collectors and entropy sinks keep being detected by their
gameplay components (`CollectorData` / `EntropySinkTag`); `structureKind` covers the producer forms.
To add a new building form: write its mesh builder + shader + MonoBehaviour, add an enum case, set
`structure_kind` in the JSON, and add a Pass 2 branch.

## Per-building structures (all implemented)

Every building now has a bespoke procedural structure (file = `Assets/Scripts/Gameplay/<Name>Structure.cs`,
selected via `structureKind`). All follow the house language (solid rounded bodies with volume; slow,
alive motion; mobile-safe unlit shaders) and — except the two non-conveyor cases — carry the port-hole
door fixtures and a flare on the production tick (DROP in `RecipeProcessData.Progress`).

- **Basic Generator: "Tesla coil"** (`BasicGeneratorStructure`, Power, no holes): a stepped column (`RevolveMeshBuilder`, tinted cyan)
  wound with copper rings, topped by a toroidal crown and a glowing tip. A ~1.5s heartbeat flares the tip and strikes lightning
  (`LineRenderer`) from the crown to the ground, while `GroundRipples` spread outward: power broadcasting. Not a producer, so the
  pulse is time-driven.
- **Power Relay — "broadcast pylon"** (`PowerRelayStructure`, Power `1×2`, no holes): a tall smooth amber
  mast (`RoundedBoxMeshBuilder`, narrow, sized to the footprint) crowned by a horizontal spinning
  broadcast ring (`TorusMeshBuilder`), emitting **wider, slower** ground ripples than the generator.
  Deliberately distinct from the generator's thin cyan spire (taller, amber, prominent crown ring). A
  spreader, not a source — extends coverage but generates no eV; the pulse is time-driven. Reuses the
  registered `MobileIdleBuilder/CollectorStructure` shader (no new shader/GraphicsSettings entry).
- **Strong Force Combiner: "quark confinement"** (`StrongForceCombinerStructure`, 2x1): above a rounded housing
  (`RoundedBoxMeshBuilder`, tall enough for the door fixtures), three quarks in the three colour charges circle each other, joined
  by flux tubes (`StructureParts.StretchBetween`). With craft progress they spin faster and the tubes pull tighter, then a white
  nucleon flashes when one is forged.
- **Fusion Reactor: "star in a magnetic bottle"** (`FusionReactorStructure`, 1x1): a low plinth (host, so it takes the power tint), a
  plasma ring (`TorusMeshBuilder` + the `EntropySinkTorus` shader) inside six field coils, and a small star (`AtomNucleusMeshBuilder` +
  the `AtomGenerator` shader) that swells with craft progress and flares on fusion. Its port holes are scaled down and sit low
  (`CreateHoleFixture`'s `low`), because the plinth has little face to fill. `fission_reactor` is still a placeholder cube.
- **Isotopic Manipulator — "breathing nucleus"** (`IsotopicManipulatorStructure`, 1×1): the Atom
  Generator's body + shader, green-tinged, with a slow heavy breathing swell and one tilted equatorial
  ring whose grey neutrons drift radially IN and OUT (opposite phase) — slower by design.
- **Molecular Synthesizer — "bonded molecule"** (`MolecularSynthesizerStructure`, 1×1): a solid teal
  central bulb (`AtomNucleusMeshBuilder` + AtomGenerator shader) with two smaller satellite lobes fused
  to it; the cluster tumbles slowly; a bond flare on each synthesis. Central bulb on the host so it keeps
  power/hover tints.
- **Materials Forge — "crucible"** (`MaterialsForgeStructure`, 1×1): a rounded steel furnace block
  (`RoundedBoxMeshBuilder`) with an orange heat crown and a shimmering molten pool on top; on each forge a
  white-hot ingot rises from the pool and cools to steel, with a body flare.
- **Component Fabricator — "precision core"** (`ComponentFabricatorStructure`, 1×1, endgame): a sleek
  tall violet rounded body crowned by a slowly rotating halo ring (`TorusMeshBuilder`); a charging glow
  builds with progress, then a crystalline component assembles at the apex and rises with the strongest
  flare of the chain.
- **Radioactive Containment — "lead shell"** (`RadioactiveContainmentStructure`, 2×2, no holes): a sealed
  lead-grey dome (`RoundedBoxMeshBuilder` sized to footprint) ringed near the apex by a slow amber-green
  containment field (`TorusMeshBuilder`); irregular (randomised) decay flashes flare the shell and
  brighten the ring. Contains rather than emits.

`RoundedBoxMeshBuilder` (superellipsoid dome) is the reusable form for footprint-filling solid bodies;
`CollectorMeshBuilder` (with FrontFlattenFrac=1) gives round spindles; `AtomNucleusMeshBuilder` gives the
nucleus bulb; `RevolveMeshBuilder` turns any profile into a revolved part, or makes a flat sector. Pick/compose
these for any future building rather than adding one-off builders. `StructureParts` adds child parts (no colliders,
shadows or probes) and owns their meshes and materials; its `Shaded` material makes a sphere primitive read as lit from
above using an already registered shader. `GroundRipples` draws travelling rings on the ground.

## Placement power-radius preview

While a power building (generator or relay) is being positioned, the influence radius is shown as a flat
circular **ring** on the ground plus **lighting up the placed buildings inside it** (reverting those
outside) live as the ghost moves — replacing the old square blue tile wash. Two pieces:
- `Assets/Scripts/Grid/PlacementRadiusIndicator.cs` — a looped `LineRenderer` circle laid flat in XZ,
  created at runtime by `BuildingPlacementController` (no scene wiring, like the ghost arrow). `Show`/`Hide`.
- `BuildingVisualizer.HighlightBuildingsInRange` / `ClearRangeHighlight` — tint the in-range cubes'
  `PresenceReceiver` to a teal-green preview colour (reusing the `SetHover`/`RefreshPowerTint` colour path)
  and restore on placement/cancel. `RefreshPowerTint` stands down while the preview is active.
The set of lit buildings uses the shared `PowerCoverageMath.FootprintWithinRadius` (a building lights up
if any part of its footprint square overlaps the circular power area), so it equals what `PowerGridSystem`
will actually power and matches the drawn ring. `GridRenderer.ShowPowerCoverage` (the tile wash, still used
by the building inspector when a placed generator is selected) also routes through the same helper.
