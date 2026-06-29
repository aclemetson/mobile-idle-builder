# Economy & Balance

**Scope:** Currencies, formulas, progression gates, and where authoritative numbers live. Use these numbers; do NOT derive your own. For specific item / recipe / building / cost values not listed, look up `Assets/Data/game_data.json` — the single source of truth that the editor importer turns into ScriptableObjects. Never duplicate that dataset here. (`docs/gameplay_loop_data.js` / `docs/gameplay-loop.html` are a human-only visual reference and may lag behind `game_data.json` — do not treat them as authoritative.)

> Verified against: `b3defb5`, 2026-06-28. If code contradicts this doc, trust the code and update this doc.

## Currencies

| Currency | Earned | Spent | Storage |
|---|---|---|---|
| **Entropy (e)** — base | Entropy sink consumes items; starting 250/run (`game_config.starting_entropy`) | research, building placement | `PlayerProgressData.BaseCurrency` |
| **Prestige currency (✦)** | on prestige: `floor(max(0, log10(netWorth / 5000) × 50))` (`PrestigeSystem.cs:46`; base/scale from `game_config`), modified by Entropy Echo (+5%/lvl) and Stellar Engine megastructure (×2); also achievements, login calendar, and crystal conversion | 12 prestige-shop upgrades (`PersistentUpgradeService.cs:48`, ≈5→5,400✦) + 8 managers (hire + star tiers, 10→4,000✦) | `PrestigeData.PrestigeCurrency` |
| **Crystals (◆)** — premium | achievements (2–200◆), daily challenges/login calendar, Crystal Drop ad (25◆/day), IAP packs | premium shop: speed boosts, entropy, prestige currency, time warp, research skip | `SaveData.paidCurrency` |

IAP packs (`IAPService.CrystalAmounts`): 600◆ ($0.99), 3,200◆ ($4.99), 7,500◆ ($9.99), 20,000◆ ($19.99) — consumables via Unity IAP / Google Play.

**Crystal value anchor:** $1 ≈ 4 hours of progress at the player's current stage ⇒ **150◆ = 1 "skip-hour"**. Crystals buy *time*, not raw power; the non-time sinks (entropy, prestige currency) grant a **% of the current build** so their dollar value self-scales with progress. Premium-shop sink tiers (`PremiumShopCalculator.cs`):
- **Speed boost** (2× offline): 30 min 75◆ / 2 h 270◆ / 8 h 950◆ / 24 h 2,500◆.
- **Entropy** (% of net worth, floored): 10% 300◆ / 25% 650◆ / 50% 1,200◆ / 100% 2,000◆.
- **Prestige currency** (flat ✦ grant — it persists across runs, so it is permanent power, not a time-skip): 50✦ 500◆ / 150✦ 900◆ / 500✦ 1,900◆ / 1,500✦ 3,600◆.
- **Time Warp** (instant offline collection, no wait): 2 h 350◆ / 8 h 1,300◆ / 24 h 3,500◆. Reuses `OfflineCollectionService.ComputeForDuration` (refresh snapshot via `SaveLocal`, pay out at the idle collection rate, apply to live ECS via `ECSLoadBridge.AddEntropy`/`AddInventoryItems`).

**Crystal faucet (free income):** 28-day login calendar (~750◆/cycle), 3 daily challenges (15◆ each), daily achievements (2/3/3/6/10 + 15 on full clear), weekly/monthly achievements, one-time progression. Tuned (Balanced stance) so a completionist earns ~3,500◆/mo (~23 skip-hr); daily achievements are kept low to avoid double-paying the daily challenges for the same actions.

**Rewarded ads (free, opt-in — `AdRewardCalculator.Placements`):** watch a short video for a balance-safe boost, with per-placement daily caps. Rewards are deliberately smaller than the equivalent crystal-shop tier (ads are free). Gated by `ads.enabled`; ships on a mock provider until LevelPlay is wired (`docs/levelplay-ads-setup.md`).

| Placement | Reward | Daily cap |
|---|---|---|
| Entropy Boost | 10% of net worth (floor 500e) | 5 |
| Double Offline | re-grants the just-collected idle run (offered on the idle-return modal) | 3 |
| +50% Idle | offline collection rate ×1.5 for 4h (`adsIdleBoostExpiryUtc`, applied in `OfflineCollectionService.GetAdIdleMultiplier`) | 3 |
| Production Surge | 2× production for 30 min (reuses `speedBoostExpiryUtc`) | 2 |
| Time Warp | instantly bank 1h of production (`ComputeForDuration`) | 2 |
| Crystal Drop | +25◆ | 1 |

