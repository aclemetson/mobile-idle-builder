# Feature Flags

Externally-controlled toggles that change behavior **without an app-store rebuild**, separated by
environment (dev vs prod). Use these to ship a feature hidden, kill a misbehaving system, or stage a
rollout. Backed by **Unity Remote Config** (UGS) — the same ecosystem as Auth/Cloud Save, so no new vendor.

> Verified against: `feature/feature-flags`, 2026-06-21.

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
| `pvp.enabled` | bool | `false` | `PVPService.RefreshState` forces Locked; HUD hides `btn-pvp`. Default off — backend not live. |
| `iap.enabled` | bool | `true` | `IAPService.Start` skips store init (purchases impossible); premium shop hides the Crystals tab. Other shop tabs (spend owned crystals) stay. |
| `ads.enabled` | bool | `true` | `AdService.Start` skips ad-provider init; HUD hides `btn-rewards`; the idle-return "Double" button is suppressed. The full reward/limit flow ships, but ads run on `MockAdProvider` until the LevelPlay SDK is wired (see `docs/levelplay-ads-setup.md`). |
| `cloudsave.enabled` | bool | `true` | `SaveManager` skips cloud data sync (local-only). **UGS still initializes** so Remote Config can load — this gates save sync, not auth. |
| `dailyevents.enabled` | bool | `true` | `DailyEventService.EnsureToday` no-ops; HUD hides `btn-daily`. |
| `maintenance.enabled` | bool | `false` | Master maintenance switch. See "Maintenance mode" below. Default off ⇒ fail-open. |
| `maintenance.message` | string | (generic) | Body text shown on the maintenance screen. |
| `maintenance.untilUtc` | string | `""` | ISO-8601 UTC estimated-return time; blank ⇒ no time line shown. |
| `analytics.enabled` | bool | `false` | Master switch for `TelemetryService` (UGS Analytics). **Default off** ⇒ telemetry is opt-in: flip on to open a collection phase. See `analytics.md`. |
| `analytics.phase` | string | `"default"` | Label stamped on every event as `collection_phase` so collection windows stay segmentable in Data Explorer. |
| `gamedata.updatedUtc` | string | `""` | ISO-8601 UTC publish stamp of the current game values. Drives the one-shot update-notice modal (`GameUpdateNotice.ShouldShow` vs `SaveData.lastSeenGameDataUtc`); blank ⇒ never shown. Bump it to announce a balance change to everyone on next launch. |
| `gamedata.version` | int | `0` | Monotonic, human-facing version label. Low-cardinality dimension on the `game_update_notice` analytics event (rollout reach). |
| `gamedata.noticeTitle` | string | `"Game Updated"` | Title shown on the update-notice modal. |
| `gamedata.noticeMessage` | string | (generic) | Body text shown on the update-notice modal. |
| `gamedata.overrides` | string | `"{}"` | JSON blob of curated **scalar** overrides applied onto the baked SOs at boot (`GameDataOverrides`). Covers `config` (GameConfigSO), `items` (ItemSO sell values), `research` (ResearchSO costs/durations). Empty ⇒ baked baseline. Takes effect next cold start. See "Game-data overrides" below. |

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
- **Mid-session** (`MaintenanceWatcher`, self-bootstrapped): polls every 5 min while in GameScene; if
  maintenance flips on it flushes the save (`SaveManager.SaveLocal` + `SaveToCloud`) and returns to the
  splash, which re-shows the overlay.
- **Fail-open:** any fetch failure/timeout ⇒ NOT in maintenance (offline players are never locked out).
- **Account-link-safe:** a brand-new user (no session) gets a throwaway anon session just to read the flag,
  then `SignOut(clearCredentials: true)` when maintenance is off, so GameScene's Google/anon sign-in is
  untouched. Env name is shared via `UgsEnvironment.Name`.
- Time formatting is the pure, unit-tested `MaintenanceGate.FormatEstimatedReturn`.

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
- PVP expansion behind sub-flags: friend leaderboards, spectating.
- **Tuning** (use `GetFloat`/`GetInt`): `idle.collectionRate`, `idle.capSeconds`, `prestige.base`, IAP tier
  values — would read remote overrides against `GameConfigSO` / `PremiumShopCalculator` defaults.

## Out of scope (this feature)
- Same-session live flag application (`OnFlagsChanged`).
- Remote tuning of economy scalars (only on/off kill-switches shipped).
- Audience/Game Override targeting beyond the environment split.
- A dedicated `staging` environment.
