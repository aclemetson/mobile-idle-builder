# Elements & Isotopes (Design & Taxonomy)

**Scope:** The full periodic table as game content — how every element (Z=1..118) and its key isotopes are created (existing buildings + new nuclear buildings + a new fissile-field map purchase), its craft time, its entropy yield, and the exotic particles nuclear reactions produce. This is the **design spec** for later implementation; it is not itself shipped content. For currency/formula context read `economy-balance.md`; for how to author items/recipes read `data-pipeline.md`; for the power grid read `ecs-patterns.md`; for new-building art read `visual-design.md`.

> Verified against: `992a572`, 2026-06-29 (convergent iron-peaked value model). If code contradicts this doc, trust the code and update this doc.

> **Authoritative source:** `Assets/Data/game_data.json` is the single source of truth (editor importer turns it into ScriptableObjects). Do NOT treat `docs/gameplay_loop_data.js` / `docs/gameplay-loop.html` as authoritative — they are a human-only visual reference and may lag. Values marked **(impl)** below are read from `game_data.json` as of the stamp; values marked **(design)** are proposals to author when the feature lands.

## What exists today vs. what this doc proposes

**Existing (impl):** ~20 elements (H, He, Li, Be, B, C, N, O, Si, Al, Fe, Ni, Cu, Zn, Ag, Au, Pt, W, U, Pu), 4 isotopes (deuterium, tritium, carbon-14, U-235), 2 particles (alpha, beta). Built in the **Atomic Assembler** (`atomic_assembler`) from `proton`/`neutron`/`electron`, isotopes adjusted in the **Isotopic Manipulator** (`isotopic_manipulator`), nucleons made in the **Strong Force Combiner** (`strong_force_combiner`), quarks/electrons tapped from fields by the **Harvester**, decay particles caught by **Radioactive Containment**. The `RecipeCategory.Fusion`/`Fission` enums and the `ResearchBranch.Nuclear` branch already exist but **back no recipes/buildings yet**.

**Proposed (design):** fill the table to all 118 elements; add **Fusion Reactor** and **Fission Reactor** buildings; add **Uranium/Plutonium fissile fields** as a map purchase; add the **positron / gamma photon / neutrino** particles alongside the existing neutron/alpha/beta.

## Creation regimes — the iron valley (two ladders converging on Fe)

Binding energy per nucleon peaks at iron-56. **Both** production directions release energy as they climb toward iron, and the entropy economy mirrors that exactly: **value peaks at iron and falls off on both sides.** Iron is the convergence jackpot; the feedstock at each ladder's far end (hydrogen on one side, uranium on the other) is cheap.

```
                              IRON-56  = MAX value (5,120 e)
                                /\
            fusion (build up)  /  \  fission (break down)
            big gaps, ~2x/rung/    \ small gaps, ~1.08x/Z, many rungs
                              /      \
   H(5) He Li Be B C N O .. /        \ .. Ag .. W   U(40) .. Pu .. Og(6)
   |-- FUSION, value rises ->|        |<- FISSION, value rises ->|
   cheap light feedstock              cheap heavy feedstock (fissile fields)
```

Key consequences of the convergent model (see the ladder section for numbers):
- **Iron (Z26) is the single highest-value element (5,120 e).** Everything else is worth less.
- **Uranium and the heavy naturals are CHEAP feedstock (~40 e), not the apex.** You profit by fissioning them *down* toward iron, exactly as you profit by fusing hydrogen *up* toward iron.
- **Fission is unlocked early** (around Beryllium/Boron). From then on the two ladders run in parallel and the player races both toward iron.
- Beyond the element layer, the value climb continues in the **molecule → material → component** tiers (which consume elements) — so iron capping the *element* sub-economy is intentional, not a dead end.

Six regimes (the Atomic Assembler remains a universal but deliberately expensive fallback for any element — see below):

