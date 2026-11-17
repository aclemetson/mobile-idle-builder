# Feature: belt speed research tiers

**Required reading:** `docs/agents/README.md`, `architecture.md`, `data-pipeline.md` (research `effects`),
`ecs-patterns.md` (`ResearchEffects`), `economy-balance.md` (research blocks, derived costs), `testing.md`.
**Status:** done (PR pending). User chose: warn only (no gating); Transportation II last in Foundations, off-curve (`advances_curve: false`).
**Size:** M. **Branch:** `feat/belt-speed-research` from `integration/2026-11-11`. **Order:** 4 of 4 (after
`feature-belt-density.md` and `feature-ignition-rebalance.md`).

## Problem
The only belt speed bonus is Transportation I (+10%, `transportation_1`, 62,400 ◈, Foundations block). Building
speed doubles each level (1, 2, 4, 8), so with belts at a fixed 2 items/s every Lv3 or Lv4 building outruns its
belts and the upgrade is wasted.

## Design (user-approved 2026-11-11: global research tiers, Dyson Sphere Program style)
Belt speed is raised only by research, for every belt at once. Each tier lets one more building level run full:

| Research | Total belt speed | Belt throughput (2 per tile) | Runs full up to |
|---|---|---|---|
| (none) | 1 tile/s | 2 items/s | Lv2 |
| Transportation I | ×2 | 4 items/s | Lv3 |
| Transportation II | ×4 | 8 items/s | Lv4 |

- Uses the existing `ConveyorSpeed` research effect (values add up: T-I `+1.0`, T-II `+2.0`). No new code path for
  the speed itself.
- **Placement and cost:** T-I goes where Lv3 upgrades first become affordable, T-II where Lv4 does. Its cost comes
  from the derived economy (`scripts/rebalance_curve.py`, research blocks), never hand-set. Show the proposed
  spots in the research tree editor before applying.
- **Warn instead of silently wasting an upgrade:** when a speed upgrade would push a building past what its belts
  can carry, the inspector's upgrade row says so ("Belts carry 2/s; Lv3 needs 4/s. Research Transportation I.").
  The upgrade stays purchasable. Gating Lv3/Lv4 behind the research is the alternative; ask the user before doing it.

## Implementation
1. Rewrite `transportation_1` (description, `effects` value 1.0) and add `transportation_2` to `game_data.json`,
   with prerequisites and blocks; re-import.
2. Inspector warning: compare the next level's peak per-input and output item rate (recipe quantities × speed /
   craft time) with `belt speed × belt_items_per_tile`. Put the pure function in a static helper and unit-test it.
3. Re-run the research rebalance so costs land on the curve; resync `ItemBalanceTests` / research pins.

## Tests
- `ConveyorSpeedMult` is 3.0 with both tiers (1 + 1.0 + 2.0), and 1.0 after prestige.
- Warning helper: Lv2 Combiner on proton at base speed passes, Lv3 fails, Lv3 with T-I passes.

## Docs to update
`economy-balance.md` (belt tiers table), `ecs-patterns.md` (`ResearchEffects` row), the balance artifact.
