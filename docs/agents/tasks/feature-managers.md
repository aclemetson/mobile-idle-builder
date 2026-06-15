# Feature: Managers (Idle Space Miner-style crew)

**Status:** done (PR #63, feature/managers -> release/0.3). Caveats: PowerDiscount is now LIVE via the proximity power grid (`feature-power-draw.md`) — `PowerGridSystem` reads `AppliedPowerMult` to reduce a consumer's eV draw; OutputQuantity wired into CollectorSystem too so idle==live; CraftSpeed affects crafters only (collectors ignore ProductionSpeed). Manual editor step: add ManagersSubController to the HUD GameObject in GameScene.
**Required reading:** `docs/agents/architecture.md`, `docs/agents/ecs-patterns.md` (especially the multiplier gap map), `docs/agents/save-system.md`, `docs/agents/ui-toolkit.md`, `docs/agents/data-pipeline.md`
**Scope estimate:** ~7 files created, ~8 modified, 3 test files.
**Branch:** `feature/managers` → PR into the current release branch (confirm with user).

## Design summary

Hireable managers, each with one passive bonus, assignable to ONE placed building at a time (one manager per building). Inspired by Idle Planet Miner's manager system (`docs/ipm-research.md`): managers are a prestige-currency sink and a per-building optimization puzzle. Hired managers and assignments **survive prestige** (buildings don't — assignments to destroyed buildings become unassigned but stay hired).

Bonus types (enum `ManagerBonusType`): `CraftSpeed` (× faster), `OutputQuantity` (chance-based extra output is OUT of scope — this is a flat multiplier requiring N× recipe output rounding down), `PowerDiscount` (building consumes less eV).

## ⚠ The multiplier decision (read before coding)

`ProductionSystem` does NOT read `PrestigeData.SpeedMultiplier` — live craft speed is solely `BuildingData.ProductionSpeed` (see gap map in `ecs-patterns.md`). **Decision for this feature: per-building bonuses bake into the building's components at assignment time** (option (a)) — do NOT add global singleton reads to ProductionSystem for this feature.

That means a CraftSpeed manager assignment multiplies `BuildingData.ProductionSpeed`, and you MUST apply it at every site that sets ProductionSpeed:
1. Placement: `Assets/Scripts/Gameplay/BuildingPlacer.cs:69` (`ProductionSpeed = BuildingSO.ProductionSpeedForLevel(...)`) — placement isn't assignment, so nothing extra here, but…
2. Upgrade: `Assets/Scripts/UI/HUDBuildingInspectorSubController.cs:303` resets `ProductionSpeed` from level — **this wipes a manager bonus unless re-applied here**.
3. Restore: `GridSaveService.LoadGrid()` re-places buildings via `BuildingPlacer.PlaceBuilding` (`GridSaveService.cs:256`) — re-apply assignments after grid load completes.
4. Idle: `GridSaveService.RebuildIdleSnapshot()` (`GridSaveService.cs:335`) bakes speed multipliers into offline chains — extend `getOutputRate` there so offline earnings see manager bonuses too.

Centralize: one method `ManagerService.ApplyBonusTo(Entity building)` / `RemoveBonusFrom(Entity building)`, called from all sites. Track applied state in a new `ManagerAssignmentData : IComponentData` on the building entity (managerIndex, applied multiplier) so removal divides back exactly what was multiplied.

## Balance numbers (final — do not derive your own)

8 managers; hire cost in prestige currency (✦). Per `economy-balance.md`: first prestige yields ~50–100✦, so tier-1 managers compete with cheap prestige-shop upgrades.

| id | name | bonus | value | hire cost ✦ |
|---|---|---|---|---|
| `mgr_tinker` | Tinker | CraftSpeed | 1.25× | 10 |
| `mgr_stoker` | Stoker | PowerDiscount | −20% eV | 10 |
| `mgr_packrat` | Packrat | OutputQuantity | 2× | 25 |
| `mgr_overclocker` | Overclocker | CraftSpeed | 1.5× | 40 |
| `mgr_conductor` | Conductor | PowerDiscount | −40% eV | 60 |
| `mgr_duplicator` | Duplicator | OutputQuantity | 3× | 150 |
| `mgr_chronomancer` | Chronomancer | CraftSpeed | 2× | 300 |
| `mgr_demiurge` | Demiurge | OutputQuantity | 4× | 800 |

## Data schema additions (`Assets/Data/game_data.json`)

New top-level `"managers"` array (after `"fields"`); bump `_meta.version`. Importer pass → `ManagerSO` assets in `Assets/Data/managers/` (copy ResearchSO import pattern; no cross-refs, any order after GameConfig). Add model classes to `Assets/Scripts/Editor/GameDataModel.cs`.

```json
"managers": [
  { "id": "mgr_tinker", "display_name": "Tinker",
    "description": "Assigned building crafts 25% faster.",
    "bonus_type": "CraftSpeed", "bonus_value": 1.25,
    "hire_cost_prestige": 10, "portrait_path": "TODO" }
]
```

## Save additions (`SaveData.cs` — additive; all survive prestige)

```csharp
public List<string> hiredManagers = new();                          // manager ids
public List<ManagerAssignmentEntry> managerAssignments = new();     // new [Serializable] class
// ManagerAssignmentEntry { public string managerId; public int buildingInstanceId; }
```

`buildingInstanceId`: buildings have no stable id across save/load today (`BuildingSaveData` is positional). Use the building's **grid anchor position** encoded as `x * 10000 + y` for assignment identity — positions are stable across save/restore because `LoadGrid` re-places at saved positions. Document this encoding in the entry class.

