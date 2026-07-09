# Data Pipeline: game_data.json → ScriptableObjects

**Scope:** How game content is authored, imported, and consumed. Read before adding/changing items, recipes, buildings, research, tiers, fields, dialogue, or any new content type.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.
> Power Relay added (`building_id 13`) 2026-07-03 — next free `building_id` is 14.
> `worlds` section + `WorldSO`/`WorldDatabaseSO` added 2026-07-06 (Chem/Bio Phase 1); `_meta.version` bumped to 0.3.9.
> Chem/Bio Phase 2 (2026-07-06, `_meta.version` 0.3.10): **field "type" is now a data-driven string** (`FieldSO.fieldType`/`BuildingSO.compatibleFields`/`TutorialFlowSO.collectionFilter` are `string`/`string[]`; the `FieldType` enum is gone — use the `FieldTypes` helper; JSON was already string-valued). New `ItemCategory.OrganicCompound` + items 150–153; element fields; `site_chem_lab`. New items still need a generated tile — run **Generate Element Icons** then **Import Game Data** (a new item with no icon fails `ItemIconTests`).
> Chem/Bio Phase 4a (2026-07-06, `_meta.version` 0.3.11): `chemistry_lab` research; `element_harvester` (`building_id 14`) + `compound_synthesizer` (`building_id 15`) — **next free `building_id` is 16**; 6 `collect_<element>` + 3 compound recipes (`recipe_id 164–172`, **next free 173**); Molecule items `carbon_dioxide`/`table_salt`/`sulfuric_acid` (150–156, **next free `item_id` 157**). Field-type `"ElementMetal"` added. Icons regenerated.

## Single source of truth

`Assets/Data/game_data.json` (~5,400 lines). Top-level keys:
`_meta`, `game_config`, `tiers`, `research`, `items`, `recipes`, `buildings`, `fields`, `sites`, `worlds`, `managers`, `dialogues`, `daily_rewards`, `daily_challenges`, `tutorial_steps`, `megastructure`.

Separate file: `Assets/Data/achievements.json` → imported by `Assets/Scripts/Editor/AchievementImporter.cs`.

### Conventions (from `_meta.conventions` — these are enforced)

- **Cross-refs use the string `id`** of the target object (e.g., `"required_research": "megastructure_theory"`, `"output_item": "dyson_node"`). The importer resolves them to SO references.
- **Exception:** `tutorial_steps` reference items by **numeric** `item_id`. The save file and ECS runtime also use numeric `itemId`/`recipeId`/`buildingId` (assigned on the SOs). So: JSON authoring = string ids; runtime/save = numeric ids.
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
- `input_upgrade_levels` → `inputUpgradeLevels` (input buffer capacity; `max_input_items` per tier)

The two capacity tracks are research-gated in the UI (`surplus_containment` for output, `feedstock_buffers` for input — see `economy-balance.md`). Baselines come from `base_max_output_items` / `base_max_input_items_per_slot`.

## Pitfalls

- `JsonUtility` does not support dictionaries — use parallel key/value lists or entry classes (see `SaveData.inventoryKeys/inventoryValues` for the established pattern).
- Bad string cross-refs fail as importer **warnings**, not errors — grep the Unity console output for `[GameDataImporter]` after importing.
- Bump `_meta.version` when adding content sections.
- The prestige-shop upgrade catalogue is NOT in game_data.json — it is hardcoded in `PersistentUpgradeService.cs:48`. Don't look for it in JSON.