| Regime | Z range | Direction | Building(s) | Notes |
|---|---|---|---|---|
| **Genesis** | 1 (H) | — | Atomic Assembler | `proton + electron`. The fuel source for fusion; cheapest element. |
| **Fusion ladder** | 2–26 (He → Fe) | climb **up** to iron | **Fusion Reactor** (Assembler fallback) | He (p-p chain), C (triple-alpha), O/Ne/Mg/Si (alpha process), up to Fe-56. Value rises in big ~2× steps. |
| **Fission / fragments** | 27–80 | climb **down** to iron | **Fission Reactor** | Split heavy/fissile nuclei into mid-weight fragments; each fragment is closer to iron and worth more. Many rungs, small gaps. |
| **Fissile field + decay** | 81–92 (→ U) | feedstock + decay | **Fissile Field** + decay | The naturally-occurring heavies (U, Th, and their U/Th decay chains: Pa, Ra, Rn, Po, Pb…) harvested from the field. Cheap entry point for the fission ladder. |
| **Neutron breeding** | 93–100 (Np → Fm) | bred up from U | **Fission Reactor** (breeding) | Np/Pu/Am/Cm by neutron capture on fissile fuel. Below uranium in value (further from iron). |
| **Accelerator synthesis** | 101–118 | synthesized | accelerator | Super-heavies, fleeting half-lives, lowest value. Mostly codex / prestige-completion content. |

**Why the Atomic Assembler is the "slow path" (and the reactors are the shortcuts the player wants).** The Assembler builds an atom one nucleon at a time: craft time ≈ atomic mass (seconds) and historic power cost scaled with mass (`mass × 5 eV`). Iron is 56 nucleons; uranium is 238. Doing that at scale is intentionally painful — the **Fusion Reactor** (combine two cheaper light nuclei) and **Fission Reactor** (split one expensive heavy nucleus into several mid-weight ones) are the throughput shortcuts that the nuclear research tree unlocks. This mirrors the existing "shortcut building" pattern (Strong Force Combiner is the bulk shortcut for protons/neutrons vs. hand-crafting).

## New buildings (design spec)

Reuse the existing `BuildingSO` schema (`game_data.json` `buildings[]`): `id`, `display_name`, `category`, `tier`, `requires_power`, `base_power_cost_ev`, `supported_recipes`, `input_slot_count`, `input_slot_labels`, `base_max_output_items`, `entropy_cost`, `required_research`, `structure_kind`. Power follows the current static-per-level model (see `economy-balance.md` "Power"), not the deprecated dynamic mass/neutron formula.

| Building (design) | id | Tier | Inputs | Output | Byproducts | Research gate |
|---|---|---|---|---|---|---|
| **Fusion Reactor** | `fusion_reactor` | 2 | 2 (light nuclei + optional neutron/H isotope fuel) | next element up the ladder | neutron, positron, gamma, neutrino (per reaction) | `fusion_i` |
| **Fission Reactor** | `fission_reactor` | 2 | 2 (fissile isotope + neutron trigger) | 2–3 mid-weight fragment elements | neutron ×2–3, gamma, (beta from fragments) | `fission_i` |

- **Category:** recommend reusing `BuildingCategory.Transient` (both are recipe-processing producers like the Assembler) rather than adding `Nuclear` — avoids an enum migration. If a distinct power/visual treatment is wanted, add `BuildingCategory.Nuclear` and `BuildingStructureKind.FusionReactor`/`FissionReactor` (see `visual-design.md`; both want bespoke procedural forms — a magnetic-confinement torus for fusion, a shielded rod-array core for fission — not the placeholder cube).
- **Power:** both are high-draw consumers (above Materials Forge, below Component Fabricator); a fusion run should require enough generator coverage that the player must invest in the power grid first. Authoring TBD against the static-draw ladder in `economy-balance.md`.
- **Reaction model:** a fusion/fission recipe is an ordinary `RecipeSO` with `category` = `Fusion`/`Fission`, real `inputs`, an `output_item`, and a `byproducts[]` list (the particle emissions). The Isotopic Manipulator's `neutron_adjustment[]` convention is the precedent for nuclear bookkeeping. Fission's multiple outputs are modelled as one primary `output_item` plus extra fragment items in `byproducts[]`.

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
| Neutron breeding | uranium + neutron | (U-239 →) neptunium → plutonium | beta, neutrino | transuranic synthesis |

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

