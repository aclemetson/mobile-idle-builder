# Local notifications: device testing

How to check local notifications (`NotificationService`, see `docs/agents/notifications.md`) on a real phone.
The Editor only logs the plan (`[Notifications] (editor) would schedule ...`), so OS behaviour needs a device.

## Editor smoke check

1. Enter Play mode with a save past the tutorial. Back up the save first, because Play mode touches the cloud save.
2. Start a research. The Editor does not reliably send an application pause, so call
   `NotificationService.Instance.ScheduleFromSave()` yourself (for example with the MCP `execute_code` tool).
   It runs even while the Settings toggle or permission is off.
3. The console lists the planned notifications with UTC and local times. Check that the quiet-hours shift and the merging look right.

## Dev console shortcuts (Editor and dev builds)

Real timers and quiet hours make testing slow; these skip them. Open the console by shaking the phone (three quick shakes) on device, or with the backquote key in the Editor.

| Command | Does |
|---|---|
| `notif status` | flag / Settings toggle / OS permission / active state / platform class |
| `notif plan` | what backgrounding right now would schedule, with local fire times |
| `notif test <seconds>` | one test notification N seconds from now, no quiet hours or cap. **Background the app before it fires**: coming back cancels everything pending. |
| `notif permission` | shows the OS permission prompt (if the OS still allows asking) |
| `notif prompt` | resets the opt-in answers (asked, next ask date, "Don't ask again", on-time card) and shows the card now |
| `notif exact` | shows the on-time reminders card (Android "Alarms & reminders") now; `notif status` shows `exactNeedsGrant` / `exactAsked` |

## Getting a build on an Android phone

Pick one:

- **Play internal track (recommended for real-world checks):** upload a `BuildAndroidDevelopment` AAB the usual way. It updates over the installed copy and is signed like production.
- **Sideload a dev APK (fastest loop):**
  1. Close the Unity Editor (batch builds collide with an open Editor, and `BuildScript` exits Unity).
  2. Run `MobileIdleBuilder.Editor.BuildScript.BuildAndroidDevelopmentApk` through `scripts/build-android.ps1 -ExecuteMethod MobileIdleBuilder.Editor.BuildScript.BuildAndroidDevelopmentApk`. This needs `secrets/user.keystore` and the `ANDROID_KEYSTORE_PASS` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASS` variables.
  3. Uninstall the Play copy first (`adb uninstall com.clemtek.mobileidlebuilder`): Play re-signs apps, so a locally signed APK cannot update it. Cloud save restores progress after sign-in.
  4. `adb install -r build/Android/<name>.apk`.

## Android checks

1. Fresh install on Android 13 or newer. Play through the tutorial. About 8 s after it ends, the "Stay in the
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
9. Re-ask: answer **Not now**, close and reopen the app: no card. `notif status` shows `nextAsk` 30 days out. Run `notif prompt`, tick **Don't ask again**, tap **Not now**: `neverAsk=True`, and the card never comes back.
10. On-time reminders, Android 14+ fresh install: tap **Notify me** and allow. The "Reminders on time?" card follows. **Open settings** lands on "Alarms & reminders" for the app; turn it on and go back. `notif status` shows `exactNeedsGrant=False`. Start a research, background: `adb shell dumpsys alarm` shows the game's alarm as exact (no `window=+10m`). Comeback alarms keep `window=+1h`.
11. On-time card is asked once: answer **Not now**, relaunch: no card. A player who already had notifications on gets it once at startup instead of the opt-in card. Android 12/13 normally has the grant already, so no card.
12. Entropy Surge (after the first prestige): watch a surge, background. `notif plan` shows `SurgeReady` 2 h out (or later if a boost or the daily cap holds it).

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
