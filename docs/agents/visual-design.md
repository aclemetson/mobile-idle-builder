# Visual Design — Structure Art Direction & Procedural Meshes

> Verified against: `feature/field-wire-mesh`, 2026-06-25.
> Port holes + Atom Generator structure + per-building drafts added 2026-06-27.

How buildings and field structures are rendered, and the art direction they should follow.
Read this when touching structure visuals, procedural geometry, or shaders.

## Art direction

Structures should feel **fluid and woven into the fabric of the universe — not alien, not
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
   (`BuildingVisualizer`), conveyors (`ConveyorVisualizer`), port arrows (`OutputArrow`).
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

## Reference implementations

### Field wire-mesh (tappable resource fields)
- `Assets/Scripts/Gameplay/FieldWireMeshBuilder.cs` — line-topology grid mesh.
- `Assets/Scripts/Gameplay/FieldWireMesh.cs` — MonoBehaviour; tap bounce.
- `Assets/Scripts/Gameplay/WireBounce.cs` — pure exponential-decay envelope (reusable).
- `Assets/Shaders/FieldWireMesh.shader` — URP Unlit, `_Time`-driven noise + radial bounce.

### Field-collector "spindle" structure (the Harvester building)
A round bulb tapering to a single sharp apex, rounded on every side **except a flat front
facade** that carries a glowing emission aperture. Replaces the placeholder cube for **any
building carrying `CollectorData`** (data-driven — no hardcoded id).

- `Assets/Scripts/Gameplay/CollectorMeshBuilder.cs` — surface-of-revolution builder. The
  `SpindleProfile` (`Height`, `BulbRadius`, `BaseRadius`, `ApexSharpness`, `FrontFlattenFrac`)
  and `Radius(u, p)` define the silhouette; the top collapses to **one shared apex vertex**
  (the sharp point); the **front (+Z) is sliced flat** at `FrontFlatZ(p)` so the building has a
  clear "front"; `uv.y` runs 0 (base) → 1 (apex) so the shader can glow the tip.
- `Assets/Scripts/Gameplay/ApertureMeshBuilder.cs` — `BuildArchDoor(width, height, segs)`: a solid,
  double-sided filled "doorway" (rectangle + semicircular arch top) on the flat front face, coloured
  solid black so it reads as a hole the particles stream out of.
- `Assets/Scripts/Gameplay/CollectorStructure.cs` — MonoBehaviour. Builds the spindle + aperture
  + a forward-firing particle jet, tints everything to the field colour, and orients the front to
  the building's **output direction** (`OutputDirectionData.Direction`, enum × 90° on Y; defaults
  to South when unset). Polls `CollectorData.Timer`: `CollectorSystem` subtracts the production
  interval on each deposit, so a frame-over-frame **drop** in the timer means an item was produced
  → fire a decaying `_PulseAmp` flare (reuses `WireBounce`) **and** `Emit()` a particle burst out
  the hole. The aperture particle material reuses `FieldGenerator.ParticleMaterialTemplate` (the
  Android-safe additive field material).
- `Assets/Shaders/CollectorStructure.shader` — URP Unlit, shading only: body emission + apex tip
  glow ramped by `uv.y` (`_TipColor` = brightened field colour), `_PulseAmp` spikes the flare on
  each harvest. `_BaseColor` is left for `PresenceReceiver` to drive. The bob/breathe/pulse **motion
  is on the host transform** (`CollectorStructure.AnimateTransform`), NOT the shader — so the child
  door + particles inherit it and stay locked to the surface (a shader-side bob would slide the
  surface over the stationary door and make the hole clip in and out).
- `Assets/Scripts/Tests/CollectorMeshBuilderTests.cs` — EditMode invariants (counts, single apex
  on-axis, flattened front / round back, bounds, unit normals).

Wiring: `BuildingVisualizer.Refresh()` Pass 2 detects collectors via
`em.HasComponent<CollectorData>(entity)`, sits the host on the grid plane (`localPosition.y = 0`,
uniform `cellSize` scale), and attaches `CollectorStructure`. The collector's **base colour is the
field colour** (`FieldGenerator.GetFieldAt(x,y).fieldColor`) instead of white; `BuildingVisualizer`
now tracks a per-cell `_baseColors` map so power/hover tints restore to the field colour, not white.
On **load**, a collector's visual can be created before its field has spawned from save, so
`GetFieldAt` returns null; those cells go into `_pendingFieldColor` and `ResolvePendingFieldColors()`
(called from `Update`) applies the field colour to the body + `CollectorStructure.SetFieldColor` once
the field appears (giving up after 10s).