Columns: **Z** | **Sym** | **Name** | **A** (mass number = Atomic-Assembler craft seconds) | **Path** (creation regime) | **Yield** (entropy = `base_sell_value`) | **St** (✅ exists in `game_data.json` / ◻ design). Path codes: **ASM** assembler, **FUS** Fusion Reactor, **FIS** Fission Reactor / fragment, **FLD** fissile-field harvest + decay chain, **BRD** neutron breeding, **ACC** accelerator synthesis. **Yields follow the convergent model in the ladder section below — value peaks at iron (5,120 e) and falls off on BOTH sides.** The implemented elements above iron (Ni, Cu, Zn, Ag, W, Pt, Au, U, Pu) still carry their OLD monotonic values in `game_data.json` and must be rebalanced DOWN to these targets.

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
| 10 | Ne | Neon | 20 | FUS | 1,280 | ◻ |
| 11 | Na | Sodium | 23 | FUS | 1,280 | ◻ |
| 12 | Mg | Magnesium | 24 | FUS | 1,280 | ◻ |
| 13 | Al | Aluminum | 27 | FUS | 2,560 | ✅ |
| 14 | Si | Silicon | 28 | FUS | 1,280 | ✅ |
| 15 | P | Phosphorus | 31 | FUS | 2,560 | ◻ |
| 16 | S | Sulfur | 32 | FUS | 2,560 | ◻ |
| 17 | Cl | Chlorine | 35 | FUS | 2,560 | ◻ |
| 18 | Ar | Argon | 40 | FUS | 2,560 | ◻ |
| 19 | K | Potassium | 39 | FUS | 2,560 | ◻ |
| 20 | Ca | Calcium | 40 | FUS | 2,560 | ◻ |
| 21 | Sc | Scandium | 45 | FUS | 4,096 | ◻ |
| 22 | Ti | Titanium | 48 | FUS | 4,096 | ◻ |
| 23 | V | Vanadium | 51 | FUS | 4,096 | ◻ |
| 24 | Cr | Chromium | 52 | FUS | 4,096 | ◻ |
| 25 | Mn | Manganese | 55 | FUS | 4,096 | ◻ |
| 26 | Fe | Iron | 56 | FUS (valley) | 5,120 | ✅ |
| 27 | Co | Cobalt | 59 | FIS | 4,757 | ◻ |
| 28 | Ni | Nickel | 58 | FIS | 4,420 | ✅ |
| 29 | Cu | Copper | 63 | FIS | 4,107 | ✅ |
| 30 | Zn | Zinc | 65 | FIS | 3,816 | ✅ |
| 31 | Ga | Gallium | 69 | FIS | 3,545 | ◻ |
| 32 | Ge | Germanium | 74 | FIS | 3,294 | ◻ |
| 33 | As | Arsenic | 75 | FIS | 3,060 | ◻ |
| 34 | Se | Selenium | 80 | FIS | 2,843 | ◻ |
| 35 | Br | Bromine | 79 | FIS | 2,642 | ◻ |
| 36 | Kr | Krypton | 84 | FIS (fragment) | 2,455 | ◻ |
| 37 | Rb | Rubidium | 85 | FIS | 2,281 | ◻ |
| 38 | Sr | Strontium | 88 | FIS (fragment) | 2,119 | ◻ |
| 39 | Y | Yttrium | 89 | FIS (fragment) | 1,969 | ◻ |
| 40 | Zr | Zirconium | 90 | FIS (fragment) | 1,829 | ◻ |
| 41 | Nb | Niobium | 93 | FIS | 1,700 | ◻ |
| 42 | Mo | Molybdenum | 98 | FIS (fragment) | 1,579 | ◻ |
| 43 | Tc | Technetium | 98 | FIS | 1,467 | ◻ |
| 44 | Ru | Ruthenium | 102 | FIS | 1,363 | ◻ |
| 45 | Rh | Rhodium | 103 | FIS | 1,267 | ◻ |
| 46 | Pd | Palladium | 106 | FIS | 1,177 | ◻ |
| 47 | Ag | Silver | 108 | FIS | 1,093 | ✅ |
| 48 | Cd | Cadmium | 114 | FIS | 1,016 | ◻ |
| 49 | In | Indium | 115 | FIS | 944 | ◻ |
| 50 | Sn | Tin | 120 | FIS | 877 | ◻ |
| 51 | Sb | Antimony | 121 | FIS | 815 | ◻ |
| 52 | Te | Tellurium | 130 | FIS | 757 | ◻ |
| 53 | I | Iodine | 127 | FIS (fragment) | 703 | ◻ |
| 54 | Xe | Xenon | 132 | FIS (fragment) | 654 | ◻ |
| 55 | Cs | Cesium | 133 | FIS (fragment) | 607 | ◻ |
| 56 | Ba | Barium | 138 | FIS (fragment) | 564 | ◻ |
| 57 | La | Lanthanum | 139 | FIS (fragment) | 524 | ◻ |
| 58 | Ce | Cerium | 140 | FIS (fragment) | 487 | ◻ |
| 59 | Pr | Praseodymium | 141 | FIS | 453 | ◻ |
| 60 | Nd | Neodymium | 142 | FIS (fragment) | 420 | ◻ |
| 61 | Pm | Promethium | 145 | FIS | 391 | ◻ |
| 62 | Sm | Samarium | 152 | FIS | 363 | ◻ |
| 63 | Eu | Europium | 153 | FIS | 337 | ◻ |
| 64 | Gd | Gadolinium | 158 | FIS | 313 | ◻ |
| 65 | Tb | Terbium | 159 | FIS | 291 | ◻ |
| 66 | Dy | Dysprosium | 164 | FIS | 271 | ◻ |
| 67 | Ho | Holmium | 165 | FIS | 251 | ◻ |
| 68 | Er | Erbium | 166 | FIS | 234 | ◻ |
| 69 | Tm | Thulium | 169 | FIS | 217 | ◻ |
| 70 | Yb | Ytterbium | 174 | FIS | 202 | ◻ |
| 71 | Lu | Lutetium | 175 | FIS | 187 | ◻ |
| 72 | Hf | Hafnium | 180 | FIS | 174 | ◻ |
| 73 | Ta | Tantalum | 181 | FIS | 162 | ◻ |
| 74 | W | Tungsten | 184 | FIS | 150 | ✅ |
| 75 | Re | Rhenium | 187 | FIS | 140 | ◻ |
| 76 | Os | Osmium | 192 | FIS | 130 | ◻ |
| 77 | Ir | Iridium | 193 | FIS | 120 | ◻ |
| 78 | Pt | Platinum | 195 | FIS | 112 | ✅ |
| 79 | Au | Gold | 197 | FIS | 104 | ✅ |
| 80 | Hg | Mercury | 202 | FIS | 97 | ◻ |
| 81 | Tl | Thallium | 205 | FLD (decay) | 90 | ◻ |
| 82 | Pb | Lead | 208 | FLD (decay) | 83 | ◻ |
| 83 | Bi | Bismuth | 209 | FLD (decay) | 78 | ◻ |
| 84 | Po | Polonium | 209 | FLD (decay) | 72 | ◻ |
| 85 | At | Astatine | 210 | FLD (decay) | 67 | ◻ |
| 86 | Rn | Radon | 222 | FLD (decay) | 62 | ◻ |
| 87 | Fr | Francium | 223 | FLD (decay) | 58 | ◻ |
| 88 | Ra | Radium | 226 | FLD (decay) | 54 | ◻ |
| 89 | Ac | Actinium | 227 | FLD (decay) | 50 | ◻ |
| 90 | Th | Thorium | 232 | FLD (decay) | 46 | ◻ |
| 91 | Pa | Protactinium | 231 | FLD (decay) | 43 | ◻ |
| 92 | U | Uranium | 238 | FLD (decay) | 40 | ✅ |
| 93 | Np | Neptunium | 237 | BRD | 37 | ◻ |
| 94 | Pu | Plutonium | 244 | BRD | 35 | ✅ |
| 95 | Am | Americium | 243 | BRD | 32 | ◻ |
| 96 | Cm | Curium | 247 | BRD | 30 | ◻ |
| 97 | Bk | Berkelium | 247 | BRD | 28 | ◻ |
| 98 | Cf | Californium | 251 | BRD | 26 | ◻ |
| 99 | Es | Einsteinium | 252 | BRD | 24 | ◻ |
| 100 | Fm | Fermium | 257 | BRD | 22 | ◻ |
| 101 | Md | Mendelevium | 258 | ACC | 21 | ◻ |
| 102 | No | Nobelium | 259 | ACC | 19 | ◻ |
| 103 | Lr | Lawrencium | 266 | ACC | 18 | ◻ |
| 104 | Rf | Rutherfordium | 267 | ACC | 17 | ◻ |
| 105 | Db | Dubnium | 268 | ACC | 15 | ◻ |
| 106 | Sg | Seaborgium | 269 | ACC | 14 | ◻ |
| 107 | Bh | Bohrium | 270 | ACC | 13 | ◻ |
| 108 | Hs | Hassium | 269 | ACC | 12 | ◻ |
| 109 | Mt | Meitnerium | 278 | ACC | 11 | ◻ |
| 110 | Ds | Darmstadtium | 281 | ACC | 11 | ◻ |
| 111 | Rg | Roentgenium | 282 | ACC | 10 | ◻ |
| 112 | Cn | Copernicium | 285 | ACC | 9 | ◻ |
| 113 | Nh | Nihonium | 286 | ACC | 9 | ◻ |
| 114 | Fl | Flerovium | 289 | ACC | 8 | ◻ |
| 115 | Mc | Moscovium | 290 | ACC | 7 | ◻ |
| 116 | Lv | Livermorium | 293 | ACC | 7 | ◻ |
| 117 | Ts | Tennessine | 294 | ACC | 6 | ◻ |
| 118 | Og | Oganesson | 294 | ACC | 6 | ◻ |

