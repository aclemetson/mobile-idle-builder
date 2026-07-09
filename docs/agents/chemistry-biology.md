# Chemistry & Biology Worlds — Content Model

**Scope:** The data/content model for the Chemistry and Biology tracks: the World layer, per-world fields, the new `OrganicCompound` item category, the research trees, buildings, and recipes. Balance numbers are first-pass anchors — tune against the progression map before committing to `game_data.json`. For the phased build order and constraints, see `tasks/feature-worlds-chem-bio.md`. For the existing periodic-table content this sits alongside, see `elements-and-isotopes.md`.

> Verified against: **Phases 1–3 + 4a implemented** 2026-07-06. Phase 1: the **World layer** (`WorldSO`/`WorldDatabaseSO`, `worlds` in `game_data.json`, save fields, `WorldLayout` mapping); Worlds group the existing FLAT site list via `WorldSO.siteIds` (no nested save restructure). **Phase 2:** field-type is now a **data-driven string** (the `FieldType` enum is gone — see `FieldTypes` helper); `ItemCategory.OrganicCompound` + 4 items (`glucose`/`fatty_acid`/`amino_acid`/`nucleotide`, forward-declared) exist; **element fields** (`element_field_light`/`metal`/`mineral`, dropping existing elements) and the first Chemistry site (`site_chem_lab`, wired to `world_chemistry`) are authored. **Phase 3:** `WorldService` (self-bootstrapped, mirrors `SiteService`) does unlock/travel + the economic gate; `world list/switch/unlock` dev commands. **Phase 4a:** Chemistry **C1** is playable — `chemistry_lab` research (150K, prereq `mid_elements`) is the world gate; **Element Harvester** + **Compound Synthesizer** buildings; C1 compounds (`carbon_dioxide`/`table_salt`/`sulfuric_acid`, item_id 154–156). **Still design draft (Phases 4b–5):** chem C2–C4, worlds UI + map theming. Biology fully deferred. **Balance is first-pass** (real re-tier is Phase 6).

## The World layer

A **World** wraps the existing multi-Site machinery ("Quantum Domains") one level up. Physics / Chemistry / Biology are three Worlds; each owns its own set of Sites, its own research (branch), and its own map theme. Only ONE world's ONE site is live ECS; everything else runs on idle snapshots (identical to the Site model — no multi-world ECS).

```
World (Physics)   ── sites[] ── grids[]   (live one, rest idle-snapshot)
World (Chemistry) ── sites[] ── grids[]   ← economic unlock: entropy + physics prereqs
World (Biology)   ── sites[] ── grids[]   ← economic unlock: entropy + chemistry prereqs
```

**Shared across all worlds** (one wallet each): entropy (per run), prestige currency ✦, crystals ◆, managers, prestige shop, achievements, net worth. **Prestige** resets all grids + research but **world unlocks survive** (like site unlocks). **Unlock is pure economic:** pay entropy + hold the prerequisite unlock from the prior world (no prestige-count gate).

`WorldSO` fields (proposed): `id`, `displayName`, `unlockCost` (entropy), `prereqUnlockIds` (research/item ids that must be unlocked), `siteIds` (member sites), `theme` (palette/tint for `GridRenderer`).

## Item categories

Existing categories (see `elements-and-isotopes.md`): RawResource, Nucleon, Element, Isotope, Particle, Molecule, Alloy, Component. **New (Phase 2, DONE):**

- **`OrganicCompound`** (`ItemCategory.OrganicCompound`, added Phase 2) — Biology field feedstock + biomolecule inputs. Members shipped: `glucose` (item_id 150), `fatty_acid` (151), `amino_acid` (152), `nucleotide` (153) — all tier_3, `is_harvested:false`, forward-declared (produced by Chemistry C4 recipes in Phase 4; `base_sell_value` is a placeholder to tune there). Next free `item_id` is 154. (Later: `glycerol`, `fructose`, individual bases if needed.)

Chemistry produces new **Molecule**-category items (reusing the existing category) and the first `OrganicCompound` items at its C4 tier. Biology produces new items grouped under proposed categories `Biomolecule`, `CellPart`, `Organism` (or keep them all under a single `Lifeform` category — decide at Phase 4).

---

## World 1 — Particle Physics (existing)

Unchanged early tiers; the heavy tail moves to Act IV in the Phase-6 re-tier. Summary only (authoritative content lives in `game_data.json` / `elements-and-isotopes.md`):

| Tier | Opening wall | Produces |
|---|---|---|
| P1 Subatomic | start | up/down quark, electron (from `quark_field`, `electron_field`) |
| P2 Atomic | `recombination_i` 100e → `atomic_assembly` 500e → `light_elements` 1K → `mid_elements` 2K | protons/neutrons → H…Fe, Cu, Al |
| P3 Molecules | `molecular_synthesis` 3K | water, methane, ammonia, silica |

First prestiges land across P2–P3 (wall = 50,000e net worth).

---

## World 2 — Chemistry

**Unlock (BUILT, Phase 3+4a):** `chemistry_lab` research, **150,000e**, prereq `mid_elements` (depth 3, branch `Chemistry`). It is the Chemistry **world** gate — `world_chemistry.prereq_unlock_ids = ["chemistry_lab"]`, `unlock_cost 0`. `WorldService.UnlockWorld` deducts entropy, records `unlockedWorlds` (survives prestige), unlocks the member site, and `SwitchTo` travels there (delegating the grid handoff to `SiteService.SwitchTo`). Opens the Chemistry map + Element Harvester + Compound Synthesizer.

