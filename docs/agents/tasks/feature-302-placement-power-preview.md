# Feature #302: show power range while placing a powered building, warn when unpowered

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ecs-patterns.md`, `testing.md`. The power model is
summarised in `tasks/feature-power-draw.md`; read only its first section.
**Status:** done (PR pending, integration/2026-11-07). Port-front marking (step 6) deferred.
**Size:** M. **Branch:** `feat/302-placement-power-preview` from `integration/2026-11-07`.

## Problem
In the 2026-10-31 first-session playtest, a Neutron Combiner was placed outside every generator's range. Nothing
showed the range while placing, and nothing warned the player. The building stayed unpowered until a second
generator went down.

## Power rule (verified 2026-11-07, mirror it exactly)
- `PowerGridSystem` (`Assets/Scripts/Systems/PowerGridSystem.cs` ~:125-175) marks each power node's
  `PowerNodeData.IsGridLinked`.
- **Only linked nodes** cover consumers: generators and linked relays. A consumer is connected when its footprint
  is within `InfluenceRadius` of any linked node: `IsWithinAnyGenerator` uses `FootprintWithinRadius`.
- **Managed equivalent:** `PowerCoverageMath.FootprintWithinRadius(srcMinX, srcMinY, srcMaxX, srcMaxY, tgtMinX, tgtMinY, tgtMaxX, tgtMaxY, radius)`
  (`Assets/Scripts/Grid/PowerCoverageMath.cs` ~:31). It must stay in sync with the Burst copy; don't change either.
- **Placed nodes from managed code:** `PowerConnectionGraph.GatherFromEcs(EntityManager em, float cellSize)`
  (`Assets/Scripts/Grid/PowerConnectionGraph.cs` ~:98) returns `PowerNode { AnchorX, AnchorY, Width, Height, InfluenceRadius, IsGenerator, IsGridLinked, ... }`.
  It is already cached per world and called during placement previews.

## Facts
- **Ghost:** `BuildingPlacementController.UpdateGhostAt(Vector2Int cell)` (`Assets/Scripts/Grid/BuildingPlacementController.cs` ~:459).
  - The radius preview runs only for `_pending.building.isPowerSource` (~:466). The other branch calls
    `HideRadiusPreview()` (~:641).
  - Then it calls `gridRenderer.ShowGhost(x, y, w, h, valid)`.
- **Which buildings need power:** `BuildingSO.requiresPower` (`Assets/Scripts/ScriptableObjects/BuildingSO.cs` ~:61).
- **`GridRenderer`** (`Assets/Scripts/Grid/GridRenderer.cs`):
  - `ghostValidColor` and `ghostInvalidColor` (~:22-23);
  - `ShowGhost(x, y, w, h, isValid)` (~:188);
  - `ShowPowerCoverage(gridX, gridY, w, h, radius)` (~:345) tints the tiles covered by **one** source, replaces the
    previous highlight, and is never called;
  - `ClearPowerCoverage()`.
- **Confirm path:** `ConfirmPlacement(int x, int y, RecipeSO recipe)` (~:519) calls `buildingPlacer.PlaceBuilding(...)`,
  which returns a bool. `CancelPlacement()` (~:282).
- **Toast:** `InventoryPopupController.NotifyWarning(string msg)` (`Assets/Scripts/UI/InventoryPopupController.cs`
  ~:111). The banner doesn't wrap, so keep messages under ~45 chars.

## Implementation
1. **Pure helper:** add a new static class `PlacementPower` (`Assets/Scripts/Grid/PlacementPower.cs`):
   ```csharp
   /// True when the footprint (x, y, w, h) is inside the InfluenceRadius of any grid-linked node.
   public static bool IsFootprintPowered(IReadOnlyList<PowerConnectionGraph.PowerNode> nodes, int x, int y, int w, int h)
   ```
   - Only nodes with `IsGridLinked` count.
   - Use `FootprintWithinRadius` with the node rect as the source and the footprint as the target.
2. **Multi-source coverage:** in `GridRenderer`, add `ShowPowerCoverage(IReadOnlyList<PowerConnectionGraph.PowerNode> nodes)`.
   It clears once, then tints the union of the linked nodes' coverage. Refactor the single-source loop into a
   private `AddCoverageFor(...)` that both overloads use.
3. **Ghost tint:** in `GridRenderer`, add `[SerializeField] Color ghostUnpoweredColor` (amber, e.g.
   `new Color(1f, 0.7f, 0.1f, 0.55f)`, matching the alpha of the other ghost colours). Also add an overload
   `ShowGhost(x, y, w, h, bool isValid, bool isPowered)`: an invalid ghost stays red, a valid but unpowered one is
   amber, and a valid powered one is cyan.
4. **`UpdateGhostAt`:** add a branch for `_pending.building.requiresPower && !isPowerSource`.
   - Gather the nodes **once per placement session**: cache on entering placement and clear on cancel/confirm, because
     the nodes don't change while placing.
   - Call `gridRenderer.ShowPowerCoverage(nodes)`, then `ShowGhost(..., valid, PlacementPower.IsFootprintPowered(...))`.
   - Make sure every exit path calls `ClearPowerCoverage()`: `HideRadiusPreview`, `CancelPlacement` and after a confirm.
5. **Warning on confirm:** in `ConfirmPlacement`, after `PlaceBuilding` returns true for a `requiresPower` building
   whose footprint is unpowered, call `InventoryPopupController.NotifyWarning("Unpowered: place it in generator range")`.
6. **Port fronts on the generator ghost:** while placing a power source, mark the cells directly in front of
   placed buildings' ports so the player sees they'd block a port. If this needs more than ~40 lines, skip it and
   list it in the report.
7. **Tutorial check:** grep `game_data.json` `tutorial_steps` for a step that places a powered building
   (`place_neutron_combiner`). Placement must still succeed when unpowered (this only warns), so the tutorial flow is
   unchanged.

## Tests
- **EditMode, `PlacementPowerTests.cs`:**
  - powered inside the radius of a linked generator;
  - unpowered just outside it;
  - an unlinked relay in range does **not** power;
  - a 2x2 footprint partly overlapping the circle counts as powered (touching counts);
  - no nodes means unpowered.
  - Build `PowerNode` lists by hand.

## Done when
- Placing a Combiner shows the covered tiles, with an amber ghost outside range and a cyan one inside.
- Confirming an unpowered placement shows the warning.
- The coverage tint is gone after cancel or confirm.
- Generator placement looks the same as before, plus port-front marks if done.
- The tests pass and the full EditMode suite passes. Set Status to done with the PR number.