## Milestone deep-dives

**H — Hydrogen (Z1).** `proton + electron` in the Atomic Assembler, 2s, 5e. The universal fusion fuel. Isotopes: protium (H-1), deuterium (H-2, +1n), tritium (H-3, +2n, β-decay). All impl.

**He — Helium (Z2).** Real fuel-cycle output of the p-p chain: `4 hydrogen → helium_4 + 2 e⁺ + 2 ν + γ` (via deuterium intermediate) in the Fusion Reactor; Assembler fallback `2p + 2n + 2e` (4s, 10e). Its nucleus *is* the alpha particle. Isotope He-3 is a fusion intermediate. The first reactor the player builds.

**C — Carbon (Z6).** Triple-alpha: `3 helium_4 → carbon + γ`. 12s assembler fallback, 160e. The classic "elements heavier than He skip Li/Be in stars" teachable moment — fusion can hand-wave the Li/Be/B gap (cosmic-ray spallation in reality).

**O / Si (Z8 / Z14).** Alpha-process rungs: `carbon + helium_4 → oxygen`, continuing to Ne/Mg/Si. 640e / 1,280e. Silicon is the gateway to molecules (silica) and components (semiconductor wafer) downstream — see `economy-balance.md` phases ⑫–⑭.

**Fe — Iron (Z26).** The **apex**: `silicon (+ alpha steps) → iron + γ + ν`, 56s assembler, **5,120e — the single highest-value element**. Fusion stops releasing energy here, and the fission ladder also peaks here: both ladders converge on iron, which is the production goal from either direction. Nickel (Z28, the true binding-energy peak in reality) sits one rung down the fission side (~4,420e).

