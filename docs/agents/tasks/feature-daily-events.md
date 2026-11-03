# Feature: Daily Login Rewards & Rotating Challenges

**Status:** done (PRs #54-56)
**Required reading:** `docs/agents/architecture.md`, `docs/agents/save-system.md`, `docs/agents/ui-toolkit.md` (skim `testing.md` before commit)
**Scope estimate:** ~6 files created, ~6 modified, 2 test files. Smallest of the four feature specs — do this one first.
**Branch:** `feature/daily-events` → PR into the current release branch (check with the user; `release/0.3` as of writing).

## Design summary

Mobile-idle retention layer reusing infrastructure that already exists:
1. **Login streak rewards**: a 28-day reward calendar. Each calendar day (UTC) the player logs in, they claim the next reward in the sequence; missing a day does NOT reset the streak (kindness rule — streak index just doesn't advance on missed days). After day 28, the calendar loops.
2. **Daily challenges**: 3 challenges drawn deterministically per UTC day from a pool, each granting crystals on completion. Progress tracked the same way achievements are.

Both reset at 00:00 UTC using the exact same timestamp pattern as `AchievementService.CheckPeriodResets()` (`Assets/Scripts/Achievements/AchievementService.cs:103`).

## Balance numbers (final — do not derive your own)

**Login calendar (28 days, loops):** day 1–6: 10◆; day 7: 40◆ + 500e; day 8–13: 15◆; day 14: 60◆ + 2,000e; day 15–20: 20◆; day 21: 80◆ + 10,000e; day 22–27: 25◆; day 28: 150◆ + 1✦.

**Daily challenges (3/day from pool, each 15◆):**

| id | description | trigger (existing Notify hook) | target |
|---|---|---|---|
| `dc_craft_50` | Craft 50 items | `NotifyCraft` | 50 |
| `dc_craft_200` | Craft 200 items | `NotifyCraft` | 200 |
| `dc_place_3` | Place 3 buildings | `NotifyBuildingPlaced` | 3 |
| `dc_research_1` | Complete 1 research | `NotifyResearchCompleted` | 1 |
| `dc_spend_500` | Spend 500 entropy | `NotifyEntropySpent` | 500 |
| `dc_login` | Log in (auto-completes) | `NotifyLogin` | 1 |

Daily pick = first 3 pool entries from a rotation seeded by `DateTime.UtcNow.DayOfYear % poolCount` — deterministic, no RNG state to save.

## Data schema additions (`Assets/Data/game_data.json`)

Append two top-level arrays after `"dialogues"`; bump `_meta.version` to `0.3.1`. Add matching classes/fields to `GameDataJson` in `Assets/Scripts/Editor/GameDataModel.cs` (JsonUtility silently drops unknown keys otherwise) and an import pass producing a `DailyContentSO` (copy the `ResearchSO` import pattern; see `data-pipeline.md` "Adding a NEW content type").

```json
"daily_rewards": [
  { "day": 1,  "crystals": 10,  "entropy": 0,     "prestige_currency": 0 },
  { "day": 7,  "crystals": 40,  "entropy": 500,   "prestige_currency": 0 },
  { "day": 14, "crystals": 60,  "entropy": 2000,  "prestige_currency": 0 },
  { "day": 21, "crystals": 80,  "entropy": 10000, "prestige_currency": 0 },
  { "day": 28, "crystals": 150, "entropy": 0,     "prestige_currency": 1 }
],
"daily_challenges": [
  { "id": "dc_craft_50",   "description": "Craft 50 items",        "trigger": "CraftItem",        "target": 50,  "crystals": 15 },
  { "id": "dc_place_3",    "description": "Place 3 buildings",     "trigger": "PlaceBuilding",    "target": 3,   "crystals": 15 }
]
```
(Fill all 28 days / 6 challenges per the balance tables above; days not listed use the 6-day filler value for their week.)

## Save additions (`Assets/Scripts/SaveSystem/SaveData.cs` — additive only, see save-system.md rules)

```csharp
public int    loginStreakIndex;          // 0-based position in the 28-day calendar
public string lastLoginRewardUtc;        // ISO 8601 — date of last claimed login reward
public string dailyChallengeResetUtc;    // ISO 8601 — when current challenge set expires
public List<string> dailyChallengeIds       = new();  // today's 3 challenge ids
public List<AchievementProgressEntry> dailyChallengeProgress = new();
public List<string> dailyChallengesClaimed  = new();
```

These survive prestige (do NOT add to the `PrestigeSystem` reset block).

## Files to create

| Path | Copy pattern from | Notes |
|---|---|---|
| `Assets/Scripts/Services/DailyEventService.cs` | `Assets/Scripts/Achievements/AchievementService.cs` | Singleton. `CheckDailyReset()` copies `CheckPeriodResets()` exactly (`ParseOrEpoch`/`NextMidnightUtc` patterns, `AchievementService.cs:103-158`). Subscribes to `AchievementService` Notify flow OR exposes its own `Notify*` mirrors called from the same call sites. Reward grant: crystals → `save.paidCurrency += n`; entropy → `ECSLoadBridge.Instance.AddEntropy(n)` (`ECSLoadBridge.cs:279`); ✦ → `AddPrestigeCurrency(n)`. |
| `Assets/Scripts/ScriptableObjects/DailyContentSO.cs` | `Assets/Scripts/ScriptableObjects/ResearchSO.cs` style | Holds reward calendar + challenge pool from JSON. |
| `Assets/Scripts/UI/DailyEventsSubController.cs` | `Assets/Scripts/UI/PrestigeShopSubController.cs` | `Init`/`SetECSContext`/`Refresh`; claim buttons. |
| `daily-panel` block in `Assets/UI/GameHUD.uxml` | copy `upgrades-panel` block (`GameHUD.uxml:118`) | Plus `btn-daily` in the left-drawer nav (copy `btn-upgrades`). |
| `Assets/Scripts/Tests/DailyEventServiceTests.cs` | `Assets/Scripts/PlayModeTests/AchievementResetTests.cs` | Injected timestamps; reset, streak advance, deterministic challenge pick. |
| `Assets/Scripts/PlayModeTests/DailyEventSaveTests.cs` | `Assets/Scripts/PlayModeTests/SaveSystemTests.cs` | Round-trip + legacy-load (old JSON without new fields → defaults). |

## Files to modify

| Path | Integration point |
|---|---|
| `Assets/Data/game_data.json` | new arrays (above) + `_meta.version` |
| `Assets/Scripts/Editor/GameDataModel.cs` | JSON model classes/fields |
| `Assets/Scripts/Editor/GameDataImporter.cs` | import pass → `DailyContentSO` (order: after Dialogue, no cross-refs needed) |
| `Assets/Scripts/SaveSystem/SaveData.cs` | new fields (above) |
| `Assets/Scripts/UI/HUDController.cs` | `GetComponent` field (`:106-111` region), nav button wiring (`:347-369` region), `OpenDailyPanel()` |
| `Assets/Scripts/Achievements/AchievementService.cs` | in each `Notify*` body (`:173-209`), also forward to `DailyEventService.Instance?.NotifyX(...)` — one line per hook, only for the 5 hooks the challenge pool uses |

## Implementation order

1. SaveData fields + legacy-load test (green before anything else).
2. JSON + GameDataModel + importer pass + `DailyContentSO`; run importer; data-integrity test.
3. `DailyEventService` logic (reset, streak, challenge progress) + EditMode tests with injected timestamps.
4. Notify forwarding from `AchievementService`.
5. UXML panel + `DailyEventsSubController` + HUDController wiring.
6. Full suite, manual editor check, commit.

## Test plan

- Reset rollover: now ≥ `dailyChallengeResetUtc` → new 3 challenges, progress cleared, claimed cleared.
- Streak: claim today → `loginStreakIndex+1`, same-day second claim rejected; missed day → index unchanged (no reset).
- Deterministic pick: same UTC day → same 3 ids.
- Grants: crystal claim increments `paidCurrency`; entropy claim calls `AddEntropy` (assert via ECS singleton in PlayMode).
- Save round-trip + legacy load.

## Definition of done

- [ ] `game_data.json` parses; importer run reported to user (or auto-ran via recompile)
- [ ] Full suite green BOTH platforms via `scripts/test-local.ps1`; results XML paths reported
- [ ] `DailyEventsSubController` component added to HUD GameObject in GameScene — **manual editor step, flag to user**
- [ ] Panel opens, is not clipped, claim buttons work in editor play mode
- [ ] Legacy save (pre-feature JSON) loads with defaults
- [ ] No generated `.asset` files hand-edited

## Pitfalls

- ALL time logic UTC via `DateTime.UtcNow` + ISO 8601 round-trip strings — copy `ParseOrEpoch` exactly; no local time, no `DateTime.Now`.
- Don't double-grant on the same day: claim sets `lastLoginRewardUtc` to today's UTC date; compare dates, not timestamps.
- `NotifyEntropySpent` passes a long amount — challenge progress must accumulate amount, not count of calls.
- Crystal writes go through `SaveData.paidCurrency` + `SaveManager.SaveLocal()` — there is no ECS crystal singleton.

## Out of scope

Server-authoritative time / clock-cheat prevention, push notifications, timed boost events (covered by premium shop), event-exclusive items, streak-restore purchases.
