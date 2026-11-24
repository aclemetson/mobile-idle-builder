# Economy & Balance

**Scope:** Currencies, formulas, progression gates, and where authoritative numbers live. Use these numbers; do NOT derive your own. For specific item / recipe / building / cost values not listed, look up `Assets/Data/game_data.json` — the single source of truth that the editor importer turns into ScriptableObjects. Never duplicate that dataset here. (`docs/gameplay_loop_data.js` / `docs/gameplay-loop.html` are a human-only visual reference and may lag behind `game_data.json` — do not treat them as authoritative.)

> Verified against: `b3defb5`, 2026-06-28; prestige multipliers live + research discounts + Entropy Surge 2026-11-11; net worth at list price + stored items 2026-11-19; speed burst as factory clock + one boost at a time 2026-11-19; item stars 2026-11-23. If code contradicts this doc, trust the code and update this doc.

> Item pacing across worlds (as built and the proposed restructure, with calendar days): `docs/item-timeline.html` / `scripts/item_timeline.py` (see `chemistry-biology.md`).

## Currencies

| Currency | Earned | Spent | Storage |
|---|---|---|---|
| **Entropy (e)** — base | Entropy sink consumes items; starting 250/run (`game_config.starting_entropy`) | research, building placement | `PlayerProgressData.BaseCurrency` |
| **Strings** (tournament currency, #500) | PvE tournament rewards: every band of every league (`game_config.pve.leagues[].rewards[].strings`; Bronze 5 for finishing up to Champion 300 for 1st) | tournament track + cosmetics (parts 2-3 of #500) | `SaveData.pveStrings` (survives prestige and Ascension) |
| **Prestige currency (✦)** | on prestige: `floor(max(0, log10(netWorth / 5000) × 50))` (`PrestigeSystem.cs:46`; base/scale from `game_config`), modified by Entropy Echo (+5%/lvl) and Stellar Engine megastructure (×2); also achievements, login calendar, and crystal conversion | 12 prestige-shop upgrades (`PersistentUpgradeService.cs:48`, ≈5→5,400✦) + 8 managers (hire + star tiers, 10→4,000✦) | `PrestigeData.PrestigeCurrency` |
| **Crystals (◆)** — premium | achievements (2–200◆), Daily achievements/login calendar, Crystal Drop ad (25◆/day), IAP packs | premium shop: speed boosts, entropy, prestige currency, time warp, research skip | `SaveData.paidCurrency` |

IAP packs (`IAPService.CrystalAmounts`): 600◆ ($0.99), 3,200◆ ($4.99), 7,500◆ ($9.99), 20,000◆ ($19.99) — consumables via Unity IAP / Google Play.

**Crystal value anchor:** $1 ≈ 4 hours of progress at the player's current stage ⇒ **150◆ = 1 "skip-hour"**. Crystals buy *time*, not raw power; the non-time sinks (entropy, prestige currency) grant a **% of the current build** so their dollar value self-scales with progress. Premium-shop sink tiers (`PremiumShopCalculator.cs`):
- **Speed burst** (2× factory clock, online and offline): 30 min 75◆ / 2 h 270◆ / 8 h 950◆ / 24 h 2,500◆.
- **One boost at a time** (`BoostLock`, 2026-11-19): while any timed boost runs (speed burst, +50% Idle, Cyclotron Kick), every other shop bonus and ad reward is locked. Crystal packs, the research skip and Double Offline stay available.
- **Entropy** (% of net worth, floored): 10% 300◆ / 25% 650◆ / 50% 1,200◆ / 100% 2,000◆.
- **Prestige currency** (flat ✦ grant — it persists across runs, so it is permanent power, not a time-skip): 50✦ 500◆ / 150✦ 900◆ / 500✦ 1,900◆ / 1,500✦ 3,600◆.
- **Time Warp** (instant offline collection, no wait): 2 h 350◆ / 8 h 1,300◆ / 24 h 3,500◆. Reuses `OfflineCollectionService.ComputeForDuration` (refresh snapshot via `SaveLocal`, pay out FULL production for the window, not the idle collection rate or idle boost (2026-11-20), apply to live ECS via `ECSLoadBridge.AddEntropy`/`AddInventoryItems`). Items reaching the Demon in that window count toward the Chemistry world tasks.

**Crystal faucet (free income):** 28-day login calendar (~750◆/cycle), 8 Daily achievements worth 45◆/day together (login 3, craft 50 4, craft 200 6, place 3 5, research 1 6, spend 500 5, prestige 8, Day Well Spent 8), weekly/monthly achievements, one-time progression. The separate 3-a-day challenges (15◆ each) were merged into the Daily achievements on 2026-11-20; the Daily tab keeps their ~45◆/day. Daily achievements count from the start; Weekly/Monthly/Progress unlock with the first prestige. Tuned (Balanced stance) so a completionist earns ~3,500◆/mo (~23 skip-hr). **PvE tournaments (weekly, 2026-11-23):** reward bands in `game_config.pve` pay crystals + item stars per rank: Bronze 150/100/60/30/10◆ (1st / top 3 / top 10 / top 25% / everyone), scaled x1.5 Silver, x2 Gold, x2.5 Platinum, x3 Diamond, x4 Champion (Champion 1st = 600◆ + 20★). A Bronze player winning every week adds ~600◆/month (~17% on top of the completionist faucet); first-pass values, watch them against the ~23 skip-hour target below.

**Rewarded ads (free, opt-in — `AdRewardCalculator.Placements`):** watch a short video for a balance-safe boost, with per-placement daily caps. Rewards are deliberately smaller than the equivalent crystal-shop tier (ads are free). Gated by `ads.enabled`; ships on a mock provider until LevelPlay is wired (`docs/runbooks/levelplay-ads-setup.md`).

| Placement | Reward | Daily cap |
|---|---|---|
| Double Offline | re-grants the just-collected idle run (offered on the idle-return modal) | 3 |
| +50% Idle (2h) / (4h) | offline collection rate ×1.5 for 2h or 4h, the player picks by time away (2026-11-20; `IdleRateBoostShort` / `IdleRateBoost`, both set `adsIdleBoostExpiryUtc`; offline it counts only for the part of the absence it covered, `OfflineCollectionService.BoostWeightedSeconds`) | 2 each |
| Production Surge | 2× factory clock for 30 min (reuses `speedBoostExpiryUtc`) | 2 |
| Time Warp | instantly bank 1h of production (`ComputeForDuration`) | 2 |
| Crystal Drop | +25◆ | 1 |
| Entropy Surge | **a roll** (#489, 2026-11-24): 80% pays 10% of live net worth in entropy (`AdRewardCalculator.CalcEntropyReward`, floor 500e, quoted before the ad plays), 20% pays 10◆ (`SurgeRollsCrystals`); **15 min cooldown** (`SaveData.adLastWatch`). Replaced Entropy Boost (retired; its enum id stays for old save counters) and the old "10 min of automation income" payout | none |

**Ad-free purchase** (#503, `AdFree.cs`, product `ad_free`, ~$9.99 non-consumable, `SaveData.adFree`):
- Every placement grants its reward without the ad (`AdService.SkipsAds`; it ignores the ads flag and ad availability). Cooldowns and caps still apply, and the buttons read "Claim". The Entropy Surge stays.
- **+20% entropy income** forever: live, as a factor in the `EntropyIncomeBoost` singleton (`AdFree.CombinedIncomeMultiplier` = Cyclotron boost × 1.2, written by `CyclotronController.SyncBoost`), and offline (`OfflineCollectionService` idle payout + Time Warp, `AdFree.ApplyIncome`).

Every placement except Entropy Surge has a **5 min cooldown** between watches (`AdRewardCalculator.StandardCooldownMinutes`, same `SaveData.adLastWatch` bookkeeping; the panel button shows "Ready in m:ss"). The surge has no daily cap (`AdRewardCalculator.Uncapped`, `HasDailyCap`), so only its cooldown paces it. `IncomeRateTracker.RewardPerSecond` (and `AutomationIncome.Unboosted`) fed the old income-minutes surge and are now unused by ads: a cleanup candidate. Other counters reset at 00:00 UTC (`SaveData.adWatchCounts` / `adWatchResetUtc`), same pattern as daily challenges.

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
| faucet | Daily achievements | 45◆/day for all 8 | `Assets/Data/achievements/daily_*.asset` |
| faucet | Crystal Drop rewarded ad | 25◆, cap 1/day | `AdRewardCalculator.cs` |
| faucet (paid) | IAP packs | 600 / 3,200 / 7,500 / 20,000◆ | `IAPService.cs` |
| sink | speed boost / entropy / time warp | see premium-shop tiers above | `PremiumShopCalculator.cs` |
| sink | prestige-currency conversion | 500–3,600◆ | `PremiumShopCalculator.cs:36` |
| sink | research skip | `max(1, ceil(remaining/3600×150))◆` | `PremiumShopCalculator.CalcResearchSkipCost` |

**Entropy (e)** — faucets: entropy-sink buildings / collectors (core loop), Entropy Surge & Double/Idle ads, Entropy Headstart upgrade, premium-shop entropy grant. Sinks: research, building placement (`base_cost × mult^(n-1)`), field purchases (×10 each, see Core formulas), multi-grid site unlocks (250,000e+), **World unlocks** (each world's `unlock_cost` is 0 — the gate is a research node, see `chemistry-biology.md`). Research gate ladder (first-pass): **Chemistry** `chemistry_lab` 150K → `reaction_engineering` 500K → `organic_chemistry` 2M → `biochem_precursors` 5M; **Biology** `biology_lab` 10M → `cell_biology` 50M → `multicellular_life` 250M → `ecosystems` 1B. Chem/Bio balance is **first-pass** — the real re-tier is Phase 6 (which must also lift the heavy-physics/Megastructure 50M endgame above Biology's 50M–1B, and re-check the flat-log ✦ payout against the ~1B net-worth ceiling).

**Belt speed tiers (2026-11-12, global research, DSP style):** a belt carries `belt_items_per_tile` (2) x belt speed items/s. Transportation I (`transportation_1`, Foundations, 62,400) adds `ConveyorSpeed` +1.0 (x2, 4/s: Lv3 runs full); Transportation II (`transportation_2`, last in Foundations, 1,090,000, off-curve) adds +2.0 (x4, 8/s: Lv4). Without them Lv3/Lv4 upgrades stay buyable, and the inspector's speed-upgrade row warns "Belts carry 2/s; Lv3 needs 4/s. Research Transportation I." (`BeltCapacity`, busiest input or output vs belt).

**Ignition production ratios (2026-11-11, Dyson Sphere Program style; editor: https://claude.ai/artifact/KMM9xMKKnGiCgY3WFwAoEs):** craft times up to Carbon are matched to their suppliers, so with belts at 2 items/s every line runs full and the ratios hold at every level (all four buildings double speed per level). Proton / Neutron **2 s** (one Lv1 Harvester feeds one Lv1 Combiner on its 2-quark input), Hydrogen 2 s (1 Combiner : 1 Atom Generator), Deuterium **2 s** (1 proton + 1 neutron Combiner : 1 Atom Generator), Helium-4 4 s, D + D fusion **4 s** (1 Deuterium Atom Generator : 1 reactor), 4H fusion **4 s** (2 Hydrogen Atom Generators : 1 reactor), triple-alpha Carbon 6 s (2 helium reactors : 1 Carbon reactor). `manual_craft_time` (hand crafting) is unchanged. Pinned in `IgnitionRebalanceTests` / `RecipeTimingTests`; item values are the tutorial anchor and did not change.

## Core formulas

- **Net worth** (`NetWorthSystem`, 2026-11-19): entropy held + every purchase this run at its **list price** (buildings, upgrades, research, unlocks; discounts from the prestige cost reduction and Memory Resonance are added back through `TotalEntropyDiscounted`) + inventory at `baseSellValue` + items waiting inside buildings at the Demon price (no timed boosts). Prestige ✦, the prestige wall, the Entropy Surge ad and the entropy shop packs all read it.
- **Item stars** (PvE phase 2, 2026-11-23, `ItemStars`): tournament rewards put stars on random discovered items (inputs and outputs of known recipes); each star adds **+20%** of the item's base price, additively (5 stars = x2, 10 = x3). Permanent: they survive prestige and Ascension. Applied to every runtime price: Demon/Calorimeter payout, surplus sales and stored-item value (`SurplusSale.DemonPrice`), inventory net worth, offline chains and the Demon panel. Not to escaping byproduct entropy (baked into ECS). So stars raise income and net worth together, which means past stars count toward tournament scores (by design, like Idle Planet Miner).
- **Prestige wall** (when prestige unlocks): `netWorth ≥ prestige_base_value × prestige_wall_multiplier` = 5,000 × 10 = **50,000e net worth**.
- **Field purchase (Field Synthesis, 250e after Automation I)**: extra fields bought from the Build menu cost `purchase_cost_base × purchase_cost_multiplier^n`, n = purchases of that field this run (reset on prestige). Quark fields sell as a pair at 300e → 900e → 2,700e → 8,100e…; a lepton field 200e → 600e → 1,800e…. Both ×3, lowered from ×10 on 2026-11-11 so a second hydrogen line (quarks and its electrons) is reachable soon after a prestige. The growth is still exponential so fields never become a repeatable income lever. Prestige `CostReduction` does not apply. Owner: `FieldPurchaseService`.
- **Building purchase scaling**: `base_cost × multiplier^(n-1)`, n = same-type buildings already placed; multiplier 1.5 (T1), 1.4 (T2), 1.3 (T3+) (`game_config.building_purchase_multiplier_*`).
- **Power (proximity grid, single shared eV pool)**: a consumer runs only if within a generator's `InfluenceRadius`; global `Draw` (Σ connected consumers' `DrawEV × manager PowerDiscount`) vs `Supply` (Σ generator output eV) gives `ratio = min(1, Supply/Draw)` — connected buildings craft at `ratio` speed (brownout), disconnected at 0. Generator (Basic Generator) scales **50 → 100 → 200 → 400 eV**, radius **3 → 4 → 5 → 6** tiles by level. Per-building draw eV (L1, `base_power_cost_ev`, scales with speed level): SFC 10, Atom Generator 20, Isotopic Manipulator 25, Radioactive Containment 30, Molecular Synthesizer 50, Materials Forge 100, Component Fabricator 200. (v1 uses static per-level draw; the old `power_cost_is_dynamic` mass/neutron formulas are superseded and out of scope. Harvester, Maxwell's Demon, Basic Generator draw nothing.)
- **Idle/offline**: collects `idleBaseCollectionRate = 20%` of active output, max `idleBaseMaxSeconds = 30 min` base (cut from 2h on 2026-11-16 to bring players back to collect), hard cap 12h (`GameConfigSO.cs:34-40`); both raisable by prestige-shop upgrades (`IdleCollectionRate`, `IdleTimeCap`).
- **Speed burst (premium / Production Surge ad)**: live, `FactoryClockSystem` runs production, harvesters and belts at 2× (`FactoryClock.Rate`). Offline, the idle snapshot is unboosted and `OfflineCollectionService` counts the part of the absence the burst covered twice (`BoostWeightedSeconds`): a 30 min burst over 8 h away pays 8.5 h, not 16 h.

## Progression phases & research gates (distilled from `game_data.json`)

Tutorial = phases ①–⑥ (steps 0–50, ends after first prestige). Post-tutorial roadmap:

| Phase | Gate (research → entropy cost) | Economy landmark |
|---|---|---|
| ⑦ Run 2 ramp | repeat discounts: Recombination I 75e, Hydrogen Synth 38e, Automation I 13e | H loop ~4–8 e/sec |
| ⑧ Nucleosynthesis | `atomic_assembly` → 500e (375e discounted) | Atom Generator makes He-4 (15e) only (Deuterium comes at ⑧b) |
| ⑧b Deuterium → Fusion | `deuterium_synthesis` 1,200e → `fusion_i` 4,000e (tutorial end) | D 4e; 2D→He (15e) + γ 5e escape bonus; 3He→C 160e. Everything below is **generated** (see "Derived economy") |
| Chemistry gate (2026-11-19) | Physics before the gate: `light_elements` 224k, `transportation_2` 911k, `fusion_ii` 1.58M, `mid_elements` 2.26M (H to Si, no molecules). Then five **Demon delivery stages** (`world_tasks`, one per run, survive prestige) open Chemistry: `chemistry_lab` 1.12M → `molecular_synthesis` 2.67M | O 625, Si 2.72k; water/ammonia now Chemistry molecules |
| Past the gate | `fusion_iii` 5.37M, `explosive_burning` 6.45M, `transition_metals` 7.93M; `heavy_elements` 2.32M → `radioactive_isotopes` 3.2M → `fission_i` 6.52M (all `required_world: world_chemistry`) | **Fe 18.1k**, U 46.5k (Uranium now arrives after the gate, so it prices later) |
| Actinide / capture branch (reordered 2026-11-15 so every recipe's inputs exist when it unlocks) | `slow_neutron_capture` 12.4M → `heavy_transition_metals` 14.9M → `rapid_neutron_capture` 17.9M → `lanthanides_and_heavy_metals` 23.8M → `exotic_metals` 36.3M → `precious_metals` 48.4M → `transuranic_elements` 222M → `superheavy_synthesis` 316M | Sm 35.5k, Eu 49.3k, Re 182k, Ir 234k, Pt 273k, **Au 312k**, Hg 328k |
| Chemistry / Materials | `advanced_molecules` 46M, `materials_science` 63.9M | Water 2.04k, Carbon Fiber 628k, Steel 1.06M, Ti Alloy 1.29M; world mines paced to the wage |
| ⑭ Component Engineering | 451M | Quantum Processor 18.6M, Antimatter Cell 54.3M, Plasma Ring 67M |
| ⑮ Megastructure Theory / Biology | 652M; `biology_lab` 610M → `ecosystems` 3.44B (last node, ~128h) | Dyson Node 185M, Orbital Frame 392M, Graviton Lens 915M; Ecosystem 46.9B |

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

**Logistics gates (per-building capacity upgrades are research-gated).** Each building has three per-building upgrade tracks bought with entropy in the inspector: speed (`upgrade_levels`), output capacity (`storage_upgrade_levels`), and input capacity (`input_upgrade_levels`). Speed is always available; both capacity tracks appear once Surplus Containment is purchased (gate checked in `HUDBuildingInspectorSubController` via `ResearchService.IsUnlocked`):

| Logistics research | Branch | Prereq | Cost | Unlocks |
|---|---|---|---|---|
| `surplus_containment` | Engineering | `automation_i` | 110,000e (`cost_multiplier` 2: twice its curve position) | Output **and** Input Capacity upgrades. Feedstock Buffers (input capacity) was merged in 2026-11-19; saves that owned or were researching it get Surplus Containment (`ResearchService.MergedResearch`). |

## Prestige shop (13 permanent upgrades — `PersistentUpgradeService.cs:48-130`)

All bought with prestige currency (✦); costs roughly double per level (≈5 → 5,400✦ range). The 13 live upgrade ids (no others exist — there is no "Decay Mastery"):

| id | Name | Effect | Levels |
|---|---|---|---|
| `entropy_headstart` | Entropy Headstart | +250e starting entropy / run | per-lvl |
| `memory_resonance` | Memory Resonance | −5% research cost (live: `ResearchService.EffectiveCost`; −50% at max, under `ResearchCost.MaxDiscount` 90%) | 10 |
| `assembly_line` | Assembly Line | +10% craft speed | 20 |
| `expanded_vault` | Expanded Vault | +20 inventory slots | per-lvl |
| `field_cooldown` | Quick Hands | −5% field tap cooldown (max −50%) | per-lvl |
| `quantum_yield` | Quantum Yield | +10% recipe output | 20 |
| `efficient_layouts` | Efficient Layouts | −5% building cost (max −30%) | per-lvl |
| `turnkey_builder` | Turnkey Builder | +1 pre-placed Harvester / run | per-lvl |
| `research_overdrive` | Research Overdrive | −5% research timer (max −50%; prereq Memory Resonance 2) | 10 |
| `free_research` | Free Research | +1 research per run that finishes the moment it starts (entropy still paid; `ResearchService.FreeResearchLeft`, counted in `CurrentRunData.freeResearchUsed`, reset by prestige; research already instant doesn't use one); 25✦ → 1,800✦, prereq Research Overdrive 1 (#491) | 10 |
| `entropy_echo` | Entropy Echo | +5% prestige currency earned / run | 10 |
| `idle_time_cap` | Dormant Resonance | +30 min idle runtime (base 30 min, max 12h; 23 levels, 5✦ → 700✦, no prereq) | 23 |
| `idle_collection_rate` | Idle Efficiency | +5% idle collection rate (base 20%, max 100%) | per-lvl |
| `salvage_protocols` | Salvage Protocols | +10% removal refund (base 50%, max 100%; prereq Efficient Layouts 1) | 5 |
| `surplus_resale` | Surplus Brokerage | +10% of the Demon price for auto-sold unused items (base 50%, max 100%) | 5 |

**Deep-game tails (#501, 2026-11-24):** a first pass to the Dyson Node earns about 24k✦ and the 4-year Ascension plan about 700k✦ (`scripts/item_timeline.py` `WALLS` / `ASCENSION`), while the shop held about 86k✦ and its cheap core upgrades maxed by the third wall. The uncapped, live tracks got longer tails, each extra level costing x1.25 (Quantum Yield), x1.3 (Assembly Line, Echo, world tracks) or x1.45 (Memory Resonance) the last: Memory Resonance 5→10, Assembly Line 5→20, Quantum Yield 10→20, Entropy Echo 4→10, Fission Yield 6→15, Neutron Economy 5→15, Spring Pressure 6→12, Catalysis / Growth Media / Furnace Heat / Precision 8→15. The shop now holds about 680k✦, and its priciest level is 48k✦ (Quantum Yield 20, about two loops). Not extended: capped effects (Research Overdrive, idle cap/rate, Salvage, Surplus, Quick Hands, Efficient Layouts), the flat Entropy Headstart, and Expanded Vault / Turnkey Builder, whose effects are not wired yet. `scripts/prestige_plan.py` now reads every cost from `PersistentUpgradeService.cs`. `UpgradeLevelDepthTests` guards this.

**Removal refunds** (`DemolishRefund`, applied by `DeconstructController`): removing a building returns `Fraction × spent`, where spent = placement cost after the prestige discount + every speed/storage/input upgrade bought. The building's whole spent amount leaves `TotalEntropySpent`, so net worth drops by the unrefunded part and place/remove loops can't farm prestige. Belts are free (no refund). The grid's only Maxwell's Demon can't be removed, and every removal asks to confirm (`ConfirmPrompt`).

**Surplus sales** (`SurplusSale`, #312): items a building can't use never pile up unseen. Sold at `Fraction × Demon price` (base 50%, +10% per level of the `surplus_resale` Surplus Brokerage prestige upgrade, max 100%): **an item the recipe uses that reaches a full input** (full-input autosell, 2026-11-11: the belt keeps moving instead of backing up and stalling its producers), a belt's front item jamming a needed item (`ConveyorSystem` overflow), and inputs a newly set recipe no longer uses (`HUDBuildingInspectorSubController.SetBuildingRecipe`). An item the recipe doesn't use still waits on the belt, and a building with no recipe never sells. A tail beside several inputs prefers one that takes the item (the Demon included, at full price) over selling. Fractions of entropy carry over between belt sales; the inspector's "Selling surplus" row shows the last 5 s per building (`SurplusSaleLog`). Offline, items stuck at a starved building and the excess an over-supplied building can't use at steady state sell at the same rate (`IdleGraphAnalyzer` `surplusSaleFraction`, `ExcessInto`).

**Live wiring:** `assembly_line` (craft speed) and `quantum_yield` (output) flow through `PrestigeData.SpeedMultiplier`/`OutputMultiplier`, which `ProductionSystem` and `CollectorSystem` read live (with the megastructure `GlobalProductionBonus`) and the idle snapshot folds in. Fractional output bonuses carry between crafts (`OutputCarry`). Research is priced by `ResearchCost.Effective`: Memory Resonance on every node. Research discounts come from upgrades only; the per-node "finished on an earlier run" discount (`prestige_memory_discount`) was removed 2026-11-15.


### Planned content: the Item Timeline economy (2026-11-23)

Content marked `"planned": true` in `game_data.json` (items, recipes, research, blocks) is priced by `scripts/economy_plan.py`, the model the Item Timeline page shows (https://claude.ai/artifact/68WpPYX6DBZiXku4sES1Dd), and `rebalance_curve.py` leaves it alone (it treats planned research as off-curve and planned items as already priced). Phase 1 covers Physics from `light_elements` to `iron_convergence`; phase 2 the life-chemistry chain from `chemistry_lab` to `energy_currency`; phase 3 Biology and Metallurgy; phase 4 Engineering and Deep Physics. Only the utility nodes (belts, extraction, surplus containment, nucleon harvesting) are still priced by the derived curve above. The long game is Ascension: see `chemistry-biology.md` and `AscensionRules`.

- **Wage on time, not research count:** wage(H) = 20 x 2^((H - 1) / 10) entropy per building-second at active hour H (2 active hours a day). Value = (inputs + wage x craft) / output quantity, as before.
- **Research** = 5 buildings x wage x (1.15 ^ walls cleared) x the active seconds since the previous unlock in any world (one wallet). Walls (`walls` in the plan) are where the timeline makes the player prestige several times at one spot; the 1.15 per wall is what the prestige shop's tracks are assumed to add.
- **Smoothing:** costs along the planned chain are pooled (pool adjacent violators) so a node never costs less than its prerequisite; a wall becomes a stretch of dear research.
- `progression_curve.py --report` has no prestige-upgrade model, so planned blocks read about 1.15^walls slower than their `target_hours`; that is expected.
- **World tracks** in the prestige shop: `UpgradeEffectType.WorldCraftSpeed` / `WorldOutput` with `UpgradeDef.world` (Physics: Fission Yield +15% output, Neutron Economy +10% speed). `WorldRules` writes them onto the `ActiveWorldRules` singleton (`SpeedMult`, `OutputMult`) on every world load and after a purchase; `ProductionSystem`, `CollectorSystem`, the idle snapshot and the inspector apply them. Quantum Yield now has 10 levels.

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

## Derived economy (2026-11-02): how values and research costs are set

Item values after the tutorial and every post-tutorial research cost are **generated**, not hand-tuned. Do not edit them one by one: change the curve and re-run.

- `python scripts/progression_curve.py` writes `docs/progression-curve.html` (payback per research, estimated playtime, best earning rate, every item value by unlock, flags). `--report` prints the same as a table. Re-run it after any balance change and check the flags.
- `python scripts/rebalance_curve.py` previews the derived values; `--apply` writes them (then run Import Game Data and sync the `ItemBalanceTests` pins).
- **Unlock order** = the cheapest research whose prerequisites are met, repeatedly. A recipe counts from when it is unlocked *and* its inputs can be made.
- **Anchor:** everything the Physics tutorial makes (up to `fusion_i`) keeps its value and cost. Particles keep fixed values (α 10, β 2, n 3, ν 3, γ 5).
- **Values:** a later recipe that makes a new item is priced `outputs = inputs + wage(n) × craft time`, wage(n) = the tutorial-end rate × **1.19** per research. So every craft gains and one building on the newest recipe earns about wage(n). Physics recipes price elements; Chemistry/Biology harvesters reuse those prices, and any raw collect recipe is slowed to `value ÷ wage` so a mine never out-earns its unlock (#332).
- **Back-pricing:** a recipe whose products were priced by a dearer route (Plutonium fission makes the same fragments as U-235 fission) would out-earn its wage; its harvested inputs are raised until it earns the wage, and their harvesters slow to match. Items the tutorial prices are never changed.
- **Off-curve research:** a research flagged `"advances_curve": false` (Transportation II) is a side upgrade: `rebalance_curve.py` does not count it as a wage-curve step, and it takes its position's cost share without shrinking the rest of its block. Adding one prices only itself (the block runs a little over its target); a normal node would reprice every later item and research (~19%).
- **Cost multiplier:** a research may set `"cost_multiplier"` (default 1) to cost that many times its curve position, for a node that unlocks more than its position assumes (Surplus Containment, 2 since 2026-11-19: output and input capacity). The extra sits on top of the block budget, like an off-curve node, and unlock order uses the cost before the multiplier (`progression_curve.py`), so the node stays where it is instead of being pushed later and priced again.
- **Research blocks (game phases):** `research_blocks` in `game_data.json`; every research names its `block`. Each block has `target_hours`, the time a player running 5 buildings should spend clearing it. Current plan (2026-11-19 Chemistry gate): Ignition (tutorial, 1h, costs kept), Foundations 1.4h (models 2.7h; lowered when molecular_synthesis and heavy_elements left it, so early prices stayed close), Stellar Forge 4h (fusion_ii, mid_elements: the end of Physics before the gate), First Chemistry 2h (chemistry_lab, molecular_synthesis), Deep Burning 8h (fusion_iii, explosive_burning, transition_metals), Actinide Frontier 20h (now with heavy_elements), Chemistry World 20h, Synthetic Frontier 30h, Biology World 40h. Unlock order runs block by block, cheapest first inside one.
- **Research costs:** each block's budget is spread over its research, each node 1.2× the one before (or the block's own `ramp`: Foundations uses 1.7 so the first nodes after `fusion_i` cost 6.4k, 11k, 18.6k, 31.6k instead of 52k+, 2026-11-14), and `cost = share (building-minutes) × 60 × best rate before the node`; a node never costs less than 1.05× its dearest prerequisite. Edit blocks and targets in the research tree editor (`docs/research-tree.html`, https://claude.ai/artifact/CF6rEtx6GunCJMeGqaRgg9), apply with `scripts/apply_research_tree.py`, then re-run the rebalance. `docs/progression-curve.html` shows target vs model per block.
- **Prestige pacing (Physics):** `docs/balance/prestige-pacing.xlsx`, generated by `python scripts/prestige_plan.py` from the same curve. 8 runs clear Physics (~36 active hours, the blocks' target) only if research discounts (Memory Resonance) and Assembly Line / Quantum Yield are applied live; as shipped neither is, so every run replays at full price (~84 active hours). Removing the per-node memory discount (2026-11-15) took the plan's 8 runs from ~41 to ~45 active hours (runs 2-8 each +0.1 to +0.9 h). It also models the proposed timed Entropy Surge ad (minutes of income, 15 min cooldown) against today's 10%-of-net-worth Entropy Boost.
- **Limits:** research cost is a C# `long` (`ResearchSO.costBaseCurrency`, since 2026-11-02); the script refuses a cost over 2^53 (JSON doubles stay exact below it). Prestige ✦ = `log10(netWorth / 5000) × 50`, so the smaller late values give fewer ✦ than the old curve; revisit with the prestige work.
- **Deliberate exceptions** (recipes that do not gain): the tutorial quark→nucleon steps (break even) and the Atom Generator / Manipulator on-ramps for Deuterium, Uranium and Plutonium.
- **Molecule recipes use the real formula, one molecule per craft** (H₂O = 2 H + 1 O, NH₃ = 3 H + 1 N, SiO₂ = 1 Si + 2 O, Fe₂O₃ = 2 Fe + 3 O, UF₆ = 1 U + 6 F, CH₄ = 4 H + 1 C). Several were multiples or wrong (water 4 H + 8 O, ammonia 7 N, UF₆ from silicon) until 2026-11-18; each craft time was scaled by new atoms ÷ old atoms to keep its pace per atom, then values re-derived. Pinned in `RecipeTimingTests`. Deliberate simplifications left as they are: Liquid Hydrogen (10 H, a bulk tank, not one H₂), Octane from 4 Ethane (carbon balances, spare H dropped), and the Materials Forge alloys / Biology polymers (ingredient mixes, not formulas).
- Building placement and upgrade costs were checked against the new rates (0.1–17 minutes of payback at unlock) and left as they are.

## Economy health & risks (re-evaluated 2026-06-28 — read before adding more track)

Findings from the full earning re-evaluation. These are the things most likely to bite as content/progression ("track") is extended.

1. **Prestige multipliers (resolved 2026-11-11).** Assembly Line / Quantum Yield are now live in `ProductionSystem`/`CollectorSystem` and folded into the idle snapshot, and research discounts (Memory Resonance) are charged. The per-node prestige memory discount was removed 2026-11-15 (discounts come from upgrades only). Before this, every run replayed at full price and roughly full time, which made play slow down after the first prestige.

2. **Two parallel output-scaling paths.** Output now scales via (a) prestige-shop `quantum_yield` (idle/display only) and (b) OutputQuantity managers + megastructure (live). When authoring new high-tier balance, anchor expected output to the **manager + megastructure** path, not the prestige-shop path, or live-vs-idle income will diverge.

3. **Crystal faucet vs sink headroom.** Free faucet ≈ 3,500◆/mo for a completionist (~23 skip-hours) against sinks anchored at 150◆/skip-hour. New crystal sinks are safe to add, but new *faucets* (more achievements/challenges as content grows) compound — keep the completionist monthly total near the ~23 skip-hour target so premium time-skips retain value.

4. **Prestige cadence vs new tiers.** With `prestige_scale = 50`, each ×10 of netWorth yields only ≈ +50✦ (flat, log curve). Adding higher tiers raises the netWorth ceiling but NOT proportionally the ✦ payout, so deep runs can feel ✦-starved relative to the cost of late managers (Demiurge 800✦ hire + 4,000✦/star) and deep upgrades (≈5,400✦). When adding tiers, re-check that first prestige stays ≈1–2h and that ✦ income per run keeps pace with the new sink costs — consider raising `prestige_scale` or adding ✦ faucets rather than letting the gap widen.

5. **Manager star costs are the biggest ✦ sink and are flat-authored.** Star costs live as explicit `star_costs[]` arrays in `game_data.json` (not a formula), so new managers must have their tables hand-tuned. Keep them on the same rough curve (hire cost → ★ costs scaling ~×1.5–2 per star) to avoid outliers.

**Value readouts and Statistics.** Item price is fixed (`ItemValue.PricePerItem` = `SurplusSale.DemonPrice`); output bonuses (managers, megastructure `OutputMult`) raise quantity, not price, so the readouts scale "per craft" and "per second" but not "worth each". The Statistics panel's **stored in buildings** row is `PlayerProgressData.StoredItemValue` (items in building inputs, craft staging and outputs at the Demon price): a number that keeps climbing points at a bottleneck downstream. Its **automation income** is entropy per second actually earned by automation (Demon sink, belt surplus sales, escaping byproducts) averaged over the last 60 s; "if you go idle" is that rate x `GetEffectiveCollectionRate` (base 20%, + `idle_collection_rate`) x the ad boost (1.5x). `quantum_yield` (`PrestigeData.OutputMultiplier`) is folded into the idle snapshot alongside `prestigeSpeedMultiplier`.
