# Elements & Isotopes (Design & Taxonomy)

**Scope:** The full periodic table as game content — how every element (Z=1..118) and its key isotopes are created (existing buildings + new nuclear buildings + a new fissile-field map purchase), its craft time, its entropy yield, and the exotic particles nuclear reactions produce. **Phase 1 (all 118 elements as Atomic-Assembler recipes) is now shipped data** — see "Phase 1 implementation status" below; the new nuclear buildings, fissile fields, and decay mechanic remain design-only. For currency/formula context read `economy-balance.md`; for how to author items/recipes read `data-pipeline.md`; for the power grid read `ecs-patterns.md`; for new-building art read `visual-design.md`.

> Verified against: `992a572`, 2026-06-29 (natural tent to iron + post-iron synthesis endgame). If code contradicts this doc, trust the code and update this doc.

> **Authoritative source:** `Assets/Data/game_data.json` is the single source of truth (editor importer turns it into ScriptableObjects). Do NOT treat `docs/gameplay_loop_data.js` / `docs/gameplay-loop.html` as authoritative — they are a human-only visual reference and may lag. Values marked **(impl)** below are read from `game_data.json` as of the stamp; values marked **(design)** are proposals to author when the feature lands.

## What exists today vs. what this doc proposes

**Existing (impl):** **all 118 elements (Z=1..118)** plus 4 isotopes (deuterium, tritium, carbon-14, U-235) and 2 particles (alpha, beta), all craftable in the **Atomic Assembler** (`atomic_assembler`) from `proton`/`neutron`/`electron`. Isotopes adjusted in the **Isotopic Manipulator** (`isotopic_manipulator`), nucleons made in the **Strong Force Combiner** (`strong_force_combiner`), quarks/electrons tapped from fields by the **Harvester**, decay particles caught by **Radioactive Containment**. The `RecipeCategory.Fusion`/`Fission` enums and the `ResearchBranch.Nuclear` branch already exist but **back no dedicated buildings yet** (every element is currently an Assembler recipe).

**Proposed (design):** add **Fusion Reactor**, **Fission Reactor**, **Breeder Reactor**, and **Particle Accelerator** buildings and reroute the element recipes onto them as throughput shortcuts; add **Uranium/Plutonium fissile fields** as a map purchase; extend **Radioactive Containment** with an auto-decay setting; add the **positron / gamma photon / neutrino** particles alongside the existing neutron/alpha/beta; bake the element-tile visuals.

### Phase 1 implementation status (data-first, shipped)

The full table was implemented as pure `game_data.json` data (items `item_id` 52–149, recipes `recipe_id` 50–147) generated from the locked value model below. Notes for the next agent:

- **Gating mechanism (important):** the research-node `unlocks_items`/`unlocks_recipes` arrays and the `unlockedRecipes` save list are **inert** — nothing at runtime reads them. Crafting is gated by `RecipeSO.requiredResearch`, which is now enforced by a filter in `HUDBuildingInspectorSubController` (the Assembler "Set Recipe" picker hides recipes whose research is not yet unlocked). Before Phase 1 nothing gated the picker. The tutorial unlocks each gate immediately before the matching craft, so it stays in sync.
- **Interim research gates:** new elements are gated behind the existing element-group nodes plus three **Phase-1 placeholder** nodes — `heavy_transition_metals` (Z37–54), `lanthanides_and_heavy_metals` (Z55–86), `superheavy_synthesis` (Z101–118). These are a stand-in for the eventual fusion/fission/breeding/accelerator branches in "Research gates (design)" below; when those buildings land, re-home the recipes and retire the placeholders.
- **`recipes.json` parity gap:** `Assets/Data/recipes.json` is a **separate hand-maintained** source (the importer never writes it) that feeds the *manual* craft menu (`RecipeDatabase` → `HUDController.BuildRecipeList`) and recipe-knowledge/codex tracking. The 98 new elements were added only to `game_data.json` (the Assembler/ECS path), so they do **not** appear in the manual menu or `RecipeKnowledgeService`. Elements are `can_craft_manually:false`, so this is acceptable for crafting, but the manual menu / codex will not list them until `recipes.json` is also populated.
- **Visuals:** still text-only / placeholder spheres — element-tile sprites are Phase 2.

## Creation regimes — the natural tent, then the post-iron climb

Binding energy per nucleon peaks at iron-56. The **natural** economy (fusion + fission, Z1–92) is therefore a tent that peaks at iron (~168M e) — both feedstocks (hydrogen, uranium) are cheap and both directions climb toward iron. Past uranium, elements **no longer occur naturally**; they must be **synthesized with energy input** — a *second* endgame unlocked only after iron, whose value climbs **again, past iron**, to the global peak at Oganesson (~11.3 quadrillion e).

```
 value
   |                                                    Og ~11.3Q  = GLOBAL max
   |                                                   /  (post-iron SYNTHESIS)
   |   Fe ~168M  (natural peak)            Np ~336M   /
   |  /  \                                    \      /
   | /    \  fission descent                   \    /
   |/      \____________________  U 64 -- CLIFF -\--/   U = last natural element
   H ==fusion up==> Fe ==fission down==> U | ==breed==> Fm ==accelerate==> Og
   1       ->        26      ->          92  93    ->  100  101    ->      118
   \------------- NATURAL TENT (Z1-92) -------/  \---- POST-IRON SYNTHESIS ----/
```

Key points (numbers in the ladder section):
- **Iron (Z26, ~168M e) is the NATURAL peak and a long-term goal** — a month-plus of play and many prestiges to climb either natural ladder to it (see "Pacing & gating"). It is no longer the *global* max.
- **Uranium (Z92, ~64 e) is the last natural element** and the cheap bottom of the fission descent. Everything past it is artificial.
- **Post-iron synthesis is a separate endgame, unlocked only after iron.** Value rises again past iron through two branches that mirror real-world methods — **neutron breeding** (Z93–100) then **accelerator synthesis** (Z101–118) — up to **Oganesson, the single most valuable item in the game**.
- **Natural/synthetic cliff at Z92→93:** uranium (~64) sits right next to neptunium (~336M). The jump marks the boundary where matter stops being harvested and starts being manufactured at great cost.
- The natural elements still feed the **molecule → material → component** tiers (built from the *cheap, abundant* light elements).