## Files to create

| Path | Copy pattern | Notes |
|---|---|---|
| `Assets/Scripts/ScriptableObjects/ManagerSO.cs` | `ResearchSO.cs` style | id, name, desc, bonusType enum, bonusValue, hireCost, portrait |
| `Assets/Scripts/Services/ManagerService.cs` | `Assets/Scripts/Services/PersistentUpgradeService.cs` | Hire (deduct `PrestigeData.PrestigeCurrency` via cached query like `PrestigeShopSubController.cs:137-153`), assign/unassign, `ApplyBonusTo`/`RemoveBonusFrom`, `LoadFromSave`/flush mirror of `PersistentUpgradeService` (`:224` region) |
| `Assets/Scripts/Components/ManagerAssignmentData.cs` | any component file | `{ int ManagerIndex; float AppliedSpeedMult; float AppliedOutputMult; float AppliedPowerMult; }` |
| `Assets/Scripts/UI/ManagersSubController.cs` | `PrestigeShopSubController.cs` | hire list + assignment UI |
| `managers-panel` in `Assets/UI/GameHUD.uxml` + `btn-managers` nav button | copy `upgrades-panel` (`:118`) | |
| `Assets/Scripts/Tests/ManagerServiceTests.cs` | `PremiumShopCalculatorTests.cs` + `ProductionSystemTests.cs` | bonus apply/remove exactness; upgrade re-apply |
| `Assets/Scripts/PlayModeTests/ManagerSaveTests.cs` | `SaveSystemTests.cs` | round-trip + legacy load + prestige survival |

## Files to modify

| Path | Integration point |
|---|---|
| `Assets/Data/game_data.json`, `Editor/GameDataModel.cs`, `Editor/GameDataImporter.cs` | schema + import (above) |
| `Assets/Scripts/SaveSystem/SaveData.cs` | fields above |
| `Assets/Scripts/UI/HUDBuildingInspectorSubController.cs` | `:303` — after `ProductionSpeed = ProductionSpeedForLevel(...)`, re-apply manager bonus; also add an "assign manager" row to the inspector |
| `Assets/Scripts/SaveSystem/GridSaveService.cs` | after `LoadGrid()` placement loop (`:256` region): `ManagerService.Instance?.ReapplyAllAssignments()`; in `RebuildIdleSnapshot` extend `getOutputRate` (`:328`) with per-building manager speed/output multiplier |
| `Assets/Scripts/Systems/PrestigeSystem.cs` | building destruction block (`:72-77`) destroys assigned buildings — assignments keep `managerId` but `ManagerService.ResetAssignments()` clears building links; call alongside `ResearchService.ResetAll()` (`:91`) |
| `Assets/Scripts/Systems/PowerGridSystem.cs` | PowerDiscount: building's eV draw × `ManagerAssignmentData.AppliedPowerMult` when component present |
| `Assets/Scripts/UI/HUDController.cs` | component field + nav button + `OpenManagersPanel()` |

## Implementation order

1. SaveData + entry class + legacy-load test.
2. JSON + model + importer + `ManagerSO`; run importer; schema test.
3. `ManagerAssignmentData` component + `ManagerService` core (hire/assign/apply/remove) + EditMode tests proving multiply-then-divide restores exact `ProductionSpeed`.
4. Re-apply hooks: building upgrade site, grid restore site, prestige reset. Test each.
5. Power discount in PowerGridSystem + test.
6. Idle snapshot integration + extend `OfflineCollectionServiceTests`.
7. UI (panel + inspector row) + HUD wiring.
8. Full suite, manual check, commit.

## Test plan

- Apply/remove is exact (no float drift): assign CraftSpeed 1.25 → speed ×1.25; unassign → original value (store original, don't divide if you can avoid it).
- Building upgrade with manager assigned keeps the bonus.
- Save → load: assignments restored onto re-placed buildings at the same positions.
- Prestige: hired list survives; assignments cleared; no orphan `ManagerAssignmentData`.
- Hire deducts ✦ and is rejected when unaffordable.

## Definition of done

- [ ] JSON parses; importer run; full suite green both platforms (`scripts/test-local.ps1`), results XML reported
- [ ] `ManagersSubController` component added to HUD GameObject in GameScene (manual editor step — flag to user)
- [ ] Panel + inspector assignment row visible and functional in editor play mode
- [ ] Legacy save loads; prestige round-trip manually verified (dev console: `show prestige`)
- [ ] Offline earnings reflect manager bonuses (check idle-return modal numbers after a forced background)

## Pitfalls

- The upgrade site (`HUDBuildingInspectorSubController.cs:303`) silently wiping bonuses is the most likely failure — write the regression test FIRST.
- Store the pre-bonus `ProductionSpeed` in `ManagerAssignmentData`, restore it on unassign — never divide floats to undo.
- `OutputQuantity` multiplies `RecipeOutputSlot.Quantity` handling in ProductionSystem deposit loop — do it via the assignment component on the building, gated by `HasComponent`, NOT a global query.
- One manager per building AND one building per manager — enforce both directions in `ManagerService.Assign`.

## Out of scope

Manager leveling/XP, rarity/gacha, portraits beyond `TODO` path, manager-specific dialogue, crystal-priced managers, multiple managers per building.

**Follow-up (planned next):** manager **star-tier upgrades** spending prestige currency — see `tasks/feature-manager-upgrades.md`.
