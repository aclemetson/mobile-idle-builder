# Beta Testing Plan

What each rollout of Quantum Foundry is meant to learn, how we measure it, when it is done, and where the
testers come from. Issue #532. Related: #416 (production readiness), #518 (iOS IAP), #529 (PvE analytics),
#531 (marketing), #533 (Discord).

The rule for every phase: **one main question per phase**. A phase ends when its exit criteria are met, not on
a date. The dates in the timeline are targets.

## Phases at a glance

| Phase | Track (Android / iOS) | Testers | Length | Main question | UGS environment |
|---|---|---|---|---|---|
| 0. Internal | Play **internal** / TestFlight **internal** | 1-10 (owner, friends) | until exit | Does it run, save and pay correctly on real devices? | development |
| 1. Closed beta | Play **closed** / TestFlight **external** (invite) | 20-50 | 3-4 weeks (at least 14 days, see below) | Do new players understand it and come back? | development |
| 2. Open beta | Play **open testing** / TestFlight **public link** | 200-1,000 | 4-6 weeks | Does the economy and the weekly league hold up with many players? | **production** |
| 3. Soft launch | Play + App Store **production**, 1-3 countries | organic + small paid test | 4-8 weeks | Are retention and revenue good enough to launch worldwide? | production |

`analytics.phase` (Remote Config) tags every event with `collection_phase`, so set it at the start of each phase:
`internal`, `beta_closed`, `beta_open`, `soft_launch`. Each phase's data can then be split in the dashboard.

> **Google Play production access.** Personal developer accounts created after November 2023 must run a
> closed test with **at least 12 testers opted in for 14 days in a row** before they can apply for
> production access. If this account is a personal one, phase 1 must satisfy that rule. Recruit 20 or more
> testers so drop-outs don't reset the count. Check Play Console > Dashboard for the exact requirement shown
> for this app.

## Phase 0: Internal (now)

**Goal:** no blocker reaches an outside tester.

