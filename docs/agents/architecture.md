# Architecture Map (always read this)

**Scope:** Structural map of the codebase — scenes, layers, data flow, init order, what not to touch.

> Verified against: `deea0a4`, 2026-06-11; Time section added 2026-10-22 (power grid + World/Site layer rows refreshed `3c264f2`, 2026-07-06); EntropyWallet rows 2026-10-31 (#279); ConveyorSystem tail drawing 2026-11-01 (#223); Belt Splitter 2026-11-07. If code contradicts this doc, trust the code and update this doc.

## Scene & boot flow

```
SplashScene  ──►  LoadingScreen (overlay scene)  ──►  GameScene
  auth + cloud      SceneLoader transition             single gameplay scene
  reconciliation                                       contains ECS SubScene (TestSubScene)
```

1. `SplashScene`: persistent singletons awake (`SaveManager`, `SettingsService`); UGS auth + cloud save reconciliation; then `SceneLoader.GoTo("GameScene")`. `PremiumShopService` and `IAPService` are not scene-placed — they are created at startup by `PremiumServicesBootstrap` (`[RuntimeInitializeOnLoadMethod]`, `Assets/Scripts/Bootstrap/`).
2. `GameScene`: ECS world spawns; SubScene bakes `Assets/Scripts/Authoring/*` into entities; `ECSLoadBridge` polls for ECS singletons (5s world timeout + 10s entity timeout), then applies `SaveData` → ECS and calls `GridSaveService.Instance.LoadGrid()`. Only after `ECSLoadBridge.IsLoaded == true` is gameplay state valid.
3. There is no MainMenu scene. All menus (shop, settings, prestige, etc.) are panels inside the single `Assets/UI/GameHUD.uxml`.

## Three layers

### ECS simulation (`Assets/Scripts/Systems/`, all in `SimulationSystemGroup`)

| System | Role |
|---|---|
| `ProductionSystem.cs` | Per-building craft loop: check inputs (local `BuildingInputSlot` buffer, falls back to global inventory), advance `Progress += dt × BuildingData.ProductionSpeed`, consume inputs, deposit to `BuildingOutputSlot`. Burst-compiled `ISystem`. |
| `CollectorSystem.cs` | Field harvesters: produce items at fixed rate, no inputs. |
| `ConveyorSystem.cs` | Moves items building output slot → adjacent input slot, respecting ports/direction. Input hand-off rules live in `Components/InputAcceptance.cs`: the input cap is **per item type**, a set recipe refuses items it does not use, and a mixed belt whose front item is jamming a needed item sells that front item at the surplus price instead (#200, #312; `Services/SurplusSale.cs`, which also sells inputs a newly set recipe no longer uses). A belt's tail feeds any adjacent input port facing it, ranked by `TailInputPriority` (the input its `ExitDir` points at first, Maxwell's Demon last, #275; `ConveyorSystem.TryPickInput`). `ConveyorVisualizer` draws the tail exiting toward the best-ranked input with the same ranking (`Grid/TailDelivery.cs`, #223), so picture and simulation agree. |
| `FilterSystem.cs` | Filter buildings (#321): moves input items to the output buffer only when some side takes them (`Gameplay/PortFilterRules.cs`); `ConveyorSystem` then lets each output belt pull only its own items. Offline, `IdleGraphAnalyzer` treats Filters as see-through. |
| `SplitterSystem.cs` | Belt Splitters (`is_splitter`, `splitter_2` / `splitter_3`): moves every input item to the output buffer; `ConveyorSystem.PullFromSplitter` then deals one item per tick to the ready output belts in turn (`Gameplay/SplitterRotation.cs`; a missing or full branch is skipped). Offline, `IdleGraphAnalyzer` splits the producer's flow evenly over the splitter's belts. |
| `PowerGridSystem.cs` | Proximity power grid (single shared eV pool): connects consumers within a generator's `link_radius_tiles` (range-limited connections, relayed through **Power Relay** buildings), computes global supply/draw/throttle, writes `PowerStatus` + `PowerGridState`. Runs `[UpdateBefore]` `ProductionSystem`, which throttles craft progress by `PowerStatus.ThrottleRatio`. |
| `EntropySinkSystem.cs` | Sink building consumes items → grants entropy (base currency). |
| `NetWorthSystem.cs` | Aggregates net worth (inventory + currency + spent); drives prestige wall. |
| `PrestigeSystem.cs` | Wall detection + prestige execution: currency formula, run reset, destroys buildings + every belt segment (and its item) and empties the kept Demon, since the default world outlives the scene reload; service resets, forced save. |
| `TutorialSystem.cs` | Advances `TutorialStateData` through `TutorialFlowSO.Current.steps` conditions. |
| `PVPSystem.cs` | 48h competition run state. |
| `ProductionAchievementBridge.cs` | Forwards ECS production events to `AchievementService.Notify*()`. |

### MonoBehaviour services (singletons, base class `Assets/Scripts/Bootstrap/SingletonMonoBehaviour.cs`)

| Service | Location | Role |
|---|---|---|
| `SaveManager` | `Assets/Scripts/SaveSystem/` | Orchestrates local + cloud save; 60s autosave; calls `ECSLoadBridge.FlushToSave()` before writing. |
| `ECSLoadBridge` | `Assets/Scripts/SaveSystem/` | THE bridge between SaveData and ECS singletons. See `save-system.md`. |
| `GridSaveService` | `Assets/Scripts/SaveSystem/` | Saves/restores grid buildings/conveyors/fields; rebuilds idle snapshot. |
| `ResearchService` | `Assets/Scripts/Services/` | Research purchase: prereqs + `EntropyWallet.TrySpend` (`ResearchService.Purchase`) via ECS; fires `OnResearchUnlocked`. |
| `EntropyWallet` | `Assets/Scripts/Services/` | Static. THE entropy spend path (#279): `Balance`, `TrySpend` (all-or-nothing, every pre-checked purchase), `SpendUpTo` (clamp; only for building placement, which charges after placing), `ReducedCost` (prestige `CostReduction` price for display, charge and demolish refund). Works on the caller's `PlayerProgressData` query. Never hand-roll `BaseCurrency -=`. |
| `PersistentUpgradeService` | `Assets/Scripts/Services/` | Prestige-shop permanent upgrades. Catalogue is the hardcoded `UpgradeDef[] All` array (`PersistentUpgradeService.cs:48`), NOT data-driven from JSON. |
| `PremiumShopService` / `PremiumShopCalculator` / `IAPService` | `Assets/Scripts/Services/` | Crystal IAP (4 consumable packs in `IAPService.CrystalAmounts`), speed boosts (`speedBoostExpiryUtc`), entropy/PC purchases. |
| `RecipeKnowledgeService` | `Assets/Scripts/Services/` | Recipes unlocked across runs (survives prestige). |
| `SiteService` / `WorldService` | `Assets/Scripts/Services/` | Multi-map layer. `SiteService` unlocks/switches build sites (single writer of `activeSiteIndex`). `WorldService` is the Track/World layer above it (Physics/Chemistry/Biology): self-bootstrapped (`[RuntimeInitializeOnLoadMethod]`, no scene placement), economic unlock (entropy + prereq unlocks), delegates grid handoff to `SiteService.SwitchTo`. Only ONE world's ONE site is live ECS; the rest run on idle snapshots. See `chemistry-biology.md`. |
| `AchievementService` | `Assets/Scripts/Achievements/` | `Notify*()` hooks + daily/weekly/monthly UTC period resets (`CheckPeriodResets()`). Forwards 5 of its `Notify*` hooks to `DailyEventService`. **Dormant until the first prestige:** `Evaluate()` returns early while `tutorial.hasCompletedFirstRun` is false, so during the tutorial nothing accrues, completes, toasts, or reaches save. `HUDController` flips the gate and calls `InitPostPrestige()` when the first prestige lands mid-session. Gate the whole feature here, not just the menu — a menu-only gate still let unlock toasts fire over the tutorial. |
| `DailyEventService` | `Assets/Scripts/Services/` | 28-day login reward calendar + 3 rotating daily challenges; UTC reset mirroring `CheckPeriodResets`. Reads `DailyContentSO` (Resources). Grants via `paidCurrency` / `ECSLoadBridge.AddEntropy` / `AddPrestigeCurrency`. State survives prestige. |
| `OfflineCollectionService` | `Assets/Scripts/IdleCollection/` | Static: computes offline earnings from `SaveData.idleSnapshot`, capped by `GetEffectiveIdleCap()`. |
| `ManualCraftService` | `Assets/Scripts/Gameplay/` | Recipes-panel crafting. `TryEnqueue` takes inputs and queues x1/x5/x10/max crafts (`currentRun.craftQueue`, see `save-system.md`); each item takes the recipe's `base_craft_time` divided by the fastest placed building that makes it. A building-only recipe (`crafted_in` without `inventory_menu`) needs one of those buildings placed; its current recipe and busy state do not matter (#309). UI: `CraftQueueView` strip above `recipe-list`. |
| `ToastService`, `SettingsService` | `Assets/Scripts/Services/` | UI toasts; user prefs to `settings.json`. |

### UI (`Assets/Scripts/UI/`, UI Toolkit)

`HUDController` (on a GameObject in GameScene with a `UIDocument` → `GameHUD.uxml`) owns everything; sub-controllers are **MonoBehaviour components on the same GameObject**, fetched via `GetComponent<>()` (`HUDController.cs:106-111`). See `ui-toolkit.md` before any UI work.

The Research panel shows `ResearchTreeView` (a code-built `VisualElement` inserted after `research-list`; #337), or the card list; a header button switches between them (`PlayerPrefs` `research_view_mode`, tree by default). Both work during the tutorial: the tree opens on and selects the research the current step asks for (`HUDController.TutorialResearchTargetId`), and the list still hides research beyond the immediate frontier. The tree reuses `HUDController.BuildResearchCard` for the selected node, so Start, timer and crystal skip behave the same in both views. Layout: `ResearchSO.treeX`/`treeY` from the research tree editor, else `ResearchTreeView.Layout` (blocks left to right, prerequisite-depth columns). Painter2D colours go through `.linear` (the project renders in linear colour space).

## Data flow

```
Assets/Data/game_data.json ──(Editor menu: MobileIdleBuilder > Import Game Data)──► SO assets
        SOs ──(Authoring bakers in SubScene)──► ECS components/entities
SaveData (save.json) ◄──ECSLoadBridge.FlushToSave()/ApplyLoadedSave()──► ECS singletons
        SaveData ◄──SaveManager──► local file + UGS cloud
```

## Init order rules (race conditions are a recurring bug source here)

- Singletons override `Awake()` MUST call `base.Awake()` (CLAUDE.md rule).
- Never read/write ECS singletons from a MonoBehaviour before `ECSLoadBridge.IsLoaded` is true. The bridge's polling coroutine is the only sanctioned wait; copy how `TutorialOverlayController` guards its `Update()` on `ECSLoadBridge.IsLoaded`.
- Sub-controllers receive ECS access via `SetECSContext(EntityManager)` called by `HUDController` after the bridge loads — do not create your own timing.

## Time

Anything a player waits on (research timers, field cooldowns, speed boosts, offline earnings via the background timestamp, daily/weekly/achievement resets, PVP, ad caps) reads `GameClock.UtcNow` (`Assets/Scripts/Services/GameClock.cs`), never `DateTime.UtcNow`. Release builds read the wall clock; editor/dev builds let the AI playtest harness freeze and fast-forward it (`Playtests/README.md`, "Time"). Stays on the wall clock: `SaveData.lastSaved` (cold-boot offline earnings + cloud reconcile), the idle snapshot stamp, auth policy, telemetry.

## Key ECS singletons (components in `Assets/Scripts/Components/`)

| Singleton | Fields that matter | Written by |
|---|---|---|
| `PlayerProgressData` | `BaseCurrency` (entropy), `TotalEntropySpent`, `NetWorth`, `CurrentTier`, `PrestigeWallValue/Available/Requested` | NetWorthSystem, PrestigeSystem, ECSLoadBridge, UI |
| `PrestigeData` | `RunCount`, `PrestigeCurrency(+Spent)`, `SpeedMultiplier`, `OutputMultiplier`, `CostReduction` | ECSLoadBridge, PrestigeSystem, PrestigeShopSubController |
| `TutorialStateData` | `IsActive`, `CurrentStepIndex`, `FirstRunComplete` | TutorialSystem, ECSLoadBridge |
| `InventorySlot` buffer on the `PlayerInventoryTag` entity | global player inventory (`ItemID`, `Quantity`) | ProductionSystem, ECSLoadBridge, ManualCraftService |

**Known gap (intentional, do not "fix" in passing):** `PrestigeData.SpeedMultiplier`/`OutputMultiplier` are persisted and shown in UI but `ProductionSystem` does NOT read them — live craft speed is only `BuildingData.ProductionSpeed` (per-building upgrade level). The multipliers DO affect the offline/idle snapshot (`GridSaveService.cs:335`) and `CostReduction` DOES affect building prices (`EntropyWallet.ReducedCost`, used by `HUDController` and `DemolishRefund`).

## Do NOT edit

- `docs/gameplay-loop.html`, `gameplay-loop.js`, `gameplay-loop.css`, `docs/gameplay_loop_data.js` — human-only reference site; it may lag behind code, so do NOT treat it as authoritative or look balance values up there. For authoritative item/recipe/building/cost values use `Assets/Data/game_data.json` (the single source of truth). Never load the HTML.
- Generated `.asset` files under `Assets/Data/` and `Assets/Resources/` — regenerate by editing `game_data.json` and re-running the importer (see `data-pipeline.md`).
- Any UXML other than `Assets/UI/GameHUD.uxml`, `SplashScreen.uxml`, `LoadingScreen.uxml` without first grepping that it is actually loaded at runtime (CLAUDE.md rule).
- `Assets/GoogleSignIn/` native plugin code.
