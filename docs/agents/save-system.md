# Save System: SaveData ↔ ECS ↔ Cloud

**Scope:** Save envelope, the ECS bridge, grid persistence, idle snapshot, and how to add a save field safely. Read before touching anything persisted.

> Verified against: `e4ef2c6` + multi-grids Phase 1-2, 2026-06-12. If code contradicts this doc, trust the code and update this doc.

## The envelope: `Assets/Scripts/SaveSystem/SaveData.cs`

One JSON-serialized class, written to `Application.persistentDataPath/save.json` and to UGS Cloud Save. Persistent fields survive prestige; `currentRun` is cleared on prestige.

| Field (selection) | Meaning |
|---|---|
| `prestigeCount / prestigeCurrency / prestigeCurrencySpent` | prestige meta, survives prestige |
| `prestigeSpeedMultiplier / prestigeOutputMultiplier / prestigeCostReduction` | written by `PersistentUpgradeService.cs:224-226` from purchased upgrades |
| `permanentUpgrades` (List<string>) | purchased upgrade ids (`"id:level"` style — check `PersistentUpgradeService.LoadFromSave`) |
| `idleSnapshot` + `idleCollectionApplied` | active site's offline-earnings chain snapshot + double-apply guard timestamp |
| `siteSnapshots` (List<IdleCollectionSnapshot>) | per-site offline snapshots, index = site index. Mirrors `idleSnapshot` at the active index; inactive sites keep producing from their stored entries. Populated by `GridSaveService.MirrorActiveSiteSnapshot` on every flush/load. Run state — cleared on prestige by `PrestigeSaveWatcher.ResetSitesForPrestige` |
| `unlockedRecipes / unlockedResearch / codex` | knowledge; `unlockedResearch` is per-run (cleared by PrestigeSystem) |
| `domainsIntroSeen` | one-shot guard: Quantum Domains intro dialogue shown when `materials_science` research unlocks. Survives prestige |
| `paidCurrency / crystalsPurchased` | crystals balance / lifetime IAP audit |
| `speedBoostExpiryUtc` | ISO 8601; empty = no boost. THE pattern to copy for any timed effect |
| `dailyResetUtc / weeklyResetUtc / monthlyResetUtc` | achievement period reset timestamps (UTC) |
| `loginStreakIndex / lastLoginRewardUtc` | daily login calendar position + last-claim date (UTC); survives prestige |
| `dailyChallengeResetUtc / dailyChallengeIds / dailyChallengeProgress / dailyChallengesClaimed` | rotating daily-challenge set, progress, and claims (UTC reset); survives prestige |
| `achievements / achievementProgress / unclaimedAchievements` | achievement state |
| `tutorial` (`TutorialSaveData`) | `currentStepId` (string), `hasCompletedFirstRun` survives prestige |
| `currentRun` (`CurrentRunData`) | `baseCurrency`, `totalEntropySpent`, `baseNetWorth`, inventory as parallel `inventoryKeys`(string numeric ids)/`inventoryValues`, `grids[]` + `activeSiteIndex` (multi-grids), legacy `grid` mirror, research progress. **Read the active grid via `CurrentRunData.ActiveGrid`** |
| `currentRun.grids[]` (`GridSaveData`) | one per build site; `grids[0]` aliases legacy `grid`. Each: `size`, `expansions`, `buildings[]` (numeric buildingId/recipeId, position, level, rotation), `conveyors[]` (flattened cell list), `fields[]`. Migration: `GridSaveService.EnsureActiveGrid(save)` seeds `grids[0]` from legacy `grid` on first access and keeps them mirrored while site 0 is active. `unlockedSites` (root, survives prestige) holds unlocked site ids |

## Services

| Class | Role |
|---|---|
| `SaveManager` (`Assets/Scripts/SaveSystem/SaveManager.cs`) | Owns `Current` SaveData. `SaveLocal()` = `ECSLoadBridge.FlushToSave()` then write disk. 60s autosave; `SaveLocal()` + best-effort cloud push (`PushToCloudBestEffort`) on both background (`OnApplicationPause(true)`) and quit (`OnApplicationQuit`) — fire-and-forget Task, not the `SaveToCloud` coroutine, since the player loop is suspended once backgrounded; honors the `CloudSaveEnabled` kill-switch. Cloud reconcile: newest `lastSaved` timestamp wins, loser kept as 24h backup. |
| `LocalSaveService` | JSON file read/write. |
| `UGSCloudSaveService` + `AuthSessionPolicy` | UGS Cloud Save; Google sign-in with 30/90-day re-auth policy; anonymous fallback. |
| `PrestigeSaveWatcher` | Clears idle snapshot when `PrestigeData.RunCount` changes (prevents double-earning). |
| `GridSaveService` | Building/conveyor/field persistence + idle snapshot rebuild (below). |

## The bridge: `Assets/Scripts/SaveSystem/ECSLoadBridge.cs`

- `InitializeAsync()` polls for the ECS world (5s) then for the `PlayerProgressData` / `PrestigeData` / `PlayerInventoryTag` / `TutorialStateData` entities (10s) — all four are gated so a late tutorial singleton can't leave the baked step-0 default standing — then waits for `SaveManager.CloudReconcileDone` (≤5s) so a cloud-replaced `Current` is applied to ECS rather than a stale local one, then:
  - `ApplyLoadedSave()`: permanent upgrades → `PrestigeData` (speed boost composed in at `ECSLoadBridge.cs:148`) → tutorial state → offline earnings (`OfflineCollectionService.CalculateAndApply`) → currency + inventory (skipped on fresh install so baked defaults stand, `IsNewGame` guard at `ECSLoadBridge.cs:183`).
  - Then `GridSaveService.Instance.LoadGrid()` re-places saved buildings via `BuildingPlacer.PlaceBuilding()` (`GridSaveService.cs:256`).
  - Sets `IsLoaded = true`.
