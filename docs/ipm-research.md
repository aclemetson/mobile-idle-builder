# Idle Planet Miner — Balance Reference

Research branch: `research/idle-planet-miner-values`  
Sources: community spreadsheets, strategy guides, search snippets (fandom wiki blocked direct fetch).

---

## 1. Ores — Base Sell Price per Unit

20 ores total, scaling roughly ×2.1–2.6 per tier.

| # | Ore | Base Price | Approx. Multiplier vs Prev |
|---|-----|-----------|---------------------------|
| 1 | Copper | $1 | — |
| 2 | Iron | $2 | ×2.0 |
| 3 | Lead | $4 | ×2.0 |
| 4 | Silicon (Silica) | $8 | ×2.0 |
| 5 | Aluminum | $17 | ×2.1 |
| 6 | Silver | $36 | ×2.1 |
| 7 | Gold | $75 | ×2.1 |
| 8 | Diamond | $160 | ×2.1 |
| 9 | Platinum | $340 | ×2.1 |
| 10 | Titanium | $730 | ×2.1 |
| 11 | Iridium | $1,600 | ×2.2 |
| 12 | Paladium | $3,500 | ×2.2 |
| 13 | Osmium | $7,800 | ×2.2 |
| 14 | Rhodium | $17,500 | ×2.2 |
| 15 | Inerton | $40,000 | ×2.3 |
| 16 | Quadium | $92,000 | ×2.3 |
| 17 | Scrith | $215,000 | ×2.3 |
| 18 | Uru | $510,000 | ×2.4 |
| 19 | Vibranium | $1,250,000 | ×2.5 |
| 20 | Aether | $3,200,000 | ×2.6 |

**Key pattern:** ×2.0 in early tiers, increasing to ×2.5–2.6 at the top end.

---

## 2. Alloys / Bars

First 7 are single-metal "Bars" (1,000 ore → 1 bar). After Rhodium they are called "Alloys" and require bars as inputs.

### Single-Metal Bars (confirmed recipe: 1,000 ore → 1 bar)

| Bar | Input | Sell Value | Smelt Time | $/s |
|-----|-------|-----------|------------|-----|
| Copper Bar | 1,000 Copper | ~$1,450–1,740 | 20s | ~37.80 |
| Iron Bar | 1,000 Iron | ~$3,500 (est.) | ~30s | — |
| Lead Bar | 1,000 Lead | ~$7,000 (est.) | ~40s | — |
| Silicon Bar | 1,000 Silicon | ~$14,000 (est.) | ~60s | — |
| Silver Bar | 1,000 Silver | ~$64,000 (est.) | ~120s | — |
| Gold Bar | 1,000 Gold | ~$134,000 (est.) | ~180s | — |
| Diamond Bar | 1,000 Diamond | ~$285,000 (est.) | ~240s | — |

_Note: Only Copper Bar confirmed from spreadsheet data. Remaining single-metal bars estimated from ore prices × ~1,000× value-add ratio._

### Multi-Input Alloys (confirmed)

| Alloy | Inputs | Sell Value | Smelt Time | $/s |
|-------|--------|-----------|------------|-----|
| Bronze Bar | 10 Copper Bar + 2 Silver Bar | $234,000 | 240s | ~972 |
| Steel Bar | 30 Iron Bar + 15 Lead Bar | $340,000 | 480s | ~355 |
| Platinum Bar | 1,000 Platinum ore + 2 Gold Bar | $780,000 | 600s | ~1,300 |
| Vibranium Bar | 1,000 Vibranium ore + 2 Quadium Bar | $2,050,000,000 | 2,400s | ~854,167 |

---

## 3. Items — Crafted Goods

Items = highest profit per input cost. Crafted in Crafters (separate from Smelters).

| Item | Inputs | Sell Value | Craft Time | $/s |
|------|--------|-----------|------------|-----|
| Copper Wire | 5 Copper Bar | $10,000 | 60s | 166 |
| Iron Nails | 5 Iron Bar | $20,000 | 120s | 167 |
| Battery | 10 Copper Bar + 2 Copper Wire | $70,000 | 240s | 292 |
| Laser | 5 Gold Bar + 1 Lens | $3,000,000 | 3,600s | 467 |
| Advanced Battery | 20 Steel Bar + 30 Battery | (est. ~$31M) | 9,000s | 3,480 |
| Motor | 500 Bronze Bar + 200 Hammer | $7,000,000,000 | — | — |
| Teleporter | 250 Navigation Module + 1 Gravity Chamber | $1,800,000,000,000,000 | 27,600s | — |
| Fusion Reactor | 1 Fusion Capsule + 40 Collider + 50 Nuclear Reactor | $40,000,000,000,000,000 | 30,000s | — |

---

## 4. Planets

73 total planets (P1–P73). Each new planet is more expensive and has a richer ore mix.  
~1 new ore unlocked per telescope level (every 3 planets).

| Planet | # | Cost | Ores |
|--------|---|------|------|
| Balor | P1 | $100 | Copper |
| (P2–P3) | — | ~$300–$1K | Copper + Iron |
| … | … | … | More ores as telescope advances |
| Pegasi | P73 | $6.23×10²⁸ | All ores |

