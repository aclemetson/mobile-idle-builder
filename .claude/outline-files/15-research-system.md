# 15 — Research System

## Overview
The research system is a **branching tree** that gates content, unlocks isotopes/ions, enables grid expansion, and controls access to megastructures. It is the primary pacing layer on top of the crafting system and a major source of build-to-build variation across prestige runs.

## Core Rules
- **Research unlocks isotopes and ions** — isotopes/ions do NOT unlock research
- **Research gates research** — nodes in the tree require prerequisite research nodes to be completed first
- **Research gates content** — grid expansions, megastructures, and advanced buildings require specific research
- Players choose which branches to invest in — creating different playstyle builds each run

## Research Flow
```
Base Research (always available)
  └─> Chemistry Branch
  │     └─> Ions (unlocks ion crafting)
  │           └─> Acid Chemistry (unlocks HCl, H₂SO₄ recipes)
  │                 └─> Organic Chemistry (unlocks glucose, amino acids)
  │
  └─> Nuclear Branch
  │     └─> Isotopes (unlocks Isotope Building)
  │           └─> Radioactive Isotopes (unlocks U-235, C-14, decay mechanic)
  │                 └─> Nuclear Fusion (unlocks Deuterium + Tritium → He-4 recipe)
  │                       └─> Antimatter (unlocks late-game power buildings)
  │
  └─> Materials Branch
  │     └─> Metallurgy (unlocks alloy recipes)
  │           └─> Semiconductors (unlocks silicon wafer, processor recipes)
  │                 └─> Nanotechnology (unlocks late-game component recipes)
  │
  └─> Engineering Branch
  │     └─> Automation Tier 2 (unlocks Assembler, Fabricator)
  │           └─> Grid Expansion I (unlocks first expansion purchase)
  │                 └─> Grid Expansion II
  │                       └─> ...
  │                             └─> Dyson Swarm (unlocks swarm node building)
  │                                   └─> Dyson Sphere (unlocks sphere segments — major milestone)
  │
  └─> Astrophysics Branch
        └─> Star System Mapping (unlocks first additional star system grid)
              └─> Interstellar Network (unlocks Star System Hub building)
```

## Research Mechanics

### Research Building (Lab)
- Research progress is driven by the **Research Lab** core building
- Higher level Lab = faster research speed
- Persistent upgrade: second parallel research slot (allows two simultaneous researches)
- Research is continuous — runs in background while building

### Research Cost
- Paid in **base currency** — creates meaningful tradeoff vs spending on buildings
- Some late-game research also costs **prestige currency** — major investment decisions
- Cost scales with branch depth — early nodes cheap, megastructure nodes expensive

### Prestige Behavior
- Research progress **fully resets on prestige** — tree must be re-unlocked each run
- Previously completed research nodes are **visible and known** but locked — player can plan their research path before starting
- Permanent upgrade available: "Research Memory" — reduces cost of already-completed nodes by X% on subsequent runs

## Research Panel UI
- Branching tree visualized as a node graph in the Research Panel
- Nodes show: name, cost, prerequisites, what they unlock
- Completed nodes: fully lit
- Available nodes: highlighted, purchasable
- Locked nodes: dimmed, prerequisites shown on tap
- Active research: animated progress ring around node

## Notes / Open Questions
- [ ] Define exact research costs per node tier
- [ ] Define "Research Memory" permanent upgrade % reduction
- [ ] Are any research branches mutually exclusive per run, or can all be completed given enough time?
- [ ] Does research persist in PVP competition runs or reset like a normal prestige?
