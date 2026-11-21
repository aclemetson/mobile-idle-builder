# Push campaigns (UGS Push Notifications)

How to send notifications to every player, or to a segment, right away or at a scheduled time, from the
Unity Cloud dashboard. The code side is `RemotePushService` (see `docs/agents/notifications.md`).

## One-time setup

Android was set up on 2026-11-20. iOS (APNs key) is still to do. Repeat a step only for a new environment or a lost key.

**Android (FCM):**
1. Firebase project `mobile-idle-builder` (number 835677862122), with Android app `com.clemtek.mobileidlebuilder` registered. `google-services.json` is at the repo root, gitignored.
2. Unity reads the four identifiers from `Assets/Resources/pushNotificationsSettings.asset` (Project Settings > Services > Push Notifications).
3. A Google Cloud service account (for example `unity-push-sender`) with only the **Firebase Cloud Messaging API Admin** role. If a send fails with a permission error, also give it **Firebase Viewer**.
4. That account's JSON key is uploaded to Push Notifications > Settings > Google key in **each** Unity environment. Keep the JSON in a password manager or `secrets/`; never commit it, and it doesn't need to be a GitHub secret.
5. Google Cloud > APIs & Services lists **Firebase Cloud Messaging API** as enabled.

**iOS (APNs):**
1. In the Apple Developer portal, go to Keys and create a key with **Apple Push Notifications service** enabled. Download the .p8; Apple only lets you download it once.
2. Upload it at Push Notifications > Settings > Apple key, with:
   - Key ID
   - Team ID
   - Topic = the iOS bundle id
   - **Sandbox = false**, because TestFlight and App Store builds use production APNs
3. Upload it to **each** Unity environment you send from. TestFlight builds use development, like the Android internal builds.
4. The Xcode capability is added by the push package at build time. Nothing in CI changes. The package also adds a `notificationservice` app extension, and cloud signing registers its App ID on the first build.
5. The dashboard may count a campaign aimed at every platform as incomplete while either key is missing. 2026-11-21: with no Apple key, an Android campaign showed Completed but sent nothing, while Test on Device worked. This is unconfirmed: if campaigns still don't send with both keys present, contact Unity support.

**Remote Config / Analytics:**
- `analytics.enabled` must be **true**. Devices only register for push while analytics is collecting.
- `push.enabled` defaults to true; create the key only if you need the kill switch.
- Register the `notification_preference` event schema: enabled (Boolean). The other notification event schemas were registered earlier.

## Check a single device

1. Install a dev build (internal track). Allow notifications, either through the post-tutorial card or with `notif permission` in the dev console.
2. In the dev console, run `push status`. It should show `registered=True` and a token. If not, the reason in brackets says what is missing. The device log also prints `[Push] Registered ... Token: ...`.
3. In the dashboard, open Push Notifications > Campaigns, create or open a campaign, go to **Content**, and use **Test on Device**. Paste the token and send.
4. Background the app: the notification appears in the tray. With the app open, it shows as the in-game banner instead.

## Send a campaign to everyone

1. Push Notifications > Campaigns > **Create campaign**. Campaigns are separate per environment, so check you are in **production** for real players.
2. **Audience:**
   - Leave it empty to reach every registered player.
   - To respect the in-game toggle, use an audience that excludes players whose latest `notification_preference.enabled` is false.
3. **Content:** a title, a body, and optionally an image.
4. **Schedule:**
   - **Send once** at a time. With **Player timezone** on, set it at least 13 h ahead of UTC now so every timezone gets it.
   - Or **Ongoing** between two dates, sent as players enter the audience.
5. Set a **cooldown** so the same player doesn't get it twice in a row.
6. Results show in the campaign report and in Analytics (`outOfGameSend`, `notificationOpened`).

## Troubleshooting

- `push status` says "analytics not collecting": `analytics.enabled` is off for this environment.
- Registered, but nothing arrives on Android:
  - Check the Google key is uploaded in **this** environment.
  - Check the FCM API is enabled.
  - Check the service account role.
- iOS shows "no valid aps-environment entitlement": the build did not run the push package's post-process, or the signing profile is missing the Push capability.
- iOS registered, but nothing arrives: the dashboard Apple key has Sandbox = true while the build is TestFlight or App Store, or the other way round.