Regimes (the Atomic Assembler remains a universal but deliberately expensive fallback for any element — see below):

| Regime | Z range | Direction | Building(s) | Notes |
|---|---|---|---|---|
| **Genesis** | 1 (H) | — | Atomic Assembler | `proton + electron`. The fuel source for fusion; cheapest element. |
| **Fusion ladder** | 2–26 (He → Fe) | climb **up** to iron | **Fusion Reactor** | He (p-p chain), C (triple-alpha), alpha process O→Si, up to Fe-56. Value rises in big ~2× steps. |
| **Fission / fragments** | 27–80 | climb **down** to iron | **Fission Reactor** | Split heavy/fissile nuclei into mid-weight fragments; each fragment is closer to iron and worth more. Many rungs, small gaps. |
| **Fissile field + decay** | 81–92 (→ U) | feedstock + decay | **Fissile Field** + Radioactive Containment | The naturally-occurring heavies (U, Th, and their decay chains: Pa, Ra, Rn, Po, Pb…) harvested from the field. Cheap entry for the fission ladder. |
| **Neutron breeding** (post-iron) | 93–100 (Np → Fm) | climb **up** past iron | **Breeder Reactor** | Real method: successive neutron capture + β-decay on actinide fuel. Its own research branch, gated after iron. Value rises past iron (336M → ~43B). |
| **Accelerator synthesis** (post-iron) | 101–118 (Md → Og) | climb **up** past iron | **Particle Accelerator** | Real method: heavy-ion fusion — fire a light projectile (e.g. Ca-48) at an actinide target. Its own research branch. Steepest, rarest tier (86B → ~11.3Q). Mostly codex/prestige trophies. |

**Why the Atomic Assembler is the "slow path" (and the reactors are the shortcuts the player wants).** The Assembler builds an atom one nucleon at a time: craft time ≈ atomic mass (seconds) and historic power cost scaled with mass (`mass × 5 eV`). Iron is 56 nucleons; uranium is 238. Doing that at scale is intentionally painful — the **Fusion Reactor** (combine two cheaper light nuclei) and **Fission Reactor** (split one expensive heavy nucleus into several mid-weight ones) are the throughput shortcuts that the nuclear research tree unlocks. This mirrors the existing "shortcut building" pattern (Strong Force Combiner is the bulk shortcut for protons/neutrons vs. hand-crafting).

## New buildings (design spec)

Reuse the existing `BuildingSO` schema (`game_data.json` `buildings[]`): `id`, `display_name`, `category`, `tier`, `requires_power`, `base_power_cost_ev`, `supported_recipes`, `input_slot_count`, `input_slot_labels`, `base_max_output_items`, `entropy_cost`, `required_research`, `structure_kind`. Power follows the current static-per-level model (see `economy-balance.md` "Power"), not the deprecated dynamic mass/neutron formula.

| Building (design) | id | Tier | Inputs | Output | Byproducts | Research gate |
|---|---|---|---|---|---|---|
| **Fusion Reactor** | `fusion_reactor` | 2 | 2 (light nuclei + optional neutron/H isotope fuel) | next element up the ladder | neutron, positron, gamma, neutrino (per reaction) | `fusion_i` |
| **Fission Reactor** | `fission_reactor` | 2 | 2 (fissile isotope + neutron trigger) | 2–3 mid-weight fragment elements | neutron ×2–3, gamma, (beta from fragments) | `fission_i` |
| **Breeder Reactor** | `breeder_reactor` | 5 | actinide(Z) + many neutrons | next actinide (Z+1) | beta, neutrino, gamma | `neutron_breeding_i` (post-iron) |
| **Particle Accelerator** | `particle_accelerator` | 5 | heavy target + light projectile (e.g. Ca-48) + huge eV | superheavy(Z) | neutron ×1–4, gamma | `accelerator_i` (post-iron) |

- **Category:** recommend reusing `BuildingCategory.Transient` (all four are recipe-processing producers like the Assembler) rather than adding `Nuclear` — avoids an enum migration. If a distinct power/visual treatment is wanted, add `BuildingCategory.Nuclear` and `BuildingStructureKind.FusionReactor`/`FissionReactor`/`BreederReactor`/`ParticleAccelerator` (see `visual-design.md`; bespoke procedural forms — a magnetic-confinement torus, a shielded rod-array core, a tall neutron-flux pile, a ring-collider — not the placeholder cube).
- **Power:** all are high-draw consumers; the post-iron pair (Breeder, Accelerator) are the highest in the game — the **Particle Accelerator** in particular should demand more eV than anything else, gating it behind a fully built-out power grid. Authoring TBD against the static-draw ladder in `economy-balance.md`.
- **Reaction model:** every nuclear recipe is an ordinary `RecipeSO` with `category` = `Fusion`/`Fission` (reuse for breeding/accelerator, or add `Breeding`/`Synthesis` to `RecipeCategory`), real `inputs`, an `output_item`, and a `byproducts[]` list. The Isotopic Manipulator's `neutron_adjustment[]` convention is the precedent. Fission's multiple outputs are one primary `output_item` plus extra fragments in `byproducts[]`.
- **Tier:** Breeder Reactor and Particle Accelerator are **tier 5 / post-iron** — they unlock only after iron and on their own research branches (see "Post-iron synthesis").

### Radioactive Containment — auto-decay setting (extends an existing building)

Unstable elements (everything past Z83, and all post-iron synthetics) are radioactive — they decay whether you want them to or not. **Radioactive Containment** gains a per-building **decay-mode toggle**:

