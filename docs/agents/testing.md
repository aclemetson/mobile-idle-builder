# Testing

**Scope:** Test layout, exact runner invocation, and what kind of test each change needs. Read before committing anything.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## Layout

| Suite | Location | Assembly | Notes |
|---|---|---|---|
| EditMode (19 files) | `Assets/Scripts/Tests/` | `MobileIdleBuilder.Tests` | Pure logic + manually-ticked ECS worlds. Fast. |
| PlayMode (8 files) | `Assets/Scripts/PlayModeTests/` | `MobileIdleBuilder.PlayModeTests` | Scene/service integration (save round-trips, services). |

## Running (the only sanctioned way)

```powershell
.\scripts\test-local.ps1 -TestPlatform editmode
.\scripts\test-local.ps1 -TestPlatform playmode
```

- Default Unity path: `C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe` (override with `-UnityPath`).
- The script kills orphaned batchmode Unity processes, refuses to run if the editor has the project open, runs `-batchmode -nographics -runTests -testResults TestResults\<platform>.xml`, waits up to 30 min, parses the NUnit XML, exit code 0 = all pass.
- **Never** add `-quit` to a `-runTests` invocation (kills Unity before results are written). **No results XML = the run did not happen** — check `TestResults\unity-<platform>.log`, do not assume pass (CLAUDE.md rules).
- CLAUDE.md mandates the FULL suite (both platforms) before commit. The `commit-unity` skill wraps test → commit → push → PR.

## Pattern tests to copy

| When you write... | Copy |
|---|---|
| ECS system test (manual world tick) | `Assets/Scripts/Tests/ProductionSystemTests.cs`, `PrestigeSystemTests.cs` |
| Pure calculator/service logic | `Assets/Scripts/Tests/PremiumShopCalculatorTests.cs`, `OfflineCollectionServiceTests.cs` |
| SO/data integrity after import | `Assets/Scripts/Tests/SOSchemaTests.cs`, `RecipeValidationTests.cs`, `ItemBalanceTests.cs` |
| Save round-trip / legacy load | `Assets/Scripts/PlayModeTests/SaveSystemTests.cs` |
| Service with save interaction | `Assets/Scripts/PlayModeTests/AchievementServiceTests.cs`, `AchievementResetTests.cs` (UTC period resets), `ResearchServiceTests.cs` |

## What to test per change type

| Change | Required tests |
|---|---|
| `game_data.json` content | EditMode data-integrity (extend `RecipeValidationTests`/`ItemBalanceTests` if new invariants) + JSON parses |
| New save field | PlayMode round-trip + legacy-load (missing field → default) |
| New ECS system/component | EditMode manual-world test of the core rule |
| New service | EditMode unit if pure; PlayMode if it touches SaveManager/scene |
| UI panel | `HUDControllerTests.cs` pattern for wiring; manual visibility check in editor (tests can't catch clipping) |
| Timed/reset logic | Copy `AchievementResetTests.cs` — inject timestamps, never sleep |

## Gotchas

- `SingletonMonoBehaviour` logs an error on duplicate instances, which NUnit treats as failure — tests use the established setup/teardown patterns in existing service tests; copy them rather than instantiating singletons ad-hoc.
- PlayMode tests back up and restore the real save file — copy the backup/restore fixture from `SaveSystemTests.cs`, don't write your own.
- Tests run against **generated SO assets** — if you changed `game_data.json` and didn't re-import, tests validate stale data.
