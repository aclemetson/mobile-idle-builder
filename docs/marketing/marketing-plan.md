# Marketing Plan (outline)

How Quantum Foundry gets seen by the players most likely to love it. This is an **outline to refine over several
sessions**: every section ends with open questions for the owner. Issue #531. Related: beta plan
(`docs/testing/beta-plan.md`, #532), Discord (`docs/community/discord-server.md`, #533), store listing
(`docs/store/store-listing.md`), redeem codes for incentives (#569).

> Drafted 2026-11-26. Dates are targets tied to the beta phases, and a phase moves when its exit criteria are met.

## 1. Positioning

**One line:** an idle factory game where you build matter itself, from quarks to atoms, molecules and a Dyson Sphere.

**Hook: "physics made visible."** Every building performs real physics on the belt (fusion, decay, bonding), so a
factory is a working picture of how matter is made. No other idle game shows this, so lead with it in every clip.

**Audiences, in order of priority:**

| Audience | Where they are | What sells it to them |
|---|---|---|
| Idle / incremental players | r/incremental_games, idle Discords, "best idle games" lists | deep prestige, offline progress, numbers going up with meaning, no paywall |
| Factory-builder fans on mobile | r/factorio, r/SatisfactoryGame, r/shapezio, r/AndroidGaming | belts, splitters, overpasses, a real production chain on a phone |
| Science-curious players, students, teachers | science TikTok/Shorts, r/chemistry, r/Physics, edu newsletters | real periodic table and isotopes, "learn how atoms are made by playing" |

**Comparable titles** (to say "if you liked X"; check current store standing before quoting any):
- *Cell to Singularity*: science-themed idle, the closest audience match
- *Antimatter Dimensions*, *Universal Paperclips*: incremental depth and prestige layers
- *Factorio*, *shapez*: the belt and factory fantasy (PC; we're the mobile and idle take on it)
- *Idle Miner Tycoon*-style mobile idles: the casual mobile idle reference point

**Open questions**
- Lead with "idle" or "factory" in the store title? The current title is `Quantum Foundry: Idle Factory`. Test both in ASO (section 3).
- Is the educational angle a main message or a bonus? It opens edu press and teachers but can read as "homework".

## 2. Channels

| Channel | Role | Cadence | Phase |
|---|---|---|---|
| **r/incremental_games** | core audience; the weekly Feedback Friday thread welcomes betas | Feedback Friday posts in closed beta, a launch post at open beta | 1 → 3 |
| **r/AndroidGaming**, r/iosgaming | broad mobile reach; strict self-promo rules, so post at milestones | open beta + launch | 2 → 3 |
| Factory subs (r/factorio etc.) | only a GIF post that fits the sub's rules; never spam | 1-2 times total | 2 |
| **TikTok / YouTube Shorts / Instagram Reels** | devlog clips of belts, particles and fusion; the visuals are the ad | 2-3 a week from week 1 | 0 → 3 |
| **Discord** (live, #533) | home for testers and fans; beta sign-up, patch notes, league results | always on | 0 → 3 |
| Indie press and newsletters | coverage at open beta and launch (press kit needed) | pitch batch per milestone | 2 → 3 |
| Idle-game YouTubers / streamers | keys or early access for mid-size idle channels | outreach at open beta | 2 → 3 |
| **Store listing / ASO** | turns visitors into installs; every other channel lands here | revise per phase | 2 → 3 |
| Paid UA (optional) | small test only in soft launch to measure cost per install | one network, capped budget | 3 |

**Open questions**
- Which social accounts already exist, and under which handle? One handle everywhere (e.g. `@quantumfoundry`) if it's free.
- Face-on-camera devlogs or screen-only clips with captions and music? Screen-only is cheaper and works well for factory content.

## 3. Assets

| Asset | Status | Notes |
|---|---|---|
| Store copy (title, short, full description) | **done**, `docs/store/store-listing.md` | revisit per phase |
| Icon, feature graphic | **done**, `docs/store/icon-512.png`, `feature-graphic.png` | A/B test the icon in Play store listing experiments at open beta |
| Store screenshots | partial, `docs/store/screenshots/` | need 6-8 with captions: belts, fusion, periodic table, worlds, leagues, Dyson Sphere |
| GIFs / short clips (3-10 s) | to do | belt particles, a fusion reaction, a splitter, a factory zoom-out. Reused by every channel |
| Trailer (30-45 s) | to do | needed for the store (Play promo video) and press; built from the same clips |
| Press kit page | to do | one HTML page: fact sheet, description, screenshots, logo, trailer, contact. Can live on GitHub Pages |
| Music for clips | in progress, `scripts/music/` | original tracks avoid copyright strikes on clips |

**Open questions**
- Record clips from the Editor (clean, scriptable) or on a phone (authentic)? Probably both: Editor for the trailer, phone for TikTok.

## 4. Timeline (tied to the beta plan)

| Phase (beta plan) | Marketing goal | Key actions |
|---|---|---|
| **0. Internal (now)** | build in public, collect the first followers | start the clip habit, claim handles, Discord ready for sign-ups, beta sign-up form |
| **1. Closed beta** (20-50 testers) | recruit testers who match the audience; first word of mouth | Feedback Friday post, Discord beta role, tester incentive (crystal code, #569), weekly "what we changed" posts |
| **2. Open beta** (200-1,000) | a store page people find; first press and creators | final store listing, trailer, press kit, r/AndroidGaming post, creator outreach, Play store listing experiments |
| **3. Soft launch** (1-3 countries) | measure cost per install against revenue per player | capped paid test, ASO keyword iteration, review replies |
| Launch (worldwide) | biggest single push | press batch, launch posts on every channel, launch-week league event, featuring requests to Google Play and the App Store |

## 5. Budget and metrics

**Budget (proposal, owner decides):** $0 until soft launch. Soft launch: a capped paid test (for example $200-500 on one
network) only to measure cost per install. Optional: $50-100 for trailer music or capture tools if needed.

| Metric | Target | Source |
|---|---|---|
| Beta sign-ups before closed beta | at least 50 (to keep 20+ active and meet Play's 12 testers for 14 days rule) | sign-up form, Discord role count |
| Clip performance | 1 clip in 10 above 10k views by week 8 | TikTok / YouTube analytics |
| Store conversion (page visit → install) | at least 25% on Google Play | Play Console > Store performance |
| D1 / D7 / D30 retention | 35% / 12% / 4% (beta plan targets) | UGS Analytics |
| Cost per install (soft launch, paid) | below the revenue per player from beta data | ad network + `iap_purchase`, LevelPlay revenue |
| Discord members | 100 by open beta | Discord Server Insights |

**Open questions**
- Is any paid UA wanted at all, or organic only until revenue proves out?
- What counts as success for this game: a portfolio piece with players, side income, or full-time potential? It changes how hard launch should push.

## 6. What Claude can do vs what needs the owner

| Claude can automate or draft | Needs the owner |
|---|---|
| Clip capture scripts: drive the Editor through the AI playtest harness (`playtest_*` tools) to record factory shots at set camera paths | Posting from personal or brand social accounts (logins, 2FA) |
| Captions, hooks and post copy for each clip; subreddit-rule-aware post drafts | Choosing which clips go out; anything on camera or in your voice |
| Store copy variants for ASO tests; screenshot captions; translations of store text | Running store listing experiments in Play Console / App Store Connect |
| Press kit page (HTML artifact or GitHub Pages) and a press pitch email draft | Sending pitches from your email; creator relationships |
| Beta recruiting post and sign-up form questions (appendix in the beta plan) | Approving testers, giving out codes |
| A posting calendar; scheduled posts through a Discord webhook (patch notes already automated) | Social scheduler accounts (Buffer, Later) if used |
| Weekly metrics summary from UGS / Play exports | Budget decisions, paid campaigns, payments |
| Music drafts via `scripts/music/compose.py` | Final music choice; anything needing Ableton |

## 7. First 4 weeks of concrete actions

**Week 1 (Nov 30 - Dec 6): foundations**
- [ ] Owner: claim one handle on TikTok, YouTube, Instagram and Reddit (u/), plus the domain if wanted.
- [ ] Claude: write a clip capture script for the Editor (3 camera paths: belt close-up, fusion building, factory zoom-out).
- [ ] Owner + Claude: record and post the first 2 clips. Claude drafts the captions.
- [ ] Claude: beta sign-up form questions (device, platform, idle games played, Discord handle) and a pinned Discord post.

**Week 2 (Dec 7 - 13): build in public**
- [ ] 2-3 clips (one "how a proton is made", one satisfying-belts, one devlog "what I changed this week").
- [ ] Claude: draft an r/incremental_games Feedback Friday post. Owner posts it once the closed beta build is ready.
- [ ] Claude: 6-8 store screenshots with captions from the Editor; owner approves.
- [ ] Owner: decide the tester incentive (for example a crystal code via #569, or a Discord role and title).

**Week 3 (Dec 14 - 20): recruit for the closed beta**
- [ ] Recruiting post on Discord + Reddit with the sign-up form (beta plan appendix).
- [ ] Claude: press kit page v1 (fact sheet, screenshots, logo, short description).
- [ ] 2-3 clips, reusing the best performer's format.
- [ ] Review clip numbers: which hook worked? Note it here.

**Week 4 (Dec 21 - 27): closed beta opens (if phase 0 exit criteria are met)**
- [ ] Invite the first 20-50 testers (Play closed track + TestFlight external).
- [ ] Weekly "what we changed" post in Discord (`#patch-notes` is automated; add a human summary).
- [ ] Claude: trailer shot list and a first cut from the clips so far.
- [ ] Week-4 review with Claude: update this plan with what worked, and set weeks 5-8.

## Review log

| Date | Change |
|---|---|
| 2026-11-26 | outline drafted (#531) |