**U — Uranium (Z92).** **Cheap heavy feedstock, not the apex.** Harvested from a Uranium fissile field (FLD) and worth only **~40e** (Be-tier) — you profit by fissioning it *down* toward iron, not by selling it raw. Enrich to U-235 in the Isotopic Manipulator (`uranium → uranium_235 + α`, impl); U-235 is the Fission Reactor fuel. Decay chain (α/β) seeds Th→Pa→…→Pb collection, all near the U price floor. (Its `game_data.json` value of 1,310,720e is the old monotonic model and must be rebalanced down.)

**Pu — Plutonium (Z94).** First transuranic: bred from uranium by neutron capture (`uranium + neutron → … → plutonium`, BRD) or harvested from a Plutonium field. Pu-239 is the breeder-fuel fission input. **~35e** — just below uranium (further from iron); economically marginal, valued for its role as fission fuel rather than as a sale item.

**Transuranics (Np…Og).** Bred (Np–Fm) or accelerator-synthesized (Z≳101); fleeting half-lives, **6–37e** (below the U floor). Primarily **codex completion / prestige flex**, not core economy — the further past uranium, the *less* an element is worth.

## Isotope taxonomy

Isotopes share an element's `atomic_number` but differ in `atomic_mass`; they are created in the **Isotopic Manipulator** via the `neutron_adjustment[]` convention (points the recipe at the `neutron` ItemSO so the manipulator adds/removes neutrons). `category` = `Isotope`. Radioactive isotopes set `is_radioactive`, `decay_type`, `half_life_note` (display only — half-life is **not** time-simulated), and emit a decay particle as a `byproducts[]` entry caught by Radioactive Containment.

