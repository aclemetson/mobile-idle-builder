# 03 — Buildings & Automation

## Overview
Buildings are the core automation mechanic. Players unlock them progressively to remove manual tapping and build scalable production pipelines — similar to Shapez 2 and Dyson Sphere Program.

## Build Map
- **Square grid layout** — buildings and conveyors snap to grid tiles
- Optimized for mobile touch controls — tap to place, drag to route conveyors
- Layout optimization is a core gameplay loop — players rearrange and rebuild for efficiency

### Grid Expansion
- Grid starts small each run — forces tight, intentional planning from the start
- Expansions are **unlocked via research** then **purchased with base currency**
- Expansion slots must be planned for — spending currency on expansion is a meaningful tradeoff vs spending on buildings/upgrades
- Research gates control when expansions become available, pacing the build naturally
- **Dyson Sphere** is the final research-gated expansion unlock — a major milestone
- After the Dyson Sphere, further expansions are purely currency-purchased with **escalating costs** — infinite scaling with natural friction
- Each new **star system** (researched) provides a fresh grid to build on in addition to expanding the current one

## Building Classifications

### Core Buildings — Always Present, Upgradeable
These buildings must exist on every map and can never be removed — only upgraded. They form the skeleton of every build and persist as essential infrastructure across all tiers.

| Building | Function | Upgrade Path |
|----------|----------|-------------|
| **Resource Vault** | Stores all materials, acts as central inventory | Capacity, access speed |
| **Production Hub** | Central node that links conveyor belts into a unified network | Throughput, multi-network support |
| **Research Lab** | Drives all research progress | Speed, parallel research slots |
| **Radioactive Containment** | Required to house any radioactive isotope building | Capacity, decay particle collection rate |

### Transient Buildings — Tier-Relevant, Can Become Obsolete
These buildings are essential during specific tiers but may be abstracted or replaced as the player progresses and multipliers reduce their time cost to negligible levels.

| Building | Relevant Tier | Becomes Obsolete When... |
|----------|--------------|--------------------------|
| **Basic Combiner** | Subatomic → Atomic | Replaced by Assembler |
| **Isotope Building** | Atomic → Molecular | Permanent multipliers reduce isotope time to near-instant |
| **Collector** | Early game | Replaced by automated Production Hub links |
| **Assembler** | Molecular → Alloys | Replaced by Fabricator |
| **Reactor** | Alloys → Components | Absorbed into advanced fabrication chain |
| **Quantum Lab** | Late game research | Merged into upgraded Research Lab |

### Building Tiers

#### Tier 1 — Basic
| Building | Function |
|----------|----------|
| Basic Combiner | Combines 2 inputs into 1 output |
| Collector | Gathers raw particles passively over time |
| Storage Unit | Local buffer (feeds into Resource Vault) |

#### Tier 2 — Intermediate
| Building | Function |
|----------|----------|
| Assembler | Handles multi-input recipes |
| Isotope Building | Adds/removes neutrons to produce isotopes — time tradeoff for bonus value |
| Splitter | Divides output into multiple streams |
| Conveyor | Moves materials between buildings on the grid |

#### Tier 3 — Advanced
| Building | Function |
|----------|----------|
| Fabricator | High-speed multi-recipe production |
| Reactor | Atomic/molecular synthesis at scale |
| Containment Unit | Houses radioactive isotopes, collects decay particles |
| Dyson Swarm Node | Megastructure component — produces large-scale energy currency |

#### Tier 4 — Megastructure
| Building | Function |
|----------|----------|
| Dyson Sphere Segment | Assembled from swarm nodes — ultimate production multiplier |
| Star System Hub | Connects multiple grid maps across researched star systems |

## Automation Pipeline Design
- Buildings connect via **conveyors** or **wireless links** (later tier)
- Each building has **input/output slots**
- Players arrange buildings in a 2D grid or free-placement layout (TBD)

## Upgrade System

### Two Upgrade Tracks

#### Persistent Upgrades (Prestige Currency — Main Menu)
- Purchased from the **main menu before entering a build**
- Bought with **prestige currency** earned from previous runs
- Survive all prestiges permanently
- Should feel **impactful and rewarding** — these are the milestones players work toward
- Examples:
  - Vault capacity permanently increased by 50%
  - Research Lab gains a second parallel research slot
  - Containment building passively collects 2× decay particles
  - All Tier 1 buildings start pre-placed on new runs
  - Conveyor speed permanently +25%

#### Non-Persistent Upgrades (Base Currency — In-Build)
- Purchased **during a run** using base currency
- Reset on prestige
- Fairly basic — incremental improvements to get through the current run
- Examples:
  - +10% combiner speed
  - +1 storage buffer slot
  - Conveyor routing priority setting
  - Slight reduction in isotope build time

### Upgrade UX
- Persistent upgrades browsed and purchased in a dedicated **main menu upgrade screen**
- Non-persistent upgrades accessed by **tapping a building** on the grid
- Both tracks visible to the player at all times — persistent upgrades shown greyed out in-build with a note that they're purchased from the main menu

## Power System

### Overview
Buildings require power to operate. Power is measured in **electron volts (eV)** — keeping the atomic theme consistent throughout. Managing power coverage is a spatial planning challenge layered on top of production optimization.

### Power Buildings
- Dedicated buildings that generate eV and broadcast it within a **radius of influence**
- Buildings within the radius draw power automatically — no manual wiring
- Each power building has a **max eV rating** — determines how many buildings it can sustain simultaneously
- If a building falls outside all power radii, it goes offline until power coverage is extended

### Energy Grid
- Power buildings can **link to adjacent power buildings** at a slightly larger radius than their influence zone
- Linked buildings form an **energy grid** — power is shared and balanced across all connected nodes
- Allows players to chain power buildings across the map rather than clustering production near a single source
- Grid linking radius is larger than influence radius — encourages deliberate power infrastructure layout

### Power Building Progression
| Building | Output | Notes |
|----------|--------|-------|
| Basic Generator | Low eV | Early game, small radius |
| Fusion Reactor | Medium eV | Requires Deuterium/Tritium input |
| Antimatter Plant | High eV | Late game, large radius |
| Dyson Swarm Node | Very High eV | Megastructure tier, map-wide influence |
| Dyson Sphere | Effectively unlimited eV | End-game power source, powers entire star system grid |

### Design Notes
- Power planning should feel like a meaningful constraint, not a chore — the radius system makes it visual and spatial
- Dyson Sphere transitions power from a constraint to an abundance — a deliberate late-game reward
- Power buildings count toward prestige net worth
