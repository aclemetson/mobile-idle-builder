# Feature: Ignition production rebalance (quarks to Carbon) and harvester upgrade fix

**Required reading:** `docs/agents/README.md`, `architecture.md`, `data-pipeline.md`, `economy-balance.md`
(tutorial anchor, derived economy), `ecs-patterns.md` (collectors), `testing.md`.
**Status:** done (PR pending); first-session pacing playtest still to run.
**Size:** M. **Branch:** `feat/ignition-rebalance` from `integration/2026-11-11`. **Order:** 2 of 4 (after
`feature-belt-density.md`; the craft times assume belts carry 2 items/s).

## Problem
1. **Harvester speed upgrades do nothing.** `CollectorData.OutputRate` is set from `baseOutputRate` at placement
   (`BuildingPlacer` ~:154, `BuildingAuthoring` ~:129) and never again. `OnSpeedUpgradeBought` only writes
   `BuildingData.ProductionSpeed`, which `CollectorSystem` never reads. Lv2 to Lv4 cost 10,500 ◈ for nothing.
2. **Lines don't match.** Craft times weren't set against what their suppliers deliver. Deuterium (1.5 s) needs
   0.67 protons/s and 0.67 neutrons/s; D + D fusion (1 s) needs 2 deuterium/s; neither supplier comes close.

## Design (user-approved 2026-11-11)
Values come from the Ignition Production Balance editor (https://claude.ai/artifact/KMM9xMKKnGiCgY3WFwAoEs). Read
`balance/working` with ArtifactData first; if it isn't saved, use the proposal below.

| Recipe | Building | Craft time (s) | Ratio it gives at any level |
|---|---|---|---|
| `proton`, `neutron` | Strong Force Combiner | 1 → **2** | 1 Harvester per Combiner on the 2-quark input |
| `hydrogen` | Atom Generator | 2 | 1 Combiner : 1 Atom Generator |
| `assemble_deuterium` | Atom Generator | 1.5 → **2** | 1 proton + 1 neutron Combiner : 1 Atom Generator |
| `helium_4` | Atom Generator | 4 | 1 + 1 Combiners : 1 Atom Generator |
| `fuse_deuterium` | Fusion Reactor | 1 → **4** | 1 Deuterium Atom Generator : 1 reactor |
| `fuse_helium` | Fusion Reactor | 2 → **4** | 2 Hydrogen Atom Generators : 1 reactor |
| `fuse_carbon` | Fusion Reactor | 6 | 2 helium reactors : 1 Carbon reactor |

Level speeds stay ×2 per level (1, 2, 4, 8) for Harvester, Combiner, Atom Generator and Fusion Reactor; costs are
unchanged. Item values don't change (tutorial items anchor the derived economy).

## Implementation
1. **Harvester fix:** set `CollectorData.OutputRate` from `BuildingSO.ProductionSpeedForLevel` (harvester
   `upgrade_levels.output_rate`) at placement, on speed upgrade and on load. Keep managers' CraftSpeed ignored by
   collectors (documented). Make the inspector's "Output rate" and `IdleGraphAnalyzer` read the same value; add a
   regression test that an upgraded harvester produces at its level's rate after a save/load.
2. **Craft times:** edit `game_data.json`, re-import (batchmode
   `-executeMethod MobileIdleBuilder.Editor.GameDataImporter.Import -quit` with the Editor closed, or the MCP), and
   check the manual-craft queue's unit time (same craft times) still feels right.
3. **Pacing:** run the `first-session` playtest and compare against the 45 to 60 minute tutorial target. Lines run
   about 1.5× faster at Lv1 than today, so the tutorial will likely shorten; report the measured time before
   touching research costs.

## Tests
- Collector rate per level (1, 2, 4, 8) after placement, upgrade and load.
- Pin the new craft times in the recipe balance tests that already pin tutorial values.

## Docs to update
`economy-balance.md` (Ignition craft times and ratios), `ecs-patterns.md` (collector rate row), and the balance
artifact if the shipped values differ from the saved ones.
