# 02 — Crafting System

## Philosophy
Every recipe should mirror real-world science as closely as gameplay allows. The crafting tree is the backbone of the entire game.

### Simplification Rule
- **Accurate inputs, simplified structure** — recipes use the correct atomic/molecular building blocks but don't require players to understand exact molecular geometry or bonding mechanics
- Example: Glucose doesn't require modeling a ring structure — it requires the correct ratio of Carbon, Hydrogen, and Oxygen atoms (C₆H₁₂O₆)
- Example: Steel requires Iron + Carbon in correct proportions, not a full metallurgical process
- Where real science is too abstract or complex, we capture the **spirit and inputs** accurately
- Tooltips and codex entries can explain the real-world complexity for curious players without it being a gameplay requirement

## Crafting Hierarchy

```
Quarks
  └─> Protons / Neutrons / Electrons
        └─> Atoms (H, He, C, O, Fe, Si...)
        │     └─> Isotopes (Deuterium, Tritium, C-14, U-235...)
        │     └─> Ions (H⁺, O²⁻, Fe³⁺...)
        └─> Molecules (H₂O, CO₂, CH₄, C₆H₁₂O₆...)
              └─> Alloys & Compounds (Steel, Bronze, SiO₂...)
                    └─> Components (Wire, Circuits, Batteries...)
                          └─> Structures & Megastructures
```

## Isotope System

### Isotope Building
- A dedicated building that adds or removes neutrons from a base atom
- **Tradeoff:** Slower production time than standard atoms
- **Bonus:** Isotopes have higher sell value / yield more prestige currency
- Unstable (radioactive) isotopes additionally emit decay particles over time

### Isotopes as Recipe Gates
- Certain recipes **require specific isotopes** — reflecting real nuclear physics
- Example: Helium-4 synthesis requires Deuterium + Tritium fusion (proton-proton chain)
- Example: Carbon-14 unlocked via researching Carbon Dating (or vice versa — bidirectional)
- These gates create meaningful decision points: invest in isotope infrastructure or skip and find another path

### Research ↔ Isotope Relationship
- Bidirectional unlock system:
  - Researching a topic can unlock an isotope (e.g. research Nuclear Fusion → unlock Tritium)
  - Crafting an isotope can unlock a research topic (e.g. craft C-14 → unlock Carbon Dating research)
- Creates organic discovery moments that reward experimentation

## Radioactive Decay Mechanic

### Alpha & Beta Decay
- Radioactive isotopes (e.g. U-235, C-14, Tritium) passively emit **alpha particles (He-4 nuclei)** or **beta particles (electrons/positrons)** over time
- These particles are collected as a **special secondary currency** or rare crafting input
- Buildings housing radioactive isotopes function as passive generators of rare resources

### Risk / Reward
- Radioactive buildings produce valuable particles but may have:
  - Slower base production
  - Occasional "decay events" requiring player attention
  - Special containment building requirements (unlocked via research)

### Decay Particles as Currency
- Alpha/beta particles feed into a separate economy — used for late-game recipes, research acceleration, or special upgrades
- Creates a meaningful reason to invest in nuclear chemistry even after progressing past that tier

## Example Recipes (Draft)

### Subatomic → Atomic
| Output | Inputs | Real-World Basis |
|--------|--------|-----------------|
| Proton | 2 Up Quarks + 1 Down Quark | QCD quark model |
| Neutron | 1 Up Quark + 2 Down Quarks | QCD quark model |
| Hydrogen (H) | 1 Proton + 1 Electron | Simplest atom |
| Helium-4 (He) | 2 Protons + 2 Neutrons + 2 Electrons | Standard alpha particle |

### Isotopes (via Isotope Building)
| Output | Base Atom | Neutrons Added | Notes |
|--------|-----------|---------------|-------|
| Deuterium (H-2) | Hydrogen | +1 | Stable, used in fusion |
| Tritium (H-3) | Hydrogen | +2 | Radioactive, beta decay |
| Carbon-14 (C-14) | Carbon | +2 | Radioactive, unlocks Carbon Dating research |
| Uranium-235 (U-235) | Uranium | — | Fissile, alpha decay, rare currency source |

### Fusion Gates (Isotope-Required Recipes)
| Output | Inputs | Real-World Basis |
|--------|--------|-----------------|
| Helium-4 (fusion path) | Deuterium + Tritium | Thermonuclear fusion reaction |
| Neutron (fusion byproduct) | Deuterium + Tritium | D-T fusion emits high-energy neutron |

### Ions (Chemistry Tier Only)
| Output | Base Atom | Change | Real-World Use |
|--------|-----------|--------|---------------|
| H⁺ | Hydrogen | -1 Electron | Acids, proton chemistry |
| O²⁻ | Oxygen | +2 Electrons | Oxide formation |
| Fe³⁺ | Iron | -3 Electrons | Rust, biological transport |
| Na⁺ | Sodium | -1 Electron | Salts, electrolytes |
| Cl⁻ | Chlorine | +1 Electron | Salts, acids |

### Molecular (Ion-Enabled)
| Output | Inputs | Real-World Basis |
|--------|--------|-----------------|
| Water (H₂O) | 2× H + 1× O | Covalent bond |
| Hydrochloric Acid (HCl) | H⁺ + Cl⁻ | Ionic bond, strong acid |
| Salt (NaCl) | Na⁺ + Cl⁻ | Ionic bond |
| Glucose (C₆H₁₂O₆) | 6× C + 12× H + 6× O | Accurate atomic inputs, simplified structure |

### Alloys & Materials
| Output | Inputs | Real-World Basis |
|--------|--------|-----------------|
| Steel | Iron + Carbon | Iron-carbon alloy |
| Bronze | Copper + Tin | Historical alloy |
| Glass | Silicon + Oxygen (SiO₂) | Silica glass |
| Silicon Wafer | Silicon (refined) | Semiconductor base |

## Crafting Mechanics
- **Manual craft:** Tap to combine inputs one at a time
- **Queue crafting:** Hold to queue multiple outputs
- **Building craft:** Buildings handle recipes automatically at set rates

## Unlocking Recipes

### First Run
- Recipes are discovered by crafting or reaching the required tier
- Unknown recipes are hidden until the unlock condition is met
- On first unlock, a codex entry is created and an educational tooltip is shown

### Prestige Runs
- All previously discovered recipes are **known but locked** at the start of each run
- Known recipes are visible in menus and the codex — players can plan their full build path before starting
- Recipes must still be **re-unlocked through play** (crafting the item, reaching the tier, or completing the research)
- Due to permanent multipliers, re-unlocking progresses significantly faster each run
- This preserves the satisfaction of progression while eliminating the frustration of not knowing what's ahead

### Unlock Conditions
| Method | Example |
|--------|---------|
| Craft the item | Craft Hydrogen → unlocks Hydrogen recipe permanently |
| Reach a tier | Entering Molecular tier reveals all molecular recipe slots |
| Complete research | Research Carbon Dating → unlocks C-14 recipe |
| Craft an isotope | First craft of C-14 → unlocks Carbon Dating research |
| Building placement | Place Isotope Building → reveals isotope recipe slots |

## Notes / Open Questions
- [ ] Should recipes ever be simplified for mobile UX vs strict accuracy?
- [ ] How many items total in the crafting tree at launch?
- [ ] Will there be "exotic" or fictional late-game materials?
