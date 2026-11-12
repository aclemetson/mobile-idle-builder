# Feature: auto-sell items that arrive at a full input

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ecs-patterns.md`, `economy-balance.md`
(Surplus Brokerage, value readouts, Statistics), `save-system.md` (idle/offline), `testing.md`.
**Status:** pending.
**Size:** M. **Branch:** `feat/full-input-autosell` from `integration/2026-11-11`. **Order:** independent; can land
before or after the belt tasks.

## Problem
Auto-selling exists (`SurplusSale`, #312: 50% of the Demon price, +10% per level of the **Surplus Brokerage**
prestige upgrade `surplus_resale`, max 100%), but it only fires on a **mixed** belt that jams, when an item the
recipe is short of waits behind the front item (`InputAcceptance.Decide` → `Overflow`). A belt carrying one item
type to a full input just backs up, so the planned "build-up gets sold" never happens: the harvester stalls and its
output is lost instead of earning anything.

## Design (user-approved 2026-11-11: full inputs only)
- When an item arrives at an input whose recipe **uses** it and that item's buffer is at its cap
  (`BuildingInventoryConfig.InputCapacity`, per item type), the building takes it and sells it at
  `SurplusSale.CurrentFraction()` of its Demon price. The belt keeps moving.
- **Unchanged:** an item the recipe doesn't use still waits on the belt (a wrong belt stays visible), except for the
  existing jam rule. A building with **no recipe set** still waits. Maxwell's Demon still pays full price.
- With several inputs next to one belt tail (`TailInputPriority`), sell only when no candidate input would accept
  the item; never sell into one building while another adjacent input has room.
- Input Capacity upgrades keep their value: a bigger buffer smooths bursts before anything is sold.

## Implementation
1. `InputAcceptance.Decide`: return `Overflow` for `usable && countOfItem >= perItemCap && recipeSet`. Update its XML
   doc (the "a belt carrying one item type never overflows" line becomes wrong).
2. `ConveyorSystem` overflow path already sells at the surplus fraction with carry (`_surplusCarry`); confirm it
   credits `PlayerProgressData` and the Statistics automation income, and that `TryPickInput` prefers an accepting
   input over an overflowing one.
3. **Show it:** the building inspector gets a line "Selling surplus: X/s at 50%" while it overflows (rate over the
   last few seconds), so the player sees why the number went up. Optional: a small "+◈" pop over the building,
   throttled.
4. **Offline/idle:** `IdleGraphAnalyzer` currently treats a producing building as consuming its suppliers' whole
   output ("consumed internally"). Split each incoming edge into what the recipe uses at steady state and the
   excess, and credit the excess as a chain entry at `price × surplusSaleFraction`, matching live play.
5. Check the tutorial still reads correctly (crafts are counted by `CraftLog`, not inventory) and that the
   `_reserved_research_ids` entry `sell_excess_inputs` in `game_config` is either removed or noted as superseded
   by Surplus Brokerage.

## Tests
- `Decide`: full + used + recipe set → `Overflow`; full + unused → `Wait`; full + no recipe → `Wait`; room → `Accept`.
- Live: a Lv1 Combiner on proton fed 2 up quarks/s sells exactly the up quarks it can't use, at 50% (and at 60%
  with one Surplus Brokerage level); the belt never stops.
- Idle: the same layout's offline snapshot pays the same surplus per second as the live run.

## Docs to update
`economy-balance.md` (Surplus Brokerage paragraph), `save-system.md` / `ecs-patterns.md` idle rows, and the
`InputAcceptance` comment.
