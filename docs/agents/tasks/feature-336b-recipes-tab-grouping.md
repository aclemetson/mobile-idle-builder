# Feature #336 phase B + #287 step 2: Recipes tab reads game_data.json, one row per item

**Required reading:** `docs/agents/README.md`, `architecture.md`, `data-pipeline.md`, `save-system.md` (recipe
knowledge), `ui-toolkit.md` (HUD Recipes tab), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07).
**Size:** M-L. This is the riskiest task in the batch because it touches saves. **Branch:**
`feat/336-recipes-tab-grouping` from `integration/2026-11-07`.

## Problem
1. **#287 step 2:** the runtime still reads a second, hand-maintained recipe list.
   - `Assets/Data/recipes.json` is loaded by `RecipeDatabase` (`Assets/Scripts/ScriptableObjects/RecipeDatabase.cs`,
     a `TextAsset recipesJsonAsset`, `Load()` ~:83).
   - It has 44 recipes and is out of sync with `game_data.json`, which has 199 recipes, 20 with
     `can_craft_manually`.
   - #287 step 1 (removing `RecipeAssetGenerator` and `items.json`) is already merged in PR #298.
2. **#336 part 3:** the Recipes tab lists recipes in file order, one row per recipe. It should show one row per
   **output item**, with that item's methods inside.

## Facts (verified 2026-11-07)
- **The runtime type is `RecipeJson`,** defined in `RecipeDatabase.cs`. Don't confuse it with the editor-only
  `RecipeJson` in `Editor/GameDataModel.cs`. Its fields:
  - `id`, `name`, `tier`, `inputs` (`{id, quantity}`), `output {id, quantity}`, `byproducts`, `base_craft_time`;
  - `crafted_in` (list of building ids, plus `"inventory_menu"` for manual);
  - `requires_research`, `unlocks_research`;
  - `requiresBuilding` = `crafted_in` lacks `"inventory_menu"`; `EscapeEntropyPerCraft(ItemDatabase)`.
- **Consumers of `RecipeDatabase.Instance.Recipes` / `GetRecipe(id)` / `RecipeJson`:**
  - `HUDController` (~:670 and `BuildRecipeList` ~:1421-1512, `BuildCraftButtons` ~:1521, `OnCraftPressed` ~:1553,
    `BuildInputsText` ~:2464);
  - `ManualCraftService` (the whole API takes `RecipeJson`; `FindRecipe` ~:284 has a test seam
    `RecipeLookupOverride`);
  - `RecipeKnowledgeService.SyncWithRecipeDatabase` (~:190) and `CountCodexEntries` (~:102);
  - `ResearchService.RecipesGatedBy` (~:252-258);
  - `DevConsoleController` (~:1289);
  - `CraftQueueView.cs` (~:62, shows `recipe.name`).
  - Tests: `RecipesJsonConsistencyTests`, `CodexCountTests`, `HUDControllerTests`, `RecipeTimingTests`,
    `RecipeValidationTests`, and in PlayMode `ManualCraftServiceTests` and `RecipeKnowledgeServiceTests`.
- **Ids:** 41 of the 44 legacy ids also exist in `game_data.json`. At least `helium` differs: legacy `helium`
  outputs `helium_4`, and the game_data recipes for it are `helium_4`, `fuse_helium` and `fuse_deuterium`.
  - **Saved recipe knowledge** (`RecipeKnowledgeService`) and the manual **craft queue** in the save store recipe
    ids. A legacy id that disappears must be **mapped**, not dropped.
  - Find every id mismatch with a script before designing the map.

