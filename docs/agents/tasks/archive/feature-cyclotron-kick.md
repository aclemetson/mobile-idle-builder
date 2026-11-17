# Post-Carbon pacing: Foundations ramp + Cyclotron Kick minigame

**Required reading:** `docs/agents/README.md`, `architecture.md`, `economy-balance.md` ("Derived economy"), `save-system.md` ("Adding a save field"), `ui-toolkit.md`.

**Status:** done, first version for testing (branch `feat/post-carbon-pacing`, 2026-11-14). Tuning to follow playtests.

## Why

After the tutorial's first Carbon (`fusion_i`, 4,000e) the next research cost 52k-90k (Light Elements, the Carbon line, 442k): 13-110x the last step. Players felt forced into several "redundant" prestiges before anything was in reach. The user picked option 1 of three:

1. **Chosen:** curve fix + the Cyclotron Kick minigame (first paying a research discount; since 2026-11-15 a timed entropy boost plus Insight for upgrades).
2. Fallback: a different or extra minigame. Cloud Chamber (tap particle tracks by their signature) or Decay Chain (puzzle: alpha/beta decays to reach a target element).
3. Fallback: curve fix only.

Keep 2 and 3 in mind if playtests say 1 doesn't work.

## Part A: Foundations ramp (curve fix)

- `research_blocks` entries may set `"ramp"` (default `BLOCK_RAMP` 1.2 in `scripts/rebalance_curve.py`): each node's share of the block budget is `ramp` x the one before. A steeper ramp front-loads cheap nodes; the block total (its `target_hours`) is unchanged.
- Foundations is `"ramp": 1.7`. The first nodes now step up 1.6-1.8x at a time from `fusion_i`: Rapid Extraction II 6,430, Transportation I 11,000, Nucleon Harvesting 18,600, Isotopes 31,600, then Surplus Containment 108k, Molecular Synthesis 184k, Light Elements 443k. The cost moved to the back of the block (Heavy Elements 894k, Feedstock Buffers 1.81M, Transportation II 3.08M). No item value changed.
- `scripts/apply_research_tree.py` (research tree editor) does not know `ramp`; re-add it by hand if a tree apply drops it.

## Part B: Cyclotron Kick

**Unlock:** `SaveData.prestigeCount >= 2` (`CyclotronProgress.UnlockPrestiges`). Drawer button `btn-cyclotron` shows from then on: "Cyclotron (x2.4 4m)" while a boost runs, else "Cyclotron (ready)" or the minutes left, "Cyclotron (42m)".

**Run (`Gameplay/CyclotronRun.cs`, pure):** 30 s. A particle circles between two dees; the gap is vertical, so it crosses at the top (270°) and bottom (90°) of each lap, screen angles. Tap within ±18° of a crossing to kick (±5° = perfect): energy +0.013 x quality (good) or +0.02 (perfect), x(1 + 0.1 x combo) up to +50%; speed x1.035 per kick (180 -> 540 °/s). A tap outside a window, or a second tap in the same crossing, is a miss: -0.03 energy, combo reset. A crossing left un-kicked resets the combo. Orbit radius = 0.3 + 0.62 x energy of the ring, so the particle spirals outward. A flawless run reaches ~60% at 15 s and 100% by the end.

**Meta (`Gameplay/CyclotronProgress.cs`, pure, on SaveData). Reworked 2026-11-15: the reward used to be Insight as a % discount on the next research; the user switched it to a timed entropy multiplier with upgrades.**
- Cooldown: 60 min, counted from the run's start (`cyclotronLastRunUtc`); offline time counts; never played = ready. The intro card counts down ("Next run in 42m 05s (one run every 60 min)") and Start reads "Ready in ..." until then.
- **Entropy boost:** Collect starts it: x1.5 + 1.5 x energy, one decimal (x1.5-x3), for 5 min (`cyclotronBoostMultiplier`, `cyclotronBoostExpiryUtc`). A new boost replaces the old one; the longest boost (15 min) is always shorter than the shortest cooldown (30 min), so they never overlap.
- **Where the boost applies:** live entropy from the factory: `EntropySinkSystem` (Demon buildings), surplus autosell in `ConveyorSystem`, escaping byproducts in `ProductionSystem`, and manual Maxwell's Demon deposits (`MaxwellsDemonController.GetMultiplier`, so the earn preview matches). Systems read the `EntropyIncomeBoost` ECS singleton, which `CyclotronController.SyncBoost` creates at runtime and rewrites when the boost starts or ends. **Not** applied: offline collection, ad/achievement/tutorial rewards, recipe-change refunds.
- **Insight:** an upgrade currency now (`cyclotronInsight`, int). A run pays 1 + 1 per 25% energy (1-5).
- **Upgrades (bought on the intro card):** Longer Boost +1 min a level, 10 levels (5 -> 15 min), costs 3 + 2 x level (120 in all); Faster Recharge -5 min a level, 6 levels (60 -> 30 min), costs 4 + 3 x level (69 in all). Levels in `cyclotronBoostLevel` / `cyclotronCooldownLevel`.
- Bonus: energy >= 90% has a 25% chance of +5 crystals.
- Everything survives prestige.
- Research prices no longer take any Cyclotron discount (`ResearchCost.Effective` lost its `insight` overload).

**UI (`UI/CyclotronController.cs`, `UI/CyclotronView.cs`):** modal `cyclotron-modal` in `GameHUD.uxml` with three steps (`cyclotron__intro` / `__play` / `__result`). The intro step also holds `cyclotron__insight` and two upgrade rows (`cyclotron__boost-upgrade[-text]`, `cyclotron__cooldown-upgrade[-text]`). The drawer button shows the boost while it runs: "Cyclotron (x2.4 4m)". `CyclotronView` is a Painter2D `[UxmlElement]` (dees, glowing gap while the particle is in a window, orbit, particle, tap pulse); PointerDown anywhere on it taps. The controller steps the run in <= 1/120 s sub-steps on unscaled time. X mid-run ends the run (still paid); X on the result collects. Android back shares the Surge-offer layer in `BackNavigation`. The modal counts toward `HUDController.MenuOpen` (hides the Surge icon).

**Save fields (additive):** `cyclotronLastRunUtc` (null = never played), `cyclotronBoostExpiryUtc`, `cyclotronBoostMultiplier`, `cyclotronInsight`, `cyclotronBoostLevel`, `cyclotronCooldownLevel`. The old `researchInsight` float was dropped (JsonUtility ignores it in old saves; only internal test builds had it).

**Tests:** `CyclotronTests` (kick rules, combo, orbit, run end, flawless run maxes, unlock, cooldown and Faster Recharge, boost strength/length/expiry and Longer Boost, no overlap, Insight curve, upgrade costs/caps, bonus, save round-trip and legacy load, UI text, UXML elements).

**Verified in Play Mode (first version, 2026-11-14; MCP, save snapshotted and restored):** drawer button after `set prestige_count 2`; intro, play and result screens; mistimed taps show "Mistimed" with a red pulse; Collect banked 4% and research prices dropped by it (Rapid Extraction II 6,430 -> 6,173); a scripted perfect run at 60% shows the wider orbit. The 2026-11-15 boost/upgrade rework is unit-tested only; not yet checked in Play Mode.

## Open questions for tuning

- Is 30 s and 18° windows the right difficulty on a phone? Is 60 min (30 min maxed) right for the post-prestige lull?
- Is x1.5-x3 for 5 min worth an hourly 30 s run, and are the upgrade prices (120 / 69 Insight to max) paced right?
- Should the boost also cover offline collection for the minutes it overlaps?
