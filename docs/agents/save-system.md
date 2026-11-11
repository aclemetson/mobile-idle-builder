# Save System: SaveData ↔ ECS ↔ Cloud

**Scope:** Save envelope, the ECS bridge, grid persistence, idle snapshot, and how to add a save field safely. Read before touching anything persisted.

> Verified against: `e4ef2c6` + multi-grids Phase 1-2, 2026-06-12; prestige reset + scene reload (`PrestigeRunReset`) 2026-11-07; recipe-id migration (#336) 2026-11-07; Services table (`SaveManager`, `LocalSaveService`) refreshed for #280, 2026-10-31; prestige multipliers live + research discounts + Entropy Surge 2026-11-11. If code contradicts this doc, trust the code and update this doc.

## The envelope: `Assets/Scripts/SaveSystem/SaveData.cs`

One JSON-serialized class, written to `Application.persistentDataPath/save.json` and to UGS Cloud Save. Persistent fields survive prestige; `currentRun` is cleared on prestige.

| Field (selection) | Meaning |
|---|---|
| `prestigeCount / prestigeCurrency / prestigeCurrencySpent` | prestige meta, survives prestige |
| `prestigeSpeedMultiplier / prestigeOutputMultiplier / prestigeCostReduction` | written by `PersistentUpgradeService.cs:224-226` from purchased upgrades |
| `permanentUpgrades` (List<string>) | purchased upgrade ids (`"id:level"` style — check `PersistentUpgradeService.LoadFromSave`) |
| `idleSnapshot` + `idleCollectionApplied` | active site's offline-earnings chain snapshot + double-apply guard timestamp |
| `siteSnapshots` (List<IdleCollectionSnapshot>) | per-site offline snapshots, index = site index. Mirrors `idleSnapshot` at the active index; inactive sites keep producing from their stored entries. Populated by `GridSaveService.MirrorActiveSiteSnapshot` on every flush/load. Run state — cleared on prestige by `PrestigeRunReset.ResetSitesForPrestige` |
| `unlockedRecipes / unlockedResearch / codex` | knowledge; `unlockedResearch` is per-run (cleared by PrestigeSystem) |
| `researchCompletedEver` | every research finished on any run; survives prestige. A listed node costs less on later runs by its `prestige_memory_discount` (`ResearchService.EffectiveCost` → `ResearchCost.Effective`, with Memory Resonance). Backfilled on load from this run's unlocks and, for older saves, from research whose gated recipes are all known (recipe knowledge survives prestige) |
| `adLastWatch` (`AdLastWatchEntry` id/utc) | last watch per ad placement (ISO 8601 UTC); drives per-placement cooldowns (Entropy Surge, 15 min). Survives prestige |
| `devConsoleUsed` | sticky: a dev-console command ran on this save. Set by `DevConsoleController`, stamped on telemetry `player_snapshot`. Survives prestige (never cleared) |
| `domainsIntroSeen` | one-shot guard: Quantum Domains intro dialogue shown when `materials_science` research unlocks. Survives prestige |
| `paidCurrency / crystalsPurchased` | crystals balance / lifetime IAP audit |
| `speedBoostExpiryUtc` | ISO 8601; empty = no boost. THE pattern to copy for any timed effect |
| `dailyResetUtc / weeklyResetUtc / monthlyResetUtc` | achievement period reset timestamps (UTC) |
| `loginStreakIndex / lastLoginRewardUtc` | daily login calendar position + last-claim date (UTC); survives prestige |
| `dailyChallengeResetUtc / dailyChallengeIds / dailyChallengeProgress / dailyChallengesClaimed` | rotating daily-challenge set, progress, and claims (UTC reset); survives prestige |
| `achievements / achievementProgress / unclaimedAchievements` | achievement state |
| `tutorial` (`TutorialSaveData`) | `currentStepId` (string), `hasCompletedFirstRun` survives prestige |
| `currentRun` (`CurrentRunData`) | `baseCurrency`, `totalEntropySpent`, `baseNetWorth`, inventory as parallel `inventoryKeys`(string numeric ids)/`inventoryValues`, `grids[]` + `activeSiteIndex` (multi-grids), `activeWorldIndex` (worlds; 0 = Physics), legacy `grid` mirror, research progress. **Read the active grid via `CurrentRunData.ActiveGrid`** |
| `currentRun.fieldPurchaseIds / fieldPurchaseCounts` | fields bought this run (parallel lists, FieldSO id → purchases; a quark pair counts once). Drives the escalating price and the `FieldPurchasedMin` tutorial condition. Run state, so prestige clears it with `currentRun`. Bought fields themselves save in `grids[].fields` like generated ones. Owner: `FieldPurchaseService` |
| `currentRun.craftQueue` (`CraftQueueSaveData`) | recipe-menu craft queue (#310): `entries` (recipeId, count, unitSeconds) + `activeCompleteUtc` (ISO "O", read against `GameClock.UtcNow`) for the front item. Inputs were already taken from the inventory when queued; cancel refunds them. Next item starts at the previous one's finish time, so a queue catches up after the app was closed. Legacy recipe ids in `entries` (and in `recipe_knowledge.json`) are rewritten through `LegacyRecipeIds` on first read. Run state, so prestige clears it with `currentRun`. Owner: `ManualCraftService` (ticks once `ECSLoadBridge.IsLoaded`); pure rules in `CraftQueue` |
| `currentRun.grids[]` (`GridSaveData`) | one per build site; `grids[0]` aliases legacy `grid`. Each: `size`, `expansions`, `buildings[]` (numeric buildingId/recipeId, position, level, rotation), `conveyors[]` (flattened cell list), `fields[]`. Migration: `GridSaveService.EnsureActiveGrid(save)` seeds `grids[0]` from legacy `grid` on first access and keeps them mirrored while site 0 is active. `unlockedSites` (root, survives prestige) holds unlocked site ids. **Worlds (Chem/Bio P1–3):** `unlockedWorlds` (root, survives prestige) mirrors `unlockedSites` for world ids (`world_physics` implicit); worlds are a logical grouping over the flat site list (`WorldSO.siteIds`), mapped via `Services/WorldLayout.cs`. `Services/WorldService` unlocks a world (deduct entropy + record `unlockedWorlds` + unlock member sites) and travels via `SiteService.SwitchTo`; `SiteService.SwitchTo` is the single writer of `activeSiteIndex` and keeps `activeWorldIndex` in sync (`WorldLayout.WorldIndexForSite`). `ResetSitesForPrestige` clears `activeWorldIndex`→0 with `activeSiteIndex`; `unlockedWorlds` is preserved. Idle earnings already span all worlds (flat `siteSnapshots`) |

## Services

| Class | Role |
|---|---|
| `SaveManager` (`Assets/Scripts/SaveSystem/SaveManager.cs`) | Owns `Current` SaveData. `SaveLocal()` = `ECSLoadBridge.FlushToSave()` then write disk. **Autosave** (`AutoSaveTick`, every `autoSaveIntervalSeconds` = 60s) always writes locally but pushes to the cloud only when the save's content hash differs from the last *successful* push AND `cloudPushIntervalSeconds` (300s) have passed since the last push started (`ShouldAutoPush`); honors the `CloudSaveEnabled` kill-switch. `SaveToCloud()` (explicit saves, e.g. `MaintenanceWatcher`) always pushes. `SaveLocal()` + best-effort cloud push (`PushToCloudBestEffort`) on both background (`OnApplicationPause(true)`) and quit (`OnApplicationQuit`), always, unthrottled — fire-and-forget Task, not a coroutine, since the player loop is suspended once backgrounded. `ICloudSaveService.PushAsync` returns `Task<bool>` (never throws; true only if the write landed). Cloud reconcile: newest `lastSaved` wins (`IsNewerStamp`, culture-invariant, never throws; an empty local stamp always loses), loser kept as 24h backup. |
| `LocalSaveService` | JSON file read/write. `LocalSaveService.SaveDirectory` is the one folder for `save.json` and every sidecar file (`SettingsService`, `RecipeKnowledgeService`, `FeatureFlagService`, `SaveWipe` resolve paths from it); tests redirect it to a temp folder (see `testing.md`). **Crash-safe (#280):** writes go to `save.json.tmp`, are flushed to disk, then swapped in with `File.Replace` (delete + move fallback); `SaveWithBackup` copies the old `save.json` to `.bak` first. `Load()` tries `save.json` → `save.json.tmp` → `save.json.bak`, skipping empty/unparseable files, and reports `LastLoadSource`. After a fallback load the corrupt `save.json` is never rotated over the good `.bak`. `Serialize` hashes the JSON with `lastSaved` blanked (`LastWrittenContentHash`, FNV-1a 64) and splices the stamp in, so the save is serialized once. |
| `UGSCloudSaveService` + `AuthSessionPolicy` | UGS Cloud Save; Google sign-in with 30/90-day re-auth policy; anonymous fallback. |
| `PrestigeRunReset` | Static. `ResetRun(save, startingEntropy)` wipes the run part of the save on prestige (new `currentRun`, grids + idle snapshots, research, manager assignments; tutorial retired). `BeginReload()` then reloads GameScene through the loading screen; while `ReloadPending`, `SaveLocal` skips the ECS/grid flush so the old scene can't write its belts and fields back. |
| `GridSaveService` | Building/conveyor/field persistence + idle snapshot rebuild (below). |

## The bridge: `Assets/Scripts/SaveSystem/ECSLoadBridge.cs`

- `InitializeAsync()` polls for the ECS world (5s) then for the `PlayerProgressData` / `PrestigeData` / `PlayerInventoryTag` / `TutorialStateData` entities (10s) — all four are gated so a late tutorial singleton can't leave the baked step-0 default standing — then waits for `SaveManager.CloudReconcileDone` (≤5s) so a cloud-replaced `Current` is applied to ECS rather than a stale local one, then:
  - `ApplyLoadedSave()`: permanent upgrades (multipliers recomputed from the levels via `PersistentUpgradeService.FlushEffectsToSave`) → `PrestigeData` (pure prestige values; the timed speed boost is not composed in) → tutorial state → offline earnings (`OfflineCollectionService.CalculateAndApply`) → currency + inventory (skipped on fresh install so baked defaults stand, `IsNewGame` guard at `ECSLoadBridge.cs:183`).
  - Then `GridSaveService.Instance.LoadGrid()` re-places saved buildings via `BuildingPlacer.PlaceBuilding()` (`GridSaveService.cs:256`).
  - Sets `IsLoaded = true`.
- `FlushToSave()` (called by every `SaveManager.SaveLocal()`): snapshots ECS → SaveData. `PrestigeData.SpeedMultiplier` is persisted as-is: it never contains the timed speed boost (older builds composed it in at load and divided it back out on save, which persisted it doubled if the boost expired mid-session).
- Write-through helpers for live purchases: `AddEntropy()` / `AddPrestigeCurrency()` (`ECSLoadBridge.cs:279-294`).

## Idle/offline earnings

- `GridSaveService.RebuildIdleSnapshot()` (end of `FlushToSave()`/`LoadGrid()`) walks the saved grid, computes producer→sink chains via `IdleGraphAnalyzer.BuildSnapshot`, baking in `prestigeSpeedMultiplier × boost × megastructure` (`speedMult`) and `prestigeOutputMultiplier × megastructure` into each source's output multiplier.
- `RebuildIdleSnapshot()` also mirrors the active snapshot into `save.siteSnapshots[activeSiteIndex]` (`MirrorActiveSiteSnapshot`).
- On next launch `OfflineCollectionService.CalculateAndApply()` pays out: elapsed seconds capped by `GetEffectiveIdleCap()` (config base + `IdleTimeCap` upgrade), rate scaled by `IdleCollectionRate` upgrade; `idleCollectionApplied` timestamp prevents double-apply. It iterates **all** `siteSnapshots` (every unlocked site earns for the offline window), falling back to the lone `idleSnapshot` on legacy/single-site saves.
- `CalculateAndApply` only credits `SaveData.currentRun`. On cold boot that is enough (the save is copied into ECS afterwards). On **resume from background** (`SaveManager.OnReturnFromBackground`) ECS is already live, so the payout is also written through via `ECSLoadBridge.AddEntropy` / `AddInventoryItems`; without that the next `FlushToSave` overwrites it with the stale ECS total (#185). Any new mid-session payout path must do the same (see `AdService.ApplyCollectionResult`).

## Research timer (`ResearchService`)

- One research runs at a time. `SaveData.activeResearchId` + `activeResearchCompleteUtc` (ISO-UTC, same timed-effect pattern as `speedBoostExpiryUtc`) hold the in-progress node and its finish time. `ResearchService` caches these on `Start()` (`RestoreActiveTimer`) and completes offline if already elapsed; `Update()` completes it live; `SkipActive()` finishes it early for crystals.
- **Prestige:** research fully resets, so both fields are cleared (set to null) by `PrestigeRunReset.ResetRun` (called from `PrestigeSystem`) alongside `unlockedResearch`, and `ResearchService.ResetAll()` drops the cached timer. (Recipe knowledge survives prestige — that is separate, in `unlockedRecipes`.)

## Multi-grids / sites (`SiteService`)

- `Assets/Scripts/Services/SiteService.cs` (scene singleton, sibling of `ResearchService`/`GridSaveService`): loads `Resources/SiteDatabase` (generated by the importer; index = canonical site index). `UnlockSite(id)` deducts entropy via the `PlayerProgressData` singleton and appends to `unlockedSites` (survives prestige). `SwitchTo(index)` = `SaveLocal()` (flush current site → `grids[current]` + snapshot) → set `activeSiteIndex` → `GridSaveService.ClearGrid()` → `LoadGrid(forceApply)` → first-visit `FieldGenerator.GenerateForSite()` → `SaveLocal()`. Currency/inventory are global and untouched.
- `GridSaveService.EnsureSiteGrid(save, index)` pads `grids` so a newly unlocked/visited site has a slot. `LoadGrid` restores fields even when a site has no buildings/conveyors.
- Inactive sites are NEVER live ECS (load-bearing constraint). Dev console: `site list` / `site switch <n>` / `site unlock <id>` / `domains intro` (replay the intro). UI: `sites-panel` + `SitesSubController` (Travel/Unlock).
- **Domains introduction:** one-shot dialogue `intro_quantum_domains` plays when the player unlocks `materials_science` research (the 150K gate before the first 250K domain). Routed via `HUDController.OnResearchUnlocked` → `SitesSubController.NotifyResearchUnlocked`; guarded by `save.domainsIntroSeen`. Dialogue resolved at runtime via `Resources/DialogueDatabase` (`DialogueDatabaseSO`, generated by the importer — the general runtime id→DialogueSO lookup) so no Inspector wiring is needed.
- **Prestige:** `PrestigeRunReset.ResetSitesForPrestige(save)` (part of `ResetRun`, called from `PrestigeSystem.OnUpdate`) clears all `grids`, resets `activeSiteIndex` to 0, and wipes `idleSnapshot` + `siteSnapshots`. `unlockedSites` is intentionally preserved (site unlocks survive prestige, like recipe knowledge).

## Adding a save field — the rules

1. **Additive only.** Never rename/remove/retype existing fields. New fields get field initializers as defaults — old JSON missing the field deserializes to the default, which must mean "feature never used".
2. Checklist: field in `SaveData.cs` (+ default) → write site (service or `FlushToSave()`) → read site (`ApplyLoadedSave()` if ECS-visible, or service `LoadFromSave`) → reset-on-prestige decision (does `PrestigeSystem.OnUpdate` need to clear it? see `PrestigeSystem.cs:103-110`) → test.
3. Test: round-trip (save → load → assert) AND legacy-load (deserialize a JSON string *without* your field, assert defaults). Copy patterns in `Assets/Scripts/PlayModeTests/SaveSystemTests.cs`.
4. Timed effects: store ISO 8601 UTC string, compare with `DateTime.UtcNow`, copy `speedBoostExpiryUtc` (store the expiry, read it where it takes effect, e.g. `PremiumShopService.GetSpeedBoostMultiplier`). Never compose a timed multiplier into a persisted ECS value: it outlives its expiry.

## Pitfalls

- `SaveLocal` skips the ECS **and grid** flush whenever `ECSLoadBridge.SaveApplied` is false, which includes the whole load window before `IsLoaded`. Until `LoadGrid` runs, ECS holds no buildings, so a grid flush would erase every saved building, belt and field. This happened on device: a research timer that finished while the app was closed completes in `ResearchService.Start` and calls `SaveLocal` seconds before the load lands. Regression: `ECSLoadBridgeSaveGuardTests.SaveLocal_WhileStillLoading_KeepsSavedBuildingsAndBelts`.
- `JsonUtility` cannot serialize dictionaries — use parallel lists (`inventoryKeys/Values`) or `[Serializable]` entry classes (`AchievementProgressEntry`).
- Anything you write to ECS after load must also be flushed back in `FlushToSave()`, or it silently reverts on next launch.
- Prestige (`PrestigeSystem.OnUpdate`) flushes ECS, calls `PrestigeRunReset.ResetRun`, writes the save without re-flushing, then reloads GameScene (`PrestigeRunReset.BeginReload`). A run-scoped field outside `currentRun` must be cleared in `ResetRun`; the new scene loads the reset save like a returning player. The default ECS world is NOT rebuilt by the reload, so run-scoped *entities* must be destroyed by `PrestigeSystem` itself (buildings, every `ConveyorSegmentData` belt with its `ConveyorItemData`, the kept Demon's input/output buffers); anything left there is drawn in the new run and written back by the next save.
