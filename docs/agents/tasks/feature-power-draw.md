# Feature: Per-Building Power Draw (make PowerGridSystem real)

**Status:** pending
**Required reading:** `docs/agents/architecture.md`, `docs/agents/ecs-patterns.md` (multiplier gap map + system anatomy), `docs/agents/economy-balance.md` (eV / power economy), `docs/agents/save-system.md` (if any new persisted field). Plus current code: `Assets/Scripts/Systems/PowerGridSystem.cs`, `Assets/Scripts/Components/PowerNodeData.cs`, `Assets/Scripts/Systems/ProductionSystem.cs`.
**Scope estimate:** M–L. New per-building draw model + system logic + production gating + UI; ~4 files created, ~6 modified, 2 test files.
**Branch:** `feature/power-draw` -> PR into the current release branch (confirm with user).

## Why

`PowerGridSystem` is a **clamp-only placeholder** (`PowerGridSystem.cs:22-31`): it just bounds each
`PowerNodeData.CurrentEV` to `[0, MaxEV]`. There is **no per-building eV consumption** anywhere —
`BuildingData` has no power field, nothing draws eV, and production never checks supply. The power bar
(`HUDStatusBarController.cs:138-139`) sums node supply but consumption is always effectively zero.

Direct consequence (the trigger for this task): the **Managers `PowerDiscount` bonus is inert**. The
`ManagerAssignmentData.AppliedPowerMult` value (and its star-tier scaling from PR #65) is baked onto
building entities but **never read** because there is no draw to discount. Same for any future
power-cost balancing. This feature turns that parked multiplier into a live effect.

## Design summary (decide details before coding)

1. **Per-building draw.** Add an eV-draw figure per building — either a field on `BuildingData`
   (`EVDraw`) sourced from `BuildingSO` (data-pipeline: new `ev_draw` in `game_data.json` ->
   `BuildingSO`, baked at placement in `BuildingPlacer.cs:69` region), or a dedicated component.
   Active/crafting buildings draw; idle ones draw less or nothing (decide).
2. **Aggregate + gate.** `PowerGridSystem` (rename intent: it becomes the real grid) sums generation
   (`PowerNodeData`) vs. total draw, and exposes available power. When demand > supply, gate or throttle
   production — coordinate with `ProductionSystem` (it runs `[UpdateBefore]`/`[UpdateAfter]` already;
   `PowerGridSystem` is `[UpdateAfter(typeof(ProductionSystem))]` today, may need reordering).
3. **Apply the manager discount.** Each building's effective draw = `EVDraw × AppliedPowerMult` when
   `HasComponent<ManagerAssignmentData>` (gated, like `AppliedOutputMult` in `ProductionSystem`).
4. **Idle/offline.** Decide whether power constrains offline earnings (`GridSaveService.RebuildIdleSnapshot`)
   or is live-only. Keep consistent with how OutputQuantity is handled.

## Files (indicative)

| Path | Change |
|---|---|
| `Assets/Data/game_data.json` + `Editor/GameDataModel.cs` + `Editor/GameDataImporter.cs` | `ev_draw` per building |
| `Assets/Scripts/ScriptableObjects/BuildingSO.cs` | `evDraw` field (+ per-level if needed) |
| `Assets/Scripts/Components/BuildingData.cs` (or new component) | `EVDraw` |
| `Assets/Scripts/Gameplay/BuildingPlacer.cs` | set draw at placement (`:69` region) |
| `Assets/Scripts/Systems/PowerGridSystem.cs` | real aggregate/gate/consume logic; apply `AppliedPowerMult` |
| `Assets/Scripts/Systems/ProductionSystem.cs` | gate/throttle craft on available power |
| `Assets/Scripts/UI/HUDStatusBarController.cs` | show real draw vs. supply |
| `Assets/Scripts/Tests/PowerGridSystemTests.cs` | extend beyond clamp: draw aggregation, under-supply gating, PowerDiscount applied |

## Test plan

- Total draw = sum of active building draws; gating triggers when draw > supply.
- A `PowerDiscount` manager (and a starred one) reduces its building's draw by exactly `AppliedPowerMult`.
- Removing/unassigning the manager restores full draw.
- Clamp behaviour from the existing `PowerGridSystemTests` still passes.

## Pitfalls

- Burst: `PowerGridSystem` is `[BurstCompile]` — no managed calls inside. Read `ManagerAssignmentData`
  via `SystemAPI.HasComponent`/`GetComponent`, not `ManagerService`.
- Don't break the `PowerGridSystem` clamp contract the current tests assert; extend, don't replace.
- Coordinate update order with `ProductionSystem` so power is evaluated against the same frame's craft state.
- Anything newly persisted must be covered by `ECSLoadBridge.FlushToSave()` (see save-system.md).

## Out of scope (unless expanded)

Power line/adjacency routing between nodes and buildings, brownout visual FX, battery/storage buildings,
power as a tradeable resource. Keep v1 to a single aggregate grid (supply pool vs. total draw).

## Follow-on it unblocks

Removes the "PowerDiscount inert" caveat from Managers (`feature-managers.md`) and Manager Upgrades
(`feature-manager-upgrades.md`, PR #65); update both + the `ecs-patterns.md` gap map when this lands.
