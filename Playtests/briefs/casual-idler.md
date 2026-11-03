# Scenario 2: casual-idler

**Persona.** A casual free-to-play player. Checks in a few times a day for a few minutes, does the
obvious next thing, then closes the app and waits for timers and offline earnings. Reads hints, skims
dialogue, never optimizes layouts, never spends money.

**Setup.** README "Scenario runs", with:
- `fresh_start` scope `all`
- clock `turn_based`, `action_seconds` **4** (a relaxed player)
- `playtest_benchmark start` with scenario `casual-idler`

**How this player plays.**
- **Sessions.** Each player day has five check-ins. After each one, `away` until the next:

  | Check-in | Away after it |
  | --- | --- |
  | 08:00 | 4.5 h |
  | 12:30 | 4.5 h |
  | 17:00 | 3 h |
  | 20:00 | 2.5 h |
  | 22:30 | 9.5 h overnight |

- **In a session.** 10 to 25 actions: claim the welcome-back earnings, claim free rewards, start the
  next affordable research, buy or place the one thing the hint points at, tap a field a few times.
  Then end the session; no waiting around with the app open beyond a short `wait` (60 player
  seconds at most).
- **No crystals spent**, even free ones. No purchases, no cheats after setup.
- **Follow the tutorial** as it asks. If the next step is unclear, this player waits until the next
  session rather than experimenting. Log that as a finding.

**Goal.** First prestige completed. `playtest_benchmark end` with outcome `reached_goal`.

**Time box.** 14 player days, or 600 agent turns. When either runs out, end with outcome
`time_box`.

**Report.** The README format, plus:
- the benchmark milestone table (player time, the in-game day and check-in);
- the three longest stretches between meaningful milestones, and what the player was waiting on;
- whether each welcome-back payout felt worth coming back for (offline earnings vs the idle cap).