## Implementation
1. **Single source.** Make `RecipeDatabase` build its `RecipeJson` list from the imported `RecipeSO` assets
   instead of `recipes.json`.
   - **Keep `RecipeJson` as the runtime view type** so the consumers above barely change.
   - **Use the existing Resources pattern (verified 2026-11-07), not scene wiring.** `GameDataImporter` already writes
     `Assets/Resources/BuildingDatabase.asset` (`BuildingDatabaseSO`), `ResearchDatabase`, `DialogueDatabase` and
     others, and runtime code calls `Resources.Load<BuildingDatabaseSO>("BuildingDatabase")`.
     - Add a `RecipeDatabaseSO` (`public RecipeSO[] allRecipes;`) that the importer writes to
       `Assets/Resources/RecipeDatabase.asset`, the same way it writes `BuildingDatabaseSO`.
     - `RecipeDatabase.Load()` reads it with `Resources.Load<RecipeDatabaseSO>("RecipeDatabase")`.
     - Because the asset is generated, the reviewer re-imports it and commits it.
   - **Scene:** `Assets/Scenes/GameScene.unity` (~:325) serializes `recipesJsonAsset: {fileID: 4900000, guid: fabdbb24d26dea3448a77ed089ff3f01, type: 3}`
     on the `RecipeDatabase` component. After removing the C# field, delete exactly that one YAML line. It is the
     only scene edit.
   - Conversion: `id`=`so.id`, `name`=`so.displayName` (product names after phase A), `inputs`/`output`/`byproducts`
     from the SO, `base_craft_time`, and `crafted_in` = `validBuildings` ids plus `"inventory_menu"` when
     `canCraftManually`. Fill in `requires_research` and `unlocks_research` too.
   - Put the conversion in a pure static method so it's testable.
2. **Legacy id map.** Add `LegacyRecipeIds` (static `Dictionary<string,string>`, old id to new id) for every legacy id
   missing from `game_data.json`.
   - Apply it when loading recipe knowledge and the craft queue from a save, in `RecipeKnowledgeService`'s load and
     wherever the craft queue is restored (grep).
   - Follow the existing retired-id pattern if one exists, e.g. `ECSLoadBridge.RetiredSteps`.
3. **Delete `Assets/Data/recipes.json` and its `.meta`.**
   - Remove the `recipesJsonAsset` field: grep the scene for its GUID reference, and remove or replace it.
   - Replace `RecipesJsonConsistencyTests` with an importer-output test: every `can_craft_manually` recipe in
     `game_data.json` appears in the runtime list with matching inputs and output.
4. **Grouping.** `HUDController.BuildRecipeList` groups the known recipes by `output.id`.
   - **One method:** the row looks exactly like today.
   - **Several methods:** one row with the item icon and name, expandable through the existing foldout or expand
     pattern (grep `Foldout` or `expand` in `HUDController` and `ui-toolkit.md`). Expanded, each method shows:
     - its building ("Made in a Fusion Reactor" for building-only methods, replacing `[Building]`);
     - its inputs (`BuildInputsText`), time, and lock state ("Research X to unlock");
     - for manual methods, the existing `BuildCraftButtons`.
   - The row's main craft buttons use the **selected** method: the first unlocked manual one by default, remembered
     for the session in a `Dictionary<outputId, recipeId>`.
   - Put the grouping in a pure static `RecipeGrouping.Group(IEnumerable<RecipeJson>)` that returns ordered groups,
     keeping first-appearance order.
5. **Codex and counts:** `RecipeKnowledgeService.CountCodexEntries` and `CodexCountTests` may change because there
   are more recipes now. Update the expected numbers **only** where the difference is explained by the new
   recipes, and say so in the report.
6. **Docs:**
   - `data-pipeline.md`: `recipes.json` is gone; `RecipeDatabase` reads the importer output.
   - `elements-and-isotopes.md`: remove the parity-gap note.
   - Refresh the stamps.

## Tests
- **EditMode:**
  - `RecipeGrouping.Group`: single method, three Helium-4 methods, order kept;
  - the SO-to-`RecipeJson` conversion, including `crafted_in` with `inventory_menu`;
  - the importer-output test that replaces `RecipesJsonConsistencyTests`;
  - the legacy id map: every mapped target exists in `game_data.json`.
- **PlayMode:**
  - `RecipeKnowledgeServiceTests`: a save holding the legacy id `helium` loads as the new id;
  - `ManualCraftServiceTests` still pass, including through the test seam.

## Done when
- `recipes.json` is gone, and only `game_data.json` defines recipes.
- The Recipes tab shows one Helium-4 row with three methods; manual crafting still works through the chosen method.
- An old save keeps its known recipes and its craft queue.
- The full suite passes. Set Status to done with the PR number, and close #287 by hand.
