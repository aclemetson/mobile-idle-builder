# Rewarded Ads — Unity LevelPlay Go-Live Runbook

The rewarded-ads feature ships **fully built but running on a mock ad provider**. All reward logic,
per-placement daily caps, the Free Rewards panel, and the "double offline earnings" prompt work today in
the Editor and CI via `MockAdProvider` (instant reward, no network). This runbook covers the manual,
account-level steps to swap in the real **Unity LevelPlay** SDK and go live on Android + iOS.

Nothing here is wired into the build pipeline yet, on purpose: adding a half-configured ad SDK (no app
key / ad units / Android resolver) would risk the currently-green Android and iOS pipelines for no
functional gain. Do these steps when you are ready to ship ads.

## Architecture recap (already in the repo)

- `Assets/Scripts/Services/Ads/IAdProvider.cs` — the seam AdService talks to.
- `MockAdProvider.cs` — default everywhere except a device build with `LEVELPLAY_ADS` defined.
- `LevelPlayAdProvider.cs` — real SDK adapter, compiled only under `#if LEVELPLAY_ADS`.
- `AdService.cs` — selects the provider: `LevelPlayAdProvider` when `LEVELPLAY_ADS && !UNITY_EDITOR`, else Mock.
- `AdRewardCalculator.cs` — placement table, reward math, daily-cap bookkeeping (pure, unit-tested).
- Kill-switch: the `ads.enabled` Remote Config flag (`FeatureFlags.AdsEnabled`). Off ⇒ provider never
  initialises and the Free Rewards nav button is hidden.

## 1. LevelPlay dashboard

1. Create / open the app in the [LevelPlay (ironSource) dashboard](https://platform.ironsrc.com/).
2. Add **two app entries** — one Android, one iOS — and copy each **App Key**.
3. Create a **Rewarded Video** ad unit for each platform; copy each **Ad Unit ID**.
4. (Optional, recommended) add mediation networks later; not required for first launch.
5. Put the keys/IDs into `LevelPlayAdProvider.cs` (the `REPLACE_WITH_*` constants), or better, load them
   from a config asset / Remote Config so they are not hard-coded.

## 2. Add the SDK package

In `Packages/manifest.json` add:

```json
"com.unity.services.levelplay": "8.x.x"
```

(Use the latest verified version. After import, run any **Force Resolve** the package prompts for and
verify the Android External Dependency Manager resolves Google Play services.)

## 3. Enable the integration

Add `LEVELPLAY_ADS` to **Scripting Define Symbols** for Android and iOS
(`Project Settings → Player → Other Settings → Scripting Define Symbols`, or via the build script).
With the define set, device builds use `LevelPlayAdProvider`; the Editor stays on the mock.

Then verify the API calls in `LevelPlayAdProvider.cs` against the installed SDK version — class/method
names (`LevelPlay.Init`, `LevelPlayRewardedAd`, the event names) can change between major versions. The
structure (init → load → readiness → show-with-result-callback) must not change; AdService depends on it.

## 4. Android

- The package's Gradle dependencies are injected by the External Dependency Manager; no manual edit of
  `Assets/Plugins/Android/mainTemplate.gradle` should be needed. If a network adapter needs an extra
  repo/dependency, add it there (next to the existing `play-services-auth` line).
- Google Play: declare the advertising ID. Recent Google Play services include the
  `com.google.android.gms.permission.AD_ID` permission automatically; confirm it is present in the built
  AAB and disclose ad usage in the Play Console Data Safety form.

## 5. iOS (App Tracking Transparency + privacy)

- Add `NSUserTrackingUsageDescription` to the Info.plist. Inject it in `scripts/archive-upload-ios.sh`
  next to the existing `ITSAppUsesNonExemptEncryption` step, e.g.:

  ```sh
  /usr/libexec/PlistBuddy -c "Add :NSUserTrackingUsageDescription string 'We use your data to show you more relevant ads.'" "$PLIST" || \
  /usr/libexec/PlistBuddy -c "Set :NSUserTrackingUsageDescription 'We use your data to show you more relevant ads.'" "$PLIST"
  ```

- Implement the ATT request in `AdConsent.RequestIfNeededAsync()` (currently a no-op scaffold) — request
  tracking authorization and await the user's choice before `LevelPlay.Init`. The CocoaPods `pod install`
  already runs in the iOS archive script, so LevelPlay's pods are picked up automatically.
- Verify the SDK's `PrivacyInfo.xcprivacy` is included and the App Store privacy questionnaire matches.

## 6. Turn it on

- Create the `ads.enabled` key in the Unity **Remote Config** dashboard for each environment (default
  `true`; flip to `false` as an instant kill-switch). See `docs/agents/feature-flags.md`.
- Build to a device with LevelPlay **test mode / test devices** enabled and verify each placement grants:
  Entropy Boost, +50% Idle, Production Surge, Time Warp, Crystal Drop, and the idle-return "Double".

## Reward / cap reference

| Placement      | Reward                                   | Daily cap |
|----------------|------------------------------------------|-----------|
| Entropy Boost  | 10% of net worth (floor 500) in entropy  | 5         |
| Double Offline | re-grants the just-collected offline run | 3         |
| +50% Idle      | +50% offline collection rate for 4h      | 3         |
| Production Surge | 2× production for 30 min               | 2         |
| Time Warp      | instantly bank 1h of production          | 2         |
| Crystal Drop   | +25 crystals                             | 1         |

Tune values in `AdRewardCalculator.Placements` and keep `docs/agents/economy-balance.md` in sync.