Counters reset at 00:00 UTC (`SaveData.adWatchCounts` / `adWatchResetUtc`), same pattern as daily challenges.

> **Remote balance overrides:** any `game_config` scalar (and many per-entity values) can be overridden at runtime via the `GameDataOverrides` remote-config layer without a rebuild (see `feature-flags.md`). When debugging "the number in game ≠ the number in `game_data.json`", check for an active override before assuming the doc/code is wrong.

## Currency faucets & sinks (consolidated map)

Every source/sink for each currency, for balancing as new content is added. Amounts above are authoritative; this is the index.

**Prestige currency (✦)**
| Direction | Source/Sink | Amount | Owner |
|---|---|---|---|
| faucet | prestige run | `floor(log10(netWorth/5000)×50)` | `PrestigeSystem.cs` |
| faucet (mod) | Entropy Echo upgrade | +5%/lvl on the above | `PersistentUpgradeService.cs` |
| faucet (mod) | Stellar Engine (megastructure stage 5) | ×2 on the above | `MegastructureService.GetPrestigeGainBonus` |
| faucet | achievements / 28-day login calendar | per-reward (small) | `AchievementService.cs` / `DailyEventService.cs` |
| faucet (paid) | crystal → ✦ conversion (premium shop) | 50/150/500/1,500✦ for 500/900/1,900/3,600◆ | `PremiumShopCalculator.cs:36` |
| sink | 12 prestige-shop upgrades | ≈5 → 5,400✦ | `PersistentUpgradeService.cs` |
| sink | hire 8 managers + star tiers (max 5★) | 10 → 4,000✦ | `ManagerService.cs` / `game_data.json` |

**Crystals (◆)**
| Direction | Source/Sink | Amount | Owner |
|---|---|---|---|
| faucet | achievements (daily/weekly/monthly/progression) | 2–200◆ | `AchievementService.cs` |
| faucet | 28-day login calendar | ~750◆/cycle | `DailyEventService.cs` |
| faucet | 3 daily challenges | 15◆ each (~45◆/day) | `DailyEventService.cs` |
| faucet | Crystal Drop rewarded ad | 25◆, cap 1/day | `AdRewardCalculator.cs` |
| faucet (paid) | IAP packs | 600 / 3,200 / 7,500 / 20,000◆ | `IAPService.cs` |
| sink | speed boost / entropy / time warp | see premium-shop tiers above | `PremiumShopCalculator.cs` |
| sink | prestige-currency conversion | 500–3,600◆ | `PremiumShopCalculator.cs:36` |
| sink | research skip | `max(1, ceil(remaining/3600×150))◆` | `PremiumShopCalculator.CalcResearchSkipCost` |

**Entropy (e)** — faucets: entropy-sink buildings / collectors (core loop), Entropy Boost & Double/Idle ads, Entropy Headstart upgrade, premium-shop entropy grant. Sinks: research, building placement (`base_cost × mult^(n-1)`), multi-grid site unlocks (250,000e+).

## Core formulas

