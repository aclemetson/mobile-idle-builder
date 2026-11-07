# Feature #339: research effects (Transportation I = +10% conveyor speed)

**Required reading:** `docs/agents/README.md`, `architecture.md`, `data-pipeline.md` (research fields, importer),
`ecs-patterns.md` (multipliers / singletons), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07).
**Size:** M. **Branch:** `feat/339-research-effects` from `integration/2026-11-07`.

## Problem
Research can only **unlock** things. **Transportation I** (`transportation_1`, `Assets/Data/game_data.json`
~:1080, Engineering, Foundations block) says "Increases conveyor belt speed by 10%." It does nothing, because:
- every belt's `ConveyorSegmentData.TransportTime` is a fixed 1.0;
- no global conveyor-speed multiplier exists.

## Design
- **Data:** a research `effects` list of `{ type, value }`, e.g. `{ "type": "ConveyorSpeed", "value": 0.10 }`.
  Values for the same type **add up** across unlocked research, so total belt speed = 1 + Σ value.
- **Runtime:** a new ECS singleton `ResearchEffects` on the player entity that `ConveyorSystem` reads.
  `ResearchService` recomputes it.
- **Later types use the same list,** e.g. harvester speed or sell bonus. Only `ConveyorSpeed` is wired now.

## Facts (verified 2026-11-07)
- **`ResearchJson`** is in `Assets/Scripts/Editor/GameDataModel.cs` ~:116. It already holds the scalar modifier
  `field_cooldown_mult`. The JSON editor window writes back through this model, so a key missing from it is
  **lost on save**.
- **Importer:** `GameDataImporter.GenerateResearchPass1` (`Assets/Scripts/Editor/GameDataImporter.cs` ~:321) copies
  scalars, e.g. `so.fieldCooldownMult = data.field_cooldown_mult` (~:333).
- **`ResearchSO`** (`Assets/Scripts/ScriptableObjects/ResearchSO.cs`) has a `[Header("Modifiers")]` section holding
  `fieldCooldownMult`.
- **Existing modifier reader:** `ResearchService.GetFieldCooldownMultiplier()` (`Assets/Scripts/Services/ResearchService.cs`
  ~:87) loops `_allResearch` and checks `IsUnlocked(r.id)`.
- **ResearchService lifecycle:**
  - `Start()` (~:46) gets the EntityManager and rebuilds `_unlockedIds` from `save.unlockedResearch`. **No event
    fires on load.**
  - `CompleteResearch` (~:212) and `ForceUnlock` (~:296, dev only) fire `OnResearchUnlocked`.
  - `ResetAll()` (~:339, called on prestige from `PrestigeSystem.cs` ~:107 and `PrestigeSaveWatcher.cs` ~:67) clears
    the set and fires nothing.
  - Research with `resets_on_prestige=false` may come back afterwards, so recompute, don't just reset to 1.
- **Singleton pattern to copy:** `GlobalProductionBonus` (`Assets/Scripts/Components/GlobalProductionBonus.cs`).
  - Baked in `PlayerInventoryAuthoring.cs` ~:50.
  - Written by `MegastructureService.ApplyBonusToECS` (~:206) through
    `_em.CreateEntityQuery(ComponentType.ReadWrite<...>())` and `SetSingleton`.
  - **Do not add the field to `GlobalProductionBonus`:** MegastructureService overwrites the whole struct.
- **Init race:** the player entity comes from a SubScene and may not exist yet when `Start()` runs (see CLAUDE.md
  "ECS+MonoBehaviour race"). Use a dirty flag, set on load, complete, force-unlock and reset. `Update()` pushes the
  value once the query is non-empty, then clears the flag.
- **Conveyor:**
  - `ConveyorSystem.cs` ~:93: `float newProg = item.Progress + dt / seg.TransportTime;`.
  - `ConveyorSystem` is a `SystemBase` with no Burst, so reading a singleton with a `SystemAPI.HasSingleton` /
    `TryGetSingleton` guard is fine.
  - `TransportTime` is set to 1 only in `ConveyorPlacer.cs` ~:117 and is not saved. **Leave it at 1**, so the bonus
    applies live without rewriting segments.
- **Belt priming:** `BeltPrimer.cs` ~:141 takes `headSeg.TransportTime`. Divide it by the speed multiplier when the
  singleton exists (the item spacing depends on it).
- **Offline:** `IdleGraphAnalyzer` works from steady-state rates and never uses belt time. **No change.** Say so in the
  PR.
- **Research card UI:** `HUDController.BuildResearchCard` (`Assets/Scripts/UI/HUDController.cs` ~:971). The
  description label (class `research-card-desc`) is at ~:988-993. The list view and the tree detail panel both use
  this card.
- **Tree editor scripts:**
  - `scripts/rebalance_curve.py` and `scripts/apply_research_tree.py` (existing nodes) merge per key, so `effects`
    survives.
  - The new-node template in `apply_research_tree.py` (~:91) and the export in `scripts/research_tree.py`
    (~:20-45) don't carry it.

