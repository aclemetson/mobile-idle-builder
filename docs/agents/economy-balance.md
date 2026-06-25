# Economy & Balance

**Scope:** Currencies, formulas, progression gates, and where authoritative numbers live. Use these numbers; do NOT derive your own. For specific item values not listed, look up `docs/gameplay_loop_data.js` (the authoritative full dataset) — never duplicate it here.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## Currencies

| Currency | Earned | Spent | Storage |
|---|---|---|---|
| **Entropy (e)** — base | Entropy sink consumes items; starting 250/run (`game_config.starting_entropy`) | research, building placement | `PlayerProgressData.BaseCurrency` |
| **Prestige currency (✦)** | on prestige: `floor(max(0, log10(netWorth / 5000) × 50))` (`PrestigeSystem.cs:46`; base/scale from `game_config`) | prestige shop permanent upgrades (catalogue: `PersistentUpgradeService.cs:48`, costs 5→5400✦) | `PrestigeData.PrestigeCurrency` |
| **Crystals (◆)** — premium | achievements (2–200◆), daily challenges/login calendar, IAP packs | premium shop: speed boosts, entropy, prestige currency | `SaveData.paidCurrency` |

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

## Core formulas

- **Prestige wall** (when prestige unlocks): `netWorth ≥ prestige_base_value × prestige_wall_multiplier` = 5,000 × 10 = **50,000e net worth**.
- **Building purchase scaling**: `base_cost × multiplier^(n-1)`, n = same-type buildings already placed; multiplier 1.5 (T1), 1.4 (T2), 1.3 (T3+) (`game_config.building_purchase_multiplier_*`).
- **Power (proximity grid, single shared eV pool)**: a consumer runs only if within a generator's `InfluenceRadius`; global `Draw` (Σ connected consumers' `DrawEV × manager PowerDiscount`) vs `Supply` (Σ generator output eV) gives `ratio = min(1, Supply/Draw)` — connected buildings craft at `ratio` speed (brownout), disconnected at 0. Generator (Basic Generator) scales **50 → 100 → 200 → 400 eV**, radius **3 → 4 → 5 → 6** tiles by level. Per-building draw eV (L1, `base_power_cost_ev`, scales with speed level): SFC 10, Atom Generator 20, Isotopic Manipulator 25, Radioactive Containment 30, Molecular Synthesizer 50, Materials Forge 100, Component Fabricator 200. (v1 uses static per-level draw; the old `power_cost_is_dynamic` mass/neutron formulas are superseded and out of scope. Harvester, Maxwell's Demon, Basic Generator draw nothing.)
- **Idle/offline**: collects `idleBaseCollectionRate = 20%` of active output, max `idleBaseMaxSeconds = 2h` base, hard cap 12h (`GameConfigSO.cs:34-40`); both raisable by prestige-shop upgrades (`IdleCollectionRate`, `IdleTimeCap`).
- **Speed boost (premium)**: timed multiplier composed onto `PrestigeData.SpeedMultiplier` at load (`ECSLoadBridge.cs:148`), stripped at save (`:236`).

## Progression phases & research gates (distilled from `gameplay_loop_data.js`)

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

## Prestige shop (permanent upgrades — `PersistentUpgradeService.cs:48-130`)

Tier 1 (no prereqs): Entropy Headstart (+250e/run/lvl, 5–80✦), Memory Resonance (−5% research/lvl), Assembly Line (+10% craft speed/lvl), Expanded Vault (+20 slots/lvl). Tier 2: Quantum Yield (+10% output/lvl), Efficient Layouts (−5% build cost/lvl). Tier 3: Decay Mastery, Turnkey Builder, Research Overdrive (-5% research timer/lvl, 10 levels, max -50%; prereq Memory Resonance 2), Entropy Echo (+5% prestige gain/lvl), plus idle-collection upgrades. Costs roughly double per level (5 → 5,400✦ range).

**Wiring caveat:** craft-speed/output upgrades currently affect idle earnings + display only, not live ECS production — see the gap map in `ecs-patterns.md` before building anything on top of them.

## Balance derivation method (when a task file needs NEW numbers)

Reference game data lives in `docs/ipm-research.md` (Idle Planet Miner economy: ore ×2.0–2.6/tier scaling, planet costs ×3.8, galaxy-sell prestige cadence). House rules used so far:
1. Sell values scale ~×4 per recipe step within a tier, ×8–×40 across tier boundaries (see ⑧–⑮ landmarks above).
2. Research gates ≈ 10–30 minutes of current-phase income at the time they're reached.
3. Prestige cadence target: first prestige ≈ 1–2h session; later runs faster via discounts/upgrades.
4. Premium pricing anchors to the 600◆/$0.99 pack; a "nice-to-have" boost ≈ 50–150◆.