**Planet price formula (from P1 to P73):** Roughly exponential — $100 to $6.23×10²⁸ over 73 steps ≈ ×3.8 per planet on average.

### Planet Upgrade Formulas (confirmed)

| Stat | Formula (at level L) |
|------|---------------------|
| Mining Rate | 0.25 + 0.1(L−1) + 0.017(L−1)² |
| Ship Speed | 1 + 0.2(L−1) + (1/75)(L−1)² |
| Cargo Capacity | 5 + 2(L−1) + 0.1(L−1)² |

---

## 5. Rooms / Station Upgrades

Unlocked when galaxy value reaches **$10,000,000** (10M).  
Purchased with **Credits** (earned by selling galaxies worth ≥ $10M).

| Room # | Cost (Credits) |
|--------|---------------|
| 1st | 3 |
| 2nd | 6 |
| … | Increases |

### Key Rooms

| Room | Effect | Max Level |
|------|--------|-----------|
| Workshop | Craft speed multiplier | 50 |
| Laboratory | Project costs (halved at max) | 11 |
| Underforge | Smelting recipe cost reduction (-26% at L5) | 5+ |
| Dorms | Crafting recipe cost reduction (-26% at L5) | 5+ |
| Forge Mothership | Reduces alloy smelt time | — |

---

## 6. Resource Stars

Each ore can earn up to 10+ resource stars.  
Effect: **+20% of base value per star**.

| Stars | Value Multiplier |
|-------|----------------|
| 0 | 1× |
| 5 | 2× |
| 10 | 3× |
| N | 1 + (N × 0.2)× |

---

## 7. Galaxy Selling / Prestige Analog

IPM's loop: build planet fleet → maximize galaxy value → **sell the galaxy** for Credits → restart with permanent upgrades.

- Galaxy must be worth ≥ **$10M** to unlock Rooms
- Rooms persist across galaxy resets (permanent)
- Optimal sell points: exact orders of magnitude ($10M, $100M, $1B…)

---

## 8. Managers

- Promote by combining **3 managers of the same star rating** → 1 higher-star manager
- Managers with **≥3 stars** grant global passive bonuses
- Manager skills include: Mine Speed, Smelt Speed, Craft Speed, All Smelt Speed (secondary)

---

## 9. Economy Ratios (Key Takeaways)

| Ratio | Value |
|-------|-------|
| Ore-to-bar input | 1,000 ore → 1 bar |
| Bar value vs ore inputs | ~1,450× (Copper Bar) to >100× |
| Item value vs bar inputs | Copper Wire: ~3.4× bar input cost |
| Tier-to-tier ore scaling | ×2.0–2.6 per tier |
| Star value boost | +20% per star |
| Smelt time scaling | 20s (Copper Bar) → 2,400s (Vibranium Bar) |
| Craft time range | 60s (simple items) → 30,000s (endgame items) |

---

## 10. Mapping to Our Game

| IPM Concept | Our Equivalent | Notes |
|-------------|----------------|-------|
| Ores (20 tiers, ×2–2.6) | Raw particles (quarks → atoms → molecules) | Our tiers are scientifically named rather than metals |
| 1,000 ore → 1 bar | Recipe input quantities | Adjust per tier; IPM uses flat 1,000 for base bars |
| Bars → Alloys chain | Molecule → Compound → Alloy chain | Same multi-step refinement model |
| Items (Copper Wire etc.) | Assembled components (circuits, batteries) | We map to real-world science analogs |
| Planet purchase price (×3.8/planet) | Building/machine unlock cost curve | Exponential cost curve reference |
| Galaxy sell threshold ($10M) | Prestige threshold | Our `prestige_base = 500` (see project memory) |
| Resource stars (+20%/star) | Research multipliers | Could inform our research upgrade increment sizing |
| Rooms (permanent upgrades) | Post-prestige permanent research tree | IPM rooms persist; same model planned for us |
| Mining rate formula (quadratic) | Harvester production scaling | Quadratic formula gives good feel without runaway growth |

### Calibration Notes

- **Our tier count:** We go quarks→protons/neutrons→atoms→molecules which is ~6–8 meaningful tiers vs IPM's 20 ores. We should consider whether to expand tiers or use larger per-tier multipliers.
- **Ore-to-unit ratio:** IPM uses 1,000 flat for base bars. We could start at 100 for early tiers and scale up to keep early game fast.
- **Smelt time:** IPM starts at 20s and goes to 2,400s (120× spread). Our Tier 0 production is near-instant. Consider anchoring first recipe at 5–10s and scaling to minutes by Tier 4+.
- **Prestige:** IPM's "galaxy sell" is analogous to our prestige. Their recommended floor is $10M galaxy value — implies ~30–60 minutes of active play before first reset feels rewarding. Our `prestige_base = 500` units may need revisiting against this benchmark once production rates are tuned.

---

_Sources: community Google Sheets (smelt value calculator, general IPM spreadsheet), tap-guides.com strategy guide, web search snippets from idle-planet-miner.fandom.com (wiki direct access blocked 403)._