| Element | Isotopes (in-game) | Recipe | Notes |
|---|---|---|---|
| Hydrogen | protium / deuterium (H-2, +1n) / tritium (H-3, +2n) | `hydrogen + 1n` / `+2n` | deuterium impl (8e), tritium impl (12e, β, 12.3 yr) |
| Helium | He-3 / He-4 | He-4 impl; He-3 = fusion intermediate (design) | He-4 = alpha nucleus |
| Carbon | C-12 / C-14 (+2n, β, 5,730 yr) | `carbon + 2n` | C-14 impl, 240e |
| Uranium | U-238 / U-235 (fissile) | `uranium → U-235 + α` | U-235 ≈ 2× U = **~80e** under the convergent model (impl value 2,621,440e to be rebalanced); fissile |
| Plutonium | Pu-239 (fissile, design) | breeding | weapons/breeder fuel; base ~35e |
| Cobalt | Co-60 (design, β/γ) | `cobalt + n` | classic radioactive-source teachable; near-iron, base ~4,760e |

**Isotope yield rule:** nominal `base_sell_value × 1.5` (`isotope_sell_multiplier`, per `economy-balance.md`), tuned per isotope — deuterium ≈1.6× H, C-14 = 1.5× C, U-235 = 2× U. Because the base element value now follows the convergent (iron-peaked) curve, isotopes track their parent — a near-iron isotope is valuable, a heavy-actinide isotope is cheap. Authoritative per-isotope values live in `game_data.json`.

## Entropy-yield ladder (the single "entropy generated" number)

