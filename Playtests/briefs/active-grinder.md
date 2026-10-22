# Scenario 3: active-grinder

**Persona.** An engaged free-to-play player who keeps the app open and pushes for speed by playing well:
taps fields on cooldown, reinvests every unit of entropy, keeps research always running, optimizes
layouts and belts, upgrades the bottleneck. **No paid resources:** never spends crystals (earned ones
included), never buys anything.

**Setup.** README "Scenario runs", with:
- `fresh_start` scope `all`
- clock `turn_based`, `action_seconds` **1.5** (a quick, practiced player)
- `playtest_benchmark start` with scenario `active-grinder`

**How this player plays.**
- **App stays open.** No `away`, except for sleep: after every 16 player hours awake, `away` 8 h.
  Mark each sleep with `playtest_benchmark mark`.
- **Never idle without a reason.** Before any `wait`, check for something useful to do: tap a ready
  field, affordable research, an upgrade, a building that would raise output, a jammed belt. Waits
  are short (at most 120 player seconds), then look again.
- **Research is never idle.** Start the next research the moment one finishes, picking the one that
  unlocks the most production.
- **Optimize.** Use `playtest_state` to find idle buildings (`crafting: false`, `inputsSatisfied:
  false`, `power` not `ok`) and fix the cause. Upgrade the slowest link of the main chain first.
- **Skip dialogue quickly** but follow the tutorial's goals.
- `time_scale` up to 10 is fine during waits; player time is unaffected.

**Goal.** First prestige completed. `playtest_benchmark end` with outcome `reached_goal`.

**Time box.** 5 player days, or 800 agent turns. When either runs out, end with outcome `time_box`.

**Report.** The README format, plus:
- the benchmark milestone table;
- where waiting was forced: stretches where nothing useful could be done, how long they lasted, and the
  blocker (a research timer, cost, or missing input);
- the three biggest throughput gains and the actions that caused them;
- a comparison with the latest `casual-idler` run (`python scripts/benchmark-report.py`).