### Entropy-sink "torus" structure (Maxwell's Demon)
A flat gold donut that fills the 3x3 footprint, surfaced with swirling gold patterns that a C#
script crossfades between at random. Replaces the placeholder cube for **any building carrying
`EntropySinkTag`** (the Maxwell's Demon marker — data-driven, no hardcoded id).

- `Assets/Scripts/Gameplay/TorusMeshBuilder.cs` — surface-of-revolution builder: a small circular
  tube (`MinorRadius`) swept around the Y axis at `MajorRadius`, so the ring lies **flat** in XZ
  with its hole facing up. `Default` outer diameter ≈ 1 local unit (host is scaled to fill 3x3).
  Seam column **and** row are duplicated for a clean wrap; `uv.x` runs around the ring, `uv.y` around
  the tube — these drive the shader patterns.
- `Assets/Scripts/Gameplay/EntropySinkStructure.cs` — MonoBehaviour. Builds the torus, gold material,
  and is the **random pattern swapper**: holds a pattern for a random dwell (3–6 s), then crossfades
  (`_Blend` 0→1) to a new random pattern and repeats; only writes `_Blend` while fading (idle
  otherwise). Also turns the ring slowly on Y. The per-pixel swirl itself is `_Time`-driven in the
  shader.
- `Assets/Shaders/EntropySinkTorus.shader` — URP Unlit, gold `_BaseColor` (left for `PresenceReceiver`
  to drive) + `_AccentColor` highlights tracing one of **5 procedural patterns** (spiral stripes,
  radial bands, woven checker, sunburst, drifting noise) selected by `_PatternA`/`_PatternB` and
  crossfaded by `_Blend`; `_SwirlSpeed` scrolls them via `_Time`. No textures (mobile safe).
- `Assets/Scripts/Tests/TorusMeshBuilderTests.cs` — EditMode invariants (vertex/triangle counts,
  central hole exists, flat low profile, bounds, unit normals).

Wiring: `BuildingVisualizer.Refresh()` Pass 2 detects sinks via `em.HasComponent<EntropySinkTag>(entity)`,
sits the host on the grid plane (`localPosition.y = 0`) scaled to the full footprint, gives it a **gold
base colour** (so power/hover tints restore to gold, not white), and attaches `EntropySinkStructure`.

Two sink-specific deviations from the normal building visuals:
- **No port arrows.** Pass 3 skips arrow creation for the sink (the input ports still exist in ECS for
  conveyor deposits); bespoke input visuals are planned later.
- **Selection-only grid highlight.** Instead of permanently colouring its footprint tiles like every
  other building, the sink's anchor is added to `_deferHighlight` and its tiles light up only while it is
  selected. `BuildingInspectorController` drives this via `BuildingVisualizer.SelectBuilding(x,y)` /
  `DeselectBuilding()`, the latter also hooked to `MaxwellsDemonController.OnClosed` so the X button keeps
  the highlight in sync. `SelectBuilding`/`DeselectBuilding` are no-ops for non-deferred buildings.

### Atom Generator "orbital nucleus" structure (the Atom Generator building)
A round glowing nucleus circled by tilted electron orbit rings, each carrying a bright electron that
travels around it. The body is **round on every side** (no flat front facade) — its holes are placed
as separate fixtures at the port edges (see "Port holes" below), so the nucleus stays a clean atom.
Selected **data-drivenly** by `BuildingStructureKind.AtomGenerator` (see "Choosing a structure" below),
not a hardcoded id.

- `Assets/Scripts/Gameplay/AtomNucleusMeshBuilder.cs` — surface-of-revolution builder. An ellipse
  silhouette (perfect round dome) peaking at the equator, with a small planted base floor; the top
  collapses to **one shared apex vertex**; `uv.y` runs 0 (base) → 1 (apex) so the shader can glow the
  core + tip. Unlike `CollectorMeshBuilder` it does **not** flatten the front.
- `Assets/Scripts/Gameplay/AtomGeneratorStructure.cs` — MonoBehaviour. Builds the nucleus + 3 tilted
  electron orbits (thin `TorusMeshBuilder` rings; the whole pivot spins about its own normal so the
  symmetric ring looks fixed while the attached electron orbits) + slow breathe motion. Polls
  `RecipeProcessData.Progress`: `ProductionSystem` resets it to 0 on completion, so a frame-over-frame
  **drop** means an atom was assembled → fire a decaying `_PulseAmp` flare (reuses `WireBounce`).
- `Assets/Shaders/AtomGenerator.shader` — URP Unlit, shading only: body emission + a hot nucleus core
  glowing brightest at the equator and apex, `_PulseAmp` flares it on each assembly. `_BaseColor` is
  left for `PresenceReceiver` to drive (warm gold base).
- `Assets/Scripts/Tests/AtomNucleusMeshBuilderTests.cs` — EditMode invariants (counts, single apex
  on-axis, **round** cross-section, bounds, unit normals).

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
  `local_facing: "West"`, etc. See the `_note_ports` on `atomic_assembler`.)

**Mechanism:** `BuildingVisualizer.Refresh()` Pass 3 already iterates `PlacedPortData` per building to
draw the port arrows; for any building carrying `BuildingVisualStyle` it also spawns a hole fixture per
port (`CreateHoleFixture`) at the resolved edge, rotated so the fixture's local +Z faces outward. The
arch-door / intake meshes and their materials are **shared singletons** built once at the cell size
(`EnsureHoleAssets`) so holes are allocation-free; they're tracked in `_spawnedPortArrows` for cleanup.

**Future polish (not yet done):** holes are tinted by a generic output-warm / input-cool scheme.
`PlacedPortData` does not carry which input *slot* (item) a port feeds, so per-item tinting (proton =
gold, neutron = grey, electron = blue, as in the drafts below) needs a port→slot mapping first.

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

- **Basic Generator — "energy spire"** (`BasicGeneratorStructure`, Power, no holes): tall thin round
  spindle (`CollectorMeshBuilder`, FrontFlattenFrac=1) tapering to a crackling tip; electric cyan; a
  ~1.5s heartbeat flares the tip and emits expanding, fading flat light-rings (`TorusMeshBuilder`) on the
  ground — power broadcasting. Not a producer, so the pulse is time-driven.
- **Strong Force Combiner** (`StrongForceCombinerStructure`, 2×1): a soft **rounded rectangular prism**
  (`RoundedBoxMeshBuilder` — a superellipsoid dome that fills the footprint, Roundness 0.5) flowing from
  its two inlet faces to its single outlet face, doors flush on the flat faces. A binding glow builds
  with craft progress, then flares when a nucleon is forged. (An earlier orbiting-quark "knot" was
  dropped — it read too flat.)
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
nucleus bulb. Pick/compose these for any future building rather than adding one-off builders.
