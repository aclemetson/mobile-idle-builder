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
- `TestAdProvider.cs` — ad test mode (`ads.test_mode`, dev builds only): no ad network, a "Test Ad"
  card instead, reward still granted. See "Ad test mode" below.
- `LevelPlayAdProvider.cs` — real SDK adapter, compiled only under `#if LEVELPLAY_ADS`.
- `AdService.cs` — selects the provider once at startup (`CreateProvider`): `TestAdProvider` when
  `FeatureFlags.AdsTestMode`, else `LevelPlayAdProvider` when `LEVELPLAY_ADS && !UNITY_EDITOR`, else Mock.
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
  identifiers — not secrets). iOS ATT and SKAdNetwork ids are wired (section 5); the remaining
  go-live work is the store privacy forms (`docs/runbooks/store-privacy-declarations.md`).

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
- Google Play: declare the advertising ID. `play-services-ads-identifier` (pulled in by
  `Assets/LevelPlay/Editor/IronSourceSDKDependencies.xml`) merges the
  `com.google.android.gms.permission.AD_ID` permission into the manifest, so LevelPlay's own
  "Declare AD_ID Permission" setting is not needed. Confirm it on a built bundle with
  `java -jar bundletool.jar dump manifest --bundle <aab>` and look for `AD_ID`. Store answers:
  `docs/runbooks/store-privacy-declarations.md`.

## 5. iOS (App Tracking Transparency + privacy)

Shipped in the repo:

- **ATT prompt:** `AdConsent.RequestIfNeededAsync()` calls the native bridge
  `Assets/Plugins/iOS/TrackingAuthorization.mm`. When the status is still undetermined it shows the system
  prompt and waits (up to 60 s) for the answer before `LevelPlay.Init`.
- **Info.plist + framework:** `Assets/Scripts/Editor/IOSTrackingPostProcess.cs` writes
  `NSUserTrackingUsageDescription` and weak-links `AppTrackingTransparency.framework` into UnityFramework.
- **SKAdNetwork ids:** added by the LevelPlay package's own iOS post-process
  (`NetworkManagerSettings.AddNetworksSkadnetworkID`, on by default while
  `Assets/LevelPlay/Editor/NetworkManagerSettings.asset` does not exist). It downloads the id lists at
  build time, so a build without network access logs an error and ships without them.
- The CocoaPods `pod install` already runs in the iOS archive script, so LevelPlay's pods are picked up.

Still to verify on the first TestFlight build: the prompt appears once on first launch, `SKAdNetworkItems`
is in the built Info.plist, and the IronSource pod's `PrivacyInfo.xcprivacy` is in the archive. Store
answers: `docs/runbooks/store-privacy-declarations.md`.

## 6. Turn it on

- Create the `ads.enabled` key in the Unity **Remote Config** dashboard for each environment (default
  `true`; flip to `false` as an instant kill-switch). See `docs/agents/feature-flags.md`.
- Build to a device with LevelPlay **test mode / test devices** enabled and verify each placement grants:
  Entropy Boost, +50% Idle (2h and 4h), Production Surge, Time Warp, Crystal Drop, the idle-return "Double", and the
  Entropy Surge side icon (after the first prestige: watch, No thanks restarts its 15 min timer, X keeps it).
- **Test Suite (no advertising IDs needed):** make a **Development Build** (Build Settings → Development
  Build → `Debug.isDebugBuild`), launch on device, open **Free Rewards → "Launch Ad Test Suite"**. The SDK
  opts in via `SetMetaData("is_test_suite","enable")` and `LevelPlay.LaunchTestSuite()`, both gated to dev
  builds (never in release). Pick the Rewarded ad unit / ironSource network → Load → Show a test ad.
- **No ad loaded = no icon.** The Entropy Surge icon and every Watch button wait for a loaded rewarded ad.
  A failed init or load (offline, no fill) is retried with backoff (`AdLoadRetry`: 5 s doubling to a 2 min
  cap, reset on success), so one failure no longer leaves the whole session without ads. To diagnose on a
  device: `adb logcat -s Unity | grep "\[Ads\]"` shows `LevelPlay initialised`, `Rewarded ad loaded`, or
  the load error and the next retry delay.

## Ad test mode (dev builds never show real ads by default)

Real ads on a developer's own phone risk invalid-traffic flags on the LevelPlay account (especially
taps). So dev builds use fake ads unless you deliberately turn them off.

- **Which builds:** `FeatureFlags.IsDevBuild` is true in the Editor and in builds compiled with
  `DEV_ENVIRONMENT` (currently every Android/iOS build, which also points them at the UGS
  "development" environment). Production builds compile it to false and ignore `ads.test_mode`.
- **Flag:** `ads.test_mode` (bool). The code default is `true`, so a fresh install, or a launch where
  Remote Config hasn't loaded, uses test ads. Create the key as `true` in the **development**
  environment only.
- **What the player sees:** the Free Rewards panel says "TEST ADS". Watching opens a "Test Ad" card:
  **Finish** = ad watched (reward granted, daily cap and cooldown counted), **Skip** / Android back =
  dismissed early (no reward). Everything after the ad runs the real `AdService` path.
- **Testing real LevelPlay ads:** first register the phone in the LevelPlay dashboard (**Settings →
  Test devices → Add test device**, using the advertising ID from Android **Settings → Security and
  privacy → Ads**, then enable test ads for it). Then set `ads.test_mode` to `false` in the
  development environment and relaunch the app twice (the first launch may still use the cached value).
  The Free Rewards panel then shows **Launch Ad Test Suite**, which is gated on `IsDevBuild`, not
  `Debug.isDebugBuild` (our internal builds are non-debuggable, so `isDebugBuild` is always false there).
- **Never tap real ads on your own device.**

## Reward / cap reference

| Placement      | Reward                                   | Daily cap |
|----------------|------------------------------------------|-----------|
| Entropy Boost  | 10% of net worth (floor 500) in entropy  | 5         |
| Double Offline | re-grants the just-collected offline run | 3         |
| +50% Idle (2h) | +50% offline collection rate for 2h      | 2         |
| +50% Idle (4h) | +50% offline collection rate for 4h      | 2         |
| Production Surge | 2× production for 30 min               | 2         |
| Time Warp      | instantly bank 1h of production          | 2         |
| Crystal Drop   | +25 crystals                             | 1         |
| Entropy Surge  | 10 min of automation income (floor 500); 15 min cooldown; offered by a side icon (`EntropySurgeOfferController`) | 8 |

Tune values in `AdRewardCalculator.Placements` and keep `docs/agents/economy-balance.md` in sync.
