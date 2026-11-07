# Feature #336 phase A: name every recipe after its product, add a method line in the building picker

**Required reading:** `docs/agents/README.md`, `architecture.md`, `data-pipeline.md`, `ui-toolkit.md` (inspector
section), `testing.md`.
**Status:** done (PR #350).
**Size:** M. **Branch:** `feat/336-recipe-names` from `integration/2026-11-07`.
**Phase B** (the Recipes tab on game_data.json, grouped by item, #287 step 2) is a separate task:
`feature-336b-recipes-tab-grouping.md`. **Do not touch the Recipes tab, `RecipeDatabase` or `recipes.json` here.**

## Problem
115 of 199 recipes in `Assets/Data/game_data.json` are named with a verb or method: "Assemble Deuterium", "Fuse
Iron", "Harvest Proton", "Slow capture: Nickel". "Fuse Deuterium" even makes Helium-4. The convention is that a
recipe is named after its product. 22 items have more than one recipe, and the method names exist to tell those
apart.

## Facts (verified 2026-11-07)
- **Recipe JSON fields:** `id`, `display_name`, `recipe_id`, `tier`, `category`, `inputs` (`[{item, quantity}]`),
  `output_item`, `output_quantity`, `byproducts`, `valid_buildings` (building ids), `can_craft_manually`,
  `known_from_start`, `required_research`, ... There is **no** `outputs` or `building` field.
- **Generators:** no generator writes recipe `display_name`. `rebalance_curve.py` only writes craft times. The
  importer copies `display_name` into `RecipeSO.displayName`, so hand edits are safe; re-import afterwards.
- **`RecipeSO`** (`Assets/Scripts/ScriptableObjects/RecipeSO.cs`) has `id`, `displayName`, `inputs`
  (`RecipeIngredient {item, quantity}`), `outputItem`, `outputQuantity`, `byproducts`, `validBuildings`
  (`BuildingSO[]`), `canCraftManually`.
- **Model and importer:** the JSON model is the recipe class in `Assets/Scripts/Editor/GameDataModel.cs`. The
  importer is `GameDataImporter.cs`; grep for `display_name` in its recipe pass.
- **Picker, recipe buttons:** `HUDBuildingInspectorSubController.cs` ~:388-400, `—— Set Recipe ——`, label
  `r.displayName ?? r.name`.
- **Picker, field collector buttons:** ~:788-800, `—— Set Collection Target ——`, label
  `r.displayName ?? r.outputItem?.displayName ?? r.name`.
- **Text that quotes recipe names** in `game_data.json`:
  - tutorial hints near :14214 ("set its recipe to Assemble Deuterium"), :14265 ("Fuse Deuterium") and :14286
    ("Fuse Carbon");
  - a research description near :382 ("Fuse Lithium (Helium + Deuterium)…").
  - Grep for each old name before replacing it.
- **Generated outputs that copy recipe names:**
  - `scripts/generate-tutorial-walkthrough.py` (`Playtests/tutorial-walkthrough.md`);
  - `scripts/research_tree.py` (`docs/research-tree.html`).
  - Regenerate both.
- **Items with several recipes:** see the table in issue #336 (`gh issue view 336`).

## Implementation
1. **Data migration**, a one-off script you run but do **not** commit, e.g. in the scratchpad. Use
   `scripts/gamedata_edit.py`; read its API first.
   - For every recipe, set `display_name` to the `display_name` of the item whose `id` equals `output_item`.
   - Fission (two products): name it after `output_item`; the co-product appears on the method line.
   - Keep `id` and everything else unchanged.
   - Check the diff: only `display_name` lines change, plus `method_label` where added.
2. **`method_label`:** an optional string (default `""`) added to the JSON model, the importer and
   `RecipeSO.methodLabel`.
   - Set it only where two recipes in the **same building** make the same product and inputs alone don't read
     clearly. Example: the Fusion Reactor's Helium-4 from 4 Hydrogen gets "p-p chain".
   - Keep it to a handful of recipes.
3. **`RecipeSO.MethodLine()`**, a pure function in a static helper `RecipeText` so EditMode can test it without
   assets, returning e.g.:
   - `"<Building display name> · 1 Proton, 1 Neutron, 1 Electron"` (first valid building + inputs);
   - `"Fusion Reactor · p-p chain (4 Hydrogen)"` when `methodLabel` is set;
   - for byproducts or co-products, append `" → + Krypton, 3 Neutron"`;
   - for no inputs (harvesters), just the building name.
   - Use the `·` (middle dot) and `→` characters. This is C#, not PowerShell, so that's fine. Check the font renders
     them: if `·` shows as a box in UI Toolkit, use `-` instead and note it.
4. **Picker:** the buttons at both sites show `r.outputItem?.displayName ?? r.displayName`.
   - When **two or more** entries in that button list share the same `outputItem`, add a second smaller label
     inside each of those buttons with `RecipeText.MethodLine(r)`. Use class `recipe-method-line` and add a style to
     the USS actually loaded for the inspector (grep which USS defines `craft-btn`).
   - The button layout must still fit: check the button's flex-direction, and wrap the label column in a
     `VisualElement` if needed.
5. **Text follow-ups:**
   - Update the three tutorial hints and the research description to the new names (e.g. "set its recipe to
     Deuterium").
   - Re-import game data, then run `python scripts/generate-tutorial-walkthrough.py` and
     `python scripts/research_tree.py`. Check each script's usage.
   - Commit the regenerated `RecipeSO` assets (`Assets/Data/recipes/*.asset` or wherever the importer writes them)
     and the tutorial asset.
6. **Lookups by name:** grep `displayName ==` and `displayName)` across `Assets/Scripts` for code that finds a recipe
   by its display name. Switch any such code to `id`, and list them in the report.
   - `Assets/Data/recipes.json` and `RecipesJsonConsistencyTests` may compare names. If that test now fails because
     of the rename, **report it**; don't edit `recipes.json` (phase B deletes it). If the failure blocks the suite,
     relax only the name comparison in that test, with a comment pointing to #287.

## Tests
- **`RecipeValidationTests.cs`:** every recipe's `display_name` equals its output item's `display_name`. Read from
  `game_data.json` the way the existing tests in that file do.
- **New `RecipeTextTests.cs`:** `MethodLine` with inputs, with a method label, with a co-product, and with no inputs.
- **`TutorialDataFlowTests`:** if it checks hints, no hint mentions "Assemble " or "Fuse ".

## Done when
- The Atom Generator's picker shows "Deuterium".
- The Fusion Reactor's picker shows two "Helium-4" buttons, each with its method line.
- The data test passes, and the full EditMode suite passes.
- The game data is re-imported, the generated docs are refreshed, and the tutorial hints are updated.
- Set Status to done with the PR number.