- `FlushToSave()` (called by every `SaveManager.SaveLocal()`): snapshots ECS → SaveData. Note it **divides the speed boost back out** before persisting (`ECSLoadBridge.cs:236`) so the boost isn't compounded across saves. Copy this strip-on-save pattern for any runtime-composed multiplier.
- Write-through helpers for live purchases: `AddEntropy()` / `AddPrestigeCurrency()` (`ECSLoadBridge.cs:279-294`).

## Idle/offline earnings

- `GridSaveService.RebuildIdleSnapshot()` (end of `FlushToSave()`/`LoadGrid()`) walks the saved grid, computes producer→sink chains via `IdleGraphAnalyzer.BuildSnapshot`, baking in `prestigeSpeedMultiplier × boost` at `GridSaveService.cs:335`.
- `RebuildIdleSnapshot()` also mirrors the active snapshot into `save.siteSnapshots[activeSiteIndex]` (`MirrorActiveSiteSnapshot`).
- On next launch `OfflineCollectionService.CalculateAndApply()` pays out: elapsed seconds capped by `GetEffectiveIdleCap()` (config base + `IdleTimeCap` upgrade), rate scaled by `IdleCollectionRate` upgrade; `idleCollectionApplied` timestamp prevents double-apply. It iterates **all** `siteSnapshots` (every unlocked site earns for the offline window), falling back to the lone `idleSnapshot` on legacy/single-site saves.

## Multi-grids / sites (`SiteService`)

- `Assets/Scripts/Services/SiteService.cs` (scene singleton, sibling of `ResearchService`/`GridSaveService`): loads `Resources/SiteDatabase` (generated by the importer; index = canonical site index). `UnlockSite(id)` deducts entropy via the `PlayerProgressData` singleton and appends to `unlockedSites` (survives prestige). `SwitchTo(index)` = `SaveLocal()` (flush current site → `grids[current]` + snapshot) → set `activeSiteIndex` → `GridSaveService.ClearGrid()` → `LoadGrid(forceApply)` → first-visit `FieldGenerator.GenerateForSite()` → `SaveLocal()`. Currency/inventory are global and untouched.
- `GridSaveService.EnsureSiteGrid(save, index)` pads `grids` so a newly unlocked/visited site has a slot. `LoadGrid` restores fields even when a site has no buildings/conveyors.
- Inactive sites are NEVER live ECS (load-bearing constraint). Dev console: `site list` / `site switch <n>` / `site unlock <id>` / `domains intro` (replay the intro). UI: `sites-panel` + `SitesSubController` (Travel/Unlock).
- **Domains introduction:** one-shot dialogue `intro_quantum_domains` plays when the player unlocks `materials_science` research (the 150K gate before the first 250K domain). Routed via `HUDController.OnResearchUnlocked` → `SitesSubController.NotifyResearchUnlocked`; guarded by `save.domainsIntroSeen`. Dialogue resolved at runtime via `Resources/DialogueDatabase` (`DialogueDatabaseSO`, generated by the importer — the general runtime id→DialogueSO lookup) so no Inspector wiring is needed.
- **Prestige:** `PrestigeSaveWatcher.ResetSitesForPrestige(save)` (also called from `PrestigeSystem.OnUpdate`) clears all `grids`, resets `activeSiteIndex` to 0, and wipes `idleSnapshot` + `siteSnapshots`. `unlockedSites` is intentionally preserved (site unlocks survive prestige, like recipe knowledge).

## Adding a save field — the rules

1. **Additive only.** Never rename/remove/retype existing fields. New fields get field initializers as defaults — old JSON missing the field deserializes to the default, which must mean "feature never used".
2. Checklist: field in `SaveData.cs` (+ default) → write site (service or `FlushToSave()`) → read site (`ApplyLoadedSave()` if ECS-visible, or service `LoadFromSave`) → reset-on-prestige decision (does `PrestigeSystem.OnUpdate` need to clear it? see `PrestigeSystem.cs:103-110`) → test.
3. Test: round-trip (save → load → assert) AND legacy-load (deserialize a JSON string *without* your field, assert defaults). Copy patterns in `Assets/Scripts/PlayModeTests/SaveSystemTests.cs`.
4. Timed effects: store ISO 8601 UTC string, compare with `DateTime.UtcNow`, copy the `speedBoostExpiryUtc` trio of sites (`SaveData.cs:30`, apply `ECSLoadBridge.cs:148`, strip `ECSLoadBridge.cs:236`).

## Pitfalls

- `JsonUtility` cannot serialize dictionaries — use parallel lists (`inventoryKeys/Values`) or `[Serializable]` entry classes (`AchievementProgressEntry`).
- Anything you write to ECS after load must also be flushed back in `FlushToSave()`, or it silently reverts on next launch.
- Prestige calls `SaveManager.SaveLocal()` at the end of `PrestigeSystem.OnUpdate` — your field's post-prestige value must already be correct in ECS/SaveData by then.
