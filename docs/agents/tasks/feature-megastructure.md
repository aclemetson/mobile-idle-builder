# Feature: Dyson Sphere Megastructure (endgame project)

**Status:** pending
**Required reading:** `docs/agents/architecture.md`, `docs/agents/ecs-patterns.md`, `docs/agents/save-system.md`, `docs/agents/ui-toolkit.md`, `docs/agents/economy-balance.md`
**Scope estimate:** ~6 files created, ~7 modified, 2 test files.
**Branch:** `feature/megastructure` → PR into the current release branch (confirm with user).

## Design summary

The visible long-term goal the 5-tier production chain builds toward (Dyson Sphere Program inspiration). A single multi-stage construction project: the player **contributes items** (tier-5 megastructure components) from inventory into the current stage; when a stage's full bill of materials is contributed, the stage completes and grants a permanent global reward. Progress is the game's endgame meter.

**The content already exists** — phase ⑮ in `docs/gameplay_loop_data.js` and `game_data.json` already define `megastructure_theory` research (50M e, `game_data.json:456`) and the items/recipes `dyson_node`, `orbital_frame`, `graviton_lens` (`game_data.json:1645-1712`, recipes `:2944+`). This task adds only the construction project: stage definitions, contribution mechanic, state, and UI.

**Prestige interaction (pinned decision):** completed stages and contributed-but-incomplete progress BOTH survive prestige — the megastructure is a meta-progression layer above prestige, like the prestige shop. (Late-game items take days to produce; losing partial contributions on prestige would make the two systems fight each other.) Therefore megastructure state lives in persistent SaveData fields, NOT in `currentRun`, and `PrestigeSystem`'s reset block (`PrestigeSystem.cs:57-110`) is NOT modified.

## Balance numbers (final — do not derive your own)

5 stages. Component sell values: Dyson Node 5T, Orbital Frame 20T, Graviton Lens 100T (from `gameplay_loop_data.js` phase ⑮).

| Stage | Name | Bill of materials | Reward (permanent, stacking) |
|---|---|---|---|
| 1 | Scaffold Ring | 10× dyson_node | +10% global output |
| 2 | Support Lattice | 25× dyson_node, 5× orbital_frame | +10% global craft speed |
| 3 | Inner Shell | 50× dyson_node, 20× orbital_frame, 2× graviton_lens | +25% global output |
| 4 | Stabilizer Array | 40× orbital_frame, 10× graviton_lens | +25% global craft speed |
| 5 | Stellar Engine | 100× dyson_node, 80× orbital_frame, 30× graviton_lens | prestige currency gain ×2 |

Rewards apply as multipliers surfaced through `MegastructureService.GetOutputBonus()` / `GetSpeedBonus()` / `GetPrestigeGainBonus()`.

**⚠ Reward wiring reality check** (gap map in `ecs-patterns.md`): global speed/output multipliers are not currently read by live `ProductionSystem`. For this feature, wire rewards where they verifiably work today:
- Stage 5 prestige bonus → multiply `earned` in `PrestigeSystem.OnUpdate` next to the existing `PrestigeGainMultiplier` application (`PrestigeSystem.cs:50-52`).
- Output/speed bonuses → fold into the idle snapshot (`GridSaveService.cs:335` region) AND into `BuildingData.ProductionSpeed` at placement (`BuildingPlacer.cs:69`) and upgrade (`HUDBuildingInspectorSubController.cs:303`) for speed; output via ProductionSystem deposit multiplier gated on a singleton — mirror whichever approach the managers feature (`feature-managers.md`) landed if it merged first; coordinate to avoid double-applying.

## Data schema additions (`Assets/Data/game_data.json`)

New top-level `"megastructure"` object (after `"fields"`); bump `_meta.version`. Importer pass → single `MegastructureSO` asset (copy ResearchSO import pattern; must run AFTER Items import because stages cross-ref item ids). Model classes in `Editor/GameDataModel.cs`.

```json
"megastructure": {
  "id": "dyson_sphere",
  "display_name": "Dyson Sphere",
  "required_research": "megastructure_theory",
  "stages": [
    { "id": "ms_stage_1", "display_name": "Scaffold Ring",
      "costs": [ { "item": "dyson_node", "quantity": 10 } ],
      "reward_type": "OutputMultiplier", "reward_value": 0.10 }
  ]
}
```
(All 5 stages per the balance table; `reward_type` enum: `OutputMultiplier` | `SpeedMultiplier` | `PrestigeGainMultiplier`.)

## Save additions (`SaveData.cs` — additive, persistent section, survives prestige)

```csharp
public int megastructureStage;                                   // completed stages count (0–5)
public List<string> megastructureContribKeys   = new();          // item ids (string numeric, like inventoryKeys)
public List<int>    megastructureContribValues = new();          // contributed counts toward CURRENT stage
```

## Files to create

