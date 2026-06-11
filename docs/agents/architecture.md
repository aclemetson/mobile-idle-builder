# Architecture Map (always read this)

**Scope:** Structural map of the codebase — scenes, layers, data flow, init order, what not to touch.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## Scene & boot flow

```
SplashScene  ──►  LoadingScreen (overlay scene)  ──►  GameScene
  auth + cloud      SceneLoader transition             single gameplay scene
  reconciliation                                       contains ECS SubScene (TestSubScene)
```

1. `SplashScene`: persistent singletons awake (`SaveManager`, `SettingsService`, `PremiumShopService`, `IAPService`); UGS auth + cloud save reconciliation; then `SceneLoader.GoTo("GameScene")`.
2. `GameScene`: ECS world spawns; SubScene bakes `Assets/Scripts/Authoring/*` into entities; `ECSLoadBridge` polls for ECS singletons (5s world timeout + 10s entity timeout), then applies `SaveData` → ECS and calls `GridSaveService.Instance.LoadGrid()`. Only after `ECSLoadBridge.IsLoaded == true` is gameplay state valid.
3. There is no MainMenu scene. All menus (shop, settings, prestige, etc.) are panels inside the single `Assets/UI/GameHUD.uxml`.

## Three layers

### ECS simulation (`Assets/Scripts/Systems/`, all in `SimulationSystemGroup`)

| System | Role |
|---|---|
| `ProductionSystem.cs` | Per-building craft loop: check inputs (local `BuildingInputSlot` buffer, falls back to global inventory), advance `Progress += dt × BuildingData.ProductionSpeed`, consume inputs, deposit to `BuildingOutputSlot`. Burst-compiled `ISystem`. |
| `CollectorSystem.cs` | Field harvesters: produce items at fixed rate, no inputs. |
| `ConveyorSystem.cs` | Moves items building output slot → adjacent input slot, respecting ports/direction. |
| `PowerGridSystem.cs` | eV energy production/consumption; gates production on available power. |
| `EntropySinkSystem.cs` | Sink building consumes items → grants entropy (base currency). |
| `NetWorthSystem.cs` | Aggregates net worth (inventory + currency + spent); drives prestige wall. |
| `PrestigeSystem.cs` | Wall detection + prestige execution: currency formula, run reset, building destruction, service resets, forced save. |
| `TutorialSystem.cs` | Advances `TutorialStateData` through `TutorialFlowSO.Current.steps` conditions. |
| `PVPSystem.cs` | 48h competition run state. |
| `ProductionAchievementBridge.cs` | Forwards ECS production events to `AchievementService.Notify*()`. |

### MonoBehaviour services (singletons, base class `Assets/Scripts/Bootstrap/SingletonMonoBehaviour.cs`)

| Service | Location | Role |
|---|---|---|
| `SaveManager` | `Assets/Scripts/SaveSystem/` | Orchestrates local + cloud save; 60s autosave; calls `ECSLoadBridge.FlushToSave()` before writing. |
| `ECSLoadBridge` | `Assets/Scripts/SaveSystem/` | THE bridge between SaveData and ECS singletons. See `save-system.md`. |
| `GridSaveService` | `Assets/Scripts/SaveSystem/` | Saves/restores grid buildings/conveyors/fields; rebuilds idle snapshot. |
| `ResearchService` | `Assets/Scripts/Services/` | Research purchase: prereqs + `DeductEntropy()` (`ResearchService.cs:168`) via ECS; fires `OnResearchUnlocked`. |
| `PersistentUpgradeService` | `Assets/Scripts/Services/` | Prestige-shop permanent upgrades. Catalogue is the hardcoded `UpgradeDef[] All` array (`PersistentUpgradeService.cs:48`), NOT data-driven from JSON. |
| `PremiumShopService` / `PremiumShopCalculator` / `IAPService` | `Assets/Scripts/Services/` | Crystal IAP (4 consumable packs in `IAPService.CrystalAmounts`), speed boosts (`speedBoostExpiryUtc`), entropy/PC purchases. |
| `RecipeKnowledgeService` | `Assets/Scripts/Services/` | Recipes unlocked across runs (survives prestige). |
| `AchievementService` | `Assets/Scripts/Achievements/` | `Notify*()` hooks + daily/weekly/monthly UTC period resets (`CheckPeriodResets()`, `AchievementService.cs:103`). |
| `OfflineCollectionService` | `Assets/Scripts/IdleCollection/` | Static: computes offline earnings from `SaveData.idleSnapshot`, capped by `GetEffectiveIdleCap()`. |
| `ToastService`, `SettingsService` | `Assets/Scripts/Services/` | UI toasts; user prefs to `settings.json`. |

### UI (`Assets/Scripts/UI/`, UI Toolkit)

`HUDController` (on a GameObject in GameScene with a `UIDocument` → `GameHUD.uxml`) owns everything; sub-controllers are **MonoBehaviour components on the same GameObject**, fetched via `GetComponent<>()` (`HUDController.cs:106-111`). See `ui-toolkit.md` before any UI work.

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

## Key ECS singletons (components in `Assets/Scripts/Components/`)

| Singleton | Fields that matter | Written by |
|---|---|---|
| `PlayerProgressData` | `BaseCurrency` (entropy), `TotalEntropySpent`, `NetWorth`, `CurrentTier`, `PrestigeWallValue/Available/Requested` | NetWorthSystem, PrestigeSystem, ECSLoadBridge, UI |
| `PrestigeData` | `RunCount`, `PrestigeCurrency(+Spent)`, `SpeedMultiplier`, `OutputMultiplier`, `CostReduction` | ECSLoadBridge, PrestigeSystem, PrestigeShopSubController |
| `TutorialStateData` | `IsActive`, `CurrentStepIndex`, `FirstRunComplete` | TutorialSystem, ECSLoadBridge |
| `InventorySlot` buffer on the `PlayerInventoryTag` entity | global player inventory (`ItemID`, `Quantity`) | ProductionSystem, ECSLoadBridge, ManualCraftService |

**Known gap (intentional, do not "fix" in passing):** `PrestigeData.SpeedMultiplier`/`OutputMultiplier` are persisted and shown in UI but `ProductionSystem` does NOT read them — live craft speed is only `BuildingData.ProductionSpeed` (per-building upgrade level). The multipliers DO affect the offline/idle snapshot (`GridSaveService.cs:335`) and `CostReduction` DOES affect building prices (`HUDController.cs:961`).

## Do NOT edit

- `docs/gameplay-loop.html`, `gameplay-loop.js`, `gameplay-loop.css` — human reference site. Look values up in `docs/gameplay_loop_data.js` if needed; never load the HTML.
- Generated `.asset` files under `Assets/Data/` and `Assets/Resources/` — regenerate by editing `game_data.json` and re-running the importer (see `data-pipeline.md`).
- Any UXML other than `Assets/UI/GameHUD.uxml`, `SplashScreen.uxml`, `LoadingScreen.uxml` without first grepping that it is actually loaded at runtime (CLAUDE.md rule).
- `Assets/GoogleSignIn/` native plugin code.
