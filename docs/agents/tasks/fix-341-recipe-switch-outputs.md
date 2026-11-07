# Fix #341: switching a building's recipe moves old-recipe outputs to the inventory

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ecs-patterns.md` (buffers), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07).
**Size:** S. **Branch:** `fix/341-recipe-switch-outputs` from `integration/2026-11-07`.

## Problem
A newly placed Nucleon Harvester starts on `harvest_proton` (`NucleonHarvesterMerge.DefaultRecipeId`,
`Assets/Scripts/Gameplay/NucleonHarvesterMerge.cs:16`). If it finishes a craft before the player switches it to
Neutrons, a Proton sits in its `BuildingOutputSlot` buffer and leaves on the belt after the switch.

Switching recipe already sells the unused **inputs** (`SellInputsTheRecipeDoesNotUse`, #312). Nothing touches the
**outputs** made under the old recipe.

**Decision (user, 2026-11-07):** on a recipe switch, outputs the new recipe does **not** produce move into the
player's inventory. They are not sold. Outputs the new recipe also produces stay in the buffer and keep shipping.

## Facts
- **Switch path:** `HUDBuildingInspectorSubController.SetBuildingRecipe` (`Assets/Scripts/UI/HUDBuildingInspectorSubController.cs`
  ~:1110-1160). It:
  - resets `RecipeProcessData`;
  - rebuilds `RecipeInputSlot` and `RecipeOutputSlot` (the recipe *definition* buffers);
  - calls `SellInputsTheRecipeDoesNotUse(entity)` (:1147, defined ~:1344);
  - updates the dynamic power draw, then calls `_hud?.NotifyBuildingRecipeSet()`.
- **Real output buffer:** `BuildingOutputSlot` (`ItemID`, `Quantity`).
- **Existing manual "empty output" path:** `EmptyOutputToInventory` (~:1323). It reaches the player inventory with
  `_em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity())` and calls
  `SlotBufferUtils.AddToInventory(ref inv, id, qty)`.
- **Pure-helper precedent:** `SurplusSale.SellUnusedInputs` (`Assets/Scripts/Services/SurplusSale.cs` ~:43) iterates
  the buffer backwards and `RemoveAt`s what the recipe doesn't use. `InputAcceptance.RecipeUses` is its predicate.
- **Popup:** `InventoryPopupController.Notify(string itemIdOrKey, int qty)`. Check its signature; it is used with
  `"entropy"` at ~:1355. Use the existing per-item form if one exists; otherwise skip per-item popups.

## Implementation
1. **Add a pure static helper.** It moves the outputs the new recipe doesn't produce, e.g. in a new
   `Assets/Scripts/Services/RecipeSwitch.cs` or next to `SurplusSale`:
   ```csharp
   /// Moves output-buffer items that recipeOutputs doesn't produce into inventory. Returns how many items moved.
   public static int MoveUnproducedOutputsToInventory(DynamicBuffer<BuildingOutputSlot> outputBuffer,
                                                      DynamicBuffer<RecipeOutputSlot> recipeOutputs,
                                                      ref DynamicBuffer<InventorySlot> inventory)
   ```
   - Iterate backwards.
   - An item stays when any `recipeOutputs[j].ItemID == slot.ItemID`.
   - Otherwise call `SlotBufferUtils.AddToInventory` and then `RemoveAt`.
2. **Call it from `SetBuildingRecipe`** right after `SellInputsTheRecipeDoesNotUse(entity)`, through a private wrapper
   `MoveOutputsTheRecipeDoesNotMake(entity)`.
   - Guard on `_ecsReady`, a non-empty `_inventoryQuery` and `HasBuffer<BuildingOutputSlot>`, the same way
     `EmptyOutputToInventory` does.
   - If anything moved, show a popup (see Facts).
3. **No other changes.** The default recipe stays.

## Tests (EditMode)
Add the tests to a new `Assets/Scripts/Tests/RecipeSwitchTests.cs`. Copy the manual-world setup from an existing
EditMode test that builds buffers (e.g. `ConveyorHandoffTests.cs` or `ProductionSystemTests.cs`).
- An output item the new recipe doesn't make moves to the inventory with its quantity, and the buffer drops it.
- An output item the new recipe *does* make stays in the buffer, and the inventory is unchanged.
- A mixed buffer gives the right split, and the helper returns the moved count.
- Moving an item already in the inventory adds to the existing slot.

## Done when
- Switching a Nucleon Harvester that holds a Proton to Neutrons puts the Proton in the inventory, and no Proton
  leaves on the belt.
- The tests above pass, and the full EditMode suite passes.
- Status above is set to done with the PR number.
