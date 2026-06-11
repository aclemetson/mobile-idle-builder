# Data Pipeline: game_data.json → ScriptableObjects

**Scope:** How game content is authored, imported, and consumed. Read before adding/changing items, recipes, buildings, research, tiers, fields, dialogue, or any new content type.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## Single source of truth

`Assets/Data/game_data.json` (~5,400 lines). Top-level keys:
`_meta`, `game_config`, `tiers`, `research`, `items`, `recipes`, `buildings`, `fields`, `dialogues`, `tutorial_steps`.

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

Generated asset locations: `Assets/Data/settings|tiers|research|recipes|buildings|fields|dialogue|tutorial/` and `Assets/Resources/Items/`, `Assets/Resources/ResearchDatabase.asset`.

## HARD RULE

After ANY edit to `game_data.json` or `achievements.json`:
1. Validate the JSON parses (no trailing commas; `JsonUtility` is strict).
2. The importer must run before testing in-editor. It auto-runs on recompile if the JSON is newer than the tutorial asset, but if you are not recompiling, tell the user to run **MobileIdleBuilder → Import Game Data** manually.
3. Never hand-edit the generated `.asset` files.

## Example entries (trimmed)

```jsonc
// item
{ "id": "dyson_node", "display_name": "Dyson Node", "tier": "tier_5",
  "base_sell_value": 5000000000000, "icon_path": "TODO", "codex_entry": "..." }

// recipe (inputs/output are string item ids)
{ "id": "orbital_frame", "inputs": [ { "item": "dyson_node", "quantity": 2 } ],
  "output_item": "orbital_frame", "output_quantity": 1,
  "base_craft_time": 30.0, "required_research": "megastructure_theory" }

// research
{ "id": "megastructure_theory", "display_name": "Megastructure Theory",
  "cost_base_currency": 50000000, "prerequisites": ["component_engineering"],
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

## Pitfalls

- `JsonUtility` does not support dictionaries — use parallel key/value lists or entry classes (see `SaveData.inventoryKeys/inventoryValues` for the established pattern).
- Bad string cross-refs fail as importer **warnings**, not errors — grep the Unity console output for `[GameDataImporter]` after importing.
- Bump `_meta.version` when adding content sections.
- The prestige-shop upgrade catalogue is NOT in game_data.json — it is hardcoded in `PersistentUpgradeService.cs:48`. Don't look for it in JSON.
