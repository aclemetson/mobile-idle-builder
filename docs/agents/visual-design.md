# Visual Design — Structure Art Direction & Procedural Meshes

> Verified against: `feature/field-wire-mesh`, 2026-06-25.

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

## Extending to other buildings (future)

The collector spindle and the entropy-sink torus are the two worked examples of the house pattern.
To give another building type its own form, add a profile/builder following the `CollectorMeshBuilder`
/ `TorusMeshBuilder` pattern and branch in `BuildingVisualizer` Pass 2. A general per-building
procedural-structure system is intentionally out of scope until more building art direction is locked.