**Fields (drop existing Element items — mined, not synthesized). BUILT in Phase 2:** introduced on `site_chem_lab` via the absolute-count density-override path (like the fissile fields); the site zeroes the default `quark_field`/`electron_field`. `site_chem_lab` has `unlock_cost: 0` because the real gate is the Chemistry **world**.

| Field id | `field_type` | Drops (equal weight) |
|---|---|---|
| `element_field_light` | `"Element"` | hydrogen, carbon, nitrogen, oxygen |
| `element_field_metal` | `"ElementMetal"` | iron, copper, aluminum, nickel |
| `element_field_mineral` | `"Element"` | silicon, sodium, chlorine, sulfur, phosphorus, calcium |

**Metal fields are a separate type (`"ElementMetal"`)** so the C1 Element Harvester (`compatible_fields ["Element"]`) can't idle-farm the physics-era high-value metals (iron 167M etc.); a metal-capable harvester lands with C2/C3 if needed.

**Tiers, walls, buildings, recipes:**

| Tier | Wall (research → cost) | Building | Recipes (output ← inputs) | Status |
|---|---|---|---|---|
| C1 Inorganic Chemistry | `chemistry_lab` 150K (opens world) | Element Harvester (6 recipes: H/C/O/Na/Cl/S) · Compound Synthesizer | carbon_dioxide ← C+O · table_salt ← Na+Cl · sulfuric_acid ← S+O+H | **BUILT (4a)** |
| C2 Reactions & Catalysis | `reaction_engineering` ~500K | Catalytic Reactor | chlorine_gas · sodium_hydroxide · nitric_acid · catalyst (enables faster C3) | pending (4b) |
| C3 Organic Chemistry | `organic_chemistry` ~2M | Organic Synthesizer | methane · ethane · octane · ethanol · polymer_precursor (from hydrocarbons + catalyst) | pending (4b) |
| C4 Biochemistry *(culmination)* | `biochem_precursors` ~5M | Organic Synthesizer (or Biochem Lab) | **glucose · amino_acid · fatty_acid · nucleotide** → these are World 3's field feedstock | pending (4b) |

C1 shipped only the three *new* compounds (CO2, table salt, sulfuric acid). Water/ammonia were intentionally left to Physics P3 (they already exist there); C2–C4 extend into acids/salts/hydrocarbons the physics tree never had. **The C1 element sell-values are physics-era exponential (chlorine 327K, iron 167M), so raw-element sinking is over-valued** — the intended loop is harvest→synthesize→sink compounds; the real economy pass is Phase 6.

---

## World 3 — Biology

**Unlock:** `biology_lab` research, **~10–25M e**, prereq `biochem_precursors` (must have crafted organic compounds). Opens the Biology world + map + organic harvesters + Biosynthesizer. **New `ResearchBranch.Biology` enum value.**

**Fields (drop new `OrganicCompound` items):**

| Field id | Drops |
|---|---|
| `amino_acid_field` | amino_acid |
| `sugar_field` | glucose |
| `lipid_field` | fatty_acid |
| `nucleotide_field` | nucleotide |

**Tiers, walls, buildings, recipes (proposed):**

| Tier | Wall (research → cost) | Building | Recipes (output ← inputs) |
|---|---|---|---|
| B1 Biomolecules | `biology_lab` (opens world) | Biosynthesizer | protein ← amino_acid · carbohydrate ← glucose · lipid_membrane ← fatty_acid · nucleic_acid (DNA/RNA) ← nucleotide |
| B2 Cellular Biology | `cell_biology` ~50M | Cell Assembler | ribosome ← protein+nucleic_acid · mitochondria · cell_membrane ← lipid_membrane · prokaryotic_cell ← organelles |
| B3 Multicellular Life | `multicellular_life` ~250M | Tissue Culture | eukaryotic_cell · tissue ← cells · organ ← tissue |
| B4 Ecosystems *(bio endgame)* | `ecosystems` ~1B | Bioreactor / Biosphere | organism ← organs · population · ecosystem |

---

## Act IV — Heavy / Cosmic Physics (designed here, re-tier deferred to Phase 6)

Intended order once re-tiered: **Heavy Elements & Isotopes → Transuranics/Superheavies (fusion/fission) → Advanced Materials → Components → Megastructure.** These already exist in `game_data.json`; Phase 6 lifts their gates/costs **above Biology's ~1B** and coordinates with the `elements-and-isotopes.md` yield redesign. Do NOT move them as part of Phases 1–5.

## Balance guardrails

- Anchor output scaling to **OutputQuantity managers + megastructure `GlobalProductionBonus`** (live), NOT the prestige-shop multipliers (idle/display only). See `economy-balance.md` §"Economy health & risks".
- Costs above are first-pass. Slide them together on the progression map before writing to `game_data.json`.
- Watch the flat-log ✦ payout when Biology raises the net-worth ceiling to ~1B — the ✦ faucet does not scale with it (`economy-balance.md` §4).

## Open decisions (resolve during build)

1. ~~`FieldType` enum extension vs. data-driven string refactor (Phase 2).~~ **RESOLVED (Phase 2): data-driven string** — enum deleted, `FieldTypes` helper added.
2. One `Lifeform` item category vs. `Biomolecule`/`CellPart`/`Organism` split (Phase 4).
3. Whether C4 organics get their own building or extend the Organic Synthesizer.
4. Final gate costs + whether biology tiers need intermediate sub-gates for pacing.
