# 0003: One `game_data.json` as the source of truth for all content

**Status:** Accepted

## Context

The game has hundreds of interlinked content records: 186 items, 199 recipes, 28 buildings, 48 research nodes, fields, sites, worlds, managers, dialogue, daily rewards and 84 tutorial steps. Authoring them as individual Unity `.asset` files caused real problems:
- Cross-references broke silently.
- Diffs were unreadable YAML.
- Bulk edits (rebalancing every price, say) meant clicking through the Inspector.
- Scripts and AI agents couldn't safely read or change content.

## Decision

- All content lives in `Assets/Data/game_data.json`. Objects reference each other by string `id`, and keys starting with `_` are inline documentation.
- An editor importer (`GameDataImporter`, menu **MobileIdleBuilder > Import Game Data**) validates the file and generates the ScriptableObjects and databases the runtime uses. Generated assets are never edited by hand.
- The runtime and save files use numeric ids assigned on the ScriptableObjects. JSON stays readable and saves stay compact.

## Consequences

- Content changes are reviewable text diffs, and tooling can transform the data: `scripts/rebalance_curve.py`, `scripts/gamedata_edit.py` and the research-tree editor all write the JSON.
- Validation lives in one place. Tests check data invariants, for example that every recipe's inputs exist, that recipe names match their product, and that removed tutorial steps map to a resume point.
- Every content change needs a re-import, and a stale import is a classic "works on my machine" bug. The data-pipeline doc and the commit workflow both call for re-importing.
- Enums used by serialized data are append-only, so existing saves and assets keep their values.
