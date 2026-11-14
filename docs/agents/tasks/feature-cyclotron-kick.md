# Post-Carbon pacing: Foundations ramp + Cyclotron Kick minigame

**Required reading:** `docs/agents/README.md`, `architecture.md`, `economy-balance.md` ("Derived economy"), `save-system.md` ("Adding a save field"), `ui-toolkit.md`.

**Status:** done, first version for testing (branch `feat/post-carbon-pacing`, 2026-11-14). Tuning to follow playtests.

## Why

After the tutorial's first Carbon (`fusion_i`, 4,000e) the next research cost 52k-90k (Light Elements, the Carbon line, 442k): 13-110x the last step. Players felt forced into several "redundant" prestiges before anything was in reach. The user picked option 1 of three:

1. **Chosen:** curve fix + the Cyclotron Kick minigame paying Insight.
2. Fallback: a different or extra minigame. Cloud Chamber (tap particle tracks by their signature) or Decay Chain (puzzle: alpha/beta decays to reach a target element).
3. Fallback: curve fix only.

Keep 2 and 3 in mind if playtests say 1 doesn't work.

## Part A: Foundations ramp (curve fix)

- `research_blocks` entries may set `"ramp"` (default `BLOCK_RAMP` 1.2 in `scripts/rebalance_curve.py`): each node's share of the block budget is `ramp` x the one before. A steeper ramp front-loads cheap nodes; the block total (its `target_hours`) is unchanged.
- Foundations is `"ramp": 1.7`. The first nodes now step up 1.6-1.8x at a time from `fusion_i`: Rapid Extraction II 6,430, Transportation I 11,000, Nucleon Harvesting 18,600, Isotopes 31,600, then Surplus Containment 108k, Molecular Synthesis 184k, Light Elements 443k. The cost moved to the back of the block (Heavy Elements 894k, Feedstock Buffers 1.81M, Transportation II 3.08M). No item value changed.
- `scripts/apply_research_tree.py` (research tree editor) does not know `ramp`; re-add it by hand if a tree apply drops it.

## Part B: Cyclotron Kick

**Unlock:** `SaveData.prestigeCount >= 2` (`CyclotronProgress.UnlockPrestiges`). Drawer button `btn-cyclotron` shows from then on: "Cyclotron (ready)" or the minutes left, "Cyclotron (42m)".

**Run (`Gameplay/CyclotronRun.cs`, pure):** 30 s. A particle circles between two dees; the gap is vertical, so it crosses at the top (270°) and bottom (90°) of each lap, screen angles. Tap within ±18° of a crossing to kick (±5° = perfect): energy +0.013 x quality (good) or +0.02 (perfect), x(1 + 0.1 x combo) up to +50%; speed x1.035 per kick (180 -> 540 °/s). A tap outside a window, or a second tap in the same crossing, is a miss: -0.03 energy, combo reset. A crossing left un-kicked resets the combo. Orbit radius = 0.3 + 0.62 x energy of the ring, so the particle spirals outward. A flawless run reaches ~60% at 15 s and 100% by the end.

**Meta (`Gameplay/CyclotronProgress.cs`, pure, on SaveData):**
- Cooldown: one run an hour, counted from the run's start (`cyclotronLastRunUtc`); offline time counts; never played = ready. The intro card counts down ("Next run in 42m 05s") and the Start button reads "Ready in ..." until then. (First version had 3 stored runs, +1 per 25 min; the user switched to a plain hourly cooldown, 2026-11-14.)
- Insight = 4% + 11% x energy, whole points (4-15%). Banked in `SaveData.researchInsight`, capped at 40%.
- Bonus: energy >= 90% has a 25% chance of +5 crystals.
- Everything survives prestige.

**Insight in prices:** `ResearchCost.Effective(..., insight)` stacks it multiplicatively with Memory Resonance and prestige memory, so every research price shown (cards, tree, list) includes it. `ResearchService.Purchase` consumes it (`CyclotronProgress.ConsumeInsight`) once the price is paid.

**UI (`UI/CyclotronController.cs`, `UI/CyclotronView.cs`):** modal `cyclotron-modal` in `GameHUD.uxml` with three steps (`cyclotron__intro` / `__play` / `__result`). `CyclotronView` is a Painter2D `[UxmlElement]` (dees, glowing gap while the particle is in a window, orbit, particle, tap pulse); PointerDown anywhere on it taps. The controller steps the run in <= 1/120 s sub-steps on unscaled time. X mid-run ends the run (still paid); X on the result collects. Android back shares the Surge-offer layer in `BackNavigation`. The modal counts toward `HUDController.MenuOpen` (hides the Surge icon).

**Save fields (additive):** `cyclotronLastRunUtc` (null = never played), `researchInsight`.

**Tests:** `CyclotronTests` (kick rules, combo, orbit, run end, flawless run maxes, unlock, hourly cooldown, Insight curve and cap, bonus, price stacking, save round-trip and legacy load, UI text, UXML elements).

**Verified in Play Mode (MCP, save snapshotted and restored):** drawer button after `set prestige_count 2`; intro, play and result screens; mistimed taps show "Mistimed" with a red pulse; Collect banked 4% and research prices dropped by it (Rapid Extraction II 6,430 -> 6,173); a scripted perfect run at 60% shows the wider orbit.

## Open questions for tuning

- Is 30 s and 18° windows the right difficulty on a phone? Is one run an hour right for the post-prestige lull?
- Does 4-15% per run (cap 40%) close the gap enough, or should Insight also apply to research time?
