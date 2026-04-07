# 01 — Game Design

## Core Concept
A bottom-up crafting game blending **active** and **idle** mechanics. Players start from the most fundamental building blocks of matter — subatomic particles — and work their way up through atoms, molecules, alloys, and beyond. The game is educational by design, with crafting steps reflecting real-world science as closely as possible.

Early game is **actively engaging** (tutorial-driven, hands-on crafting). Late game shifts toward **idle automation**, where players focus on pipeline optimization. An **infinite prestige/reset system** allows players to loop back to the beginning with permanent bonuses, making each run faster and more powerful than the last.

## Gameplay Loop
1. **Manual crafting** — tap/interact to produce basic particles
2. **Unlock buildings** — automate production steps
3. **Chain automation** — connect buildings into pipelines
4. **Unlock new tiers** — atoms → molecules → alloys → advanced materials
5. **Scale up** — optimize, expand, accumulate net worth
6. **Prestige** — reset the build, earn prestige currency, start a faster and stronger run

## Prestige System
The prestige loop is **infinite** — there is no cap on resets. Each prestige makes subsequent runs faster, and accumulated permanent bonuses make PVP increasingly competitive.

### On Prestige, the player keeps:
- **Recipe knowledge** — all discovered crafting recipes remain unlocked
- **Permanent multipliers** — bought with prestige currency, survive all resets forever
- **Prestige currency balance** — carries over and grows each run

### On Prestige, the player loses:
- **All buildings** — must be rebuilt using base currency (faster each run due to knowledge + multipliers)
- **Non-persistent multipliers** — must be re-unlocked each playthrough, creating a satisfying "spin-up" phase
- **Inventory / stockpiles** — all materials reset

### Prestige Currency
- Earned based on the **net worth of your build** at the moment of prestige
- Larger, more optimized builds yield more prestige currency
- Spent on **permanent multipliers** (production speed, output quantity, building cost reduction, etc.)
- Incentivizes optimizing each run before resetting rather than resetting immediately

### Non-Persistent Multipliers
- Unlocked each playthrough via normal progression
- Examples: conveyor speed boost, crafter efficiency, storage capacity
- Get faster to re-unlock each run due to permanent multiplier stack
- Give early/mid game a sense of purpose even on run 50+

## Progression Tiers
| # | Tier | Examples |
|---|------|---------|
| 1 | Subatomic | Quarks, electrons, protons, neutrons |
| 2 | Atomic | Hydrogen, Helium, Carbon, Iron, Silicon... |
| 3 | Molecular | H₂O, CO₂, O₂, organic compounds |
| 4 | Alloys & Materials | Steel, bronze, glass, semiconductors |
| 5 | Basic Components | Copper wire, gears, basic circuits, batteries |
| 6 | Advanced Components | Embedded circuits, processors, solar cells |
| 7 | Structures | Satellites, space stations, orbital platforms |
| 8 | Megastructures | Dyson swarms, Dyson spheres, ringworlds |

### Notes on Megastructure Tier
- Megastructures are gated behind the **Research system** (see 15-research-system.md)
- Players cannot build multiple Dyson swarms/spheres until they've researched additional star systems
- Each researched system unlocks a new build space and scales prestige net worth significantly
- This tier is the primary long-term PVP differentiator — who has the most efficient Dyson network

## Tutorial Flow

### First Run
- Player spawns with nothing, fully guided through manual crafting
- Step 1: Combine quarks → proton / neutron
- Step 2: Combine proton + electron → Hydrogen atom
- Step 3: First building unlocked — **Basic Combiner**
- Tutorial continues nudging player through early automation until the first prestige wall
- At the prestige wall, progression to the next tier feels **intentionally slow / nearly impossible**
- The game surfaces the **Prestige prompt** as the solution — framed as a positive, exciting choice
- Player prestiges for the first time, earns prestige currency, and is directed on how to spend it
- First prestige effectively acts as the **second half of the tutorial** — teaching the loop

### Prestige Runs
- Tutorial does **not** auto-play on repeat runs
- Tutorial remains **accessible on demand** (e.g. help menu / "?" button) for players who forget mechanics
- Early stages (e.g. neutron production) become **nearly negligible** in time due to permanent multipliers
- Each prestige wall sits one step further than the last — players always feel they're making meaningful new progress before hitting the next ceiling
- The cycle of "optimize → hit wall → prestige → go further" is the core retention loop

### Prestige Wall Design
- Each wall should feel **genuinely slow without a reset** — not artificially padded, but a real resource/time gap
- The gap shrinks with each prestige but never fully disappears — there is always a next wall
- Wall placement aligns with tier transitions (e.g. Molecular → Alloys is the first wall, Alloys → Components is the second, etc.)

## Educational Design Goals

### Philosophy
Education is **inherent to the crafting system** — players learn by doing, not by reading. Crafting hydrogen teaches you that it's one proton and one electron. Discovering isotopes teaches you what changes when you add a neutron. The science is the gameplay.

### How It Works
- Recipes reflect real-world science as closely as gameplay allows
- Players naturally absorb atomic structure, chemistry, and physics through repetition and progression
- No explicit "lessons" — knowledge is gained through play

### Codex
- Every item, recipe, and concept unlocked in-game is logged in an in-game **Codex**
- Codex entries include:
  - What the item is in real life
  - Its real-world properties and uses
  - How it relates to adjacent items in the crafting tree
  - Fun facts (e.g. isotope variations, real-world abundance)
- Codex is **discovery-gated** — entries only appear after the player has crafted or unlocked the item
- Acts as both a **reference tool** and a **trophy/progress tracker**

### Example Codex Entries
| Item | Codex Content |
|------|--------------|
| Hydrogen (H) | Simplest element, 1 proton + 1 electron. Most abundant element in the universe. |
| Deuterium | Isotope of Hydrogen with 1 neutron added. Used in nuclear fusion research. |
| Tritium | Isotope of Hydrogen with 2 neutrons. Radioactive, used in thermonuclear weapons. |
| Hydrogen Ion (H⁺) | Hydrogen atom with electron removed — essentially a bare proton. Forms acids in water. |

### Scope
- Educational depth scales with tier — subatomic/atomic tiers are richest in science content
- Later tiers (components, megastructures) lean more into engineering and sci-fi concepts
- Marketed as a value-add, not the primary selling point — players discover it naturally

## Notes / Open Questions
- [ ] How complex should late-game pipelines get on a small screen?
- [ ] Narrative and dialogue details — see 16-narrative-dialogue.md
