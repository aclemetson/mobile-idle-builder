# Feature: PvE Tournaments (weekly 24h league runs)

**Status:** all six phases built. Phases 1-5 merged into `integration/2026-11-23` (PRs #492-#496; module deployed). Phase 6 (onboarding) on `feat/pve-6-onboarding` (2026-11-23). Remaining: the open items at the bottom.
**Size:** XL (6 phases, one PR each, stacked where they share files).
**Required reading:** `architecture.md`, `save-system.md`, `feature-flags.md`, `ui-toolkit.md`, `economy-balance.md` (currencies), `analytics.md` (snapshot loop). Source design: `docs/pve.txt`.

## Summary

Once a week, players race a fresh run for 24 hours against a small group (a **bracket**) of players in their **league**. Starting the tournament is a normal prestige. Score = net worth at the 24h mark. Players start at different times, so the leaderboard compares everyone **at the viewer's own elapsed time**. When the window closes, the top of each bracket is promoted and the bottom demoted, and rewards are claimable: crystals plus **item stars**, a permanent +20% value per star on a random discovered item.

Decisions taken with the user (2026-11-23):

| Question | Decision |
|---|---|
| Run model | **Real prestige.** Joining runs the normal prestige (prestige currency paid, run reset), and the 24h tournament *is* the new run. After 24h the score freezes and the run carries on as a normal run. |
| Boosts | **All allowed** (ads, crystal speed-ups, Time Warp, Cyclotron Kick). Leagues sort spenders over time. |
| Item stars | **Permanent; they raise the item's value**, so both entropy income and net worth. Drawn from items the player has ever discovered (recipe knowledge). Shown on recipes. |
| Not entering a week | No change: the player stays in their league. |
| Old PvP code | `PVPService`, `PVPSystem`, `PVPRunData`, `pvp.enabled` and the AWS `apiBaseUrl` calls are a dead stub. **Replace them** (keep the `pvpRun` save field so legacy JSON still loads, but stop using it). |

The six side items at the bottom of `docs/pve.txt` are tracked as issues #486-#491 and are out of scope here.

## Schedule (all UTC, server time only)

- **Entry window:** Friday 00:00 to Saturday 00:00. A player can join at any moment in the window.
- **Run:** 24h from the player's own join time. The latest possible finish is Sunday 00:00.
- **Settlement:** Sunday 00:10 at the earliest (00:00 + the final-snapshot grace); the schedule runs at 00:15. Every run is over by then, so ranks are final. Promotion, demotion and rewards are computed, and players claim on their next login.
- **Week id:** ISO date of the window's Friday, e.g. `2026-11-27`.
- Every time decision goes through **server time**: the Cloud Code clock on the backend, `ServerClock` on the client (the same pattern achievements use). Never the device clock or `GameClock` (dev builds can fast-forward `GameClock`). The playtest harness gets a server-side override instead (Phase 3).

## Backend: UGS Cloud Code + Cloud Save Custom Items

Answer to "does Unity offer temporary per-league storage": yes.
- **Cloud Save Custom Items** are game-owned records that clients cannot write directly. Each bracket is one custom item.
- **Cloud Code** is the only writer. As built (phase 3) it is a **C# module** (`CloudCode/PveModule`, deployed from the Editor's Deployment window via `Assets/CloudCode/PveModule.ccmr`), not JS scripts: JS scripts cannot share code, while the module links the same `PveRules.cs` / `PveModels.cs` / `PlayerNameRules.cs` the client compiles, and embeds game_data.json for the league table and name filter.
- **Triggers + Scheduler** run settlement weekly (Sunday 00:15 UTC; `SettleAfter` = Sunday 00:00 + the 10 min final-snapshot grace).

UGS Leaderboards was rejected because it keeps only the current score, with no history to read "as of" an elapsed time.

Packages to add: `com.unity.services.cloudcode`, `com.unity.services.deployment`. Scripts live in `Assets/CloudCode/` (`*.js` + `*.js.meta`).

### Data

| Store | Key | Contents |
|---|---|---|
| Custom item `pve_<week>` | `index` | open bracket id per league, bracket counter, `settled` flag |
| Custom item `pve_<week>_<league>_<n>` | `bracket` | `{ league, capacity, members: [{ playerId, name, startUtc, snaps: [{ t, nw, rate }] }] }`. `t` = seconds since that player's start, `nw` = net worth, `rate` = idle net-worth rate per second (offline-chain rate) |
| Player protected data (Cloud Save "protected" access class: client read-only) | `pve_profile` | `{ league, lastWeek, bracketId, history: [{ week, league, rank, size, result }], pendingRewards: [...] }` |

Bracket writes use Cloud Save `writeLock` (optimistic concurrency) with retry, so two joins or snapshots can't clobber each other. Snapshots are thinned server side: within each 10 min of `t`, keep the last one, so a bracket of 100 x 24h stays small.

### Scripts

| Script | Called by | Does |
|---|---|---|
| `pve_status` | client on open/login | server now, window state, week id, profile (league, pending rewards), current bracket id if joined |
| `pve_join` | client "Start" | rejects outside the window, rejects a second join the same week, places the player in their league's open bracket (opens a new one at `capacity`), records `startUtc` = server now, writes the first snapshot (`t=0`) |
| `pve_snapshot` | client every 5 min of play, on leaderboard open, on background | appends `{t, nw, rate}`; `t` is computed **server side** from `startUtc`. Drops snaps past 24h, except that the first snap past 24h is clamped to `t = 86400` using projection (below) |
| `pve_board` | client on leaderboard open (after `pve_snapshot`) | returns each member's **value at the viewer's `t`** (rule below), rank, and whether the value is real or projected |
| `pve_settle` | Scheduler, Sunday 00:00 UTC (plus a lazy call from `pve_status` if the index isn't `settled`) | final score per member = value at `t = 86400`; ranks; promote/demote; writes `pendingRewards` + `history` to each member's profile; marks `settled` |
| `pve_claim` | client | moves `pendingRewards` into the response and clears them (the client applies crystals + stars and saves) |

### The "value at time t" rule (shared by board and settle)

For a member `m` and a time `t` (the viewer's elapsed seconds, or 86400 at settle):
1. `m` has a snap at or before `t`, and a later snap exists or `t` is within m's played span: use the **last snap at or before `t`** (real). This covers the doc's 14h-ahead player.
2. `m`'s last snap is before `t` (the player is behind, or stopped): **project** it as `nw_last + rate_last x (t - t_last)`, capped at the idle cap the client reported, and mark it *projected*. This covers the doc's 6h player.
3. No snap yet (impossible after join, which writes `t=0`): 0.

Settle uses the same function at `t = 86400`. A player who went offline before their 24h mark is scored on their projected idle earnings, which matches what they would actually have when they come back.

### Anti-cheat (light, server side)

- Times come from the server only.
- Reject `nw` that drops by more than 1% between snaps (net worth only goes down through prestige, which is blocked during a run; see Client below).
- Reject growth above a config ceiling factor per second.
- The client sends `devConsoleUsed`; such players are put in a separate test bracket (`pve_<week>_test_<n>`), never ranked against real players.

## Leagues and rewards (`game_config`, so balance changes are data-only)

| League | Bracket size | Promote top | Demote bottom |
|---|---|---|---|
| Bronze | 100 | 15 | 0 (floor) |
| Silver | 75 | 12 | 10 |
| Gold | 50 | 8 | 8 |
| Platinum | 40 | 6 | 6 |
| Diamond | 30 | 4 | 5 |
| Champion | 20 | 0 (top) | 4 |

- Everyone starts in Bronze. `pve_test_bracket_size` (default 5) overrides sizes while the `pve.small_brackets` flag is on, for testing.
- A bracket smaller than its capacity at settle scales promote/demote counts proportionally, rounded down, minimum 1 if it has 3 or more members.
- Rewards are per rank band per league: `{ crystals, stars }`. Bands: 1st, 2nd-3rd, 4th-10th, top 25%, the rest. Higher leagues multiply both. Numbers go in `game_config.pve_rewards`, and the first-pass values are set in Phase 5 against `economy-balance.md` crystal pricing. A "special currency" slot is reserved in the reward schema, not implemented.

## Client

### Phase 1: Nickname (independent, ships first)
- `PlayerNameService`: UGS Authentication `UpdatePlayerNameAsync` / `GetPlayerNameAsync` (UGS appends `#1234`; show it dimmed).
- Validation (`PlayerNameRules`, pure, unit-tested): 3-16 chars, letters/digits/`_`/`-` (UGS player names refuse spaces, so typed spaces become `_`), at least one letter. Run it against `game_config.name_filter_patterns`, an industry-standard list of profanity/slur regexes (leetspeak-normalised: `0->o, 1->i/l, 3->e, 4->a, 5->s, 7->t, @->a, $->s`, repeated letters collapsed). `pve_join` re-checks with the same list, because the client can't be trusted.
- Prompt (`PlayerNamePromptController`, `name-prompt` in GameHUD.uxml): once per launch after the main tutorial (`tutorial.isActive == false`), only when online, 3 s after the game is idle (before the 8 s notification opt-in). **Later** sets `namePromptSkipped` (only Settings and, in Phase 5, PvE ask again). Settings > Account has a `btn-player-name` row. Dev console: `name status`, `name check <text>`, `name prompt`.
- Save: `playerName` (full UGS form `Name#1234`) + `namePromptSkipped`, both root fields that survive prestige. UGS is the source of truth: `PlayerNameService.RefreshAsync` overwrites the cache on each launch.
- **Done 2026-11-23** (`PlayerNameRules`, `PlayerNameService` + `IPlayerNameBackend`, `PlayerNamePromptController`, `PlayerNameTests`).

### Phase 2: Item stars (independent of the backend)
- Save: `itemStarIds` / `itemStarCounts` (parallel lists, survive prestige), owned by a static `ItemStars` (`Count(id)`, `Multiplier(id) = 1 + 0.2 x stars`, `Grant(n, rng)`).
- `Grant` draws uniformly from discovered items (`RecipeKnowledgeService` known outputs, excluding the Demon/non-sellables), one star per draw, repeats allowed.
- **One value accessor:** add `ItemValue.Price(itemId)` = base sell value (or 1) x `ItemStars.Multiplier`, and route every runtime `baseSellValue` read through it: `SurplusSale.DemonPrice`, `EntropySinkSystem`, `NetWorthSystem`, `MaxwellsDemonController`, `GridSaveService` (idle pricing), `IdleGraphAnalyzer`/`StreamIdleAnalyzer` callers, `RecipeDatabase`/`RecipeSO` value totals. `EntropySinkSystem` reads SOs on the main thread today; keep it that way (no Burst blob change). Editor/importer reads stay on raw `baseSellValue`.
- UI: star badge (`★ n`, +x%) on Recipes rows and the building inspector's item value line.
- Dev console: `stars grant <n>`, `stars list`, `stars clear`.
- Tests: multiplier math, grant draws only discovered items, prestige keeps stars, net worth/sink/idle chain values scale.
- **Done 2026-11-23** (`ItemStars`, `ItemStarsTests`). As built: one multiplier, `ItemStars.Multiplier(itemId)`, applied at `SurplusSale.DemonPrice`, `EntropySinkSystem`, `NetWorthSystem` (inventory), `GridSaveService` (idle pricing) and `MaxwellsDemonController` (via `ItemStars.StarredBase`); `RecipeSO`/`RecipeDatabase` value totals and byproduct escape entropy stay on base price. Badge text "★3 +60%" on the Recipes worth line and the inspector's first value line (`HUDController.StarBadge`). Dev console: `stars grant <n>`, `stars list`, `stars clear`.

### Phase 3: Backend (done 2026-11-23, as built)
- C# Cloud Code module `PveModule` (`CloudCode/PveModule/PveModule/`): functions `Status`, `Join(dev)`, `Snapshot(netWorth, rate, cap)`, `Board`, `Claim(week)`, `SettleWeek(week?)` (refuses player calls; the Sunday schedule's trigger calls it). Each returns a JSON string of a shared response model (`Assets/Scripts/PvE/PveModels.cs`), parsed on the client with JsonUtility.
- Shared pure rules `Assets/Scripts/PvE/PveRules.cs` (week/phase/window, `AddSnap` validation + 10-min thinning, `ValueAt`, `Rank`, `Moves`, `NextLeague`, `RewardFor`, ids), compiled into both the game and the module.
- Storage changed from the table above: a bracket is one private custom item with a `meta` key plus **one key per member** (`m_<playerId>`), so snapshot writes never contend; joins take a seat through the week index `pve_<week>/index`. Every read-modify-write uses Cloud Save write locks with retry. Dev-console saves get `_dev` brackets.
- Config: `game_config.pve` (run hours, test bracket size, snapshot bucket, the six leagues with sizes, promote/demote and reward bands; first-pass rewards, revisit in phase 5) → `GameConfigSO.pve`; the module embeds game_data.json.
- Test switches live in the private custom item `pve_settings/settings` (`smallBrackets`, `allowDebugTime`), not Remote Config. `Join` re-checks the UGS player name with the same filter; no name means `name_required`.
- Lazy settlement: `Status` settles the caller's finished bracket if the schedule hasn't yet. Settlement is idempotent (history per week, `meta.settled`).
- Client: `PveApi` (`IPveTransport`, Cloud Code `CallModuleEndpointAsync<string>`), dev console `pve status|join|snap|board|claim|time`. Packages `com.unity.services.cloudcode` 2.10.4 + `com.unity.services.deployment` 1.7.2 (the newest that run on 6000.3 with Core 1.16).
- Tests: Unity `PveRulesTests`; .NET `CloudCode/PveModule/PveModule.Tests` (embedded config, JSON field round trip).
- Manual setup: `docs/runbooks/pve-backend-setup.md` (enable Cloud Code, deploy, schedule + trigger, `pve_settings`).

### Phase 4: Tournament run
- **Done 2026-11-23 (as built):** `TournamentService` + pure `TournamentRules` (`Assets/Scripts/PvE/`), `TournamentPanelController` on the existing `pvp-panel` (status, timer, Start, leaderboard with promote/demote zones; the server's `Board` now returns the scaled `promote`/`demote`). Join is always allowed (even before the prestige wall; the confirm card shows the prestige currency gained). The join's prestige is tracked by `prestigeCountAtJoin` and finished on the next load if the app died in between; no snapshot before it. Unlock = the Chemistry world tutorial done (`TournamentRules.IsUnlocked`). **Offline at the 24h mark:** the server scores the player by projecting their last snapshot (idle rate, capped); to cover a last snapshot lost when the OS suspended the app, the device keeps the latest in-run snapshot in `SaveData.tournament.pending*` (saved on backgrounding) and, if the run is over, delivers it on the next launch with its own elapsed time (`Snapshot(atT)`; `PveRules.SnapshotTime` refuses times more than 2 min ahead of the server's, the server refuses anything once the week is settled). Only in-run values are ever kept, so a late delivery can't add play from after the 24h. Dev console: `tournament status|unlock|forget`. `PVPService`/`PVPSystem`/`PVPSystemTests`/`PlayerProgressData.PVPRunRequested` deleted and the component removed from GameScene. Tests: `TournamentRulesTests`.
- `TournamentService` (self-bootstrapped like `WorldService`) replaces `PVPService`; delete `PVPSystem`, `PVPService`, `PVPSystemTests` and the HUD PvP wiring. `FeatureFlags`: `pve.enabled` (default **true**, kill-switch) replaces `pvp.enabled`.
- Save (`tournament`, additive, survives prestige): `weekId`, `bracketId`, `startUtc`, `league`, `lastSnapshotT`, `scoreFrozen`.
- Join flow: `pve_status` checks the window → confirm modal ("This prestiges your current run: +N prestige currency") → `pve_join` → on success, set `PrestigeRequested` (the normal prestige, with currency). The `tournament` block survives `PrestigeRunReset`, and the new run is the tournament run.
- During the 24h: **block manual prestige** (button disabled with a countdown) so net worth can't be reset mid-run.
- **Changed 2026-11-23 (#505): leave early instead of a lock.** Prestige/Ascension mid-run asks ("End your tournament run?") and then calls the module's `Retire(netWorth)` before prestiging: a last snapshot at the current net worth with idle rate 0 (never below the last recorded), member marked `retired` (later snapshots get `retired`). The panel has a Retire button doing the same without prestiging. Board rows show "(retired)"; status reports `retired` so another device follows. Offline, the prestige is refused rather than leaving a stale projection.
- Snapshots: every 5 min of real play (reuse the telemetry snapshot loop's cadence), on leaderboard open, on `SaveManager.Backgrounding`. Payload `{nw, rate}`: `rate` = net-worth rate from the idle snapshot chains (what `OfflineCollectionService` would pay per second, before the cap).
- After 24h, the score is frozen (server side). The run continues normally, and prestige unlocks again.

### Phase 5: PvE panel + rewards
- **Done 2026-11-23 (as built):** `pvp-*` renamed to `pve-*` (`pve-panel`, drawer `btn-tournament`). League badge (`pve-league-badge--{id}` colours), result banner for the latest settled tournament (Results/Waiting/Open screens), "You: #3 of 12", and a Claim button for the next reward this save hasn't applied. **Claim order: local first** (`TournamentService.ClaimAsync`: crystals + `ItemStars.Grant` from discovered items + week recorded in `SaveData.pveClaimedWeeks`, saved) **then** the server `Claim`; a failed server step is retried on each status refresh without paying again. Reveal card via `ConfirmPrompt` ("★×2 Carbon: now ★5 (+100% value)"). Pure text in `TournamentPanelText`; tests in `TournamentRulesTests`.
- Reuse `pvp-panel` in `GameHUD.uxml`, renamed to `pve-panel` (grep every query first; rename the drawer button to "Tournament").
- States: Locked (before the Chemistry tutorial), Waiting (outside the window, shows a countdown to Friday 00:00 plus last week's result), Open (explainer + Start), Running (timer, league badge, leaderboard), Finished (frozen score, "results Sunday 00:00"), Results (rank, promote/demote banner, Claim). A status whose `phaseEndsUtc` has passed is stale (`TournamentRules.StatusStale`): the state follows the clock (and drops last week's `joinedThisWeek`) and the open panel refetches every 15 s until a fresh one lands, so Start appears the moment entry opens (2026-11-26).
- Leaderboard rows: rank, name, value at your time; projected values in italics with a "~" and a tooltip ("estimated: idle since 6h 12m"); your own row pinned.
- Claim: crystals via `paidCurrency` write-through; stars via `ItemStars.Grant`, then a reveal list ("★ Carbon +20%").

### Phase 6: Unlock + onboarding
- **Done 2026-11-23 (as built):** `TournamentIntroController` (added to the HUD at runtime). Once per entry window (`SaveData.tournamentIntroWeek`), when unlocked and not entered this week, after the game has been calm for 5 s (no tutorial, including a world flow in progress; no dialogue, modal or open Tournament panel), it plays `pve_intro` the first time (`tournamentIntroSeen`) and `pve_intro_weekly` after, then pulses the way to `btn-tournament` (TutorialPulseResolver: drawer handle, then the button) for up to 30 s or until the panel opens; the panel's Open screen is the explainer with Start. Finishing the Chemistry tutorial inside the window therefore shows its last message, then this ~5 s later. Dev console: `tournament intro`, `tournament intro full`. **Player-facing times are local** (2026-11-23): `pve_intro_weekly` says "open until {entry_closes}", filled at play time in a runtime copy of the dialogue with `TournamentRules.EntryClosesLocal` ("Friday 7:00 PM" at UTC-5); the panel's next-opening line and the entry-closed error use `NextOpensLocal`. The schedule itself stays UTC on the server.
- Unlock when the Chemistry world tutorial is complete (`TutorialFlows` / `SaveData.worldTutorials` for `world_chemistry` done).
- First login inside an entry window after unlock: one-shot popup (`tournamentIntroSeenWeek`).
- If the Chemistry tutorial finishes inside a window: after its last message closes, wait ~5 s, pulse the drawer's Tournament button (`TutorialPulseResolver`), open the panel on the explainer, and allow Start.
- Dialogue ids `pve_intro_*` in `game_data.json` (importer).

## Acceptance criteria

- [ ] A name with a profanity pattern (including leetspeak) is refused client side and by `pve_join`.
- [ ] Stars raise sink payout, net worth, surplus sales and offline earnings by exactly +20%/star and survive prestige.
- [ ] Joining outside the window, or twice in one week, is refused by the server whatever the device clock says.
- [ ] Two players 10h and 14h in see each other's values at their own elapsed time. A 6h player shows to the 10h viewer as projected.
- [ ] A player who stops at 20h is scored at 24h by projection, capped at their idle cap.
- [ ] Settle promotes and demotes per the table (scaled for small brackets); the next week's join lands in the new league.
- [ ] Rewards claim once (a second claim returns nothing); crystals and stars persist through save/load and a cloud round trip.
- [ ] Manual prestige is blocked during the 24h and unblocked after.
- [ ] Dev-console saves never share a bracket with clean saves.
- [ ] Full suite green (`scripts/test-local.ps1`); docs updated: `architecture.md` (TournamentService, PVP rows removed), `save-system.md` (new fields), `feature-flags.md` (`pve.*`), `economy-balance.md` (stars), `ui-toolkit.md` (pve-panel).

## Open items for later

- ~~Special tournament currency~~: **Strings** (#500, 2026-11-24). Every reward band pays them (`PveRewardBand.strings` → `PveReward.strings`; Bronze 60/35/20/10/5 up to Champion 300/175/100/50/25 for 1st / 2nd-3rd / 4th-10th / top 25% / everyone who finished). Claimed into `SaveData.pveStrings`, with the balance in the panel header. Spent on a tournament track (Star Resonance, Lucky Draw, Surge Luck, Harmonics) and cosmetics (leaderboard name colours and titles, belt themes): parts 2 and 3.
- Bracket size growth toward 100 as the player base grows (config only).
- Push notification at Friday 00:00 / when your 24h is nearly up (reuse `NotificationPlanner`).
