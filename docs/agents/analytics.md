# Analytics / Telemetry

Player-data collection for **economy balancing** — how currency rates scale, how net worth grows per
run, when/how often players prestige, how big builds get, where progression stalls. Backed by **UGS
Analytics** (`com.unity.services.analytics`), the same ecosystem as Auth / Cloud Save / Remote Config.

> Verified against: `feature/player-telemetry-analytics`, 2026-06-23.

## How it works

- **Package:** `com.unity.services.analytics` 6.3.0 (depends on `services.core` 1.16.0, already present).
  Guarded by the `ANALYTICS` versionDefine in `MobileIdleBuilder.asmdef` (same pattern as `REMOTE_CONFIG`),
  so the project still compiles if the package is absent — the sink just no-ops.
- **Sink abstraction:** `IAnalyticsSink` (interface) → `UnityAnalyticsSink` (live, wraps
  `AnalyticsService.Instance.StartDataCollection()` / `RecordEvent(CustomEvent)`). Tests inject a recording
  mock. A second sink could be added later without touching any gameplay tap (mirrors `ICloudSaveService`).
- **Service:** `TelemetryService` — self-bootstrapping singleton (`[RuntimeInitializeOnLoadMethod]`, no scene
  placement, persists across scenes), never throws. Started from `SaveManager.InitialCloudReconcile` *after*
  reconcile so UGS auth + Remote Config flags are ready and `playerId` is synced. Runs on **every** exit path
  (including when the cloud-save kill-switch is off — analytics is independent of cloud save).
- **Gating / phasing:** collection is **off by default**, gated by the `analytics.enabled` flag. Every event
  is stamped with `player_id` and `collection_phase` (`analytics.phase` flag) so collection windows stay
  segmentable. Flags are fetched once per launch ⇒ a toggle takes effect on the **next app start** (right
  granularity for phase boundaries). See `feature-flags.md`.
- **Free tier:** UGS Analytics is free to 50k MAU with 13-month raw retention; dashboard/Data-Explorer CSV
  export is free. (Automated Snowflake "Data Access" raw streaming is the paid add-on — not used.)

### Key files
- `Assets/Scripts/Analytics/IAnalyticsSink.cs` — backend abstraction.
- `Assets/Scripts/Analytics/UnityAnalyticsSink.cs` — live UGS sink (`#if ANALYTICS`).
- `Assets/Scripts/Analytics/TelemetryService.cs` — singleton: `StartIfEnabled`, `Record*`, snapshot loop.
- Flags: `analytics.enabled` / `analytics.phase` in `FeatureFlags.cs`.
- Taps: `SaveManager.InitialCloudReconcile` (start), `AchievementService.Notify{BuildingPlaced,
  ResearchCompleted,TierReached}`, `PrestigeSystem.OnUpdate` (rich pre-reset event), `MegastructureService.Deduct`.
- Tests: `Assets/Scripts/Tests/TelemetryServiceTests.cs`.
- Dashboard + data dictionary: `docs/analytics/`.

## Events (v1)

Custom events must also be registered as schemas in the UGS dashboard (snake_case). Every event additionally
carries `player_id` + `collection_phase`.

| Event | Params | Tap |
|---|---|---|
| `prestige_completed` | run_count, networth_before, prestige_currency_earned, playtime_run_sec, building_count, highest_tier | `PrestigeSystem.OnUpdate` (values captured pre-reset) |
| `building_placed` | building_id, building_count_after | `AchievementService.NotifyBuildingPlaced` |
| `research_completed` | research_id, entropy_spent_total | `AchievementService.NotifyResearchCompleted` |
| `tier_reached` | tier | `AchievementService.NotifyTierReached` (no caller yet — fires when one is added) |
| `megastructure_stage` | stage | `MegastructureService.Deduct` on stage completion |
| `player_snapshot` | networth, base_currency, entropy_per_sec, prestige_currency, paid_currency, prestige_count, building_count, highest_tier, megastructure_stage, research_unlocked_count, playtime_total_sec | snapshot loop (5 min) + each prestige |

`entropy_per_sec` = NetWorth growth rate over the snapshot interval — a clean income-rate proxy, since
spending entropy moves BaseCurrency→TotalEntropySpent without changing NetWorth (only production raises it).
`playtime_*_sec` are session-relative (`realtimeSinceStartup`), not lifetime totals.

## Reading the data
Unity Dashboard → Analytics → Data Explorer (free, built-in). For the custom charts, one-click **Export CSV**
and open `docs/analytics/dashboard.html`. The metric→balancing-lever map is in `docs/analytics/data-dictionary.md`.

## Out of scope (v1)
- GDPR/consent gating + sampling/batching (needed before store-scale release).
- Automated/live HTML feed (would need paid UGS Data Access or a second sink).
- Same-session phase switching (flags are once-per-launch).