## Implementation
1. **Data model:**
   - `GameDataModel.cs`: add `[Serializable] internal class ResearchEffectJson { public string type = ""; public float value; }`
     and `public ResearchEffectJson[] effects = Array.Empty<ResearchEffectJson>();` on `ResearchJson`.
   - Runtime side: add `public enum ResearchEffectType { ConveyorSpeed }` and
     `[Serializable] public struct ResearchEffect { public ResearchEffectType type; public float value; }`, e.g. in a
     new `Assets/Scripts/ScriptableObjects/ResearchEffect.cs`.
   - `ResearchSO`: add `public ResearchEffect[] effects;` under Modifiers.
2. **Importer:**
   - Map each `ResearchEffectJson` to a `ResearchEffect` with `Enum.TryParse`.
   - On an unknown type, call `Debug.LogError` naming the research id and the type, then skip it.
3. **`game_data.json`:** add `"effects": [ { "type": "ConveyorSpeed", "value": 0.10 } ]` to `transportation_1`.
   - Edit through `scripts/gamedata_edit.py` (`GameData.update`) or by hand, keeping the formatting.
   - Then re-import (menu `MobileIdleBuilder/Import Game Data`, through the Unity MCP if available) so
     `Assets/Data/research/transportation_1.asset` (or wherever research assets live) carries it. **Commit the
     regenerated asset.**
4. **Component:** `Assets/Scripts/Components/ResearchEffects.cs` holds
   `public struct ResearchEffects : IComponentData { public float ConveyorSpeedMult; }`. Bake it with 1 in
   `PlayerInventoryAuthoring` next to `GlobalProductionBonus`.
5. **`ResearchService`:**
   - `public static float SumEffect(IEnumerable<ResearchSO> unlocked, ResearchEffectType type)` is the pure helper,
     used by the tests.
   - `public float GetEffectTotal(ResearchEffectType type)` passes the unlocked research to it.
   - A private `_effectsDirty` flag and `TryApplyEffectsToECS()` that calls
     `SetSingleton(new ResearchEffects { ConveyorSpeedMult = 1f + GetEffectTotal(ConveyorSpeed) })` when the query is
     non-empty.
   - Set the dirty flag at the end of `Start`, `CompleteResearch`, `ForceUnlock` and `ResetAll`. Call
     `TryApplyEffectsToECS` from `Update`, adding an `Update` if none exists; it is cheap when not dirty.
6. **`ConveyorSystem`:**
   - At the top of `OnUpdate`, read `float speedMult = SystemAPI.TryGetSingleton<ResearchEffects>(out var fx) ? fx.ConveyorSpeedMult : 1f;`
     and guard against ≤ 0.
   - Use `dt * speedMult / seg.TransportTime` at ~:93.
   - Look for any other `TransportTime` division in the file and apply the same factor.
7. **`BeltPrimer`:** effective `transportTime / speedMult`, when the singleton is present in that world.
8. **UI:** in `BuildResearchCard`, after the description, add one label per effect (class `research-card-effect`, with
   a small style in the USS the research panel actually loads; grep first). The text comes from a pure
   `ResearchEffectText.Describe(ResearchEffect)`, e.g. `ConveyorSpeed 0.10` gives "+10% belt speed".
9. **Optional, do it if simple:** add `effects` to the `scripts/research_tree.py` export and the
   `apply_research_tree.py` new-node template, read-only display is enough.

## Tests
- **EditMode, new `ResearchEffectsTests.cs`:**
  - `SumEffect` adds two `ConveyorSpeed` effects (0.10 + 0.05 = 0.15);
  - other types are ignored;
  - an empty list gives 0;
  - `Describe` formats "+10% belt speed".
- **EditMode, ConveyorSystem:** copy the world setup in `ConveyorHandoffTests.cs`. With the `ResearchEffects`
  singleton at 1.1, an item advances 10% more per tick than without it.
- **EditMode, BeltPrimer:** extend `BeltPrimerTests.cs` (~:59) if the effective time changes the placements.
- **Data test:** in an existing test that parses research from `game_data.json` (e.g.
  `ProgressionBalanceTests.cs`), `transportation_1` has one `ConveyorSpeed` effect of 0.10.
- **PlayMode, optional:** in `PlayModeTests/ResearchServiceTests.cs`, `ResetAll` drops the total to 0.

## Docs
- `docs/agents/ecs-patterns.md`: add `ResearchEffects` to the multipliers/singletons section.
- `docs/agents/data-pipeline.md`: add `effects` to the research field list.
- Refresh both "Verified against" stamps.

## Done when
- With Transportation I researched, belts are 10% faster: live, after save/load, and gone after a prestige reset
  (unless the node survives prestige).
- The research card shows "+10% belt speed".
- The tests above pass and the full EditMode suite passes.
- Set Status to done with the PR number.
