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
| `cloudsave.enabled` | bool | `true` | `SaveManager` skips cloud data sync (local-only). **UGS still initializes** so Remote Config can load — this gates save sync, not auth. |
| `dailyevents.enabled` | bool | `true` | `DailyEventService.EnsureToday` no-ops; HUD hides `btn-daily`. |

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

- `ads.enabled` — rewarded video (`AdService`, future).
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
