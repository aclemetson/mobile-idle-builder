# Data Pipeline: game_data.json → ScriptableObjects

**Scope:** How game content is authored, imported, and consumed. Read before adding/changing items, recipes, buildings, research, tiers, fields, dialogue, or any new content type.

> Verified against: `0af6ae2`, 2026-11-07 (recipes read from the importer output, #336 phase B). If code contradicts this doc, trust the code and update this doc.
> Power Relay added (`building_id 13`) 2026-07-03 — next free `building_id` is 14.
> `worlds` section + `WorldSO`/`WorldDatabaseSO` added 2026-07-06 (Chem/Bio Phase 1); `_meta.version` bumped to 0.3.9.
> Chem/Bio Phase 2 (2026-07-06, `_meta.version` 0.3.10): **field "type" is now a data-driven string** (`FieldSO.fieldType`/`BuildingSO.compatibleFields`/`TutorialFlowSO.collectionFilter` are `string`/`string[]`; the `FieldType` enum is gone — use the `FieldTypes` helper; JSON was already string-valued). New `ItemCategory.OrganicCompound` + items 150–153; element fields; `site_chem_lab`. New items still need a generated tile — run **Generate Element Icons** then **Import Game Data** (a new item with no icon fails `ItemIconTests`).
> Chem/Bio Phase 4a (2026-07-06, `_meta.version` 0.3.11): `chemistry_lab` research; `element_harvester` (`building_id 14`) + `compound_synthesizer` (`building_id 15`); 6 `collect_<element>` + 3 compound recipes (`recipe_id 164–172`); Molecule items `carbon_dioxide`/`table_salt`/`sulfuric_acid` (150–156). Field-type `"ElementMetal"` added.
> Chem/Bio Phase 4b + full Biology (2026-07-09, `_meta.version` → 0.3.14): C2–C4 + Biology B1–B4 content. Enums grew: `ItemCategory` += `Biomolecule`/`CellPart`/`Organism`; `ResearchBranch` += `Biology` (both **appended** to preserve serialized ints). Buildings 16–23 (metal_harvester, catalytic_reactor, organic_synthesizer, organic_harvester, biosynthesizer, cell_assembler, tissue_culture, bioreactor) — **next free `building_id` is 24**. Recipes 173–206 — **next free `recipe_id` is 207**. Items 157–178 (8 chem C2–C3 + 14 biology) — **next free `item_id` is 179**. New field-type `"Organic"`. New worlds `world_biology` + site `site_bio_lab` + 4 organic fields. Icons regenerated (run the batch **twice** for brand-new items — see the two-pass note below).

## Single source of truth

`Assets/Data/game_data.json` (~5,400 lines). Top-level keys:
`_meta`, `game_config`, `tiers`, `research_blocks`, `research`, `items`, `recipes`, `buildings`, `fields`, `sites`, `worlds`, `managers`, `dialogues`, `daily_rewards`, `daily_challenges`, `tutorial_steps`, `megastructure`.
- **Research blocks** (`research_blocks`, 2026-11-02): game phases with `id`, `name`, `order`, `target_hours`, `anchor`. Every `research[]` node names its `block`. Imported into `ResearchDatabaseSO.blocks` (with each block's first-arrival state for the dev console's `block load`) and `ResearchSO.block` / `treeX` / `treeY` (the in-game research tree). They also drive `scripts/rebalance_curve.py`, `scripts/progression_curve.py` and the research tree editor. `research[].cost_base_currency` is a `long`. **Research effects (#339):** `research[].effects` is a list of `{ "type", "value" }` (`ResearchEffectJson` -> `ResearchSO.effects` of `ResearchEffect`, type = `ResearchEffectType` name; an unknown type logs an error and is skipped). Values of one type add up across unlocked research; only `ConveyorSpeed` is wired (`ResearchEffects` singleton, see `ecs-patterns.md`). `field_cooldown_mult` remains the separate scalar modifier.

Separate file: `Assets/Data/achievements.json` → imported by `Assets/Scripts/Editor/AchievementImporter.cs`.

### Conventions (from `_meta.conventions` — these are enforced)

- **Cross-refs use the string `id`** of the target object (e.g., `"required_research": "megastructure_theory"`, `"output_item": "dyson_node"`). The importer resolves them to SO references.
- **Exception:** `tutorial_steps` reference items by **numeric** `item_id`. The save file and ECS runtime also use numeric `itemId`/`recipeId`/`buildingId` (assigned on the SOs). So: JSON authoring = string ids; runtime/save = numeric ids.
- **Tutorial `on_enter` guidance:** `pulse_button_id` names the button the step ultimately wants tapped; `TutorialPulseResolver` pulses the next visible step towards it (drawer handle, then the panel button, then the target). Build-menu Place buttons are `btn-place-<building id>` / `btn-place-conveyor`. `highlight_target` also accepts `route:<from>><to>` (start and end tiles of the next belt), `suggest:<building>:<from>><to>` (where to place a building), and `power:<building>` (nearest free spot inside a placed generator's influence circle, so the building gets power; spots whose every port front is free for a belt and not beside Maxwell's Demon win, #275; `power:<building>@<near>` ranks those spots by how close the building's inputs are to `<near>`'s output tiles instead, e.g. `power:fusion_reactor@atomic_assembler`, #311), with BuildingSO ids as keys. A bare BuildingSO id (`atomic_assembler`, `fusion_reactor`) highlights the first placed building of that type; see `TutorialHighlighter` / `TutorialRouteGuide`. Placement glows (`suggest:`, `power:`, a field id) clear whenever any building is placed and, if the step has not advanced 0.5s later, come back recomputed without a camera pan; a field glow skips fields already built on (#308). `route:` and placement targets keep searching until the step ends instead of timing out after 15s, so a route whose start tile is taken (e.g. `reroute_neutrons_to_atom_gen`, after the old belt is deconstructed) glows once it frees up (#307).
- **Tutorial `advance_condition` for world state:** prefer conditions that read the live game over one-shot UI events, which are lost if the player acts during an earlier step. `ConveyorLinked` (`from_building_id` optional = any, `building_id` = target, `min_count` = distinct sources) passes when that many sources have a belt whose tail sits in front of a target input (with `from_building_id` = `building_id`, e.g. reactor to reactor, every building of that type is both a source and a target; the matching `route:fusion_reactor>fusion_reactor` glow runs from the reactor whose recipe makes what another reactor's recipe consumes); `BuildingUpgradeMin` (`building_id`, `min_level`) passes when a building of that type reaches that speed level. `BuildingRecipeSet` (`building_id`, numeric `recipe_id`) passes when a building of that type has that recipe active (the `combiner_recipe_set` UiEvent fires for any recipe, so it can't tell Neutron from Proton). `ItemCraftedMin` (`items`) passes when each item was crafted that many times this session (`CraftLog`, fed by manual crafts and `ProductionAchievementBridge`) or is held; use it instead of `InventoryMin` for automated output, which a belt sells at the Demon before it reaches the inventory. `maxwells_demon` is matched by its `EntropySinkTag`. These are evaluated in `TutorialSystem`, so saves already past the action advance on load. `TutorialSystem.CurrentStepConveyorLinks` publishes the live link count for a `ConveyorLinked` step (-1 otherwise); when a placed belt adds no link, `TutorialOverlayController` warns "Belt not linked" and re-shows the step's route highlight (#255).
- **Step reward:** `reward_entropy` on a step adds that much entropy once, when `TutorialSystem` advances the step (never on entry, which re-runs on load). It is ignored on `UiEvent` steps (the importer warns), because those advance in `TutorialOverlayController`.
- **Dialogue pause:** if any line of a dialogue has `pause_game: true`, the whole dialogue pauses from its first line until it closes (`DialogueController.PausesGame`); it never toggles page by page. A step with both a dialogue and a `hint` shows the hint bar `TutorialOverlayController.HintRevealDelaySeconds` (2s) after the dialogue closes, cancelled if the step changes or another dialogue opens first, so the bar never flashes between back-to-back dialogues (#238).
- **Removing a tutorial step:** saves store the step's string id, and an unknown id restarts the tutorial at step 0. Add the removed id to `ECSLoadBridge.RetiredSteps` (removed id -> step to resume at); `SaveTutorialStepTests` checks every entry is gone from `game_data.json` and points at a live step.
- **Fields for sale (Field Synthesis):** a `fields[]` entry with `purchase_cost_base > 0` appears in the Build menu once `purchase_research` is finished. `purchase_cost_multiplier` scales each purchase this run and `purchase_group_size` fields are placed side by side per purchase (quark fields are always a pair). The Place button is `btn-place-<field id>`. Tutorial condition `FieldPurchasedMin` (`field_id`, `min_count` = purchases, a pair counts once) reads the run's purchase count. Runtime: `FieldPurchaseService`, `BuildingPlacementController.BeginFieldPlacement`.
- **Recipes at runtime (#287, #336):** `game_data.json` is the only recipe source (`recipes.json` is gone). `GameDataImporter` writes `RecipeSO` assets and `Assets/Resources/RecipeDatabase.asset` (`RecipeDatabaseSO`); `RecipeDatabase.Load()` converts them with `RecipeDatabase.FromSO`/`ConvertAll` to the `RecipeJson` view (`crafted_in` = valid building ids, preceded by `inventory_menu` when `can_craft_manually`). Recipes with no inputs (harvest `collect_*`) are dropped. The Recipes panel and codex drive off this list: a recipe appears only once `ResearchService` marks it known, which matches its `requires_research` against finished research (also backfilled on load). The panel shows one row per output item (`RecipeGrouping.Group`), with a "N ways to make this" toggle when an item has several methods. Saved recipe ids that no longer exist are mapped in `LegacyRecipeIds.Map` (applied to `recipe_knowledge.json` on load and to `currentRun.craftQueue` in `ManualCraftService.Queue`); retire a recipe id the same way. `RecipeSourceTests` checks the runtime list against `game_data.json`.
- **Recipe names (#336):** a recipe's `display_name` is its output item's `display_name` (a test enforces it); the picker tells same-product recipes apart with a second line from `RecipeText.MethodLine` (building, inputs, optional `method_label`, non-escaping co-products).
- **Escaping byproducts:** an item with `escapes_as_entropy: true` (neutrino, gamma photon) listed in a recipe's `byproducts[]` pays `quantity x base_sell_value` entropy on every craft instead of being produced (`RecipeSO.EscapeEntropyPerCraft`). The runtime `RecipeJson` carries the same `byproducts` list so queued Recipe-panel crafts pay it too. Other byproducts are inert.
- **Field buildings and their recipes:** a `MustBeOnField` building offers the recipes that collect one of the field's drops; when none do, every input-free recipe it supports (the 2x1 Nucleon Harvester makes `harvest_proton`/`harvest_neutron` on quark fields). Both placement and the inspector use `FieldRecipes.For`.
- **Filter buildings (#321):** `is_item_filter: true` makes a recipe-less building route items: each item leaves by the output side its rules name, otherwise by the "everything else" side, otherwise it waits in the input (`PortFilterRules`). A new Filter sends everything straight through (the output facing the same way as the input). Rules live in the `OutputPortFilter` buffer and save as `BuildingSaveData.portFilters` ([facing, itemId, …], itemId -1 = everything else).
- **Byproducts:** a recipe's `byproducts[]` are produced as real items next to the main output (`RecipeSO.ProducedOutputs`), except those with `escapes_as_entropy`, which pay entropy instead. `assignable_outputs: true` on a building lets the player pick which output side each product leaves by (the Filter's rules without its pass-through behaviour).
- **Recipe modes:** `recipe_mode_labels` + `recipe_mode_prefixes` (parallel arrays) split a building's recipes into groups the inspector shows one at a time (Neutron Capture: Slow `s_capture_` / Fast `r_capture_`). `power_cost_is_dynamic: true` makes the building draw its active recipe's `fixed_power_cost_ev` when that is higher than its own.
- **Obsolete buildings:** `is_obsoleteable` + `obsoleted_by_research` (a research id) make the Build menu show "Obsolete. <obsolete_condition>" once that research is done; the building stays placeable.
- Keys prefixed with `_` are documentation comments, ignored by the importer.
- Asset paths use `"TODO"` when unknown — importer warns and leaves the field null (this is normal; many icons are TODO).
- `null` and `[]` are equivalent for arrays.

## The importer

`Assets/Scripts/Editor/GameDataImporter.cs` — Editor menu **MobileIdleBuilder → Import Game Data**. Also auto-runs after script compilation when `game_data.json` is newer than `Assets/Data/tutorial/tutorial_flow.asset` (`AutoImportIfMissing()`, `GameDataImporter.cs:51`).

Generation order is a strict dependency chain (documented in the file header): GameConfig → Tiers pass 1 → Research pass 1 → Items → Recipes → Buildings → wire RecipeSO.validBuildings → Research pass 2 → Fields → Dialogue → Tiers pass 2.

JSON is parsed with `JsonUtility` into `GameDataJson` (`Assets/Scripts/Editor/GameDataModel.cs`) — **so new JSON fields require matching C# fields in GameDataModel.cs or they are silently ignored.**

Generated asset locations: `Assets/Data/settings|tiers|research|recipes|buildings|fields|dialogue|tutorial/` and `Assets/Resources/Items/`, `Assets/Resources/ResearchDatabase.asset`, `Assets/Resources/DailyContent.asset` (login calendar + challenge pool).

## HARD RULE

After ANY edit to `game_data.json` or `achievements.json`:
1. Validate the JSON parses (no trailing commas; `JsonUtility` is strict).
2. The importer must run before testing in-editor. It auto-runs on recompile if the JSON is newer than the tutorial asset, but if you are not recompiling, tell the user to run **MobileIdleBuilder → Import Game Data** manually.
3. Never hand-edit the generated `.asset` files.

## Editing surfaces

Two tools edit `game_data.json` directly; both still require the importer to run afterward (see HARD RULE):

- **In-Unity:** `MobileIdleBuilder → Game Data Editor` (`GameDataEditorWindow.cs`). Saves via `JsonUtility.ToJson`, which **drops all `_comment`/`_note_*` doc keys** and fully reformats.
- **Browser:** `docs/balance-editor.html` — a balance/progression editor for designers. Presents an item-centric join view (base sell value, isotope multiplier, joined recipe craft time + `valid_buildings`, unlocking research, tutorial flag) plus Recipes/Buildings/Research/Config tabs and a Rebalance tab (deterministic "house rules" + a copy/paste Claude bridge). It reads/writes the real `game_data.json`.
  - **Launch:** it uses the File System Access API, which needs a localhost origin and Chrome/Edge — run a static server in `docs/` (e.g. `python -m http.server 8080`) and open `http://localhost:8080/balance-editor.html`. It does **not** work over `file://`. Non-Chromium browsers fall back to file-input load + download save.
  - **Comment-safe:** the browser's `JSON.parse` keeps `_`-prefixed keys and insertion order, and a custom serializer (`serializeGameData`) inlines primitive arrays and preserves `.0` float formatting. Round-trip is data-identical (verified deep-equal) and **comments survive**. The **first** save canonicalizes the file's hand-formatting (one-time large, data-neutral diff — commit it on its own); every save after that is a minimal, reviewable diff.
  - **`_in_tutorial`:** the editor's per-item "Tutorial" checkbox writes a doc-only `_in_tutorial` boolean on the item. Underscore-prefixed, so the importer ignores it — it is design metadata only, distinct from actual `tutorial_steps` membership (shown read-only as "In steps").
  - It does **not** update the `docs/gameplay_loop_data.js` mirror — that stays hand-maintained.

## Example entries (trimmed)

```jsonc
// item
{ "id": "dyson_node", "display_name": "Dyson Node", "tier": "tier_5",
  "base_sell_value": 5000000000000, "icon_path": "TODO", "codex_entry": "..." }

// recipe (inputs/output are string item ids)
{ "id": "orbital_frame", "inputs": [ { "item": "dyson_node", "quantity": 2 } ],
  "output_item": "orbital_frame", "output_quantity": 1,
  "base_craft_time": 30.0, "required_research": "megastructure_theory" }

// research  (duration_seconds = research timer; 0 = instant unlock. See economy-balance.md for the depth curve.)
{ "id": "megastructure_theory", "display_name": "Megastructure Theory",
  "cost_base_currency": 50000000, "duration_seconds": 14400,
  "prerequisites": ["component_engineering"],
  "unlocks_items": ["dyson_node"], "unlocks_recipes": ["dyson_node"] }
```

## Adding a NEW content type (new SO + JSON section)

Copy the `ResearchSO` handling end-to-end:
1. SO class in `Assets/Scripts/ScriptableObjects/` (mirror `ResearchSO.cs` style).
2. JSON model class + field in `GameDataJson` (`Assets/Scripts/Editor/GameDataModel.cs`).
3. New top-level array in `game_data.json` (follow `_meta.conventions`).
4. Import pass in `GameDataImporter.RunImport()` — respect the dependency order; if your type references items/recipes, import after them (two-pass if it is also referenced by earlier types).
5. Consumption: load via direct SO reference on a MonoBehaviour, or a `Resources/` database asset like `ResearchDatabaseSO`.
6. EditMode test validating the import (copy `Assets/Scripts/Tests/SOSchemaTests.cs` pattern).

## Buildings: capacity upgrade tracks

`buildings[]` entries carry three parallel per-building upgrade arrays, all mapped by `GameDataImporter` to `BuildingSO` and bought in the inspector:
- `upgrade_levels` → `upgradeLevels` (speed / power scaling)
- `storage_upgrade_levels` → `storageUpgradeLevels` (output buffer capacity)
- `input_upgrade_levels` → `inputUpgradeLevels` (input buffer capacity **per item type**; `max_input_items` per tier; enforced by `InputAcceptance` in `ConveyorSystem`)

The two capacity tracks are research-gated in the UI (`surplus_containment` for output, `feedstock_buffers` for input — see `economy-balance.md`). Baselines come from `base_max_output_items` / `base_max_input_items_per_slot`.

## Pitfalls

- `JsonUtility` does not support dictionaries — use parallel key/value lists or entry classes (see `SaveData.inventoryKeys/inventoryValues` for the established pattern).
- Bad string cross-refs fail as importer **warnings**, not errors — grep the Unity console output for `[GameDataImporter]` after importing.
- Bump `_meta.version` when adding content sections.
- The prestige-shop upgrade catalogue is NOT in game_data.json — it is hardcoded in `PersistentUpgradeService.cs:48`. Don't look for it in JSON.
