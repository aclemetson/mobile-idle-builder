# Feature: several items per belt tile (belt density)

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ecs-patterns.md` (Conveyor rows, `ResearchEffects`),
`data-pipeline.md` (game_config, importer), `testing.md`.
**Status:** done (PR pending).
**Size:** L. **Branch:** `feat/belt-density` from `integration/2026-11-11`. **Order:** 1 of 4 (the other belt and
balance tasks assume it).

## Problem
A belt tile holds one item (`ConveyorItemData` is a single component on each segment entity). In
`ConveyorSystem` pass 2 an item stops at progress 0.5 until the next tile is completely empty, so a new item can
leave only every 1.5 s: **a belt carries about 0.66 items/s** (simulated with the real pass order). Items don't queue
up next to each other, and every Harvester (1/s) and building output already outruns its belt.

## Design (user-approved 2026-11-11)
- A tile holds up to **`belt_items_per_tile` items, default 2**, kept in game data so it can be tuned later
  (`game_config.belt_items_per_tile`, int 1 to 4, imported into `GameConfigSO`, published to ECS as a singleton
  field so `ConveyorSystem` and `BeltPrimer` read the live value).
- Items keep a minimum spacing of `1 / belt_items_per_tile` tiles **along the whole chain**, across tile
  boundaries too. A blocked line packs at that spacing (no mid-tile parking).
- Throughput = belt speed (tiles/s) / spacing = **2 items/s** at the base speed of 1 tile/s.
  Research speed bonuses (`ResearchEffects.ConveyorSpeedMult`) multiply it, see `feature-belt-speed-research.md`.

## Implementation
1. **Data:** replace the per-segment `ConveyorItemData` component with a `DynamicBuffer<ConveyorItem>`
   `{ int ItemID; float Pos; }` ordered front (highest `Pos`) to back. Keep the empty-buffer check cheap (it is the
   hot "occupied?" test today). Belt items are not saved (`ConveyorSaveData` stores cells only), so no save
   migration is needed.
2. **Movement (pass 2):** walk each chain from tail to head. The front item of a segment may advance to 1.0 only if
   the next segment (or the tail's target input) has room: the next segment's rearmost item is at `Pos >= spacing`
   minus the overshoot. Each item behind advances to at most `ahead.Pos - spacing`. Use the same `dt * speedMult /
   TransportTime`.
3. **Transfer (pass 3):** move an item into the next segment at `Pos - 1`. Merge cells keep the `MergeCursor`
   round-robin, offering entry only when the target has room. Tail deposit, `InputAcceptance`, `Overflow` and
   `TailInputPriority` are unchanged; they act on the front item.
4. **Head pull (pass 4):** a chain head takes from the building output when its rearmost item is at
   `Pos >= spacing` (or the head is empty). Splitters (`PullFromSplitter`) and Filters follow the same rule.
5. **`BeltPrimer`** (idle/offline belt priming): fill each primed belt at the new spacing. **`IdleGraphAnalyzer`**
   uses steady-state rates, never belt time, so it needs no change; confirm it.
6. **`ConveyorVisualizer`:** draw every item in the buffer at its `Pos`. Pool item visuals so a packed belt doesn't
   allocate each frame. Check the item sprite size reads at 2 per tile on a phone.
7. **Tutorial and checks:** grep `ConveyorItemData` (ConveyorSystem 22 uses, ConveyorVisualizer, BeltPrimer, and
   tests: ConveyorHandoff, ConveyorMerge, Splitter, Filter, ResearchEffects, PrestigeSystem, DecayChain,
   AtomGeneratorPort). `TutorialSystem` link checks read cells, not items; confirm.

## Tests
- Throughput: a straight belt with an infinite source and sink delivers `speed / spacing` items/s (±2%) for
  `belt_items_per_tile` = 1, 2 and 4, at speed 1 and with a 2x research multiplier.
- A blocked belt packs to exactly `belt_items_per_tile` items per tile and never overlaps closer than the spacing.
- Spacing holds across a tile boundary and through a merge cell (two inbound belts alternate).
- Splitter and Filter still alternate and route; existing tests are updated to the buffer API, not deleted.

## Docs to update
`ecs-patterns.md` (Conveyor rows), `economy-balance.md` (belt throughput line), and the Ignition Production
Balance artifact's belt note (https://claude.ai/artifact/KMM9xMKKnGiCgY3WFwAoEs).