Per the locked design, **"entropy generated" by an element = its entropy yield = `base_sell_value`** (the Maxwell's-Demon entropy sink pays `base_sell_value × qty` when an item is sunk). No separate thermodynamic-byproduct mechanic.

**Convergent model — value is a tent peaking at iron.** Two ladders climb toward Fe-56 (the binding-energy peak) from opposite ends; iron is the single maximum and both feedstocks (hydrogen, uranium) are cheap. Equivalent rungs on the two ladders are worth comparable amounts; the fission ladder is *denser* (more elements, smaller per-step gaps) because there are far more nuclides between uranium and iron than between hydrogen and iron.

**Fusion ladder (Z1→26, climb up).** The existing curated doubling ladder — big ~2× steps, few rungs, floored at H = 5 e:

```
el:  H  He  Li  Be   B    C    N    O   ...  Si  ... (Al) ...   Fe(peak)
Z :  1   2   3   4    5    6    7    8        14      13         26
e :  5  10  20  40   80  160  320  640      1280    2560       5120
```
(Al > Si is an authored quirk preserved from `game_data.json`; light values H..O are pinned for tutorial balance.)

**Fission ladder (Z92→26, climb down).** A dense descending ladder: `yield(Z) = round( 40 × 128^((92 − Z) / 66) )` for Z ≥ 27. Iron-anchored at the top (the formula gives 5,120 at Z = 26), uranium floored at 40 e (Be-tier — cheap feedstock), continuing below 40 for the transuranics. Per-step ratio ≈ `128^(1/66) ≈ 1.076` (about +7.6% per Z toward iron) — small gaps, exactly because the fission side is rung-dense.

```
Z :  27   28   29   30   36   47   56   74   78   79   82   90   92   94   100  118
el:  Co   Ni   Cu   Zn   Kr   Ag   Ba    W   Pt   Au   Pb   Th    U   Pu   Fm   Og
e : 4757 4420 4107 3816 2455 1093  564  150  112  104   83   46   40   35   22    6
```

**Why iron is the cap (and that's fine).** Per-Z doubling across 118 elements would reach 5×2^117 ≈ 10^35 — unusable; and economically you don't want 118 ever-larger tiers. Capping the *element* layer at iron keeps the numbers sane, makes iron a genuine goal both ladders race toward, and hands the continuing value climb to the **molecule → material → component** tiers that consume elements (Steel 1.5M, Components 50B–100T — see `economy-balance.md`).

> **Migration note:** the implemented elements **above iron** (Ni 10,240 → Pu 2,621,440 in `game_data.json`) follow the OLD monotonic ladder and must be rebalanced **down** to the fission-ladder targets above (e.g. Ni 10,240→~4,420, U 1,310,720→40, Pu 2,621,440→35). Fusion-side and sub-iron values are unchanged. Downstream tier-3+ recipes that consume heavy elements (e.g. `uranium_hexafluoride`) inherit a far cheaper cost basis — review their sell values when this lands.

> **Balance caveat:** lowering the heavy-element ceiling drops late-game netWorth, which feeds prestige `✦` (≈log10 netWorth, `economy-balance.md` risk #4). Re-check first-prestige cadence after rebalancing; the molecule/material/component tiers (unchanged) should carry netWorth past the old element ceiling.

## Research gates (design)

New nodes on the existing `ResearchBranch.Nuclear` / `Astrophysics` branches. **Fission opens early** — once the player has fused a few rungs (around Beryllium/Boron) they unlock `fissile_extraction`, gain the Uranium field, and start the *second* ladder. From there fusion and fission run **in parallel**, both racing toward iron; `heavy_elements` is repurposed (the old "Uranium = endgame" gate is gone). Entropy costs follow the "≈10–30 min of current-phase income" house rule.

| Research (design) | Branch | Prereq | Unlocks |
|---|---|---|---|
| `fusion_i` | Nuclear | `atomic_assembly` | Fusion Reactor; He/Li/Be/B/C fusion |
| `fissile_extraction` | Nuclear | `fusion_i` (≈ at Be/B) | **Uranium/Plutonium fissile fields (map purchase)**; harvest cheap heavy feedstock + U/Th decay chains |
| `fission_i` | Nuclear | `fissile_extraction` | Fission Reactor; split fissile fuel into mid-weight fragments (climb down toward iron) |
| `fusion_ii` | Nuclear | `fusion_i` | alpha process O→Si (Z8–14) |
| `fission_ii` | Nuclear | `fission_i` | denser fragment chains toward the near-iron metals (Co/Ni/Cu/Zn) |
| `fusion_iii` / `fission_iii` | Astrophysics | `fusion_ii` / `fission_ii` | the last rungs of each ladder converging on **Fe** (the jackpot) |
| `transuranics` | Nuclear | `fission_i` | neutron breeding Np→Fm; accelerator synthesis Z≥101 (low-value codex tail) |

## Known gaps / TBD (reconcile at implementation)

- **Enum additions:** `FieldType.Uranium`/`Plutonium`; `DecayType.BetaPlus` (positron); optional `BuildingCategory.Nuclear` + `BuildingStructureKind.FusionReactor`/`FissionReactor`. Each is a code change outside this doc pass.
- **Yield rebalance (high priority):** the implemented above-iron elements (Ni→Pu) still hold OLD monotonic values in `game_data.json` and must be lowered to the convergent fission-ladder targets (`yield = round(40 × 128^((92−Z)/66))`); iron becomes the global element max (5,120e). All (design) particle/isotope yields are likewise placeholders.
- **Power authoring:** Fusion/Fission Reactor `base_power_cost_ev` not yet placed on the static-draw ladder.
- **Codex-only tail:** Z≳101 (and most transuranics) are intended as codex/prestige-completion content, not core-loop economy — confirm before authoring full recipes.
- **Fission fragment modelling:** multi-output fission (one `output_item` + fragments in `byproducts[]`) needs the production system to credit byproduct items on craft — verify the existing byproduct path (used by U-235 enrichment's alpha) handles multiple/element byproducts, not just particles.
- **Maintenance:** any PR that implements part of this (new elements, reactors, fields, particles) MUST update this doc and refresh the `Verified against` stamp.
