# 0005: A single gameplay scene, with every menu as a HUD panel

**Status:** Accepted

## Context

The original flow had a MainMenu scene with Play, Shop, Settings, Achievements and Leaderboard buttons before the game scene. For an idle game this was friction: players open the app to check on their factory, not to choose from a menu. Each scene change also tore down and rebuilt the ECS SubScene and the HUD, which multiplied the initialization races described in [0001](0001-dots-ecs-simulation.md).

## Decision

- Boot is `SplashScene` (auth, cloud-save reconciliation, maintenance and update checks), then a `LoadingScreen` overlay, then `GameScene`, where the player stays.
- Every menu (shop, settings, research, prestige, achievements, worlds, statistics) is a panel in one UI Toolkit document, `Assets/UI/GameHUD.uxml`. It's driven by `HUDController` and per-panel sub-controllers on the same GameObject.
- Switching sites or worlds swaps grid state inside the scene instead of loading a new one. Prestige is the one deliberate exception: it reloads `GameScene` so the run restarts from a clean state.

## Consequences

- Launch to playing takes one step, and there's only one place where ECS and the UI initialize.
- `GameHUD.uxml` is large. Changes have to go through the documented sub-controller pattern (see `docs/agents/ui-toolkit.md`), and contributors must check that a UXML file is actually loaded at runtime before editing it.
- The prestige reload has to clear state that outlives the scene in the default ECS world, such as belts, their items and the Demon. Tests pin that down.
