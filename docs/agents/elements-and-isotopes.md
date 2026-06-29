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
                       IRON-56  = MAX value (~168,000,000 e)
                          /\           <- the long-term apex: a month+
        fusion (build up)/  \fission    of play and many prestiges away
        big ~2x/Z steps /    \ smaller ~1.25x/Z steps, many rungs
                       /      \
   H(5) He Li B C N O /        \ .. Ag .. W   U(64) Pu .. Og(1)
   |- FUSION rises ->|          |<- FISSION rises -|
   cheap light feedstock        cheap heavy feedstock (fissile fields)
```

Key consequences of the convergent model (see the ladder section for numbers):
- **Iron (Z26) is the single highest-value element (~168,000,000 e) and a deliberate long-term apex.** The player should NOT reach iron quickly — it takes **a month-plus of regular play and a good stack of prestiges + prestige upgrades** to climb either ladder to the summit (see "Pacing & gating"). Everything else is worth less.
- **Uranium and the heavy naturals are CHEAP feedstock (~64 e), not the apex.** You profit by fissioning them *down* toward iron, exactly as you profit by fusing hydrogen *up* toward iron.
- **Fission is unlocked early** (around Beryllium/Boron). From then on the two ladders run in parallel and the player slowly races both toward iron over many prestige cycles.
- Beyond the element layer, the value climb continues in the **molecule → material → component** tiers (which consume the cheap, abundant elements) — so iron capping the *element* sub-economy is intentional, not a dead end.

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
| 93 | Np | Neptunium | 237 | BRD | 51 | ◻ |
| 94 | Pu | Plutonium | 244 | BRD | 41 | ✅ |
| 95 | Am | Americium | 243 | BRD | 33 | ◻ |
| 96 | Cm | Curium | 247 | BRD | 26 | ◻ |
| 97 | Bk | Berkelium | 247 | BRD | 21 | ◻ |
| 98 | Cf | Californium | 251 | BRD | 17 | ◻ |
| 99 | Es | Einsteinium | 252 | BRD | 13 | ◻ |
| 100 | Fm | Fermium | 257 | BRD | 11 | ◻ |
| 101 | Md | Mendelevium | 258 | ACC | 9 | ◻ |
| 102 | No | Nobelium | 259 | ACC | 7 | ◻ |
| 103 | Lr | Lawrencium | 266 | ACC | 5 | ◻ |
| 104 | Rf | Rutherfordium | 267 | ACC | 4 | ◻ |
| 105 | Db | Dubnium | 268 | ACC | 3 | ◻ |
| 106 | Sg | Seaborgium | 269 | ACC | 3 | ◻ |
| 107 | Bh | Bohrium | 270 | ACC | 2 | ◻ |
| 108 | Hs | Hassium | 269 | ACC | 2 | ◻ |
| 109 | Mt | Meitnerium | 278 | ACC | 1 | ◻ |
| 110 | Ds | Darmstadtium | 281 | ACC | 1 | ◻ |
| 111 | Rg | Roentgenium | 282 | ACC | 1 | ◻ |
| 112 | Cn | Copernicium | 285 | ACC | 1 | ◻ |
| 113 | Nh | Nihonium | 286 | ACC | 1 | ◻ |
| 114 | Fl | Flerovium | 289 | ACC | 1 | ◻ |
| 115 | Mc | Moscovium | 290 | ACC | 1 | ◻ |
| 116 | Lv | Livermorium | 293 | ACC | 1 | ◻ |
| 117 | Ts | Tennessine | 294 | ACC | 1 | ◻ |
| 118 | Og | Oganesson | 294 | ACC | 1 | ◻ |

## Milestone deep-dives

**H — Hydrogen (Z1).** `proton + electron` in the Atomic Assembler, 2s, 5e. The universal fusion fuel. Isotopes: protium (H-1), deuterium (H-2, +1n), tritium (H-3, +2n, β-decay). All impl.

**He — Helium (Z2).** Real fuel-cycle output of the p-p chain: `4 hydrogen → helium_4 + 2 e⁺ + 2 ν + γ` (via deuterium intermediate) in the Fusion Reactor; Assembler fallback `2p + 2n + 2e` (4s, 10e). Its nucleus *is* the alpha particle. Isotope He-3 is a fusion intermediate. The first reactor the player builds.

**C — Carbon (Z6).** Triple-alpha: `3 helium_4 → carbon + γ`. 12s assembler fallback, 160e. The classic "elements heavier than He skip Li/Be in stars" teachable moment — fusion can hand-wave the Li/Be/B gap (cosmic-ray spallation in reality).

**O / Si (Z8 / Z14).** Alpha-process rungs: `carbon + helium_4 → oxygen`, continuing to Ne/Mg/Si. 640e / 1,280e. Silicon is the gateway to molecules (silica) and components (semiconductor wafer) downstream — see `economy-balance.md` phases ⑫–⑭.

**Fe — Iron (Z26).** The **apex**: `silicon (+ alpha steps) → iron + γ + ν`, 56s assembler, **~167,772,160 e (5 × 2²⁵) — the single highest-value element and the game's long-term element goal**. Fusion stops releasing energy here and the fission ladder also peaks here: both ladders converge on iron, reachable only after a month-plus of play and many prestiges (see "Pacing & gating"). Nickel (Z28, the true binding-energy peak in reality) sits two rungs down the fission side (~107M e), and Cobalt (Z27) one rung down (~134M e) — the near-iron elements are themselves endgame-grade.

**U — Uranium (Z92).** **Cheap heavy feedstock, not the apex.** Harvested from a Uranium fissile field (FLD) and worth only **~64e** (Be/B-tier — unlocked around the same time) — you profit by fissioning it *down* toward iron, not by selling it raw. Enrich to U-235 in the Isotopic Manipulator (`uranium → uranium_235 + α`, impl); U-235 is the Fission Reactor fuel. Decay chain (α/β) seeds Th→Pa→…→Pb collection, all near the U price floor. (Its `game_data.json` value of 1,310,720e is the old monotonic model and must be rebalanced down.)

**Pu — Plutonium (Z94).** First transuranic: bred from uranium by neutron capture (`uranium + neutron → … → plutonium`, BRD) or harvested from a Plutonium field. Pu-239 is the breeder-fuel fission input. **~41e** — just below uranium (further from iron); economically marginal, valued for its role as fission fuel rather than as a sale item.

**Transuranics (Np…Og).** Bred (Np–Fm) or accelerator-synthesized (Z≳101); fleeting half-lives, **6–37e** (below the U floor). Primarily **codex completion / prestige flex**, not core economy — the further past uranium, the *less* an element is worth.

## Isotope taxonomy

Isotopes share an element's `atomic_number` but differ in `atomic_mass`; they are created in the **Isotopic Manipulator** via the `neutron_adjustment[]` convention (points the recipe at the `neutron` ItemSO so the manipulator adds/removes neutrons). `category` = `Isotope`. Radioactive isotopes set `is_radioactive`, `decay_type`, `half_life_note` (display only — half-life is **not** time-simulated), and emit a decay particle as a `byproducts[]` entry caught by Radioactive Containment.

| Element | Isotopes (in-game) | Recipe | Notes |
|---|---|---|---|
| Hydrogen | protium / deuterium (H-2, +1n) / tritium (H-3, +2n) | `hydrogen + 1n` / `+2n` | deuterium impl (8e), tritium impl (12e, β, 12.3 yr) |
| Helium | He-3 / He-4 | He-4 impl; He-3 = fusion intermediate (design) | He-4 = alpha nucleus |
| Carbon | C-12 / C-14 (+2n, β, 5,730 yr) | `carbon + 2n` | C-14 impl, 240e |
| Uranium | U-238 / U-235 (fissile) | `uranium → U-235 + α` | U-235 ≈ 2× U = **~128e** under the convergent model (impl value 2,621,440e to be rebalanced); fissile |
| Plutonium | Pu-239 (fissile, design) | breeding | weapons/breeder fuel; base ~41e |
| Cobalt | Co-60 (design, β/γ) | `cobalt + n` | classic radioactive-source teachable; near-iron, base ~134M e |

**Isotope yield rule:** nominal `base_sell_value × 1.5` (`isotope_sell_multiplier`, per `economy-balance.md`), tuned per isotope — deuterium ≈1.6× H, C-14 = 1.5× C, U-235 = 2× U. Because the base element value now follows the convergent (iron-peaked) curve, isotopes track their parent — a near-iron isotope is valuable, a heavy-actinide isotope is cheap. Authoritative per-isotope values live in `game_data.json`.

## Entropy-yield ladder (the single "entropy generated" number)

Per the locked design, **"entropy generated" by an element = its entropy yield = `base_sell_value`** (the Maxwell's-Demon entropy sink pays `base_sell_value × qty` when an item is sunk). No separate thermodynamic-byproduct mechanic.

**Convergent model — a tall tent peaking at iron (~168M e).** Two ladders climb toward Fe-56 (the binding-energy peak) from opposite ends; iron is the single maximum and both feedstocks (hydrogen, uranium) are cheap. Iron is a **long-term apex** — the climb up either ladder is metered out over a month-plus of prestiges (see "Pacing & gating"). The fission ladder is *denser* (more elements, smaller per-step gaps) because there are far more nuclides between uranium and iron than between hydrogen and iron.

**Fusion ladder (Z1→26, climb up).** A clean per-Z doubling: `yield(Z) = 5 × 2^(Z − 1)`. This preserves the tutorial rungs (H..O) exactly and simply keeps doubling all the way to iron — every element gets a distinct rung, big ~2× steps:

```
el:  H  He  Li  Be   B    C    N    O    F    Ne ...  Si  ...  Ca   ...   Mn          Fe(apex)
Z :  1   2   3   4    5    6    7    8    9    10      14       20         25            26
e :  5  10  20  40   80  160  320  640 1280  2560   40,960  2,621,440  83,886,080  167,772,160
```
(This replaces the old thinned ladder — it removes the Al>Si inversion and makes value strictly rise with Z to iron. H..O stay pinned for tutorial balance.)

**Fission ladder (Z92→26, climb down).** A dense descending ladder anchored to the same iron peak: `yield(Z) = round( 64 × 2,621,440^((92 − Z) / 66) )` for Z ≥ 27 (the constant `2,621,440 = iron / 64`). Iron-anchored at the top (gives 167,772,160 at Z = 26), uranium floored at **64 e** (Be/B-tier — cheap feedstock), transuranics below that (floored at 1). Per-step ratio ≈ `2,621,440^(1/66) ≈ 1.25` (about +25% per Z toward iron) — still smaller gaps than the fusion side's 2×/rung, because the fission side packs ~66 rungs into the same height.

```
Z :     27        28        30        36        47       56     74    82   92  94 100 118
el:     Co        Ni        Zn        Kr        Ag       Ba      W    Pb    U  Pu  Fm  Og
e : 134,112,510 107,205,900 68,504,244 17,873,675 1,522,149 202,862 3,603 601 64  41  11   1
```

So the near-iron elements on *both* sides (Mn/Cr on fusion, Co/Ni on fission) are themselves endgame-grade millions, and the cheap feedstocks (H = 5, U = 64) bookend the tent.

**Why iron is the cap (and that's fine).** Capping the *element* layer at iron (rather than letting heavies run to billions, as the old monotonic ladder did) keeps the table a single coherent tent and makes iron a genuine summit both ladders race toward. The continuing value climb past iron lives in the **molecule → material → component** tiers, which are built from the **cheap, abundant elements** (H, C, N, O, Si, common metals) — not from iron. Iron and the near-iron high-value elements are a prestige-flex / collection / special-recipe goal, not a bulk crafting input.

> **Migration note:** the implemented elements (Si, Al, Fe, Ni…Pu) follow the OLD thinned/monotonic values in `game_data.json` and must be **re-derived from the two formulas above** — fusion `5 × 2^(Z−1)` (e.g. Si 1,280→40,960, Fe 5,120→167,772,160), fission `round(64 × 2,621,440^((92−Z)/66))` (e.g. Ni 10,240→~107M, U 1,310,720→64, Pu 2,621,440→41). H..O are unchanged. Downstream tier-3+ recipes that consume heavy elements (e.g. `uranium_hexafluoride`, `steel`/`iron_oxide`) need their cost basis and sell values reviewed — molecules/materials should be re-pointed at the cheap elements where possible, and any iron-bearing recipe re-priced above iron.

> **Balance caveat:** iron at ~168M (vs the old 5,120) is intentional but raises late-element netWorth sharply, which feeds prestige `✦` (≈log10 netWorth, `economy-balance.md` risk #4). The point is that iron is *gated* (not cheaply farmable) so netWorth rises gradually; verify the per-rung research/power/throughput gates actually meter the climb to ≈a month before authoring final numbers.

## Pacing & gating — iron is ~a month away

Iron must read as a **long-term apex**: the player should reach it only after **a month-plus of regular play and a healthy stack of prestiges + prestige upgrades**, not in the first few sessions. The ~168M e value is one half of that; the other half is *gating the climb* so the value is earned, not farmed early. Four compounding gates meter each rung toward iron (no single wall — they stack):

1. **Research depth (prestige-currency gated).** Each fusion/fission tier unlocks only a few more rungs (`fusion_i` → He–C, `fusion_ii` → through Si, … up to the transition metals → Fe). The deep tiers cost **prestige currency (✦)** and/or large entropy, so they are unaffordable until the player has prestiged repeatedly and bought into the prestige shop. The last rungs (Cr/Mn→Fe, Co/Ni→Fe) sit behind the most expensive nodes.
2. **Power (eV grid).** Heavier elements demand far more power per craft; reaching iron requires a power grid scaled up over many prestiges (generators + `PowerDiscount` managers). The near-iron rungs should out-draw anything the early grid can supply.
3. **Input volume (combinatorial).** One iron is 26 p + 30 n + 26 e, or 13 fused heliums up a 25-rung chain — every rung multiplies the upstream demand. Producing iron at any meaningful rate needs the whole lower chain running at high throughput, which only the **prestige output/speed multipliers** (managers, megastructure `GlobalProductionBonus`, Quantum Yield) make viable.
4. **Prestige-multiplier dependence.** Because the per-rung ~2× value step roughly tracks the cost step, net progress up the ladder comes from the *permanent* multipliers that survive prestige. Each prestige nudges the reachable rung up by ~1–2; reaching iron is therefore a function of accumulated prestige power, i.e. real-time played.

> **Tuning levers (to dial the ~1-month target after playtest):** raise the deep-tier research ✦ costs; steepen the final transition-metal rungs (e.g. ×3–4 per Z for Sc→Fe, pushing iron into the billions); raise near-iron power draw; or slow the per-prestige reachable-rung gain. Keep H..O untouched (tutorial). The exact curve needs an idle-sim pass — this section defines the *shape* (gated, compounding, prestige-bound), not final constants.

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
- **Yield rebalance (high priority):** all implemented element values must be re-derived from the convergent formulas — fusion `5 × 2^(Z−1)` (iron becomes the global max at **167,772,160 e**), fission `round(64 × 2,621,440^((92−Z)/66))` (U→64, Pu→41). The old thinned/monotonic `game_data.json` values are superseded. All (design) particle/isotope yields are likewise placeholders.
- **Pacing not yet simulated:** the "~1 month to iron" target depends on research ✦ costs, power draw, and prestige-multiplier curves that need an idle-sim pass (see "Pacing & gating"). The value curve is set; the *gates* are described in shape only.
- **Cross-tier reconciliation:** with iron at ~168M, tier-3+ recipes that consume heavy/iron elements (`iron_oxide`, `steel`, `uranium_hexafluoride`) need re-pricing — molecules/materials should draw on cheap abundant elements, and any genuinely iron-bearing item must sell above iron.
- **Power authoring:** Fusion/Fission Reactor `base_power_cost_ev` not yet placed on the static-draw ladder.
- **Codex-only tail:** Z≳101 (and most transuranics) are intended as codex/prestige-completion content, not core-loop economy — confirm before authoring full recipes.
- **Fission fragment modelling:** multi-output fission (one `output_item` + fragments in `byproducts[]`) needs the production system to credit byproduct items on craft — verify the existing byproduct path (used by U-235 enrichment's alpha) handles multiple/element byproducts, not just particles.
- **Maintenance:** any PR that implements part of this (new elements, reactors, fields, particles) MUST update this doc and refresh the `Verified against` stamp.
