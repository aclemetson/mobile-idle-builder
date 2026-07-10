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

## 0. Status — already wired

- The **`com.unity.services.levelplay` 9.4.1** package is already in `Packages/manifest.json`.
- `MobileIdleBuilder.asmdef` references `Unity.LevelPlay` and auto-defines **`LEVELPLAY_ADS`** via a
  versionDefine (same package→define pattern as `UNITY_PURCHASING`). So `LevelPlayAdProvider` already
  compiles against the installed SDK; **device builds use it, the Editor stays on the mock**.
- The adapter is verified against the 9.4.1 API (`LevelPlay.Init(appKey)`,
  `new LevelPlayRewardedAd(adUnitId)`, `OnAdRewarded(info, reward)` / `OnAdClosed(info)` / `OnAdDisplayFailed(info, error)`).
- **App Key + Rewarded Ad Unit ID per platform are wired** in `LevelPlayAdProvider.cs` (public client
  identifiers — not secrets). Remaining go-live work is platform/store config: iOS ATT
  (`NSUserTrackingUsageDescription` + `AdConsent`), SKAdNetwork ids, and store privacy forms (below).

## 1. Your identifiers — what goes where

> The numeric **Unity Ads Game IDs** are NOT what the SDK init takes. `LevelPlay.Init` needs the
> alphanumeric **App Key**. The Game IDs configure the Unity Ads *network adapter* in the dashboard.

| You have | Type | Where it goes |
|---|---|---|
| Org Core ID `4327788` | Unity org id | Account-level only; not used in code or per-app config. |
| iOS Game ID `6141453` | Unity Ads Game ID | *Optional* — only if adding Unity Ads as an extra network (dashboard → SDK Networks). Unused with ironSource-only. |
| Android Game ID `6141452` | Unity Ads Game ID | *Optional* — only if adding Unity Ads as an extra network (dashboard → SDK Networks). Unused with ironSource-only. |
| **App Key** (Android + iOS) | LevelPlay app key | **Code** — `LevelPlayAdProvider.AppKey`. Find it in **Project Settings → LevelPlay → Apps** ("AppKey: ...") or the dashboard per app. |
| **Rewarded Ad Unit ID** (Android + iOS) | LevelPlay ad unit | **Code** — `LevelPlayAdProvider.RewardedAdUnit`. Create a **Rewarded** ad unit per platform in the dashboard. |

## 2. Dashboard setup

1. In the [LevelPlay dashboard](https://platform.ironsrc.com/), confirm the **Android and iOS app**
   entries exist (each has an **App Key**).
2. Ensure **at least one ad network** is enabled (mediation has no demand otherwise). **ironSource
   Bidding** is enabled and is sufficient on its own to serve and test rewarded ads. Adding **Unity Ads
   (LevelPlay)** as an extra network — entering your **Game IDs** (Android `6141452`, iOS `6141453`) under
   **SDK Networks** — is *optional* and only increases fill/revenue. With ironSource-only, the Game IDs
   are unused.
3. Create a **Rewarded Video** ad unit per platform → copy each **Ad Unit ID**.
4. After any package/network change, run the LevelPlay **Integration Manager** in the Editor and any
   **Force Resolve** the Android External Dependency Manager prompts for.

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
- **Test Suite (no advertising IDs needed):** make a **Development Build** (Build Settings → Development
  Build → `Debug.isDebugBuild`), launch on device, open **Free Rewards → "Launch Ad Test Suite"**. The SDK
  opts in via `SetMetaData("is_test_suite","enable")` and `LevelPlay.LaunchTestSuite()`, both gated to dev
  builds (never in release). Pick the Rewarded ad unit / ironSource network → Load → Show a test ad.

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
