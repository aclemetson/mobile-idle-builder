# 0001: Run the factory simulation in DOTS/ECS, everything else in MonoBehaviours

**Status:** Accepted

## Context

An automation game's cost grows with the factory: every building crafts on a timer, every belt tile moves items, and a power grid connects them. A late-game save holds hundreds of buildings and belt segments, and it has to tick at a steady frame rate on mid-range Android phones without draining the battery. Classic MonoBehaviour `Update()` per object scales poorly here: managed allocations, scattered memory and one virtual call per object per frame.

The rest of the game (UI, platform SDKs, cloud save, ads, IAP, Remote Config) is built around managed APIs and MonoBehaviour lifecycles, and ECS gains nothing there.

## Decision

- The simulation is pure ECS (`Assets/Scripts/Systems/`, `Components/`): production, harvesting, conveyors, splitters, filters, the power grid, the entropy sink, net worth, prestige and the tutorial state machine. Hot systems are Burst-compiled `ISystem`s.
- Content is baked into entities from ScriptableObjects in a SubScene (`Assets/Scripts/Authoring/`).
- Everything else stays in MonoBehaviour or static services.
- Exactly one class, `ECSLoadBridge`, moves state between `SaveData` and ECS singletons. Managed code waits for `ECSLoadBridge.IsLoaded` before it touches ECS.

## Consequences

- Production, belts and power scale to large factories with Burst-compiled, cache-friendly loops.
- The pure logic tests well: EditMode tests build a small `World`, tick systems by hand and assert on components, with no scene and no Play mode.
- Two worlds means initialization races. A MonoBehaviour that reads a singleton before the SubScene bakes gets nothing, and a save written mid-load once wiped progress. The fixes became rules: the `IsLoaded` gate, the `SaveApplied` flush guard, and `base.Awake()` in every singleton override (see `CLAUDE.md`).
- The offline-earnings path can't run ECS while the app is closed. `IdleGraphAnalyzer` re-derives the factory's steady-state flow from a saved snapshot instead, and it has to stay in step with the live systems. Tests cover both.
