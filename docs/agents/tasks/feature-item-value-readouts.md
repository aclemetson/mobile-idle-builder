# Feature: item value readouts (Recipes tab + building inspector)

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ui-toolkit.md`, `economy-balance.md` (the "Derived
economy" and surplus sections only), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07).
**Size:** M. **Branch:** `feat/item-value-readouts` from `integration/2026-11-07`.

## Goal
Show players what their products are worth:
- In the **Recipes tab**, each product row shows its price per item.
- In the **building inspector**, show:
  - the price each;
  - the value **per craft**, including output bonuses;
  - the value **per second** at the building's current speed.

## How value actually works (verified 2026-11-07; mirror it exactly)
- **Price never changes.** Every automated sale pays `ItemSO.baseSellValue`, or **1** when it is ≤ 0:
  - `EntropySinkSystem.cs` ~:42-49;
  - `SurplusSale.DemonPrice` (`Assets/Scripts/Services/SurplusSale.cs` ~:25).
  - No research, upgrade or prestige bonus raises price.
- **Bonuses raise quantity and speed** (`ProductionSystem.OnUpdate`, `Assets/Scripts/Systems/ProductionSystem.cs`):
  - **Progress per frame** = `deltaTime × BuildingData.ProductionSpeed × powerRatio × GlobalProductionBonus.SpeedMult`.
    A craft finishes at `RecipeProcessData.CraftTime`.
    - `powerRatio` comes from `PowerStatus` / the power grid; read how `ProductionSystem` computes it just above the
      progress line.
  - **`outMult`** = `ManagerAssignmentData.AppliedOutputMult` (when present) × `GlobalProductionBonus.OutputMult`.
  - **Each output** gives `qty = (int)(outputs[i].Quantity × outMult)`, rounded down.
  - **Escaping byproducts** pay `(long)(RecipeProcessData.ByproductEntropy × outMult)` entropy per craft.
- **Collectors:** read `CollectorSystem.cs` (~:34-68) for the exact rate (`CollectorData.OutputRate`?) and how
  `outMult` applies, then mirror it.
- `PrestigeData.OutputMultiplier` (quantum_yield) is **not applied anywhere**. Do not include it; it's a known gap.

## Implementation
1. **`Assets/Scripts/Services/ItemValue.cs`**, a pure static class:
   - `float PricePerItem(ItemSO item)` delegates to `SurplusSale.DemonPrice(item.itemId)` (check its parameter type),
     so there is one definition of price.
   - `double PerCraft(float price, int outputQty, float outMult, float escapeEntropy)` =
     `price × floor(outputQty × outMult) + floor(escapeEntropy × outMult)`. Sum it over every output slot the recipe
     produces.
     - Add an overload or helper that takes the `RecipeOutputSlot` list plus a price lookup.
   - `double PerSecond(double perCraft, float craftTime, float speed)` = `perCraft × speed / craftTime`. Return 0 when
     `craftTime ≤ 0` or `speed ≤ 0`.
   - `double CollectorPerSecond(float price, float outputRate, float outMult)`, mirroring `CollectorSystem`.
2. **`Assets/Scripts/UI/NumberFormat.cs`**:
   - Move the body of `IdleReturnSubController.FormatLargeNumber(long)` (~:157) into `NumberFormat.Compact(double)`,
     keeping its output the same for whole numbers. Leave `FormatLargeNumber` as a one-line forwarder.
   - Add `NumberFormat.Entropy(double)` → `"◈ 12.3K"`.
   - For values < 10 that aren't whole, show one decimal ("◈ 2.5"); the per-second rates need it.
3. **Recipes tab** (`HUDController.BuildRecipeRow`, ~:1531): add a label `"Worth ◈ X each"`, using the output item's
   price, in class `recipe-bonus-hint`, placed next to the existing escape-bonus label (~:1589-1596).
   - Put the text in an `internal static` helper next to `HUDController.EscapeBonusText` (~:1673) so tests can reach
     it.
   - Show it once per group row, not on every expanded method row, since all methods in a group make the same item.
4. **Building inspector** (`HUDBuildingInspectorSubController`, the "—— Produces ——" section, ~:315-330):
   - For crafters, under the produced items:
     - `"Worth ◈ X each"` for the main output (`RecipeOutputSlot[0]`);
     - when `outMult > 1.001`: `"×{outMult:0.##} output → ◈ Y per craft"`, otherwise `"◈ Y per craft"`;
     - `"≈ ◈ Z /s at current speed"`.
   - **Speed** = `ProductionSpeed × powerRatio × globalSpeed`, read from the same components `ProductionSystem` uses.
     - When `powerRatio` is 0, show `"≈ ◈ 0 /s (no power)"`.
     - When the recipe isn't set, show nothing.
   - For collectors, add `"≈ ◈ Z /s"` next to the existing `Output rate: … /s` line (~:277).
   - Put the text building in an `internal static` helper (e.g. `ValueLines(...)` returning `string[]`) so it's testable.
   - **Layout rule:** the inspector rebuilds every 0.5 s and follows "reserve, don't add/remove". Emit the same
     number of rows each rebuild for a given building and recipe.
5. **Picker buttons** (`MakeRecipeButton`, ~:816): skip this. The method line already fills the button.

## Tests (EditMode, new `ItemValueTests.cs`)
- `PricePerItem`: a 0 price falls back to 1. If `DemonPrice` needs a database, test the fallback through whatever seam
  `SurplusSale` tests use, or test only `PerCraft`/`PerSecond`.
- `PerCraft`: qty 1 with outMult 1.5 gives floor 1; qty 2 with outMult 1.5 gives 3; escape entropy is added.
- `PerSecond`: the normal case; zero craft time and zero speed give 0.
- `NumberFormat.Compact`: 9,999 / 12.3K / 1.23M, as `FormatLargeNumber` does today (check the existing outputs before
  asserting). `Entropy(2.5)` gives "◈ 2.5".
- The inspector and recipe-row text helpers, with fixed inputs.

## Done when
- The Recipes tab rows show "Worth ◈ X each".
- The inspector shows each / per craft / per second.
- Assigning an output manager raises per craft.
- Cutting power shows 0 /s.
- The tests pass and the full EditMode suite passes. Set Status to done with the PR number.
