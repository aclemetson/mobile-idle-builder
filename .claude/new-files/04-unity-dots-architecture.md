# 04 — Unity DOTS Architecture

## Overview
This project uses Unity DOTS as the primary simulation layer. The goal is to handle large numbers of entities (particles, buildings, conveyors, power nodes) efficiently on mobile hardware. An AI builder should be able to implement most systems from this document with minimal guidance.

## Why DOTS?
- **ECS:** Efficient simulation of thousands of simultaneous entities (items in transit, buildings processing, power calculations)
- **Burst Compiler:** High-performance C# jobs compiled to native code — critical for mobile performance
- **Job System:** Multithreaded processing keeps the main thread free for UI and input

## Unity Version & Package Targets
- **Unity LTS** (latest stable at build time)
- **Entities package** (DOTS 1.x stable)
- **Unity Physics** (if collision/overlap detection needed for power radius)
- **Universal Render Pipeline (URP)** — mobile optimized
- **UI Toolkit** — for all UI (not uGUI)

## Architectural Rules
- All game simulation runs through **ECS Systems** — no simulation logic in MonoBehaviours
- MonoBehaviours are permitted **only** for: input handling, audio triggers, and UI Toolkit bridge
- All game data lives in **ScriptableObjects** (ItemSO, RecipeSO, BuildingSO) — no magic strings or hardcoded values
- Systems communicate via **ECS components and shared state** — not direct system references

## Project Structure
```
Assets/
  Scripts/
    Components/         # All IComponentData structs
    Systems/            # All SystemBase / ISystem implementations
    Authoring/          # Baker MonoBehaviours for scene authoring
    ScriptableObjects/  # ItemSO, RecipeSO, BuildingSO, TierSO, etc.
    UI/                 # UI Toolkit documents and controllers
    Audio/              # Audio manager (MonoBehaviour)
    Input/              # Input System handlers (MonoBehaviour)
  Prefabs/
    Buildings/
    Items/
    Power/
  Art/
  Audio/
  Settings/             # URP settings, Input actions
```

## Core ECS Components
```csharp
// Item being transported or stored
struct ItemData : IComponentData {
    public int ItemID;
    public int Quantity;
    public int TierLevel;
}

// Building state
struct BuildingData : IComponentData {
    public int BuildingType;
    public int UpgradeLevel;
    public float ProductionSpeed;
    public bool IsActive;         // false if unpowered
}

// Conveyor link between two grid positions
struct ConveyorData : IComponentData {
    public int2 Source;
    public int2 Destination;
    public float Speed;
    public int CarriedItemID;
}

// Power node
struct PowerNodeData : IComponentData {
    public float MaxEV;
    public float CurrentEV;
    public float InfluenceRadius;
    public float LinkRadius;
    public bool IsGridLinked;
}

// Recipe being processed
struct RecipeProcessData : IComponentData {
    public int RecipeID;
    public float Progress;        // 0.0 - 1.0
    public bool InputsSatisfied;
}
```

## Core ECS Systems
| System | Responsibility | Runs On |
|--------|---------------|---------|
| `CraftingSystem` | Processes recipes, checks inputs, produces outputs | Simulation group |
| `ConveyorSystem` | Moves items between buildings along grid paths | Simulation group |
| `PowerSystem` | Calculates eV coverage per grid tile, marks buildings active/inactive | Simulation group |
| `EnergyGridSystem` | Links power nodes, balances shared eV across grid | Simulation group |
| `IdleProgressSystem` | Calculates offline production when app is backgrounded | Startup |
| `PrestigeSystem` | Handles net worth calculation and state reset on prestige | Event-driven |
| `ResearchSystem` | Tracks research progress, fires unlock events | Simulation group |
| `UIBridgeSystem` | Reads ECS state and pushes to UI Toolkit layer | Presentation group |

## DOTS vs MonoBehaviour Boundaries
| Layer | Technology | Reason |
|-------|-----------|--------|
| Game simulation | ECS + Jobs + Burst | Performance — many entities |
| Power radius overlap | Unity Physics (ECS) | Spatial queries on grid |
| UI | UI Toolkit (MonoBehaviour bridge) | UI Toolkit not DOTS-native |
| Input | Input System (MonoBehaviour) | Standard mobile input |
| Audio | AudioSource (MonoBehaviour) | Unity audio not DOTS-native |
| Save/Load | MonoBehaviour + JSON | Simplicity, not perf-critical |

## Performance Targets
- 60fps on mid-range mobile (2021+ devices)
- 30fps minimum on low-end mobile
- Max entity count per scene: TBD based on profiling
- Power radius queries batched — not per-frame per-building

## Notes / Open Questions
- [ ] Confirm Entities 1.x version at build start
- [ ] Determine if Unity Physics needed or if power radius can use simpler distance math
- [ ] Define max grid size to set entity count budgets
