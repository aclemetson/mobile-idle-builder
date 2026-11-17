# 0004: Derive item values and research costs from a progression curve

**Status:** Accepted (PRs #333, #338)

## Context

Idle-game balance is a chain. An item's sell value sets income, income sets how long the next research takes to afford, and that research unlocks the next item. Tuning hundreds of numbers by hand meant that every new item or recipe change rippled into walls (hours of waiting) or skips somewhere else, and playtests kept finding them, for example a tutorial that modelled at almost 15 hours.

## Decision

Balance is specified once, as intent, and the numbers are generated:
- Research is grouped into **research blocks** (game phases), each with a target play time.
- `scripts/rebalance_curve.py` walks the unlock graph and derives item sell values and research costs. The goal is that a player with a reasonable factory reaches each block in its target hours.
- The same tooling regenerates interactive reports: the progression curve, prestige pacing, and the active-versus-idle hours to each milestone. They're `docs/progression-curve.html` and `docs/balance/prestige-pacing.html`, both published to GitHub Pages.
- The few hand-set anchors (starting entropy, prestige base) live in `game_config`.

## Consequences

- Adding content is local: give a new item its place in the graph and regenerate. Prices elsewhere stay consistent.
- Pacing changes are small edits. The post-Carbon "Foundations" ramp was a change to block targets, not hand-edits to dozens of prices.
- The generator becomes critical code. Its output is checked against real play by AI playtest benchmarks: active-grinder runs timed against the target hours.
- Designers have to think in curves and targets rather than "this costs 500", which takes some learning.
