# ECS Patterns (DOTS/Entities)

**Scope:** How systems, components, authoring, and the Mono↔ECS boundary work here. Read before writing or modifying any ECS code.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## Component inventory (`Assets/Scripts/Components/`)

Singletons (one entity each, baked in the SubScene): `PlayerProgressData`, `PrestigeData`, `TutorialStateData`, `PlayerInventoryTag` (+ `InventorySlot` dynamic buffer).

Per-building: `BuildingData` (type, level, `ProductionSpeed`, `IsActive`), `GridPosition`, `BuildingTransformData`, `BuildingFootprint`, `RecipeProcessData` (RecipeID, CraftTime, Progress, IsCrafting, InputsSatisfied), `PlacedPortData`, `OutputDirectionData`, `BuildingLocalInventory`/`BuildingInventoryConfig`, buffers `RecipeInputSlot`, `RecipeOutputSlot`, `BuildingInputSlot`, `BuildingOutputSlot`. Tags: `EntropySinkTag`. Other: `ConveyorData`, `CollectorData`, `PowerNodeData`, `ItemData`.

Buffer helpers live in `Assets/Scripts/Components/SlotBufferUtils.cs` (count/add/remove for inventory and slot buffers) — always use these, never hand-roll buffer loops.

## Anatomy of a system (copy `ProductionSystem.cs`)

This repo uses **`ISystem` structs** (Burst-compiled where possible), not `SystemBase` classes:

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct MySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerProgressData>();   // gate until singletons exist
    }
    public void OnUpdate(ref SystemState state)
    {
        var progress = SystemAPI.GetSingleton<PlayerProgressData>();
        foreach (var (a, b) in SystemAPI.Query<RefRO<BuildingData>, RefRW<RecipeProcessData>>()) { ... }
        SystemAPI.SetSingleton(progress);               // write singleton back explicitly
    }
}
```

- Structural changes (destroy/create entities) inside iteration: use a temp `EntityCommandBuffer`, playback after the loop — see `PrestigeSystem.cs:72-84`.
- Managed calls (services, logging) are allowed only in **non-Burst** systems — `PrestigeSystem` calls `AchievementService.Instance?.NotifyPrestige()` because it is not Burst-compiled. Never call managed code from a `[BurstCompile]` method.
- Ordering: declare `[UpdateAfter(typeof(...))]` if you depend on another system's writes in the same frame.

## Authoring/baking (`Assets/Scripts/Authoring/`)

SubScene `Assets/Scenes/GameScene/TestSubScene.unity` holds authoring GameObjects; bakers convert them to entities at bake time. `PlayerInventoryAuthoring.cs` is the singleton-entity pattern: one authoring component bakes `PlayerInventoryTag` + `InventorySlot` buffer + `PrestigeData` defaults (`PlayerInventoryAuthoring.cs:42-46`) + `PlayerProgressData`. **A new singleton component is added in an existing authoring baker, not by creating a new SubScene object** — extend `PlayerInventoryAuthoring`'s baker and set defaults that match the `SaveData` field defaults (the ECSLoadBridge comment "defaults match SaveData defaults" is a real invariant).

Runtime entity creation (buildings placed during play) bypasses baking: `Assets/Scripts/Gameplay/BuildingPlacer.cs` creates entities directly with `EntityManager` (`PlaceBuilding()` sets `BuildingData.ProductionSpeed` from `BuildingSO.ProductionSpeedForLevel` at `BuildingPlacer.cs:69` and `RecipeProcessData.CraftTime = recipe.baseCraftTime` at `:85-93`).

## Sanctioned Mono↔ECS bridge patterns (pick one, don't invent)

1. **Load/flush singletons** — `ECSLoadBridge.ApplyLoadedSave()` / `FlushToSave()`: cached `EntityQuery` + `GetSingleton`/`SetSingleton`. For persisted state.
2. **Write-through on live purchase** — `ECSLoadBridge.AddEntropy()` (`ECSLoadBridge.cs:279`): guarded by `IsLoaded` and `query.IsEmpty`. For services granting currency.
3. **Direct query from UI** — `HUDController` / sub-controllers cache an `EntityQuery` in `SetECSContext(EntityManager)` and read singletons per-refresh (`PrestigeShopSubController.cs:31-36`). UI writes singletons only for player actions (e.g., purchase deducts `PrestigeData`).

Rules: always check `query.IsEmpty` before `GetSingleton`; never touch ECS before `ECSLoadBridge.IsLoaded`; remember anything written to ECS must be covered by `FlushToSave()` or it won't persist (see `save-system.md`).

## Where multipliers actually apply (gap map — important for new bonus features)

| Multiplier | Live ECS production | Idle snapshot | UI/cost |
|---|---|---|---|
| `BuildingData.ProductionSpeed` (upgrade level) | YES (`ProductionSystem.cs:81`) | indirectly via output rate | shown in inspector |
| `PrestigeData.SpeedMultiplier` | **NO — not read by ProductionSystem** | YES (`GridSaveService.cs:335`) | shown only |
| `PrestigeData.OutputMultiplier` | **NO** | no | shown only |
| `PrestigeData.CostReduction` | n/a | n/a | YES (`HUDController.cs:961,1000`) |

If a feature needs a bonus to affect live production, either (a) bake it into `BuildingData.ProductionSpeed` at every site that sets it (`BuildingPlacer.cs:69`, upgrade at `HUDBuildingInspectorSubController.cs:303`, restore via `GridSaveService.LoadGrid` → `BuildingPlacer.PlaceBuilding`), or (b) make `ProductionSystem` read a singleton multiplier (one site, affects everything). Option (b) is simpler and is the recommended approach — but then also update `GridSaveService.RebuildIdleSnapshot()` so offline earnings stay consistent.

## Testing ECS

EditMode tests create a private `World` + system instance and tick it manually — copy `Assets/Scripts/Tests/ProductionSystemTests.cs` (creates entities, sets `BuildingData{ProductionSpeed}`, asserts `Progress` advanced by `dt × speed` at `:158`) or `PrestigeSystemTests.cs`.