- **Auto-decay ON** — housed unstable items passively convert to entropy at a **fraction of base value** (`decay_value_fraction`, design default **0.3**) over time, emitting decay particles (alpha/beta caught as usual). Hands-off, idle-friendly: the building is a partial entropy sink.
- **Auto-decay OFF** — items are simply held; conveyor them out to **Maxwell's Demon for the FULL base value**. Requires active logistics.

This is the post-iron risk/reward: the synthetics are worth a fortune, but capturing full value means routing them through the demon before they "decay" (i.e. paying the logistics cost), while AFK players still bank ~30% automatically. Schema: add `decay_mode` (enum) + `decay_value_fraction` (float) to `BuildingSO`, plus a runtime setting persisted per-building (like the existing manager assignment).

### Milestone fusion reactions (design)

| Reaction | Inputs | Output | Byproducts | Real-world analogue |
|---|---|---|---|---|
| p-p chain | 4× hydrogen (via deuterium) | helium_4 | 2× positron, 2× neutrino, gamma | stellar H burning |
| triple-alpha | 3× helium_4 | carbon | gamma | red-giant He burning |
| alpha process | carbon + helium_4 | oxygen | gamma | continues O→Ne→Mg→Si→…→Fe |
| Si burning | silicon (+ alpha steps) | iron (Fe-56) | gamma, neutrino | terminal fusion; valley floor |

### Milestone fission reactions (design)

| Reaction | Inputs | Output (primary + fragments) | Byproducts | Analogue |
|---|---|---|---|---|
| U-235 fission | uranium_235 + neutron | barium + krypton | 2–3× neutron, gamma | reactor / weapon fission |
| Pu-239 fission | plutonium (Pu-239) + neutron | xenon + zirconium | 2–3× neutron, gamma | breeder-reactor fission |

### Post-iron synthesis reactions (design)

**Neutron breeding** (Breeder Reactor) — successive neutron capture then β-decay, the real route to the actinides:

| Reaction | Inputs | Output | Byproducts | Analogue |
|---|---|---|---|---|
| U → Np | uranium + neutron | neptunium | beta, neutrino, gamma | U-238(n,γ)→U-239 →β Np-239 |
| Np → Pu | neptunium + neutron | plutonium | beta, neutrino | Np-239 →β Pu-239 |
| Pu → … → Fm | actinide(Z) + neutron | actinide(Z+1) | beta, neutrino, gamma | reactor n-capture chain up to fermium |

**Accelerator synthesis** (Particle Accelerator) — heavy-ion fusion: fire a light projectile at a heavy target. Recycles a fusion-ladder element (Ca-48) and a breeding-ladder actinide as inputs:

| Reaction | Inputs | Output | Byproducts | Analogue |
|---|---|---|---|---|
| hot fusion | actinide target (Pu/Am/Cm/Bk/Cf) + calcium (Ca-48) | superheavy (Fl…Og) | neutron ×3–4, gamma | Ca-48 + actinide → Z114–118 |
| cold fusion | lead/bismuth + medium ion (Ti/Cr/Fe/Zn) | superheavy (Rf…Cn) | neutron ×1, gamma | Pb/Bi-target route → Z104–112 |

## Fissile fields (new map purchase)

Past-iron matter has **no cheap fusion route**, so it must *enter* the economy from a field. Add two `FieldType` values alongside the existing `Quark`/`Lepton`:

- `FieldType.Uranium` — harvests `uranium` (U-238) and, with `fissile_extraction`, `uranium_235`; its decay chain seeds Th/Pa/Ra/Rn/Po/Pb collection.
- `FieldType.Plutonium` — harvests `plutonium` (Pu-239); seeds the transuranic chain via breeding.