What to check:
- Stability: no crashes in a normal session; Cloud Diagnostics receives reports with symbols (#168: `diag
  exception`, `diag crash`).
- Saves: cloud save survives reinstall and a second device; offline earnings are right after 8+ hours away.
- Money: IAP test purchases on both stores (`docs/runbooks/google-play-iap-testing.md`,
  `docs/runbooks/app-store-iap-testing.md`, #518), Ad-Free + Restore Purchases, rewarded ads fill (test mode).
- Telemetry: events arrive in UGS Analytics with `collection_phase = internal` (see "Instrumentation" below).
- Tutorial end to end on a fresh install (`playtest_control fresh_start all` in the Editor, then on device).

Exit criteria:
- [ ] No open P0 bugs; no crash in the last 3 internal builds
- [ ] #168 verified (symbolicated crash in the dashboard)
- [ ] IAP and ads pass on both platforms
- [ ] The beta telemetry events below are live and visible in the dashboard
- [ ] Feedback email (`links.feedbackEmail`) points to a project inbox, not a personal one (Remote Config, no build)
- [ ] Survey and recruiting post ready (appendices)

## Phase 1: Closed beta

**Goal:** learn whether a new player understands the game and comes back, and whether the early pacing matches
the plan.

Questions and how we answer them:

| Question | Measure | Starting target | Source |
|---|---|---|---|
| Does it crash? | crash-free users | at least 99% | Cloud Diagnostics, Play vitals |
| Do players finish the tutorial? | share of new players reaching the last tutorial step | at least 70% | `tutorial_step` funnel (new) |
| Where do they drop out? | the tutorial step with the biggest drop | no single step loses more than 10% | `tutorial_step` funnel |
| Is the opening pacing right? | time to first Carbon (tutorial end) | 45-60 min, as designed | `tutorial_step` + `playtime_total_sec` |
| Do they come back? | D1 / D7 retention | D1 at least 35%, D7 at least 12% | UGS Analytics (standard retention) |
| Does the first prestige land when planned? | playtime at the first `prestige_completed` | within 25% of `scripts/prestige_plan.py` | `prestige_completed.playtime_run_sec` |
| Is anything confusing? | survey answers + bug reports | no theme reported by more than 25% | survey, Discord, email |

Targets are starting points for an idle game. Adjust them after phase 1 and record the change here.

Setup:
- Play Console > Testing > **Closed testing**: tester list (email list or Google Group), opt-in link.
- TestFlight > **External testing** group (the first build needs Beta App Review, usually about a day).
- Discord `#beta-testers` channel with a role (#533), or an email list until Discord exists.
- Set `analytics.phase = beta_closed`.

Rhythm:
- One build a week at most, with what's-new notes (#502). Avoid more than one build per week: testers stop
  updating.
- Survey at day 3 and at day 14 (appendix B).
- Weekly triage: feedback into GitHub issues labelled `beta`, then a short "what we changed" post.

Exit criteria:
- [ ] At least 14 days with at least 12 opted-in testers (Play rule, if it applies)
- [ ] Crash-free users at least 99%, no open P0/P1 bugs
- [ ] Tutorial completion and D1 at their targets, or a fix shipped and re-measured
- [ ] Day 14 survey done; the top 3 confusion points fixed
- [ ] #416 done: production builds talk to the **production** UGS environment

## Phase 2: Open beta

**Goal:** test the systems that only work with many players: the weekly PvE leagues, the economy over weeks,
monetization, and backend load. Also, start the community and the store page.

Before it starts: #416 (production environment, all Remote Config keys in production, production LevelPlay
app key), the store listing is final (`docs/store/store-listing.md`; open testing is visible on Google Play), and
the Discord is live (#533).

| Question | Measure | Starting target | Source |
|---|---|---|---|
| Do leagues fill and feel fair? | players per league instance, finish rate, score spread | leagues fill within a day; top/median gap not extreme | Cloud Code data, #529 report |
| Does the mid/late economy hold? | prestige count and net worth per day played, vs the plan | within 25% of `prestige_plan.py` | `player_snapshot`, `prestige_completed` |
| Do players use ads? | rewarded ads per daily active user, by placement | at least 1 per DAU | `ad_reward` (new), LevelPlay dashboard |
| Does anyone pay? | IAP conversion, Ad-Free share of revenue | at least 1% of players | Play / App Store reports, `iap_purchase` (new) |
| Does the backend hold? | Cloud Code errors and cost, Cloud Save errors | no error spikes; cost inside budget | UGS dashboard |
| Week-4 retention | D7 / D30 | D7 at least 12%, D30 at least 4% | UGS Analytics |

Exit criteria:
- [ ] D7 and D30 at their targets for two weekly cohorts in a row
- [ ] 4 weekly league cycles without a backend incident
- [ ] Economy within 25% of the plan, or a rebalance shipped through game-data overrides (#530) and re-measured
- [ ] No open P0/P1 bugs

## Phase 3: Soft launch

**Goal:** check the numbers that decide a worldwide launch, in a few smaller markets first.

- Production release in 1-3 English-speaking countries with lower ad costs (for example Canada, Australia,
  New Zealand, the Philippines). Pick them with #531.
- Optional small paid test (for example a few hundred dollars on one ad network) to measure cost per install
  against revenue per player.
- Measures: D1/D7/D30, ARPDAU (average revenue per daily active user), session length and count, IAP and ad
  revenue split, store conversion (store page visitors to installs).
- Exit: a go / no-go call on a worldwide launch with the numbers above, written into this doc.

## Instrumentation needed before phase 1

UGS Analytics already gives sessions, DAU and retention through its standard events. The custom events today
(`docs/agents/analytics.md`) cover prestige, buildings, research, tiers, the megastructure, notifications and
snapshots. Phase 1 and 2 also need:

| Event | Params | Why |
|---|---|---|
| `tutorial_step` | step_id (String), step_index (Int), playtime_total_sec (Int) | tutorial funnel and time to first Carbon |
| `ad_reward` | placement (String), granted (Boolean), ad_free (Boolean) | ad use by placement; Ad-Free owners show as `ad_free` |
| `iap_purchase` | product_id (String), price (Float), currency (String) | purchases joined to progress (the stores report revenue, not progress) |
| `pve_league` | action (String: joined / finished), league_tier (Int), rank (Int) | league participation, alongside #529 |
| `feedback_opened` | source (String) | how many players reach the feedback link |

Each event also needs its schema registered in the UGS dashboard, with the same names and types. Tracked in #567.
follow-up issue (#567).

## Tools

| Need | Tool | Notes |
|---|---|---|
| Android builds | Play **internal** (CI, `release-internal.yml`), **closed**, **open** tracks | promote a tested internal build to closed/open in Play Console |
| iOS builds | TestFlight internal (up to 100), external groups (up to 10,000), public link | first external build goes through Beta App Review |
| Crashes | Unity Cloud Diagnostics (symbols uploaded by CI, #168), Play Console vitals (ANRs) | |
| Behaviour | UGS Analytics + `collection_phase`; CSV dashboard in `docs/analytics/` | |
| Leagues | Cloud Code data and the weekly report (#529) | |
| Feedback | in-game Send Feedback (email, `links.feedbackEmail`), Discord forum (#533), surveys (Google Forms) | |
| Release notes | what's-new notice (#502), Discord `#patch-notes` | |
| Issues | GitHub issues, label `beta`, one per distinct problem | |

## Recruiting

Where testers come from, in order of quality for phase 1:
1. **Friends and family** who play mobile games: reliable for the 12-tester rule; less honest feedback.
2. **Incremental / idle game communities**: r/incremental_games (the core audience), idle-game Discord servers.
   Read each community's self-promotion rules first and post where they allow it.
3. **General mobile communities**: r/AndroidGaming, r/iosgaming, r/playmygame, r/TestFlight.
4. **Science-curious players**: physics/chemistry education communities, where allowed (the "learn how atoms are
   made" angle).
5. **Tester directories** (BetaFamily and similar) for phase 2 volume.

What testers get: a **Beta Tester** Discord role, credit in the patch notes, and (once a redeem-code feature
exists, see #533) a crystal reward on the production release. Never promise anything the game can't deliver yet.

Keep a simple tester sheet (name, platform, device, join date, opted in yes/no). It tracks the Play 12-tester
count and who to thank. Keep it outside the repo: it holds personal data.

## Timeline (targets)

| When | What |
|---|---|
| Now | Phase 0 exit items; telemetry events; recruiting post and survey ready |
| + 1-2 weeks | Phase 1 starts (closed beta, about 20-50 testers) |
| + 4-6 weeks | Phase 1 exit review; #416 done; store listing final; Discord live |
| + 6-8 weeks | Phase 2 starts (open beta) |
| + 12-14 weeks | Phase 2 exit review; pick soft-launch countries with #531 |
| + 14-22 weeks | Phase 3 soft launch, then the go / no-go call |

## Appendix A: Recruiting post (phase 1)

Use one version per community and follow its rules. Keep it short.

> **Looking for beta testers: Quantum Foundry, an idle factory game where you build matter itself**
>
> I'm a solo developer making an idle factory game for Android and iPhone. You start with quarks and electrons,
> build machines that fuse them into atoms, bond the atoms into molecules, and work your way up to a Dyson
> Sphere. It keeps producing while the app is closed, there are prestige resets, and there's a weekly league.
>
> I'm looking for about 30 players for a closed beta of 3-4 weeks. You'd install it from Google Play or
> TestFlight, play the way you normally would, and answer two short surveys (about 5 minutes each). Honest
> feedback is the whole point, especially "I didn't get this" moments.
>
> Testers get a Beta Tester role in our Discord and credit in the patch notes.
>
> Interested? [sign-up form link]. Tell me your platform (Android or iPhone) and the email for your Google
> Play or Apple account so I can add you.

## Appendix B: Surveys (Google Forms)

**Day 3 survey** (first impressions, 6 questions):
1. Platform and device (short text)
2. How far did you get? (tutorial / first Carbon / first prestige / further / not sure)
3. Was there a moment you didn't know what to do next? Where? (paragraph)
4. How clear was the tutorial? (1-5)
5. What made you want to keep playing, if anything? (paragraph)
6. Anything broken or odd? (paragraph)

**Day 14 survey** (retention and economy, 8 questions):
1. How often do you open the game? (several times a day / daily / every few days / stopped)
2. If you stopped, why? (paragraph)
3. How many prestiges have you done? (0 / 1 / 2-5 / more)
4. Did progress feel too slow, about right, or too fast? Where? (choice + paragraph)
5. Did you watch rewarded ads? Why or why not? (choice + paragraph)
6. Would you buy anything in the shop? What, and at what price? (paragraph)
7. Did you join a weekly league? What did you think? (paragraph)
8. How likely are you to recommend it to a friend who likes idle games? (0-10)

Paste the answers into a GitHub issue per theme (label `beta`), not the raw responses: they can hold
personal data.
