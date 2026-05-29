# Testing Notes & Software Engineering Observations

This document captures software engineering observations made while auditing test coverage. Items are not bugs — they are tradeoffs worth discussing before the codebase grows further.

---

## Test Coverage Map (as of 2026-05-29)

### Edit Mode Tests (`Assets/Scripts/Tests/`)
| Test file | Source covered |
|---|---|
| HUDControllerTests | HUDController |
| PrestigeSystemTests | PrestigeSystem |
| RecipeValidationTests | RecipeSO / RecipeDatabase |
| TutorialSystemTests | TutorialSystem |
| CollectorSystemTests | CollectorSystem |
| EntropySinkSystemTests | EntropySinkSystem |
| PVPSystemTests | PVPSystem |
| ProductionSystemTests | ProductionSystem |
| SOSchemaTests | ScriptableObjects schema validation |

### Play Mode Tests (`Assets/Scripts/PlayModeTests/`)
| Test file | Source covered |
|---|---|
| AchievementServiceTests | AchievementService |
| ManualCraftServiceTests | ManualCraftService |
| ResearchServiceTests | ResearchService |
| SaveSystemTests | SaveManager / LocalSaveService |
| SettingsServiceTests | SettingsService |
| **RecipeKnowledgeServiceTests** *(new)* | **RecipeKnowledgeService** |

### Next highest-priority gaps
1. `GridOccupancy` — placement logic relied on by BuildingPlacer, ConveyorPlacer, and DeconstructController; pure spatial logic is well-suited to Edit Mode tests
2. `PortUtils` — pure static utility; straightforward Edit Mode tests
3. `PowerGridSystem` — no tests despite being an ECS system similar to the already-tested ones
4. `SlotBufferUtils` — pure utility; easy to add

---

## Software Engineering Observations

### 1. `RecipeKnowledgeService` — no interface, tight coupling to file system

**What:** `RecipeKnowledgeService` accesses `File.ReadAllText` / `File.WriteAllText` directly and calls `RecipeDatabase.Instance` inside `Start()`. Any code that depends on it cannot be tested in Edit Mode without the full MonoBehaviour lifecycle.

**Suggestion:** Extract an `IRecipeKnowledgeService` interface with `IsKnown(string)` and `MarkKnown(string)`. The Play Mode singleton remains the production impl; tests and other systems depend on the interface. This would also make future mock-based testing of `ResearchService` and `HUDController` (both consumers) straightforward.

**Priority:** Low — the current Play Mode tests cover the contract adequately for now.

---

### 2. `RecipeKnowledgeService.FindEntry` — O(n) linear scan

**What:** `FindEntry` iterates the entries list on every `IsKnown` / `MarkKnown` call. For a handful of recipes this is fine; it becomes a hot path if the recipe count grows significantly or if `IsKnown` is called per-frame.

**Suggestion:** Use a `Dictionary<string, RecipeKnowledgeEntry>` as an in-memory index rebuilt on load, with the `List<RecipeKnowledgeEntry>` retained only for serialisation.

**Priority:** Negligible until recipe count exceeds ~100. Flag for revisit if production sees frame-rate anomalies in the recipe UI.

---

### 3. `SyncWithRecipeDatabase` called in `Start()` — implicit ordering dependency

**What:** `RecipeKnowledgeService.Start()` calls `RecipeDatabase.Instance` and silently skips if null. This works because Unity calls `Start()` in insertion order after all `Awake()` calls in the frame, but the ordering is not explicit and can break if execution order settings change or if the RecipeDatabase is loaded asynchronously in a later phase.

**Suggestion:** Either add a `[DefaultExecutionOrder]` attribute to `RecipeDatabase` ensuring it runs before `RecipeKnowledgeService`, or move the sync call to a later lifecycle hook (e.g., an `OnEnable` triggered from `GameBootstrap` after the full scene is loaded).

The `RecipeKnowledgeService` already has `[DefaultExecutionOrder(-70)]`; `RecipeDatabase` does not appear to have one set.

**Priority:** Medium — document the dependency in `RecipeDatabase.cs` with a comment, or add `[DefaultExecutionOrder(-80)]` to `RecipeDatabase` to formalise the contract.

---

### 4. `SingletonMonoBehaviour` — duplicate detection logs an error but continues

**What:** When a duplicate singleton is detected, `GameLogger.Error` is called (which maps to `Debug.LogError`). In Unity Test Framework, `Debug.LogError` causes the test run to record a failure. If a test accidentally spawns a second instance of a singleton, it will silently mark a test as failed via the error log even though the assertions pass.

**Suggestion:** Tests should ensure they `Object.Destroy` their singleton GameObjects in `[UnityTearDown]` with `yield return null` so `OnDestroy` fires (clearing `Instance`) before the next `[SetUp]` runs. All existing Play Mode tests already follow this pattern — enforce it in code review for any new service tests.

---

### 5. Play Mode tests backup/restore the save file — correct but fragile

**What:** Every Play Mode test manually backs up and restores files in `Application.persistentDataPath`. If a test crashes mid-run the backup is never restored, potentially corrupting the developer's local save.

**Suggestion:** Consider a `TestSandboxScope` helper that redirects `Application.persistentDataPath` to a temp directory for the duration of the test suite, eliminating the manual backup/restore pattern entirely. This is a moderate refactor but would make every service test simpler and more reliable.

**Priority:** Low — the current approach works and is well-established in the codebase.
