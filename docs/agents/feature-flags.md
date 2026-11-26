# Feature Flags

Externally-controlled toggles that change behavior **without an app-store rebuild**, separated by
environment (dev vs prod). Use these to ship a feature hidden, kill a misbehaving system, or stage a
rollout. Backed by **Unity Remote Config** (UGS) — the same ecosystem as Auth/Cloud Save, so no new vendor.

> Verified against: `feature/feature-flags`, 2026-06-21; PvE tournaments (TournamentService) 2026-11-23.

## How it works

- **Provider:** Unity Remote Config (`com.unity.remote-config`, assembly `Unity.RemoteConfig`). Guarded by
  the `REMOTE_CONFIG` versionDefine in `MobileIdleBuilder.asmdef` (same pattern as `UNITY_PURCHASING`), so
  the project still compiles if the package is absent — flags just fall back to cache/defaults.
- **Resolution order (every read):** remote (fetched this session) > on-disk cache (last known values) >
  compile-time default in `FeatureFlags`. Defaults are chosen so a total fetch failure leaves the game in
  its current shipped state.
- **Cache:** `feature_flags.json` in `Application.persistentDataPath`. Loaded synchronously in
  `FeatureFlagService.Awake`, so reads are correct from the previous session *before* the network fetch
  finishes. `FetchAsync` overlays fresh remote values and rewrites the cache.
- **Environment:** inherited from the existing UGS init in `UGSCloudSaveService.InitializeAsync`
  (editor / `DEV_ENVIRONMENT` → `development`, store builds → `production`). No separate define. A value set
  in the `production` dashboard environment does not affect editor/internal builds.
- **Timing:** services read flags during `Awake`/`Start` and the HUD during init — all *after* the cache is
  loaded but the network fetch lands later (post-UGS-auth, in `SaveManager.ReconcileWithCloud`). So a flag
  change flips on the **next launch** (once cached). Acceptable for kill-switches. Same-session live
  application is intentionally out of scope (would need an `OnFlagsChanged` event).

