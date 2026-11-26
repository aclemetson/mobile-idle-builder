# Changelog

Notable changes per release. Builds are versioned `major.minor.patch`, and the patch number increases on every commit. Pull request numbers link the detail.

## 0.6 (in progress)

Building and belt editing that works with fingers, a startup that keeps your account and your newest save, weekly PvE tournaments, and a reworked menu and monetisation layer.

### Tournaments
- **Weekly PvE tournaments**: join from Friday 00:00 to Saturday 00:00 UTC. Joining is a prestige, and the next 24 hours of net worth are compared against a bracket in your league, at each player's own elapsed time. Promotion and demotion move you through six leagues. Rewards are crystals plus item stars (+20% value each) (#492-#497).
- **Leave early**: prestiging mid-run warns first, and a Retire button freezes your score (#505).
- **Strings**, the tournament currency: paid by every reward band. They buy a permanent tournament track (Star Resonance, Lucky Draw, Surge Luck, Harmonics), leaderboard name colours and titles, and belt themes (#519, #520, #521).

### Menus
- **Side drawer** as a grid of icons with captions (#510).
- **Builder**: role tabs and swipeable cards with rendered 3D building icons (#511).
- **Recipes**: item-type tabs and cards, with a drop-up to pick between several ways of making an item (#512).
- Research-gated items name the research they need (#506). The name card moves clear of the keyboard (#507).

### Economy and monetisation
- **Entropy Surge** rolls 10% of net worth in entropy (80%) or 10 crystals (20%), with no daily cap. It replaces Entropy Boost (#513).
- **Free Research** prestige upgrade: the first research of each run skips its timer (#514).
- **Longer prestige-shop tails** so the shop lasts through Ascension (about 680k prestige currency in total) (#515).
- **Ad-Free Forever** (one-time purchase): ad rewards without ads, plus +20% entropy income (#517).
- **What's new**: release notes are published by CI and shown once after an update (#516).
- **Login reward entropy scales with income**: bonus days pay minutes of current production, with the old flat amounts as the floor (#539).
- **Entropy Surge stacks** with any running boost (#537).

### Gameplay and UX
- **Building edit mode**: press and hold a placed building to move, rotate or flip it without destroying it. Its contents, recipe, upgrades, manager and filter rules move with it, and moving is free. A Remove button deletes it after a confirmation; delete mode stays for clearing several buildings (#430).
- **Belt tracing**: tap a start, tap each point along the route, and confirm to place the whole belt. It routes itself between points, never crosses itself, and can join an existing belt. Undo removes the last point (#432).
- **Taps vs. pans and zooms**: one shared rule for the map. Pinches, pans, glide stops and resting fingers no longer place or delete anything, and the tap allowance scales with screen density so real taps aren't lost to finger wobble (#432).
- The achievement reset countdown ticks while the panel is open (#429).
- **Progress bars** above producing buildings (#547). Press and hold a field to move it (#542).
- **Belts**: a split in front of a building's input feeds the building (#556), belts merge at a building's exit, and seams show while previewing (#544). Chemistry streams only flow away from their Spring (#543).
- **Discord** invite in Settings > Support (#570).

### Platform and live ops
- **Faster startup**: one UGS sign-in and one Remote Config fetch per launch, and no throwaway anonymous player on a new install (#209, #573).
- **Smaller install**: unused packages removed, IL2CPP built for size, Medium managed stripping (#207, #574, #575).
- **Maintenance windows** can be scheduled (`maintenance.startsUtc`) and the poll interval tuned remotely (#224, #572).
- **Game-data publisher**: `live-ops/gamedata.json` is published to Remote Config by a workflow (#210, #571).
- **Beta telemetry**: tutorial funnel (`tutorial_step`, with lifetime play time), rewarded ads by placement, store purchases, PvE league joins and finishes, and feedback-link use (#567, #580).
- **CI security**: one UGS service account per job (Remote Config, development Cloud Code, symbol upload), so the self-hosted runner only holds the symbols key; a UGS Key Check workflow; production game-data publishes wait for approval in a protected `production` environment; internal-build AABs are kept 3 days (#534, #581, #583, #584).
- Store readiness: iOS tracking prompt, Android crash symbols, privacy answers (#563); HUD split into sub-controllers (#286, #548-#555); Burst net-worth systems (#284, #546).

### Fixes
- The Research header no longer disappears in list mode, and no panel header shrinks under a long list (#499, #521).
- **Sign-in sticks**: a Google-linked account no longer shows as Guest after a restart (#431).
- **Newest save wins**: an upload that never reached the cloud no longer lets an older cloud save replace this device's progress (#431).
- **No more hang at 99%**: when the device and cloud saves differ, the game loads and then asks which to keep, instead of waiting behind the loading screen (#431).
- Time Warp credits world-task items (#540, #560). The tutorial can no longer soft-lock on a research step (#541, #559).

## 0.5 (November 2026)

The post-tutorial game: pacing past the first Carbon atom, belts that scale, and a production-ready platform layer.

### Gameplay
- **Cyclotron Kick minigame**: a timed-tap accelerator run. It pays a temporary entropy boost (x1.5 to x3) plus Insight for upgrades to boost length and recharge time (#393, #400).
- **Post-Carbon "Foundations" ramp** to smooth the wall after the tutorial (#393).
- **Belt Splitter** forks one belt into two or three (#359). **Filter** sorts a mixed belt (#326).
- **Belt density and speed**: two items per belt tile, and Transportation I/II research for x2/x4 belt speed (#380, #383).
- **Full-input autosell**: items that reach a full input are sold at the surplus rate instead of backing up the belt (#382).
- **Creation buffer**: big recipes keep crafting while their inputs refill (#413).
- **Statistics panel**: automation income per second, idle projection and net worth (#355, #388).
- Product values shown in the Recipes tab and building menus. Recipes are named after their product, with a method line (#350, #351, #354).
- Prestige upgrades now apply to production, and the run restarts from a clean scene (#358, #361, #370).
- Ignition rebalance: quark-to-Carbon craft times match, and Harvester upgrades take effect (#381).

### Balance
- The economy is derived from one progression curve with research blocks and target hours (#333, #338), and the curve and prestige-pacing reports were regenerated (#402, #405).
- The idle cap is cut to 30 minutes, so collecting is a reason to come back (#417).
- The Entropy Surge ad is priced from a measured, steady income rate (#401, #415).
- The per-research repeat-run discount is removed (#410).

### Visuals and UX
- 3D animated particles on quantum-field lattice belts (#397), with clean belt corners and merge arms (#399).
- Tutorial buildings show their physics in their shape (#364). Blender-rendered molecule icons (#363).
- Belt drawing explains refusals, marks continuations and routes around free ports (#352, #353).
- Compact building inspector with details behind an (i) button (#389, #391).
- Tutorial polish from AI and human playtests (#349, #366, #406).

### Platform and reliability
- One save across Android and iPhone (#396).
- A save during load can no longer wipe the grid (#368). Net worth is stored as a double so it stays exact above 16.7M (#408).
- Update prompt on boot, driven by Remote Config (#417). The maintenance check runs on app resume (#411).
- Ads: retry on failed LevelPlay init and loads, a test mode for dev builds, and 5-minute cooldowns (#372, #374, #377).
- Google sign-in shows the status code when it fails (#407).

### Repository
- Documentation rewrite: README, ADRs, runbooks, community files and a source-available license.
- CI/CD paused (manual-only workflows), actions pinned to SHAs, and the unused AWS Terraform removed.
- Docs, design pages and the privacy policy use the game's name, Quantum Foundry.

## 0.4 (October 2026)

- **Chemistry and Biology worlds**: a Track/World layer over build sites, with Chemistry C1-C4 and Biology B1-B4 content and per-world theming (#119-#128).
- **Multiple build sites** with idle aggregation across sites (#57-#61).
- **Endgame**: the Dyson Sphere megastructure, a proximity power grid with relays, building managers with star tiers, and daily login rewards and challenges.
- **Fusion and fission**: fusion-gated elements, an actinide decay-chain map, the Nucleon Harvester, and auto-sell for unused items (#320-#327).
- **Platform**: UGS Cloud Save, Remote Config feature flags with kill-switches and maintenance mode, LevelPlay rewarded ads, crystal IAP packs, UGS Analytics telemetry and Cloud Diagnostics.
- **Tooling**: an AI playtest harness (an agent plays in the Editor via MCP), a research-tree editor, and a CI pipeline to Google Play internal and TestFlight.
- The tutorial was rebuilt to end at the first Carbon atom, targeting 45-60 minutes (#275).

## 0.3 and earlier

Prototype: the DOTS simulation core, data pipeline, grid placement, belts, research, prestige, achievements and the tutorial framework.
