# Local notifications: device testing

How to check local notifications (`NotificationService`, see `docs/agents/notifications.md`) on a real phone.
The Editor only logs the plan (`[Notifications] (editor) would schedule ...`), so OS behaviour needs a device.

## Editor smoke check

1. Enter Play mode with a save past the tutorial. Back up the save first, because Play mode touches the cloud save.
2. Start a research. The Editor does not reliably send an application pause, so call
   `NotificationService.Instance.ScheduleFromSave()` yourself (for example with the MCP `execute_code` tool).
   It runs even while the Settings toggle or permission is off.
3. The console lists the planned notifications with UTC and local times. Check that the quiet-hours shift and the merging look right.

## Android (internal track build)

1. Fresh install on Android 13 or newer. Play through the tutorial. About 20 s after it ends, the "Stay in the
   loop?" card appears. Tap **Notify me**, and the OS permission dialog appears. Allow it.
2. Start a short research (or queue a craft), then press Home.
   `adb shell dumpsys notification | grep -A3 qf_` lists the channels. `adb shell dumpsys alarm | grep <package>` shows
   the pending alarms.
3. Wait for the research time. The notification arrives in the **Production** channel with the atom icon.
   It may arrive a few minutes late, because scheduling is inexact.
4. Open the app from the notification. `adb logcat -s Unity | grep Notifications` shows
   "Opened from notification ResearchDone". All other pending notifications are cancelled.
5. Reboot with something still scheduled. It still fires (reschedule on restart).
6. Settings > Push Notifications **off**, then Home. Nothing fires. Turn it back on, and there is no prompt (already granted).
7. Revoke the permission in Android settings, then turn the toggle on in game. The Android app notification settings page opens.
8. Android 12 or lower: no card and no prompt (permission is implicit). Notifications work straight away.

## iOS (TestFlight)

1. Fresh install: no permission prompt at launch. The card after the tutorial leads to the iOS prompt.
2. Background with a research running. The notification arrives, the app icon shows badge 1, and opening the app clears it.
3. Deny the prompt, then turn the toggle on in Settings. iOS Settings opens on the app page.

## Troubleshooting

- **Nothing scheduled:** look for `[Notifications] Scheduled N:` in the log. If it's missing, `IsActive` was false:
  the flag, the toggle or the permission is off.
- **Android shows a white square icon:** check that `icon_small` is still listed in Project Settings > Mobile Notifications.
- **iOS prompt at launch:** "Request Authorization on App Launch" was turned back on in
  `ProjectSettings/NotificationsSettings.asset`.