| Path | Copy pattern | Notes |
|---|---|---|
| `Assets/Scripts/ScriptableObjects/MegastructureSO.cs` | `ResearchSO.cs` style | stages with resolved ItemSO refs |
| `Assets/Scripts/Services/MegastructureService.cs` | `Assets/Scripts/Services/ResearchService.cs` | gating on `megastructure_theory` unlocked (use `ResearchService.OnResearchUnlocked` / unlocked query); `Contribute(itemId, qty)` deducts from the global `InventorySlot` buffer the same way `ResearchService.DeductEntropy` mutates ECS (`ResearchService.cs:168` pattern, but on inventory buffer via `SlotBufferUtils`); stage completion check; `GetOutputBonus()` etc.; `LoadFromSave`/flush like `PersistentUpgradeService.cs:224` region |
| `Assets/Scripts/UI/MegastructureSubController.cs` | `PrestigeShopSubController.cs` | stage progress bars, per-item contribute buttons ("contribute all" + "contribute 1") |
| `megastructure-panel` in `Assets/UI/GameHUD.uxml` + `btn-megastructure` nav button | copy `prestige-panel` (`:152`) structure | hide button until `megastructure_theory` researched |
| `Assets/Scripts/Tests/MegastructureServiceTests.cs` | `ResearchServiceTests.cs` (PlayMode) or EditMode with manual world (`ProductionSystemTests.cs`) | contribution math, stage rollover, reward query |
| `Assets/Scripts/PlayModeTests/MegastructureSaveTests.cs` | `SaveSystemTests.cs` | round-trip, legacy load, prestige survival |

## Files to modify

| Path | Integration point |
|---|---|
| `game_data.json`, `Editor/GameDataModel.cs`, `Editor/GameDataImporter.cs` | schema + import pass (after Items) |
| `Assets/Scripts/SaveSystem/SaveData.cs` | fields above |
| `Assets/Scripts/Systems/PrestigeSystem.cs` | ONLY the earn line: after `gainBonus` application (`:50-52`), multiply `earned` by `1 + MegastructureService.GetPrestigeGainBonus()` (managed call is safe — PrestigeSystem is not Burst-compiled) |
| `Assets/Scripts/SaveSystem/GridSaveService.cs` | `RebuildIdleSnapshot` (`:335` region): fold output/speed bonuses into snapshot rates |
| `Assets/Scripts/UI/HUDController.cs` | component field, nav button (research-gated visibility), `OpenMegastructurePanel()` |
| `Assets/Scripts/Achievements/AchievementService.cs` | optional: `NotifyTierReached`-style hook for stage completion toasts — reuse `ToastService` directly instead if simpler |

## Implementation order

1. SaveData fields + legacy-load test.
2. JSON + model + importer + `MegastructureSO`; run importer; schema test.
3. `MegastructureService`: contribution (inventory deduction via `SlotBufferUtils`), stage completion, reward queries + EditMode tests.
4. PrestigeSystem earn-line integration + extend `PrestigeSystemTests`.
5. Idle snapshot integration.
6. UI panel + HUD wiring (research-gated button).
7. Full suite, manual check, commit.

## Test plan

- Contribute with insufficient inventory → rejected, nothing deducted.
- Contribute exactly enough → stage completes, `megastructureStage++`, contributions reset for next stage, reward query reflects new stage.
- Over-contribution clamps (contribute 100 when 10 needed → only 10 deducted).
- Prestige with partial contributions → contributions intact after reset.
- Stage-5 prestige bonus doubles `earned` in `PrestigeSystemTests`.
- Save round-trip + legacy load.

## Definition of done

- [ ] JSON parses; importer run; full suite green both platforms (`scripts/test-local.ps1`), results XML reported
- [ ] `MegastructureSubController` component added to HUD GameObject in GameScene (manual editor step — flag to user)
- [ ] Panel hidden before `megastructure_theory` research; visible + functional after (dev console can grant research/entropy)
- [ ] Legacy save loads; prestige keeps contributions
- [ ] No generated assets hand-edited

## Pitfalls

- Contribution deducts from the GLOBAL inventory buffer — guard `ECSLoadBridge.IsLoaded` and use `SlotBufferUtils`; do not touch building-local buffers.
- Anything contributed must also be removed from inventory in the SAME frame and both ECS + SaveData must agree at next `FlushToSave()` — contribute through ECS, let the normal flush persist inventory.
- If `feature-managers.md` merged first, coordinate the output/speed bonus application sites — both features multiplying `ProductionSpeed` must compose (multiply), not overwrite each other. Check for `ManagerAssignmentData` handling at the same sites.
- The reward enum strings in JSON must match the C# enum names exactly — `JsonUtility` parses enums by string via the model class.

## Out of scope

3D/visual sphere rendering (UI progress bars only), multiple megastructures, contribution from building buffers/conveyers feeding the megastructure directly, stage-skip purchases, leaderboards.
