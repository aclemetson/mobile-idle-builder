# Scenario 4: whale-speedrun

**Persona.** A top-spending player racing to the fastest possible first prestige. Plays exactly like
`active-grinder`, and also buys crystals and spends them wherever they save the most time.

**Setup.** README "Scenario runs", with:
- `fresh_start` scope `all`
- clock `turn_based`, `action_seconds` **1.5**
- `playtest_benchmark start` with scenario `whale-speedrun`

## Spend profile (simulated purchases)

Buy only with `playtest_control console` and `iap grant <tier>`. That's the same award path as a store
purchase; the benchmark logs dollars against every milestone. Never use `add currency` or other cheats.

| Rule | Value | Why |
| --- | --- | --- |
| Pack | `iap grant 4`: Quantum Pack, 20,000 crystals for $19.99 | Best value per dollar; whales' typical transaction is about $20 |
| Daily budget | Up to $100 per player day (5 packs) | Over $100 is the usual "whale" line; heavy whales spend $1,000+ a month |
| Run cap | $500 | Keeps the run a top 1% spender, not an unlimited one |
| Timing | Buy just in time, when the next spend needs crystals | Real buyers top up when blocked, not upfront |

Crystal value in this game: 150 crystals = one skip-hour, roughly "$1 ≈ 4 hours of progress"
(`PremiumShopCalculator`). Spend where a crystal buys the most player time:
- **Research skip** when a timer blocks the next unlock (150 crystals per hour remaining).
- **Speed boost** (2x) when production, not research, is the bottleneck: Quick Burst, Production Surge,
  Overclocked, Hyperdrive. The shop prices it as an *offline* boost, and per
  `docs/agents/architecture.md` live production doesn't read the prestige speed multiplier it feeds.
  Measure whether it changes live output (crafting progress in `playtest_state` before vs after) and
  report what you find.
- **Time Warp** to bank 2, 8 or 24 h of production instantly, especially instead of sleeping.
- **Entropy packs** when one purchase unlocks the next tier of buildings.

Log each spending decision with `playtest_benchmark mark`, giving what was bought and the time it
should save.

**Sleep.** Same as active-grinder: `away` 8 h after every 16 player hours awake, unless a Time Warp
plus a skip makes sleeping pointless (note that as a finding about monetization pressure).

**Goal.** First prestige completed, as fast as possible in player time. End with outcome
`reached_goal`.

**Time box.** 3 player days, the $500 cap reached with nothing left to buy, or 800 agent turns.

**Report.** The README format, plus:
- the benchmark milestone table with cumulative dollars;
- time saved per dollar vs the latest `active-grinder` run, at key milestones (tutorial complete,
  first prestige available, first prestige);
- crystal sinks ranked by player time saved per crystal;
- any point where spending stopped helping (a wall money can't buy past).

Sources for the spend profile (dated; revisit when better genre data exists):
[EEDAR on the "$70 a month" top spender](https://www.pocketgamer.biz/eedar-lifts-lid-on-mobiles-top-spender-the-70-a-month-killer-whale) ·
[Udonis on mobile whales (GameAnalytics data: over $100 spent, about $20 per transaction)](https://blog.udonis.co/mobile-marketing/mobile-games/mobile-games-whales) ·
[Adrian Crook on whale tiers ($1,000 to $1,999 a month)](https://adriancrook.com/?p=8161)