- **Prestige wall** (when prestige unlocks): `netWorth ≥ prestige_base_value × prestige_wall_multiplier` = 5,000 × 10 = **50,000e net worth**.
- **Building purchase scaling**: `base_cost × multiplier^(n-1)`, n = same-type buildings already placed; multiplier 1.5 (T1), 1.4 (T2), 1.3 (T3+) (`game_config.building_purchase_multiplier_*`).
- **Power (proximity grid, single shared eV pool)**: a consumer runs only if within a generator's `InfluenceRadius`; global `Draw` (Σ connected consumers' `DrawEV × manager PowerDiscount`) vs `Supply` (Σ generator output eV) gives `ratio = min(1, Supply/Draw)` — connected buildings craft at `ratio` speed (brownout), disconnected at 0. Generator (Basic Generator) scales **50 → 100 → 200 → 400 eV**, radius **3 → 4 → 5 → 6** tiles by level. Per-building draw eV (L1, `base_power_cost_ev`, scales with speed level): SFC 10, Atom Generator 20, Isotopic Manipulator 25, Radioactive Containment 30, Molecular Synthesizer 50, Materials Forge 100, Component Fabricator 200. (v1 uses static per-level draw; the old `power_cost_is_dynamic` mass/neutron formulas are superseded and out of scope. Harvester, Maxwell's Demon, Basic Generator draw nothing.)
- **Idle/offline**: collects `idleBaseCollectionRate = 20%` of active output, max `idleBaseMaxSeconds = 2h` base, hard cap 12h (`GameConfigSO.cs:34-40`); both raisable by prestige-shop upgrades (`IdleCollectionRate`, `IdleTimeCap`).
- **Speed boost (premium)**: timed multiplier composed onto `PrestigeData.SpeedMultiplier` at load (`ECSLoadBridge.cs:148`), stripped at save (`:236`).

## Progression phases & research gates (distilled from `game_data.json`)

Tutorial = phases ①–⑥ (steps 0–50, ends after first prestige). Post-tutorial roadmap:

| Phase | Gate (research → entropy cost) | Economy landmark |
|---|---|---|
| ⑦ Run 2 ramp | repeat discounts: Recombination I 75e, Hydrogen Synth 38e, Automation I 13e | H loop ~4–8 e/sec |
| ⑧ Atomic Assembly | `atomic_assembly` → 500e (375e discounted) | He-4 ×10 … Fe ×5,120 sell values |
| ⑨ Heavy Elements | 3,000e | Uranium = 1,310,720e/unit |
| ⑩ Isotopes | 1,500e | isotopes sell 1.5× base element |
| ⑪ Radioactive Decay | 5,000e | needs ⑨+⑩ |
| ⑫ Molecular Synthesis | 3,000e (+ element gates 1K→80K) | tier 3 |
| ⑬ Materials Science | 150,000e | Steel 1.5M, Carbon Fiber 6M, Ti Alloy 25M |
| ⑭ Component Engineering | 5,000,000e | Quantum Processor 50B, Plasma Ring 200B, Antimatter Cell 800B |
| ⑮ Megastructure Theory | 50,000,000e | Dyson Node 5T, Orbital Frame 20T, Graviton Lens 100T |

Tiers: 1 Subatomic, 2 Atomic, 3 Molecular, 4 Materials, 5 Components (`game_data.json` `tiers`).

### Research timers & skip

Starting a research costs entropy up front and then runs a **real-time timer** (one research at a time — the lab is busy until it finishes or is skipped). Timers tick offline (`activeResearchCompleteUtc` ISO-UTC, same pattern as the speed boost) and complete on the next tick/load. Base time is authored per research as `duration_seconds` in `game_data.json`, seeded from this `depth_in_tree` curve (tutorial nodes are pinned to the low end so the tutorial never stalls):

| depth | base time | depth | base time |
|---|---|---|---|
| 0 | 10s | 5 | 60 min |
| 1 | 30s | 6 | 2 h |
| 2 | 2 min | 7 | 3 h |
| 3 | 10 min | 8 | 4 h |
| 4 | 30 min | (megastructure_theory, depth 8) | 4 h |

`duration_seconds = 0` ⇒ instant unlock (back-compat). Tutorial pins: `recombination_i` 10s, `hydrogen_synthesis` 15s, `automation_i` 20s, `atomic_assembly` 30s.

**Skip cost (crystals):** `max(1, ceil(remaining_seconds / 3600 × 150))◆` — the documented 150◆/skip-hour anchor against time remaining (`PremiumShopCalculator.CalcResearchSkipCost`). Early research skips cost only 1–2◆; the deepest 4h research costs ~600◆ at full timer. Spent against `SaveData.paidCurrency` in `ResearchService.SkipActive()`.

**Reduction:** the **Research Overdrive** prestige upgrade (`ResearchSpeed` effect) cuts timer length 5%/level up to −50%; applied at research start via `ResearchService.EffectiveDurationSeconds` (a later purchase does not retroactively shorten an in-flight timer).

**Logistics gates (per-building capacity upgrades are research-gated).** Each building has three per-building upgrade tracks bought with entropy in the inspector: speed (`upgrade_levels`), output capacity (`storage_upgrade_levels`), and input capacity (`input_upgrade_levels`). Speed is always available; the two capacity tracks only appear once their unlocking research is purchased (gate checked in `HUDBuildingInspectorSubController` via `ResearchService.IsUnlocked`):

| Logistics research | Branch | Prereq | Cost | Unlocks |
|---|---|---|---|---|
| `surplus_containment` | Engineering | `automation_i` | 2,000e | Output Capacity upgrades (idle-stockpile wall) |
| `feedstock_buffers` | Engineering | `molecular_synthesis` | 10,000e | Input Capacity upgrades (multi-input throughput wall) |

## Prestige shop (12 permanent upgrades — `PersistentUpgradeService.cs:48-130`)

All bought with prestige currency (✦); costs roughly double per level (≈5 → 5,400✦ range). The 12 live upgrade ids (no others exist — there is no "Decay Mastery"):

| id | Name | Effect | Levels |
|---|---|---|---|
| `entropy_headstart` | Entropy Headstart | +250e starting entropy / run | per-lvl |
| `memory_resonance` | Memory Resonance | −5% research cost | per-lvl |
| `assembly_line` | Assembly Line | +10% craft speed | per-lvl |
| `expanded_vault` | Expanded Vault | +20 inventory slots | per-lvl |
| `field_cooldown` | Quick Hands | −5% field tap cooldown (max −50%) | per-lvl |
| `quantum_yield` | Quantum Yield | +10% recipe output | per-lvl |
| `efficient_layouts` | Efficient Layouts | −5% building cost (max −30%) | per-lvl |
| `turnkey_builder` | Turnkey Builder | +1 pre-placed Harvester / run | per-lvl |
| `research_overdrive` | Research Overdrive | −5% research timer (max −50%; prereq Memory Resonance 2) | 10 |
| `entropy_echo` | Entropy Echo | +5% prestige currency earned / run | per-lvl |
| `idle_time_cap` | Dormant Resonance | +30 min idle runtime (base 2h, max 12h) | per-lvl |
| `idle_collection_rate` | Idle Efficiency | +5% idle collection rate (base 50%, max 100%) | per-lvl |

**Wiring caveat:** `assembly_line` (craft speed) and `quantum_yield` (output) currently affect **idle earnings + UI display only, NOT live ECS production** — they flow through `PrestigeData.SpeedMultiplier`/`OutputMultiplier`, which `ProductionSystem` does not read. See the gap map in `ecs-patterns.md` before building anything on top of them. (Megastructure Output/Speed bonuses, by contrast, ARE live — they go through `GlobalProductionBonus`, not `PrestigeData`.)

## Managers (prestige-currency sink — `game_data.json` `managers`, `ManagerService.cs`)

Hireable crew; each grants ONE passive bonus to ONE assigned building. Hired managers + assignments + star levels all survive prestige (`SaveData.managerStars`). Star upgrades (max 5★) spend prestige currency; the *star-scaled* value is what gets baked/read (single accessor `ManagerService.EffectiveBonusValue`). Bonus types: CraftSpeed (×ProductionSpeed, crafters only — collectors ignore it), OutputQuantity (×recipe output, floored), PowerDiscount (multiplier on eV draw kept, e.g. 0.8 = −20%; live in `PowerGridSystem`).

| Manager | Bonus | Base → 5★ value | Hire ✦ | Star costs ✦ (★1–★5) |
|---|---|---|---|---|
| Tinker | CraftSpeed | 1.25× → 2.05× | 10 | 0, 20, 30, 40, 50 |
| Stoker | PowerDiscount | 0.8 → 0.4 | 10 | 0, 20, 30, 40, 50 |
| Packrat | OutputQuantity | 2.0× → 6.0× | 25 | 0, 50, 75, 100, 125 |
| Overclocker | CraftSpeed | 1.5× → 2.3× | 40 | 0, 80, 120, 160, 200 |
| Conductor | PowerDiscount | 0.6 → 0.2 | 60 | 0, 120, 180, 240, 300 |
| Duplicator | OutputQuantity | 3.0× → 7.0× | 150 | 0, 300, 450, 600, 750 |
| Chronomancer | CraftSpeed | 2.0× → 2.8× | 300 | 0, 600, 900, 1200, 1500 |
| Demiurge | OutputQuantity | 4.0× → 8.0× | 800 | 0, 1600, 2400, 3200, 4000 |

Managers are the **primary prestige sink** alongside the prestige shop — note OutputQuantity managers ARE live in `ProductionSystem`/`CollectorSystem` (unlike the prestige-shop output upgrade), so they are the real driver of output scaling.

## Megastructure (Dyson Sphere — meta layer above prestige, `MegastructureService.cs`)

Endgame project gated by `megastructure_theory` research (phase ⑮). 5 stages, each consuming tier-5 components and granting a permanent stacking reward; completed stages + partial contributions survive prestige (`SaveData.megastructureStage`/`megastructureContrib*`). Reward wiring: Output/Speed bonuses push into the `GlobalProductionBonus` ECS singleton (read **live** by ProductionSystem/CollectorSystem + folded into idle); the prestige-gain bonus is read directly by `PrestigeSystem` via `GetPrestigeGainBonus()`.

| Stage | Cost (tier-5 items) | Reward |
|---|---|---|
| 1 Scaffold Ring | 10× dyson_node | +10% output |
| 2 Support Lattice | 25× dyson_node + 5× orbital_frame | +10% craft speed |
| 3 Inner Shell | 50× dyson_node + 20× orbital_frame + 2× graviton_lens | +25% output |
| 4 Stabilizer Array | 40× orbital_frame + 10× graviton_lens | +25% craft speed |
| 5 Stellar Engine | 100× dyson_node + 80× orbital_frame + 30× graviton_lens | ×2 prestige currency earned |

## Balance derivation method (when a task file needs NEW numbers)

Reference game data lives in `docs/ipm-research.md` (Idle Planet Miner economy: ore ×2.0–2.6/tier scaling, planet costs ×3.8, galaxy-sell prestige cadence). House rules used so far:
1. Sell values scale ~×4 per recipe step within a tier, ×8–×40 across tier boundaries (see ⑧–⑮ landmarks above).
2. Research gates ≈ 10–30 minutes of current-phase income at the time they're reached.
3. Prestige cadence target: first prestige ≈ 1–2h session; later runs faster via discounts/upgrades.
4. Premium pricing anchors to the 600◆/$0.99 pack; a "nice-to-have" boost ≈ 50–150◆.

## Economy health & risks (re-evaluated 2026-06-28 — read before adding more track)

Findings from the full earning re-evaluation. These are the things most likely to bite as content/progression ("track") is extended.

1. **Dead prestige multipliers (high priority).** `PrestigeData.SpeedMultiplier` and `OutputMultiplier` are shown in the UI and applied to the **idle snapshot only** — `ProductionSystem` does not read them (see `ecs-patterns.md` gap map). So the prestige-shop `assembly_line` (craft speed) and `quantum_yield` (output) upgrades barely affect *live* play. Live output scaling currently comes almost entirely from **OutputQuantity managers** + megastructure `GlobalProductionBonus`, which ARE read live. As more tiers are added, players who lean on the prestige shop will feel prestige is weak in active play. **Decision needed:** wire `PrestigeData` Speed/Output into `ProductionSystem`, or stop advertising them as live power. (Code change — out of scope for this doc pass; flagged here.)

2. **Two parallel output-scaling paths.** Output now scales via (a) prestige-shop `quantum_yield` (idle/display only) and (b) OutputQuantity managers + megastructure (live). When authoring new high-tier balance, anchor expected output to the **manager + megastructure** path, not the prestige-shop path, or live-vs-idle income will diverge.

3. **Crystal faucet vs sink headroom.** Free faucet ≈ 3,500◆/mo for a completionist (~23 skip-hours) against sinks anchored at 150◆/skip-hour. New crystal sinks are safe to add, but new *faucets* (more achievements/challenges as content grows) compound — keep the completionist monthly total near the ~23 skip-hour target so premium time-skips retain value.

4. **Prestige cadence vs new tiers.** With `prestige_scale = 50`, each ×10 of netWorth yields only ≈ +50✦ (flat, log curve). Adding higher tiers raises the netWorth ceiling but NOT proportionally the ✦ payout, so deep runs can feel ✦-starved relative to the cost of late managers (Demiurge 800✦ hire + 4,000✦/star) and deep upgrades (≈5,400✦). When adding tiers, re-check that first prestige stays ≈1–2h and that ✦ income per run keeps pace with the new sink costs — consider raising `prestige_scale` or adding ✦ faucets rather than letting the gap widen.

5. **Manager star costs are the biggest ✦ sink and are flat-authored.** Star costs live as explicit `star_costs[]` arrays in `game_data.json` (not a formula), so new managers must have their tables hand-tuned. Keep them on the same rough curve (hire cost → ★ costs scaling ~×1.5–2 per star) to avoid outliers.
