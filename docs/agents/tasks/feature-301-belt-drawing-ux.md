# Feature #301: belt drawing UX (feedback, then router cost)

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ui-toolkit.md`, `testing.md`. Repro notes are in
`Playtests/reports/2026-10-31-first-session.md` (:41-57) and `2026-10-31-active-grinder.md` (:83-104).
**Status:** 301a done (PR pending); 301b pending (integration/2026-11-07).
**Size:** L, as **two PRs**:
- **301a:** `fix/301-belt-feedback`;
- **301b:** `feat/301-router-cost`, done after 301a merges.
**Out of scope:** belt crossings. That's a new mechanic (bridge or underpass) and gets its own issue.

## Facts (verified 2026-11-07)
- **Controller:** `ConveyorPlacementController` (`Assets/Scripts/Grid/ConveyorPlacementController.cs`).
- **`HandleTap(Vector2Int cell)`** (~:271):
  - Destroy mode removes a belt.
  - Create mode with no start: `if (!IsValidEndpoint(cell)) return;` is a **silent** refusal (~:291). It covers a
    start tile that already holds a belt, or a blocked one.
  - Tapping the start again when it's already a belt is a **silent** no-op (~:301-304).
  - A later `!IsValidEndpoint(cell)` is also silent (~:307).
- **`ConfirmPath()`** (~:163-189):
  - places the chain;
  - then **auto-chains**: `_startCell = _destCell; MarkStart();`.
  - The only way out is `ResetStart()` (the "New" button, ~:196). The next tap after a confirm silently extends the
    old chain.
- **Router:** `RecomputePath` (~:346) tries `ConveyorPathfinder.BuildOneTurnPath` with both elbows, then falls back to
  `ConveyorPathfinder.FindRoute(start, end, isCellFree)` (`Assets/Scripts/Grid/ConveyorPathfinder.cs` ~:78).
  - That is Dijkstra over (cell, incoming dir) with `TurnCost = 1_000_000`, `StepCost = 1` (~:30-31), so it
    minimises turns at **any** length. That's why it takes long loops around buildings.
  - `IsFreecell` (~:435) treats every belt and building as blocked.
- **"Belt not linked":** a toast from `TutorialOverlayController` (`UnlinkedBeltMessage` ~:428,
  `WarnIfBeltUnlinked` ~:475). It is judged only when the chain ends.
- **Glow colours** (`Assets/Scripts/Grid/GridRenderer.cs` ~:26-40):
  - `ConveyorGhostColor`: orange, the previewed path;
  - `ConveyorEndpointColor`: green, start and end;
  - `tutorialHighlightColor`: amber, the tutorial guide;
  - `tutorialHoverColor`.
  - No legend exists.
- **Toast:** `InventoryPopupController.NotifyWarning(string)`. Keep messages under ~45 chars.

## 301a: feedback, no routing change
1. **Refused taps say why.** Replace the silent returns in `HandleTap` with a toast:
   - start on an existing belt: "Start on a free tile or a building output";
   - blocked end: "Can't end there".
   - Add a pure `static string RefusalReason(...)` if the reasons need to be told apart, built from the same checks
     as `IsValidEndpoint`.
   - Rate-limit: no more than one identical toast per 1.5 s.
2. **Chain continuation is visible.** After the auto-chain in `ConfirmPath`, give the start a distinct
   "continuing" marker.
   - Pulse the endpoint tile: grep `GridRenderer` for an existing pulse or highlight; reuse the tutorial pulse if
     there is one.
   - Show a one-time toast per conveyor-mode session: "Continuing from the last belt. Tap New to start elsewhere."
3. **Blocking belts are highlighted.** When `RecomputePath` finds no route, or the route `FindRoute` returns is much
   longer than the Manhattan distance (> 2×), tint the existing belt cells that sit on the straight L between start
   and end with a red-ish "blocking" colour. These are the cells `BuildOneTurnPath` would have used. Clear the tint
   on the next recompute or on exit.
4. **Glow legend.** Add a small legend to the belt overlay bar in `GameHUD.uxml`. **Grep which UXML and USS the belt
   overlay actually loads before editing**, and check the parent isn't zero-height.
   - Use three swatches with labels: "Start / end" (green), "Path preview" (orange), "Guide" (amber).
   - Read the colours from `GridRenderer` through a public static accessor, or duplicate them in USS with a comment
     pointing to `GridRenderer` fields.
5. **Tests:**
   - `RefusalReason` cases;
   - the toast rate limiter, as a pure class with an injected clock (copy `DrawerTapGuard`'s clock pattern).

## 301b: router cost (after 301a merges)
1. **Cost function in `FindRoute`.** Turns should cost a few tiles, not infinity:
   - `StepCost = 10`, `TurnCost = 30`, so a turn is about 3 extra tiles;
   - add an optional `Func<int,int,int> extraCost` parameter, default none.
2. **Extra cost from `RecomputePath`:**
   - **+60** for a cell that is the front tile of an unused building **input or output port**, so it doesn't block a
     port a later belt needs. This is the first-session Hydrogen-around-Atom-Generator case.
   - **+5** for a cell next to any building, a mild push away from hugging buildings.
   - The start and end cells cost nothing extra.
   - Port fronts: grep how `ConveyorPlacementController` or the tutorial route guide (`route:` highlight targets)
     compute building port front cells, and reuse that.
3. **Keep `BuildOneTurnPath` first,** but also run `FindRoute` and keep the L only when its total cost, including the
   extra cost, is ≤ `FindRoute`'s.
4. **Performance:** grids are small, but check that `FindRoute` stays bounded. Keep the existing `MinHeap`; just
   watch the state count.
5. **Tests** (EditMode, new `ConveyorPathfinderCostTests.cs`; check for an existing pathfinder test file first and
   extend it):
   - an open grid gives a straight or L path, same as before;
   - a 3x3 obstacle: the route goes around with the fewest extra tiles, with no long loop when a 2-turn path exists;
   - a port-front cell next to the direct path is avoided when a detour of ≤ 3 tiles exists;
   - an unreachable end returns null.

## Done when
- **301a:** refused taps explain themselves, the continuing-from-here marker is obvious, blocking belts are
  highlighted, and the legend is visible. Check with a screenshot in the Editor.
- **301b:** the first-session route (`route_neutrons_to_atom_gen`) no longer has to work around a looped Hydrogen
  belt. The pathfinder tests pass.
- The full EditMode suite passes for each PR. Set Status to done with both PR numbers.
