# Feature: Manager Upgrades (star tiers)

**Status:** pending (next up after Managers, PR #63)
**Required reading:** `docs/agents/architecture.md`, `docs/agents/save-system.md`, `docs/agents/ui-toolkit.md`, `docs/agents/ecs-patterns.md` (multiplier gap map), plus the shipped Managers code: `Assets/Scripts/Services/ManagerService.cs`, `Assets/Scripts/ScriptableObjects/ManagerSO.cs`, `Assets/Scripts/UI/ManagersSubController.cs`.
**Scope estimate:** ~0 files created, ~6 modified, 1-2 test files.
**Branch:** `feature/manager-upgrades` -> PR into the current release branch (confirm with user).

## Design summary

Let a hired manager be upgraded through **star tiers** (1 star, 2 star, 3 star, ...) spending
**prestige currency (✦)**. Each star increases the manager's effective bonus. Star level is
per-manager, persists across prestige (like the hired roster), and scales the bonus value that
`ManagerService` bakes/reads at assignment time.

This builds directly on the Managers feature. The key constraint carried over: a CraftSpeed bonus is
baked into `BuildingData.ProductionSpeed`, so **upgrading the star of a currently-assigned manager must
re-bake the bonus** on its building (same hazard as the speed-upgrade site) -- reuse
`ManagerService.ReapplyAfterSpeedReset` / a new `ReapplyAssignmentFor(managerId)` path.

## Bonus scaling (decide final numbers before coding)

Per-star multiplier applied on top of the base `bonusValue`. Proposed starting model (tune against
`economy-balance.md`):

- Star 1 = base value (as shipped).
- Each additional star adds a fixed increment to the *effect*, NOT a re-multiply, so values stay
  legible. Examples:
  - CraftSpeed: 1.25x (1 star) -> 1.45x -> 1.65x ... (+0.20 per star).
  - OutputQuantity: 2x -> 3x -> 4x (+1 per star) -- already integer-friendly.
  - PowerDiscount: 0.8 (kept) -> 0.7 -> 0.6 (-0.10 kept-fraction per star).
- Max star: 5 (final number TBD).
- Cost per star: scales in ✦, increasing per tier (e.g. base hire cost x star index, or a curve from
  the balance doc). Define a `starCosts[]` per manager or a shared formula.

Decide whether scaling lives in JSON (`game_data.json` -> `ManagerSO`) or as a runtime formula in
`ManagerService`. JSON is consistent with the rest of the data pipeline; a formula is less data churn.

## Save additions (`SaveData.cs` -- additive; survive prestige)

The hired roster is currently `List<string> hiredManagers`. Star levels need a per-manager int. Options:
- Add `List<ManagerStarEntry> managerStars` (`{ string managerId; int stars; }`), defaulting missing
  entries to 1 star; OR
- Replace/augment `hiredManagers` with a struct list carrying stars. Prefer the additive
  `managerStars` list to keep legacy loads trivial (missing -> 1 star).

## Files to modify

| Path | Integration point |
|---|---|
| `Assets/Data/game_data.json` + `Editor/GameDataModel.cs` + `Editor/GameDataImporter.cs` | if scaling/costs are data-driven: per-manager star table |
| `Assets/Scripts/ScriptableObjects/ManagerSO.cs` | star scaling fields (if data-driven) |
| `Assets/Scripts/Services/ManagerService.cs` | `GetStars(id)`, `UpgradeStar(id)` (deduct ✦), star-scaled `EffectiveBonusValue(id)`; bake/idle paths read the scaled value; re-bake assigned building on star upgrade; load/flush `managerStars` |
| `Assets/Scripts/SaveSystem/SaveData.cs` | `managerStars` additive field + entry class |
| `Assets/Scripts/UI/ManagersSubController.cs` | show star count; "Upgrade ★ (cost ✦)" button |
| `Assets/Scripts/UI/HUDBuildingInspectorSubController.cs` | inspector manager row reflects star-scaled bonus text |

## Implementation order

1. SaveData `managerStars` + legacy-load test (missing -> 1 star).
2. `ManagerService` star state + `EffectiveBonusValue` + re-bake-on-upgrade; EditMode test that an
   assigned CraftSpeed manager's building speed changes exactly when its star is upgraded.
3. Scaling/cost source (JSON or formula) + schema/balance test.
4. UI: star display + upgrade button in Managers panel; inspector text.
5. Full suite, manual check (hire -> assign -> upgrade star -> verify live + idle), commit + PR.

## Test plan

- Upgrading a star on an assigned CraftSpeed manager re-bakes exactly (no drift); unassign restores base.
- OutputQuantity star increase reflected in `ProductionSystem`/`CollectorSystem` deposit and idle snapshot.
- Star level survives prestige and save/load; legacy save (no `managerStars`) loads as all 1 star.
- Upgrade rejected when ✦ insufficient or at max star.

## Pitfalls

- Same as Managers: re-bake CraftSpeed on any change to the effective value, or the live building keeps
  the stale speed. Route every value read through one `EffectiveBonusValue(id)` so bake + idle + UI agree.
- PowerDiscount stars are still inert until `PowerGridSystem` gains a per-building eV draw (see gap map).

## Out of scope

XP/auto-leveling, rarity/gacha, crystal-priced upgrades, per-building (rather than per-manager) stars,
respec/refund.