### Key files
- `Assets/Scripts/RemoteConfig/FeatureFlags.cs` — registry: keys, defaults, typed accessors, `All[]`.
- `Assets/Scripts/RemoteConfig/FeatureFlagResolver.cs` — pure resolution/merge helpers (unit-tested).
- `Assets/Scripts/RemoteConfig/FeatureFlagService.cs` — singleton: self-bootstraps via
  `[RuntimeInitializeOnLoadMethod]` (no scene placement — no serialized fields, can't be lost in a refactor),
  loads cache, `FetchAsync`.
- Fetch hook: `SaveManager.ReconcileWithCloud` (after UGS init, before the cloud-save gate).
- Tests: `Assets/Scripts/Tests/FeatureFlagResolverTests.cs`.

## Shipped flags

| Key | Type | Default | Gates |
|---|---|---|---|
| `pve.enabled` | bool | `true` | Weekly PvE tournaments (kill-switch, 2026-11-23). Off: HUD hides `btn-tournament` (the Tournament drawer button), `TournamentService` sends no snapshots and the panel shows Disabled. Replaced `pvp.enabled` (the dead PvP stub). |
| `iap.enabled` | bool | `true` | `IAPService.Start` skips store init (purchases impossible); premium shop hides the Crystals tab. Other shop tabs (spend owned crystals) stay. |
| `ads.enabled` | bool | `true` | `AdService.Start` skips ad-provider init; HUD hides `btn-rewards`; the idle-return "Double" button is suppressed. The full reward/limit flow ships, but ads run on `MockAdProvider` until the LevelPlay SDK is wired (see `docs/runbooks/levelplay-ads-setup.md`). |
| `ads.test_mode` | bool | `true` | **Dev builds only** (`FeatureFlags.IsDevBuild`: Editor or `DEV_ENVIRONMENT`; production builds ignore the key). On: `AdService.CreateProvider` picks `TestAdProvider`, so no ad network runs and Watch opens the "Test Ad" card (`TestAdOverlayController`, `test-ad-modal` in `GameHUD.uxml`): Finish grants the reward, Skip/back grants nothing. Read once at startup, so a change applies on the next launch. Set `true` in the development environment; never create it in production. |
| `cloudsave.enabled` | bool | `true` | `SaveManager` skips cloud data sync (local-only). **UGS still initializes** so Remote Config can load — this gates save sync, not auth. |
| `dailyevents.enabled` | bool | `true` | `DailyEventService.EnsureToday` no-ops; HUD hides `btn-daily`. |
| `notifications.enabled` | bool | `true` | Local OS notifications (`NotificationService`): off schedules nothing on background and never shows the post-tutorial opt-in card. Already-scheduled ones are cancelled on the next launch/resume. See `notifications.md`. |
| `push.enabled` | bool | `true` | Remote push registration (`RemotePushService`, UGS Push Notifications). Also needs `analytics.enabled` on, since UGS targets the push token through Analytics. Off: the device never registers this launch. Already-registered tokens stay valid until the app is reinstalled. |
| `maintenance.enabled` | bool | `false` | Master maintenance switch. See "Maintenance mode" below. Default off ⇒ fail-open. |
| `maintenance.message` | string | (generic) | Body text shown on the maintenance screen. |
| `maintenance.untilUtc` | string | `""` | ISO-8601 UTC estimated-return time; blank ⇒ no time line shown. |
| `analytics.enabled` | bool | `false` | Master switch for `TelemetryService` (UGS Analytics). **Default off** ⇒ telemetry is opt-in: flip on to open a collection phase. See `analytics.md`. |
| `analytics.phase` | string | `"default"` | Label stamped on every event as `collection_phase` so collection windows stay segmentable in Data Explorer. |
| `gamedata.updatedUtc` | string | `""` | ISO-8601 UTC publish stamp of the current game values. Drives the one-shot update-notice modal (`GameUpdateNotice.ShouldShow` vs `SaveData.lastSeenGameDataUtc`); blank ⇒ never shown. Bump it to announce a balance change to everyone on next launch. |
| `gamedata.version` | int | `0` | Monotonic, human-facing version label. Low-cardinality dimension on the `game_update_notice` analytics event (rollout reach). |
| `gamedata.noticeTitle` | string | `"Game Updated"` | Title shown on the update-notice modal. |
| `gamedata.noticeMessage` | string | (generic) | Body text shown on the update-notice modal. |
| `whatsnew.version` | string | "" | Build version the what's-new notes are for (#502). Written by CI with `whatsnew.notes`. |
| `whatsnew.notes` | string | "" | Player-facing release notes (#502), from `docs/whats-new.md`. Shown once per text (`SaveData.lastSeenWhatsNewHash`) on builds >= `whatsnew.version`, never to new players (`WhatsNew.Decide`, `WhatsNewTests`). They share the update-notice card (left-aligned, scrolls); when `gamedata.updatedUtc` also changed, the balance message is appended. Blank = no notice. |
| `gamedata.overrides` | string | `"{}"` | JSON blob of curated **scalar** overrides applied onto the baked SOs at boot (`GameDataOverrides`). Covers `config` (GameConfigSO), `items` (ItemSO sell values), `research` (ResearchSO costs/durations). Empty ⇒ baked baseline. Takes effect next cold start. See "Game-data overrides" below. |
| `links.feedbackEmail` | string | `"aclemetson12492@gmail.com"` | Recipient of Settings > Support > **Send Feedback** (`mailto:` prefilled with version, player id, device; built by `HUDSettingsSubController.BuildFeedbackMailto`). Read at tap time. |
| `links.privacyPolicyUrl` | string | GitHub Pages `privacy-policy.html` URL | Opened by Settings > Support > **Privacy Policy**. Source is `docs/privacy-policy.html`, served from `develop:/docs`. Read at tap time. |
| `update.enabled` | bool | `false` | Master switch for the boot-time update prompt. See "App update prompt" below. Default off: a fetch failure never prompts. |
| `update.latest.android` / `update.latest.ios` | string | `""` | Newest published build version (semver, matches `Application.version`). An older build shows "Update" / "Later". Written by CI in **development** after each Play internal / TestFlight upload; set by hand in **production** when a release goes live. Blank: no prompt. |
| `update.minimum.android` / `update.minimum.ios` | string | `""` | Oldest supported version. A build below it gets a blocking prompt (no "Later"). Set by hand for breaking save/economy changes. Blank: never forced. |
| `update.url.android` / `update.url.ios` | string | `""` | Link the Update button opens. Blank falls back to the Play listing (Android, dev and prod; opted-in internal testers get the test build there), `itms-beta://` (iOS dev, opens TestFlight), or no button (iOS prod: set the App Store link in production before launch). |
| `update.message` | string | (generic) | Body text of the update prompt. |

## Game-data overrides

`gamedata.overrides` is a curated, **scalar-only** override layer over the baked `game_data.json` baseline (the full dataset still ships in the build and is the offline fallback). It lets you re-tune select numbers **without a store release**.

- **Schema** (arrays of entries — `JsonUtility` can't deserialize id-keyed maps):
  ```json
  { "config":   [ { "field": "prestigeBaseValue", "value": 600 } ],
    "items":    [ { "id": "iron", "field": "baseSellValue", "value": 2 } ],
    "research": [ { "id": "automation_1", "field": "durationSeconds", "value": 300 } ] }
  ```
- **Allow-list:** only fields in the `Set*Field` switches of `GameDataOverrides.cs` are honored (ids/refs/arrays are never overridable, so the importer's cross-references stay intact). Unknown field/id ⇒ ignored. See `Assets/Data/overrides.sample.json` for the documented field set.
- **Applied at boot** in-place onto the shared loaded SOs by their owners (`GameBootstrap` → GameConfig, `ItemDatabase`, `ResearchService`), so every consumer sees the override with no per-consumer change.
- **Timing:** fetched/cached via this same Feature Flag service (remote > cache > default), so a freshly-published blob takes effect on the **next cold start** — pair it with a `gamedata.updatedUtc` bump to notify players.
- **Not yet overridable:** buildings and fields (no central runtime registry — they're reached only via `BuildingPlacementController.availableBuildings` / `FieldGenerator`; a follow-up can extend `GameDataOverrides` once a registry exists).

## Maintenance mode

When `maintenance.enabled` is true the app shows the splash, then a maintenance overlay (message +
"Estimated back: <local time>" + Retry) **instead of loading GameScene**.

- **Splash gate** (`MaintenanceGate.IsUnderMaintenanceAsync`, called from `SplashScreenController`): runs a
  time-boxed UGS init + flag fetch before `SceneLoader.GoTo("GameScene")`. Checked for **every** user.
- **Mid-session** (`MaintenanceWatcher`, self-bootstrapped): polls every 5 min while in GameScene, and
  immediately when the app returns from the background (at most one check per 10 s;
  `MaintenancePollSchedule`, #224); if
  maintenance flips on it flushes the save (`SaveManager.SaveLocal` + `SaveToCloud`) and returns to the
  splash, which re-shows the overlay.
- **Fail-open:** any fetch failure/timeout ⇒ NOT in maintenance (offline players are never locked out).
- **Account-link-safe:** a brand-new user (no session) gets a throwaway anon session just to read the flag,
  then `SignOut(clearCredentials: true)` when maintenance is off, so GameScene's Google/anon sign-in is
  untouched. Env name is shared via `UgsEnvironment.Name`.
- Time formatting is the pure, unit-tested `MaintenanceGate.FormatEstimatedReturn`.

## App update prompt

On boot, an out-of-date build pauses on the splash and offers a link to update.

- **Decision** (`AppUpdateCheck.Decide`, pure, `AppUpdateCheckTests`): disabled or unparseable current version: none; current < `update.minimum.*`: **Required** (Update only); current < `update.latest.*`: **Optional** (Update / Later). Versions compare numerically via `System.Version` (`0.5.9 < 0.5.52`, missing parts = 0, `-suffix` ignored). The platform's keys are picked with `#if UNITY_IOS` (the Editor follows the active build target).
- **Where:** `SplashScreenController.ContinueOrPromptUpdate`, after the maintenance gate answers (and after a maintenance Retry succeeds). It uses the flags the gate just fetched, or the cached ones if the gate timed out. UI: `update-panel` in `SplashScreen.uxml` (reuses the maintenance panel classes; `.update-buttons`, `.update-later-btn` in `SplashScreen.uss`). Update calls `Application.OpenURL` and stays on the panel; Later loads GameScene.
- **CI writer:** `scripts/set-remote-config.ps1 -EnvironmentId <id> -Key <key> -Value <value>` (Remote Config Admin API, service-account Basic auth; GETs the environment's settings config, changes one key, PUTs the whole array back; `-DryRun` available). Called after the upload in `release-internal.yml` (`update.latest.android`) and `release-ios.yml` (`update.latest.ios`), both in **development**. The step is skipped while the secrets are missing and never fails the release (`continue-on-error`). `prod-release.yml` uploads a Play **draft**, so production's `update.latest.*` is set by hand when the release goes live.
- **What's new (#502):** `release-internal.yml` (upload job) runs `scripts/whats_new.py <version> <json>`. That strips HTML comments from `docs/whats-new.md`, caps it at 1,200 chars and skips when blank. Then `set-remote-config.ps1 -JsonFile` writes `whatsnew.notes` + `whatsnew.version` in **one PUT** (so a player on an older build never sees newer notes with an older version). Rewrite `docs/whats-new.md` in each integration -> release PR. A hotfix that leaves it unchanged republishes the same text, which nobody sees twice. Use `- ` bullets: the game font has no bullet glyph.
- **Secrets:** `UGS_GAME_CONFIG_SERVICE_KEY_ID` / `UGS_GAME_CONFIG_SERVICE_SECRET_KEY` (the UGS service account `ci-cd-automation`, which has a Remote Config edit role; the workflows map them onto the script's generic `UGS_SERVICE_KEY_ID` / `UGS_SERVICE_SECRET`, so another account can reuse the script), `UGS_PROJECT_ID`, `UGS_ENV_ID_DEVELOPMENT`, `UGS_ENV_ID_PRODUCTION` (not used by CI yet: production versions are set by hand).

## Adding a flag

1. Add a key constant, default constant, accessor, and an `All[]` entry in `FeatureFlags.cs`.
2. Wire the gate (service early-return after `base.Awake()`, and/or hide the HUD nav button in
   `HUDController.ApplyFeatureFlagGates`).
3. Create the key in the Unity Remote Config dashboard **for each environment** (development + production).
4. Update the table above.

For non-boolean remote **tuning** (not just on/off), use `GetFloat`/`GetInt`/`GetString` against the
relevant compile-time default (e.g. a `GameConfigSO` value) as the fallback.

## Future flags (documented, not built)

Candidates surfaced by the codebase audit — add when the feature lands:

- `events.enabled` / seasonal content — extends `DailyEventService`.
- `megastructure.enabled`, `multisite.enabled`, `achievements.enabled` — kill-switches for shipped systems.
- `managers.rarity.enabled` — gacha/rarity layer over `ManagerService`.
- Tournament expansion behind sub-flags: friend leaderboards, spectating.
- **Tuning** (use `GetFloat`/`GetInt`): `idle.collectionRate`, `idle.capSeconds`, `prestige.base`, IAP tier
  values — would read remote overrides against `GameConfigSO` / `PremiumShopCalculator` defaults.

## Out of scope (this feature)
- Same-session live flag application (`OnFlagsChanged`).
- Remote tuning of economy scalars (only on/off kill-switches shipped).
- Audience/Game Override targeting beyond the environment split.
- A dedicated `staging` environment.
