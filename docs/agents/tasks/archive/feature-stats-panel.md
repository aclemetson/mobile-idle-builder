# Feature: Statistics menu (automation income per second + idle projection)

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ui-toolkit.md` (new-panel checklist),
`ecs-patterns.md` (singletons), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07).
`NumberFormat`.
**Size:** M. **Branch:** `feat/stats-panel`, branched from `feat/item-value-readouts`.

## Goal
A new **Statistics** panel opened from the left drawer. Later stats will be added to it. For now it shows:
- **Automation income**: entropy per second **actually earned by automation**, as a rolling 60-second average.
- **If you go idle**: that rate × the player's current idle collection rate.
- **Idle earnings cap**: how many hours of idle time count.

## Facts (verified 2026-11-07)
- **Where automation adds entropy** (the only automated `BaseCurrency +=` sites):
  - `EntropySinkSystem.cs` ~:58 (`progress.BaseCurrency += totalEntropy;`), the Demon/sink building;
  - `ConveyorSystem.cs` ~:200 (`progress.BaseCurrency += earned;`), belt surplus sales;
  - `ProductionSystem.cs` ~:164 (`progress.ValueRW.BaseCurrency += escapedEntropy;`), escaping byproducts.
  - `CollectorSystem` pays no entropy.
  - Exclude everything else: tutorial rewards, prestige, PVP, manual taps, manual Demon UI sales, recipe-switch input
    sales, ads and purchases.
- **Singleton pattern to copy:** `ResearchEffects` (`Assets/Scripts/Components/ResearchEffects.cs`), baked in
  `PlayerInventoryAuthoring.cs` next to `GlobalProductionBonus`. Systems read it with
  `SystemAPI.HasSingleton`/`GetSingleton`, or `SystemAPI.GetSingletonRW`.
- **Idle rate:** `OfflineCollectionService` (`Assets/Scripts/IdleCollection/OfflineCollectionService.cs`):
  - `GetEffectiveCollectionRate(GameConfigSO, PersistentUpgradeService)` (~:121) gives 0..1. The base is 0.20, plus
    the `IdleCollectionRate` upgrade.
  - `GetAdIdleMultiplier(SaveData)` (~:132) is ×1.5 while the ad boost is active.
  - `GetEffectiveIdleCap(GameConfigSO, PersistentUpgradeService)` (~:112) is in seconds.
  - How callers get the arguments: `GameBootstrap.Instance?.gameConfig`, `PersistentUpgradeService.Instance`,
    `SaveManager.Instance?.Current` (see `AdService.cs` ~:255).
- **Wrong description:** `PersistentUpgradeService.cs` ~:142 says "Idle harvesters collect +5% more output per level
  (base 50%, max 100%)". The base is really 20% (`GameConfigSO.idleBaseCollectionRate = 0.20f`).
  `docs/agents/economy-balance.md:139` repeats the error.
- **Panel plumbing** (`HUDController.cs`):
  - Drawer buttons are in `Assets/UI/GameHUD.uxml` ~:62-80 inside `drawer-nav-scroll`, e.g.
    `<ui:Button name="btn-managers" text="✦ Managers" class="drawer-nav-btn drawer-nav-btn--prestige" />`. Use plain
    `drawer-nav-btn`; Statistics isn't a prestige feature.
  - `QueryElements` (~:395) finds panels; also add the new one to `_allPanels` (~:424-429).
  - `BindButtons` (~:513-541) wires buttons; close buttons are wired ~:557-572.
  - Open methods call `CloseAllPanels()`, then `sub.Refresh()`, then `SetElementVisible(panel, true)`. See
    `OpenManagersPanel` (~:1145).
  - Sub-controllers are fetched in `Awake` (~:176-190). `RewardedAdsSubController` uses
    `GetComponent<>() ?? gameObject.AddComponent<>()`, so no scene edit is needed. Do the same.
  - `OnEnable` calls `_x?.Init(root, this)` (~:222-235).
  - Template controller: `PrestigeShopSubController`.
  - Periodic refresh while open: `_researchTicker = _researchList.schedule.Execute(...).Every(1000)`, resumed on open
    and paused on close (~:767-780).
- **UI rules** (`ui-toolkit.md`):
  - Copy the `upgrades-panel` slide-panel block.
  - Use the `blocks-world-input` class.
  - The close button uses `GlyphIcon`, not ✕ (`GlyphIconTests`).
  - Respect the safe area; avoid zero-height parents.
- **Formatting:** `NumberFormat.Entropy(double)` comes from the value-readouts task.

## Implementation
1. **`Assets/Scripts/Components/AutomationIncome.cs`**: `public struct AutomationIncome : IComponentData { public double Total; }`.
   - Bake it with 0 in `PlayerInventoryAuthoring` next to `ResearchEffects`. It is not saved.
2. **Count income.** At each of the 3 sites, add the same amount to `AutomationIncome.Total` right where
   `BaseCurrency` is increased.
   - Guard each write with `SystemAPI.HasSingleton<AutomationIncome>()` so tests that don't bake it still pass.
   - Check each system's style (`SystemBase` or `ISystem`, Burst or not) and use the matching singleton API.
   - Burst systems can write a blittable double.
3. **`Assets/Scripts/Services/RollingRate.cs`**, a pure class:
   - `RollingRate(int capacity = 90)`.
   - `void Add(double time, double total)`.
   - `double Rate(double windowSeconds)` = (newest total − total of the oldest sample still inside the window) ÷ the
     time between them. It is 0 with fewer than 2 samples or no time span.
   - `void Clear()`.
   - Before a full window exists, use the samples so far.
   - If `Total` ever drops, which happens when a reload or prestige recreates the entity, clear the buffer and start
     over.
4. **`Assets/Scripts/UI/IncomeRateTracker.cs`**, a MonoBehaviour:
   - Once per second of real time (`Time.unscaledTime`), read `AutomationIncome.Total` from
     `World.DefaultGameObjectInjectionWorld` through a cached `EntityQuery`, and `Add` it.
   - Expose `public double PerSecond => _rate.Rate(60)`.
   - Skip quietly while the world or singleton isn't ready (SubScene race).
   - Add it to the HUD GameObject in `HUDController.Awake` with `GetComponent<>() ?? AddComponent<>()`. It must run
     even when the panel is closed.
5. **`Assets/Scripts/UI/StatsSubController.cs`**, following `PrestigeShopSubController`:
   - `Init(root, hud)` queries `stats-panel` and its labels; `Refresh()` fills them.
   - **Rows:**
     - "Automation income" → `"◈ X /s"`, with the subtitle "Average over the last 60 s".
     - "If you go idle" → `"◈ Y /s"`, with the subtitle `"{rate:P0} idle rate"`, plus `" (ad boost active)"` when
       `GetAdIdleMultiplier > 1`. Y = X × `GetEffectiveCollectionRate × GetAdIdleMultiplier`.
     - "Idle earnings cap" → `"Nh Mm"`.
     - A small footnote: "Idle payout is estimated from your factory layout when you leave, so it can differ
       slightly."
   - Put the row text in `internal static` helpers so they're testable, e.g. `IdleProjection(double perSecond, float
     rate, float adMult)` and `FormatCap(float seconds)`.
   - Ticker: `schedule.Execute(Refresh).Every(1000)`, resumed on open and paused on close.
6. **`GameHUD.uxml`:**
   - Add a `btn-stats` drawer button "Statistics" near the other non-prestige buttons.
   - Add a `stats-panel` slide panel copied from the `upgrades-panel` block: title "Statistics", a `GlyphIcon` close
     button, a ScrollView, and three stat rows (label + value + subtitle) plus the footnote.
   - Style in `GameHUD.uss`, reusing existing classes where possible.
7. **`HUDController`:**
   - Add the `_stats` field and the tracker in `Awake`; `Init` in `OnEnable`.
   - Add `OpenStatsPanel` (`CloseAllPanels`, `_stats.Refresh()`, show, start ticker) and the close handling.
   - Add the panel to `_allPanels` and wire the button in `BindButtons`.
8. **Fix** the upgrade description to "(base 20%, max 100%)", and fix `economy-balance.md:139`.
9. **Docs:**
   - `ui-toolkit.md`: add the Statistics panel to the panel list and refresh the stale line refs the explorer noted
     (fields ~:176-190, wiring ~:513-541).
   - `ecs-patterns.md`: add the `AutomationIncome` singleton.
   - `economy-balance.md`:
     - the value readouts from the previous task (price fixed, bonuses raise quantity);
     - the Statistics rate definition;
     - quantum_yield (`PrestigeData.OutputMultiplier`) is unwired in idle too (`GridSaveService` only folds in
       `prestigeSpeedMultiplier`).

## Tests (EditMode)
- **`RollingRateTests`:**
  - a steady 10/s over 70 samples gives 10;
  - a partial window (5 samples) gives the correct rate;
  - income stops and the rate falls toward 0 after the window;
  - a drop in Total clears the buffer;
  - fewer than 2 samples give 0.
- **ECS test:** an `EntropySinkSystem` tick with the singleton present adds the same amount to
  `AutomationIncome.Total` as to `BaseCurrency`. Copy an existing sink or production test's world setup; grep
  `EntropySinkSystem` in Tests. Add one for `ProductionSystem` escaped entropy if a test world for it already exists.
- **`StatsSubController` helpers:** `IdleProjection(100, 0.2f, 1f)` gives 20; with `adMult` 1.5 it gives 30;
  `FormatCap(7200)` gives "2h 0m".
- **UXML check:** if `HudOverlayInstanceTests` or `TutorialHintMinimizeTests` load the real UXML, add an assertion
  that `stats-panel` and `btn-stats` exist.

## Done when
- The Statistics button opens the panel.
- With a running factory, the rate settles near the expected value within ~60 s and keeps updating while open.
- The idle line equals rate × 20% on a fresh save, and shows the ad-boost note when the boost is active.
- The back button and close button work.
- The upgrade description says base 20%.
- The full EditMode suite passes. Set Status to done with the PR number.
