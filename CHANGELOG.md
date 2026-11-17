# Changelog

Notable changes per release. Builds are versioned `major.minor.patch`, and the patch number increases on every commit. Pull request numbers link the detail.

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