Fissile fields are gated behind a **map purchase**, reusing the multi-grid build-site unlock pattern (purchasable sites, 250,000e+ — see `economy-balance.md` Entropy sinks and the `feature-multi-grids` task). A site tagged with a fissile field type is the only on-ramp for Regime 3/4 matter. Harvesting reuses the existing Harvester/collector flow (the field's `output_item` is the fissile element); no new harvest code is required beyond the new `FieldType` enum values and field authoring.

## Exotic particles

Extend the existing particle set (`ItemCategory.Particle`, with `is_secondary_particle`, `particle_uses` flags, `decay_type`). Current values: alpha = 50e, beta = 30e, neutron = 3e (a Nucleon).

| Particle | id | Symbol | Charge | Source reaction | Use(s) | Entropy yield | Status |
|---|---|---|---|---|---|---|---|
| Neutron | `neutron` | n⁰ | 0 | fusion/fission byproduct | fission trigger input; isotope building (Isotopic Manipulator) | 3 | impl |
| Alpha | `alpha_particle` | α | +2 | alpha decay (U, Pu, Ra…) | recipe input; energy recovery | 50 | impl |
| Beta (β⁻) | `beta_particle` | β | −1 | beta-minus decay (tritium, C-14, fission fragments) | energy recovery | 30 | impl |
| **Positron (β⁺)** | `positron` | e⁺ | +1 | beta-plus / p-p chain | annihilates with `electron` → 2× gamma (energy recovery) | 30 (design) | design |
| **Gamma photon** | `gamma_photon` | γ | 0 | emitted by nearly every fusion/fission/decay step | **energy recovery → eV** (feeds the power grid); recipe catalyst | 20 (design) | design |
| **Neutrino** | `neutrino` | ν | 0 | beta decay & fusion | *educational:* barely interacts — mostly escapes; near-zero capture value | 1 (design) | design |

- Particles are emitted as recipe `byproducts[]` and caught by **Radioactive Containment** (existing `collects_decay_particles` / decay-collection fields). Gamma's `ParticleUse.EnergyRecovery` is the hook to convert captured gammas back into eV — a clean tie to the power grid that makes nuclear loops partially self-powering.
- **Positron** needs a new `DecayType.BetaPlus` enum value (current `DecayType` = None/Alpha/Beta). Neutrino is deliberately almost worthless to teach that neutrinos are nearly undetectable — consider making capture optional (most "escape" and are never collected).

## Master element table (Z = 1..118)

Columns: **Z** | **Sym** | **Name** | **A** (mass number = Atomic-Assembler craft seconds) | **Path** (creation regime) | **Yield** (entropy = `base_sell_value`) | **St** (✅ exists in `game_data.json` / ◻ design). Path codes: **ASM** assembler, **FUS** Fusion Reactor, **FIS** Fission Reactor / fragment, **FLD** fissile-field harvest + decay chain, **BRD** neutron breeding, **ACC** accelerator synthesis. **Yields follow the convergent model in the ladder section below — value peaks at iron (~167,772,160 e) and falls off on BOTH sides.** Every implemented element carries OLD thinned values in `game_data.json` and must be re-derived from the two ladder formulas (fusion values rise sharply; above-iron heavies like U/Pu drop to a cheap floor).

| Z | Sym | Name | A | Path | Yield (e) | St |
|---|---|---|---|---|---|---|
| 1 | H | Hydrogen | 1 | ASM | 5 | ✅ |
| 2 | He | Helium | 4 | FUS | 10 | ✅ |
| 3 | Li | Lithium | 7 | FUS | 20 | ✅ |
| 4 | Be | Beryllium | 9 | FUS | 40 | ✅ |
| 5 | B | Boron | 11 | FUS | 80 | ✅ |
| 6 | C | Carbon | 12 | FUS | 160 | ✅ |
| 7 | N | Nitrogen | 14 | FUS | 320 | ✅ |
| 8 | O | Oxygen | 16 | FUS | 640 | ✅ |
| 9 | F | Fluorine | 19 | FUS | 1,280 | ◻ |
| 10 | Ne | Neon | 20 | FUS | 2,560 | ◻ |
| 11 | Na | Sodium | 23 | FUS | 5,120 | ◻ |
| 12 | Mg | Magnesium | 24 | FUS | 10,240 | ◻ |
| 13 | Al | Aluminum | 27 | FUS | 20,480 | ✅ |
| 14 | Si | Silicon | 28 | FUS | 40,960 | ✅ |
| 15 | P | Phosphorus | 31 | FUS | 81,920 | ◻ |
| 16 | S | Sulfur | 32 | FUS | 163,840 | ◻ |
| 17 | Cl | Chlorine | 35 | FUS | 327,680 | ◻ |
| 18 | Ar | Argon | 40 | FUS | 655,360 | ◻ |
| 19 | K | Potassium | 39 | FUS | 1,310,720 | ◻ |
| 20 | Ca | Calcium | 40 | FUS | 2,621,440 | ◻ |
| 21 | Sc | Scandium | 45 | FUS | 5,242,880 | ◻ |
| 22 | Ti | Titanium | 48 | FUS | 10,485,760 | ◻ |
| 23 | V | Vanadium | 51 | FUS | 20,971,520 | ◻ |
| 24 | Cr | Chromium | 52 | FUS | 41,943,040 | ◻ |
| 25 | Mn | Manganese | 55 | FUS | 83,886,080 | ◻ |
| 26 | Fe | Iron | 56 | FUS (apex) | 167,772,160 | ✅ |
| 27 | Co | Cobalt | 59 | FIS | 134,112,510 | ◻ |
| 28 | Ni | Nickel | 58 | FIS | 107,205,900 | ✅ |
| 29 | Cu | Copper | 63 | FIS | 85,697,486 | ✅ |
| 30 | Zn | Zinc | 65 | FIS | 68,504,244 | ✅ |
| 31 | Ga | Gallium | 69 | FIS | 54,760,433 | ◻ |
| 32 | Ge | Germanium | 74 | FIS | 43,774,003 | ◻ |
| 33 | As | Arsenic | 75 | FIS | 34,991,750 | ◻ |
| 34 | Se | Selenium | 80 | FIS | 27,971,455 | ◻ |
| 35 | Br | Bromine | 79 | FIS | 22,359,622 | ◻ |
| 36 | Kr | Krypton | 84 | FIS (fragment) | 17,873,675 | ◻ |
| 37 | Rb | Rubidium | 85 | FIS | 14,287,730 | ◻ |
| 38 | Sr | Strontium | 88 | FIS (fragment) | 11,421,224 | ◻ |
| 39 | Y | Yttrium | 89 | FIS (fragment) | 9,129,816 | ◻ |
| 40 | Zr | Zirconium | 90 | FIS (fragment) | 7,298,127 | ◻ |
| 41 | Nb | Niobium | 93 | FIS | 5,833,925 | ◻ |
| 42 | Mo | Molybdenum | 98 | FIS (fragment) | 4,663,481 | ◻ |
| 43 | Tc | Technetium | 98 | FIS | 3,727,860 | ◻ |
| 44 | Ru | Ruthenium | 102 | FIS | 2,979,950 | ◻ |
| 45 | Rh | Rhodium | 103 | FIS | 2,382,091 | ◻ |
| 46 | Pd | Palladium | 106 | FIS | 1,904,179 | ◻ |
| 47 | Ag | Silver | 108 | FIS | 1,522,149 | ✅ |
| 48 | Cd | Cadmium | 114 | FIS | 1,216,765 | ◻ |
| 49 | In | Indium | 115 | FIS | 972,649 | ◻ |
| 50 | Sn | Tin | 120 | FIS | 777,509 | ◻ |
| 51 | Sb | Antimony | 121 | FIS | 621,520 | ◻ |
| 52 | Te | Tellurium | 130 | FIS | 496,826 | ◻ |
| 53 | I | Iodine | 127 | FIS (fragment) | 397,149 | ◻ |
| 54 | Xe | Xenon | 132 | FIS (fragment) | 317,470 | ◻ |
| 55 | Cs | Cesium | 133 | FIS (fragment) | 253,777 | ◻ |
| 56 | Ba | Barium | 138 | FIS (fragment) | 202,862 | ◻ |
| 57 | La | Lanthanum | 139 | FIS (fragment) | 162,163 | ◻ |
| 58 | Ce | Cerium | 140 | FIS (fragment) | 129,629 | ◻ |
| 59 | Pr | Praseodymium | 141 | FIS | 103,622 | ◻ |
| 60 | Nd | Neodymium | 142 | FIS (fragment) | 82,832 | ◻ |
| 61 | Pm | Promethium | 145 | FIS | 66,214 | ◻ |
| 62 | Sm | Samarium | 152 | FIS | 52,930 | ◻ |
| 63 | Eu | Europium | 153 | FIS | 42,310 | ◻ |
| 64 | Gd | Gadolinium | 158 | FIS | 33,822 | ◻ |
| 65 | Tb | Terbium | 159 | FIS | 27,036 | ◻ |
| 66 | Dy | Dysprosium | 164 | FIS | 21,612 | ◻ |
| 67 | Ho | Holmium | 165 | FIS | 17,276 | ◻ |
| 68 | Er | Erbium | 166 | FIS | 13,810 | ◻ |
| 69 | Tm | Thulium | 169 | FIS | 11,039 | ◻ |
| 70 | Yb | Ytterbium | 174 | FIS | 8,825 | ◻ |
| 71 | Lu | Lutetium | 175 | FIS | 7,054 | ◻ |
| 72 | Hf | Hafnium | 180 | FIS | 5,639 | ◻ |
| 73 | Ta | Tantalum | 181 | FIS | 4,508 | ◻ |
| 74 | W | Tungsten | 184 | FIS | 3,603 | ✅ |
| 75 | Re | Rhenium | 187 | FIS | 2,880 | ◻ |
| 76 | Os | Osmium | 192 | FIS | 2,302 | ◻ |
| 77 | Ir | Iridium | 193 | FIS | 1,841 | ◻ |
| 78 | Pt | Platinum | 195 | FIS | 1,471 | ✅ |
| 79 | Au | Gold | 197 | FIS | 1,176 | ✅ |
| 80 | Hg | Mercury | 202 | FIS | 940 | ◻ |
| 81 | Tl | Thallium | 205 | FLD (decay) | 752 | ◻ |
| 82 | Pb | Lead | 208 | FLD (decay) | 601 | ◻ |
| 83 | Bi | Bismuth | 209 | FLD (decay) | 480 | ◻ |
| 84 | Po | Polonium | 209 | FLD (decay) | 384 | ◻ |
| 85 | At | Astatine | 210 | FLD (decay) | 307 | ◻ |
| 86 | Rn | Radon | 222 | FLD (decay) | 245 | ◻ |
| 87 | Fr | Francium | 223 | FLD (decay) | 196 | ◻ |
| 88 | Ra | Radium | 226 | FLD (decay) | 157 | ◻ |
| 89 | Ac | Actinium | 227 | FLD (decay) | 125 | ◻ |
| 90 | Th | Thorium | 232 | FLD (decay) | 100 | ◻ |
| 91 | Pa | Protactinium | 231 | FLD (decay) | 80 | ◻ |
| 92 | U | Uranium | 238 | FLD (decay) | 64 | ✅ |
| 93 | Np | Neptunium | 237 | BRD (breeding) | 335,544,320 | ◻ |
| 94 | Pu | Plutonium | 244 | BRD (breeding) | 671,088,640 | ✅ |
| 95 | Am | Americium | 243 | BRD (breeding) | 1,342,177,280 | ◻ |
| 96 | Cm | Curium | 247 | BRD (breeding) | 2,684,354,560 | ◻ |
| 97 | Bk | Berkelium | 247 | BRD (breeding) | 5,368,709,120 | ◻ |
| 98 | Cf | Californium | 251 | BRD (breeding) | 10,737,418,240 | ◻ |
| 99 | Es | Einsteinium | 252 | BRD (breeding) | 21,474,836,480 | ◻ |
| 100 | Fm | Fermium | 257 | BRD (breeding) | 42,949,672,960 | ◻ |
| 101 | Md | Mendelevium | 258 | ACC (collider) | 85,899,345,920 | ◻ |
| 102 | No | Nobelium | 259 | ACC (collider) | 171,798,691,840 | ◻ |
| 103 | Lr | Lawrencium | 266 | ACC (collider) | 343,597,383,680 | ◻ |
| 104 | Rf | Rutherfordium | 267 | ACC (collider) | 687,194,767,360 | ◻ |
| 105 | Db | Dubnium | 268 | ACC (collider) | 1,374,389,534,720 | ◻ |
| 106 | Sg | Seaborgium | 269 | ACC (collider) | 2,748,779,069,440 | ◻ |
| 107 | Bh | Bohrium | 270 | ACC (collider) | 5,497,558,138,880 | ◻ |
| 108 | Hs | Hassium | 269 | ACC (collider) | 10,995,116,277,760 | ◻ |
| 109 | Mt | Meitnerium | 278 | ACC (collider) | 21,990,232,555,520 | ◻ |
| 110 | Ds | Darmstadtium | 281 | ACC (collider) | 43,980,465,111,040 | ◻ |
| 111 | Rg | Roentgenium | 282 | ACC (collider) | 87,960,930,222,080 | ◻ |
| 112 | Cn | Copernicium | 285 | ACC (collider) | 175,921,860,444,160 | ◻ |
| 113 | Nh | Nihonium | 286 | ACC (collider) | 351,843,720,888,320 | ◻ |
| 114 | Fl | Flerovium | 289 | ACC (collider) | 703,687,441,776,640 | ◻ |
| 115 | Mc | Moscovium | 290 | ACC (collider) | 1,407,374,883,553,280 | ◻ |
| 116 | Lv | Livermorium | 293 | ACC (collider) | 2,814,749,767,106,560 | ◻ |
| 117 | Ts | Tennessine | 294 | ACC (collider) | 5,629,499,534,213,120 | ◻ |
| 118 | Og | Oganesson | 294 | ACC (collider) | 11,258,999,068,426,240 | ◻ |

## Milestone deep-dives

**H — Hydrogen (Z1).** `proton + electron` in the Atomic Assembler, 2s, 5e. The universal fusion fuel. Isotopes: protium (H-1), deuterium (H-2, +1n), tritium (H-3, +2n, β-decay). All impl.

**He — Helium (Z2).** Real fuel-cycle output of the p-p chain: `4 hydrogen → helium_4 + 2 e⁺ + 2 ν + γ` (via deuterium intermediate) in the Fusion Reactor; Assembler fallback `2p + 2n + 2e` (4s, 10e). Its nucleus *is* the alpha particle. Isotope He-3 is a fusion intermediate. The first reactor the player builds.

**C — Carbon (Z6).** Triple-alpha: `3 helium_4 → carbon + γ`. 12s assembler fallback, 160e. The classic "elements heavier than He skip Li/Be in stars" teachable moment — fusion can hand-wave the Li/Be/B gap (cosmic-ray spallation in reality).

**O / Si (Z8 / Z14).** Alpha-process rungs: `carbon + helium_4 → oxygen`, continuing to Ne/Mg/Si. 640e / 1,280e. Silicon is the gateway to molecules (silica) and components (semiconductor wafer) downstream — see `economy-balance.md` phases ⑫–⑭.

**Fe — Iron (Z26).** The **natural peak**: `silicon (+ alpha steps) → iron + γ + ν`, 56s assembler, **~167,772,160 e (5 × 2²⁵)**. Fusion stops releasing energy here and the fission ladder also peaks here — both natural ladders converge on iron, reachable only after a month-plus of play and many prestiges (see "Pacing & gating"). Iron is no longer the *global* max: it is the gateway that **unlocks the post-iron synthesis endgame**. Cobalt (Z27, ~134M) and Nickel (Z28, ~107M) sit just down the fission side — the near-iron elements are themselves endgame-grade.

**U — Uranium (Z92).** **The last natural element — cheap feedstock, not an apex.** Harvested from a Uranium fissile field (FLD), worth only **~64e** — you profit by fissioning it *down* toward iron, not by selling it raw. Enrich to U-235 in the Isotopic Manipulator (`uranium → uranium_235 + α`, impl); U-235 is the Fission Reactor fuel, and U is also the **breeding feedstock** for the post-iron endgame. Decay chain (α/β) seeds Th→Pa→…→Pb collection. (Its `game_data.json` value of 1,310,720e is the old model and must be rebalanced down.)

**Pu — Plutonium (Z94).** **First major synthetic** — bred from uranium by neutron capture in the **Breeder Reactor** (`U + n → … → Pu`). Past the natural/synthetic cliff, so its value jumps to **~671M e** (above iron). Pu-239 is both a fission fuel *and* an accelerator target (Ca-48 + Pu → flerovium). The point where the economy crosses from "harvested" to "manufactured."

**Transuranics & superheavies (Np…Og).** The post-iron endgame: bred (Np–Fm, ~336M–43B) then accelerator-synthesized (Md–Og, ~86B–**11.3Q**). Mostly **collection/prestige trophies** but now the *highest* values in the game — **Oganesson (Z118) is the single most valuable item**, the 100%-completion capstone. All are radioactive, so each unit is either auto-decayed for ~30% in Radioactive Containment or routed to Maxwell's Demon for full value.

## Isotope taxonomy

Isotopes share an element's `atomic_number` but differ in `atomic_mass`; they are created in the **Isotopic Manipulator** via the `neutron_adjustment[]` convention (points the recipe at the `neutron` ItemSO so the manipulator adds/removes neutrons). `category` = `Isotope`. Radioactive isotopes set `is_radioactive`, `decay_type`, `half_life_note` (display only — half-life is **not** time-simulated), and emit a decay particle as a `byproducts[]` entry caught by Radioactive Containment.

| Element | Isotopes (in-game) | Recipe | Notes |
|---|---|---|---|
| Hydrogen | protium / deuterium (H-2, +1n) / tritium (H-3, +2n) | `hydrogen + 1n` / `+2n` | deuterium impl (8e), tritium impl (12e, β, 12.3 yr) |
| Helium | He-3 / He-4 | He-4 impl; He-3 = fusion intermediate (design) | He-4 = alpha nucleus |
| Carbon | C-12 / C-14 (+2n, β, 5,730 yr) | `carbon + 2n` | C-14 impl, 240e |
| Uranium | U-238 / U-235 (fissile) | `uranium → U-235 + α` | U-235 ≈ 2× U = **~128e** under the convergent model (impl value 2,621,440e to be rebalanced); fissile |
| Plutonium | Pu-239 (fissile, design) | breeding | post-iron synthetic; base **~671M e** (Pu-239 the breeder/accelerator fuel) |
| Cobalt | Co-60 (design, β/γ) | `cobalt + n` | classic radioactive-source teachable; near-iron, base ~134M e |

**Isotope yield rule:** nominal `base_sell_value × 1.5` (`isotope_sell_multiplier`, per `economy-balance.md`), tuned per isotope — deuterium ≈1.6× H, C-14 = 1.5× C, U-235 = 2× U. Because the base element value now follows the natural-tent-plus-synthetic-climb curve, isotopes track their parent — a near-iron or synthetic isotope is hugely valuable, a uranium-floor isotope is cheap. Authoritative per-isotope values live in `game_data.json`.

## Entropy-yield ladder (the single "entropy generated" number)

Per the locked design, **"entropy generated" by an element = its entropy yield = `base_sell_value`** (the Maxwell's-Demon entropy sink pays `base_sell_value × qty` when an item is sunk). No separate thermodynamic-byproduct mechanic.

Three regions: the **natural tent** (fusion up + fission down, peaking at iron ~168M), then the **post-iron synthesis climb** (rising past iron to Oganesson, the global max ~11.3Q).

**1. Fusion ladder (Z1→26, climb up).** A clean per-Z doubling: `yield(Z) = 5 × 2^(Z − 1)`. Preserves the tutorial rungs (H..O) exactly and keeps doubling to the natural peak at iron — every element a distinct rung, big ~2× steps:

```
el:  H  He  Li  Be   B    C    N    O    F    Ne ...  Si  ...  Ca   ...   Mn          Fe(apex)
Z :  1   2   3   4    5    6    7    8    9    10      14       20         25            26
e :  5  10  20  40   80  160  320  640 1280  2560   40,960  2,621,440  83,886,080  167,772,160
```

**2. Fission ladder (Z92→26, climb down).** A dense descending ladder anchored to the same iron peak: `yield(Z) = round( 64 × 2,621,440^((92 − Z) / 66) )` for 27 ≤ Z ≤ 92 (`2,621,440 = iron / 64`). Iron at the top, uranium floored at **64 e** (the last natural element). Per-step ratio ≈ `2,621,440^(1/66) ≈ 1.25` (+25% per Z toward iron) — smaller gaps than fusion's 2×/rung because it packs ~66 rungs into the same height:

```
Z :     27        28        30        36        47       56     74    82    90  92
el:     Co        Ni        Zn        Kr        Ag       Ba      W    Pb    Th   U
e : 134,112,510 107,205,900 68,504,244 17,873,675 1,522,149 202,862 3,603 601  46  64
```

**3. Post-iron synthesis ladder (Z93→118, climb past iron).** A second rising ladder for the artificial elements: `yield(Z) = round( 167,772,160 × 2^(Z − 92) )` — doubling per Z from above iron up to **Oganesson, the single most valuable item in the game**. Neutron breeding (93–100) then accelerator synthesis (101–118):

```
Z :     93        94        96         100          103          110            118
el:     Np        Pu        Cm         Fm           Lr           Ds             Og
e : 335,544,320 671,088,640 2,684,354,560 42,949,672,960 343,597,383,680 43,980,465,111,040 11,258,999,068,426,240
        (336M)    (671M)     (2.68B)      (43B)         (344B)         (44T)          (11.26Q)
```

So the curve **rises to iron, falls to uranium, then jumps the natural/synthetic cliff (U 64 → Np 336M) and rises far past iron to Og**. The near-iron elements on both natural sides (Mn/Cr, Co/Ni) are endgame-grade millions; the synthetics are the true pinnacle.

> **Migration note:** every implemented element follows OLD thinned/monotonic values in `game_data.json` and must be **re-derived from the three formulas above** — e.g. Si 1,280→40,960, Fe 5,120→167,772,160, Ni 10,240→~107M, U 1,310,720→64, **Pu 2,621,440→671,088,640** (Pu is now a post-iron synthetic, not cheap). H..O are unchanged. Downstream tier-3+ recipes that consume heavy/iron elements (`uranium_hexafluoride`, `steel`/`iron_oxide`) need their cost basis re-reviewed; the synthetic elements (up to ~10^16) now exceed even the megastructure component tier, so they sit at the very top of the entropy economy.

> **Balance caveat:** Oganesson at ~10^16 is the new netWorth ceiling, far above the old element economy. It is reachable only after the full post-iron endgame (iron first, then both synthesis branches), so it should not distort early/mid pacing — but re-check prestige `✦` (≈log10 netWorth) and the megastructure interaction, since synthetics now out-value tier-5 components. Final constants need an idle-sim pass.

## Pacing & gating — iron is ~a month away

Iron must read as a **long-term apex**: the player should reach it only after **a month-plus of regular play and a healthy stack of prestiges + prestige upgrades**, not in the first few sessions. The ~168M e value is one half of that; the other half is *gating the climb* so the value is earned, not farmed early. Four compounding gates meter each rung toward iron (no single wall — they stack):

1. **Research depth (prestige-currency gated).** Each fusion/fission tier unlocks only a few more rungs (`fusion_i` → He–C, `fusion_ii` → through Si, … up to the transition metals → Fe). The deep tiers cost **prestige currency (✦)** and/or large entropy, so they are unaffordable until the player has prestiged repeatedly and bought into the prestige shop. The last rungs (Cr/Mn→Fe, Co/Ni→Fe) sit behind the most expensive nodes.
2. **Power (eV grid).** Heavier elements demand far more power per craft; reaching iron requires a power grid scaled up over many prestiges (generators + `PowerDiscount` managers). The near-iron rungs should out-draw anything the early grid can supply.
3. **Input volume (combinatorial).** One iron is 26 p + 30 n + 26 e, or 13 fused heliums up a 25-rung chain — every rung multiplies the upstream demand. Producing iron at any meaningful rate needs the whole lower chain running at high throughput, which only the **prestige output/speed multipliers** (managers, megastructure `GlobalProductionBonus`, Quantum Yield) make viable.
4. **Prestige-multiplier dependence.** Because the per-rung ~2× value step roughly tracks the cost step, net progress up the ladder comes from the *permanent* multipliers that survive prestige. Each prestige nudges the reachable rung up by ~1–2; reaching iron is therefore a function of accumulated prestige power, i.e. real-time played.

> **Tuning levers (to dial the ~1-month target after playtest):** raise the deep-tier research ✦ costs; steepen the final transition-metal rungs (e.g. ×3–4 per Z for Sc→Fe, pushing iron into the billions); raise near-iron power draw; or slow the per-prestige reachable-rung gain. Keep H..O untouched (tutorial). The exact curve needs an idle-sim pass — this section defines the *shape* (gated, compounding, prestige-bound), not final constants.

**Post-iron is a *second* endgame, even later.** Reaching iron only *opens* the breeding and accelerator branches; completing the periodic table to Oganesson is the true 100% goal, gated on top of iron by ✦/crystal-priced research, the highest power draw in the game (Particle Accelerator), and the input loop (bred actinides + Ca-48). Its payout is enormous (up to ~10^16) but metered by the auto-decay/Maxwell's-Demon logistics choice on every unstable output — so even at the top, value is *earned* through routing, not farmed passively.

## Research gates (design)

New nodes on the existing `ResearchBranch.Nuclear` / `Astrophysics` branches. **Fission opens early** — once the player has fused a few rungs (around Beryllium/Boron) they unlock `fissile_extraction`, gain the Uranium field, and start the *second* ladder. From there fusion and fission run **in parallel**, both racing toward iron; `heavy_elements` is repurposed (the old "Uranium = endgame" gate is gone). Entropy costs follow the "≈10–30 min of current-phase income" house rule.

| Research (design) | Branch | Prereq | Unlocks |
|---|---|---|---|
| `fusion_i` | Nuclear | `atomic_assembly` | Fusion Reactor; He/Li/Be/B/C fusion |
| `fissile_extraction` | Nuclear | `fusion_i` (≈ at Be/B) | **Uranium/Plutonium fissile fields (map purchase)**; harvest cheap heavy feedstock + U/Th decay chains |
| `fission_i` | Nuclear | `fissile_extraction` | Fission Reactor; split fissile fuel into mid-weight fragments (climb down toward iron) |
| `fusion_ii` | Nuclear | `fusion_i` | alpha process O→Si (Z8–14) |
| `fission_ii` | Nuclear | `fission_i` | denser fragment chains toward the near-iron metals (Co/Ni/Cu/Zn) |
| `fusion_iii` / `fission_iii` | Astrophysics | `fusion_ii` / `fission_ii` | the last rungs of each ladder converging on **Fe** (the natural apex) |

**Post-iron branches (unlocked only after iron).** Reaching iron opens two *new* research branches — broken out from the natural tree, each with its own building, gated behind a "reach iron" milestone (e.g. a `craft Fe` condition or an `iron_mastery` capstone). These are deep endgame: their nodes cost **prestige currency (✦)** and crystals, not just entropy.

| Research branch (design) | `ResearchBranch` | Prereq | Building | Unlocks |
|---|---|---|---|---|
| **Neutron Breeding** `neutron_breeding_i…iii` | `Nuclear` (or new `Transmutation`) | reach iron | **Breeder Reactor** | Np/Pu (i) → Am/Cm/Bk (ii) → Cf/Es/Fm (iii); value climbs past iron (336M→43B) |
| **Accelerator Synthesis** `accelerator_i…iv` | new `Accelerator` (or `Astrophysics`) | `neutron_breeding_i` + reach iron | **Particle Accelerator** | Md–Rf (i) → Db–Mt (ii) → Ds–Cn (iii) → Nh–Og (iv); the steepest, rarest tier up to Oganesson (~11.3Q) |

The accelerator branch depends on breeding because its **targets are bred actinides** (Pu/Am/Cm/Bk/Cf) and its **projectile is Ca-48** — so the post-iron endgame consumes both a fusion-ladder element (calcium) and breeding-ladder actinides, closing the loop.

## Known gaps / TBD (reconcile at implementation)

- **Enum additions:** `FieldType.Uranium`/`Plutonium`; `DecayType.BetaPlus` (positron); optional `BuildingCategory.Nuclear` + `BuildingStructureKind.FusionReactor`/`FissionReactor`/`BreederReactor`/`ParticleAccelerator`; optional `RecipeCategory.Breeding`/`Synthesis`; optional `ResearchBranch.Transmutation`/`Accelerator`. Each is a code change outside this doc pass.
- **Radioactive Containment decay setting:** new `BuildingSO` fields `decay_mode` (auto-decay vs hold) + `decay_value_fraction` (default 0.3), plus a per-building persisted setting and an `EntropySinkSystem`-style passive-decay path that pays the fraction and emits decay particles.
- **Yield rebalance (high priority):** every implemented element value is re-derived from the three formulas — fusion `5 × 2^(Z−1)` (iron = natural peak **167,772,160 e**), fission `round(64 × 2,621,440^((92−Z)/66))` (U→64), synthesis `round(167,772,160 × 2^(Z−92))` (Np→336M, **Pu→671M**, Og→~1.13×10^16, the global max). Old thinned `game_data.json` values are superseded; all (design) particle/isotope yields are placeholders.
- **Pacing not yet simulated:** "~1 month to iron" and the further post-iron endgame depend on research ✦/crystal costs, power draw, and prestige-multiplier curves that need an idle-sim pass. The value curves are set; the *gates* are shape-only.
- **Cross-tier reconciliation:** iron (~168M) and especially the synthetics (up to ~10^16) now exceed the molecule/material/component/megastructure sell tiers. Tier-3+ recipes that consume heavy/iron elements (`iron_oxide`, `steel`, `uranium_hexafluoride`) need re-pricing; confirm where post-iron synthetics sit relative to the megastructure endgame (they currently out-value tier-5 components).
- **Post-iron unlock condition:** define the "reach iron" gate that opens the breeding/accelerator branches (a `craft Fe` achievement-style condition or an `iron_mastery` capstone research).
- **Power authoring:** Fusion/Fission Reactor `base_power_cost_ev` not yet placed on the static-draw ladder.
- **Codex-only tail:** Z≳101 (and most transuranics) are intended as codex/prestige-completion content, not core-loop economy — confirm before authoring full recipes.
- **Fission fragment modelling:** multi-output fission (one `output_item` + fragments in `byproducts[]`) needs the production system to credit byproduct items on craft — verify the existing byproduct path (used by U-235 enrichment's alpha) handles multiple/element byproducts, not just particles.
- **Maintenance:** any PR that implements part of this (new elements, reactors, fields, particles) MUST update this doc and refresh the `Verified against` stamp.
