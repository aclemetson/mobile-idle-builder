# Telemetry Data Dictionary

What every collected metric is, where it comes from, and **which balancing decision it informs**. This is
the "why" behind each field — read it before re-tuning the economy so you change the right lever.

All events also carry `player_id` (UGS player id) and `collection_phase` (the `analytics.phase` flag value,
so data from different collection windows stays separable).

## Metric → decision map

| Metric | Source event/field | Question it answers | Scale lever it informs |
|---|---|---|---|
| Net worth per prestige (curve over run #) | `prestige_completed.networth_before` vs `run_count` | Is per-run power growing too fast / too slow across prestiges? | prestige threshold / `prestige_base`, output multipliers |
| Prestige currency earned per run | `prestige_completed.prestige_currency_earned` | Is the reward formula `floor(log10(netWorth/prestige_base) × prestige_scale)` paying out at the right rate? | `prestige_base`, `prestige_scale` constants |
| Run length | `prestige_completed.playtime_run_sec` (histogram) | How long is a run? Early runs a grind or trivial? | early-game cost curves, research-gate costs |
| Income scaling within a run | `player_snapshot.entropy_per_sec` over `playtime_total_sec` | How steep is income growth mid-run? Does it stall? | building production speeds, recipe sell values |
| Build size / saturation | `building_placed.building_count_after`, `player_snapshot.building_count` | Are builds plateauing (grid full) or under-using space? | grid size, building costs, conveyor limits |
| Building mix | `building_placed.building_id` frequency | Which buildings are over/under-used? | per-building cost/output balance |
| Tier progression | `tier_reached.tier`, `player_snapshot.highest_tier` + time-to-tier | Where do players stall in tier progression? | tier unlock costs, research gating |
| Research path / bottleneck | `research_completed.research_id` sequence + `entropy_spent_total` | Which research is a bottleneck or skipped? | research costs & prerequisites |
| Endgame reach | `megastructure_stage.stage`, `player_snapshot.megastructure_stage` | How far into the Dyson Sphere endgame do players get, and how fast? | megastructure contribution requirements |
| Prestige re-engagement | `player_snapshot.prestige_count` distribution | Do players re-enter the prestige loop or stop after run 1? | prestige reward attractiveness, early-loop pacing |
| Paid currency balance | `player_snapshot.paid_currency` | How much Crystals (◆) do players accumulate / hold unspent? | crystal sink pricing, achievement/IAP grant rates |
| Balance-update rollout reach | `game_update_notice.data_version` | How many players actually saw a published game-data change (and at which `gamedata.version`)? | confirms a remote balance bump propagated before reading post-change metrics |
| Spendable vs banked | `player_snapshot.base_currency` vs `entropy_per_sec` | Is currency piling up unspent (nothing to buy) or always starved? | sink pacing, cost ramps |
| Manual-collection engagement | `player_snapshot.field_collections` over `playtime_total_sec` | Are players actively tapping fields, or ignoring manual collection once idle income kicks in? | field tap cooldown, drop value, early-game idle pacing |
| Field cooldown reached | `player_snapshot.field_cooldown_sec` | How far have players driven the tap cooldown down via research + Quick Hands? Is the floor too easy/hard to hit? | `FieldSO.tapCooldownSeconds`, research `field_cooldown_mult`, Quick Hands per-level reduction |

## Where to view each metric

UGS Data Explorer v2 can only **count events** broken down by a **dimension**; it **cannot aggregate a
parameter value** (no avg/sum of a number). So:

- **Data-Explorer-native** (count of an event by a low-cardinality parameter): building usage (`building_id`),
  research path (`research_id`), endgame reach (`stage`), prestige depth (`run_count`), tier/prestige-count
  distribution from `player_snapshot`, balance-update reach (`data_version`). Segment any of these by `collection_phase`.
- **CSV-only** (value curves — export CSV → `dashboard.html`): everything whose signal is a numeric *value* or
  trend — net worth per run, prestige currency per run, `entropy_per_sec` over playtime, run length,
  `base_currency` / `paid_currency` balances, and `field_cooldown_sec`. These are continuous, so Data Explorer
  can neither aggregate nor meaningfully group by them. `field_collections` is a count, so it trends as a curve
  in CSV but can also be coarsely bucketed in Data Explorer.

## Notes on derived fields

- **`entropy_per_sec`** is *NetWorth growth per second* over the snapshot interval, not a raw production
  counter. It's the cleanest income-rate proxy available without per-frame instrumentation: spending entropy
  converts BaseCurrency into TotalEntropySpent without changing NetWorth, so only real production moves it.
  First snapshot of a session reports `0` (no prior baseline).
- **`playtime_run_sec` / `playtime_total_sec`** are measured from `realtimeSinceStartup` (monotonic), so they
  are *session*-relative, not lifetime. A run that spans an app restart will under-report. Acceptable for
  dev/playtest balancing; revisit if lifetime playtime becomes a needed metric.
- **`building_count`** excludes the permanent Entropy Sink fixture (matches how PrestigeSystem treats it).
- **`tier_reached`** has no caller in gameplay yet, so tier funnels currently come from
  `player_snapshot.highest_tier`. The discrete event will populate automatically once a `NotifyTierReached`
  caller is wired.
