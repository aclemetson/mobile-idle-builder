# Notifications

**Scope:** OS notifications on Android and iOS. Local notifications are scheduled on the device from the save
when the player leaves. Remote push campaigns sent from the Unity dashboard are planned as a follow-up (UGS Push
Notifications) and will share the same permission and settings toggle.

> Verified against: `feat/local-notifications`, 2026-11-20.

## Files

| File | Role |
|---|---|
| `Assets/Scripts/Notifications/NotificationPlanner.cs` | Pure rules (unit-tested). `NotificationInputs.FromSave` reads the pending timers from `SaveData`; `NotificationPlanner.Plan` turns them into `PlannedNotification`s (quiet hours, merging, daily cap). Also the `NotificationKind` / `NotificationChannel` enums. |
| `Assets/Scripts/Notifications/NotificationTexts.cs` | All player-facing copy (English; no localization system yet), Android channel names, the opt-in card text. |
| `Assets/Scripts/Notifications/NotificationPlatform.cs` | `INotificationPlatform` + `AndroidNotificationPlatform` / `IOSNotificationPlatform` (device builds only, `#if UNITY_ANDROID && !UNITY_EDITOR` etc.) and `EditorNotificationPlatform` (logs the plan). |
| `Assets/Scripts/Notifications/NotificationService.cs` | Self-bootstrapped singleton (`AfterSceneLoad`, persists). Lifecycle, permission, Settings toggle, opt-in card, telemetry. |
| `Assets/Scripts/Tests/NotificationPlannerTests.cs` | EditMode tests for the planner and `FromSave`. |
| `ProjectSettings/NotificationsSettings.asset` | Package settings (Project Settings > Mobile Notifications). |
| `Assets/Art/Notifications/notif_icon_small.png` | Android status-bar icon, registered there as `icon_small` (white on transparent). |

## Lifecycle

- **Leaving the app** (`OnApplicationPause(true)` / quit): `SaveManager` writes the save, then raises
  `SaveManager.Backgrounding`. `NotificationService` cancels everything pending and schedules the new plan,
  but only while `IsActive` (the `notifications.enabled` flag is on, the Settings toggle is on and OS permission is granted).
  It does not use its own pause handler for this, so it always reads the state that was just saved.
- **Coming back** (Awake / `OnApplicationPause(false)`): cancels all scheduled and delivered notifications and
  clears the iOS badge, so nothing fires while the player is in the game. If the app was opened from a
  notification, logs `notification_opened_local`.
- Failures are caught and logged; a notification problem never breaks the save path.

## What gets scheduled

| Kind | Channel | When | Source |
|---|---|---|---|
| `StorageFull` | production | now + `OfflineCollectionService.GetEffectiveIdleCap` (after the tutorial) | idle cap |
| `ResearchDone` | production | `activeResearchCompleteUtc` | research timer |
| `CraftDone` | production | `CraftQueue.SecondsRemaining` | recipe craft queue |
| `CyclotronReady` | production | `CyclotronProgress.CooldownLeft`, only if `IsUnlocked` | Cyclotron Kick |
| `BoostEnded` | boosts | `BoostLock.Remaining` (when every timed boost has ended) | speed burst, Idle ad boost, Cyclotron boost |
| `DailyReward` | rewards | next UTC midnight if today's reward was claimed, else in 4 h (before the reset) | `lastLoginRewardUtc` |
| `RewardsExpiring` | rewards | 2 h before the daily/weekly/monthly reset, only if `AchievementService.HasClaimable` for it | achievement resets |
| `Comeback24h` / `Comeback72h` | reminders | now + 24 h / 72 h (after the tutorial) | none |

The rules live in `NotificationPlanner` and are covered by the tests:
- Anything less than 60 s away is skipped.
- Quiet hours run from 22:00 to 08:00 device local time; anything that would land in them moves to 08:00.
- Production alerts within 15 min of each other merge into one `ProductionCombined` ("Your foundry needs you"), fired at the latest time.
- At most 3 notifications land in any 24 h, kept in priority order: storage > combined > research > craft > Cyclotron > boost > rewards expiring > daily > comeback.

**Clocks:** the delays are measured against `GameClock.UtcNow` (the timers' own clock), and the fire time is
`DateTime.UtcNow + delay`, because the OS fires on the wall clock. That way a playtest clock offset never
moves an OS alarm. Achievement and daily resets are server/wall time, so they use `DateTime.UtcNow` directly.

Ids are stable per kind (`1000 + kind`). Android sends with an explicit id, iOS uses that id as the identifier.

## Permission and settings

- **Opt-in card.** About 20 s after the tutorial ends (`SaveData.tutorial.isActive == false` and
  `ECSLoadBridge.IsLoaded`), when no dialogue, `ConfirmPrompt` or save-conflict prompt is open, the HUD shows
  `ConfirmPrompt` with the `NotificationTexts.Prompt*` copy and a neutral (`destructive: false`) button.
  - "Notify me" shows the OS prompt.
  - "Not now" shows no OS prompt, so the OS one-time ask is saved for later.
  - Either answer sets `SettingsData.notificationPromptShown`, so the card is shown once.
  - If the OS permission is already decided (Android 12 or lower is always granted), the card is skipped.
- **Settings toggle** `toggle-notifications` (`HUDSettingsSubController`) calls `NotificationService.SetEnabledFromSettings`:
  - Off: cancels everything.
  - On, never asked: shows the OS prompt.
  - On, denied: opens the OS notification settings, because the OS won't ask twice.
  - The toggle displays `IsActive`, not just the saved preference.
- **Android:** target SDK is automatic (33+), so `POST_NOTIFICATIONS` is a runtime permission; the package adds the manifest entry.
  - Four channels: `qf_production`, `qf_boosts` and `qf_rewards` at default importance, `qf_reminders` at low (silent). Players can mute each channel in OS settings.
  - Scheduling is inexact. `SCHEDULE_EXACT_ALARM` is not used, which avoids Play policy review.
  - "Reschedule on Device Restart" is on, which adds `RECEIVE_BOOT_COMPLETED`.
- **iOS:**
  - "Request Authorization on App Launch" is OFF (the package default is on): we ask ourselves, after the tutorial.
  - Badge 1 per notification, cleared on open.
  - Local notifications need no Push capability or entitlement.

## Kill switch and telemetry

- `notifications.enabled` (Remote Config, default `true`). See `feature-flags.md`.
- Events `notification_permission` and `notification_opened_local`. See `analytics.md`; register the schemas in the dashboard.

## Adding a notification

1. Add a `NotificationKind`. Append it at the end: ids derive from the enum value, so inserting one in the middle shifts the ids of every kind after it.
2. Add its text in `NotificationTexts.For`.
3. Add its channel in `NotificationPlanner.ChannelOf`, its rank in `Priority`, and its input in `NotificationInputs` and `FromSave`.
4. Add a test in `NotificationPlannerTests`.

## Testing on device

See `docs/runbooks/notifications-device-testing.md`.
