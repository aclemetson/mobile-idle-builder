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
- `Assets/Scripts/Analytics/IAnalyticsSink.cs` — backend abstraction. Calls return `bool` (accepted or not) and
  `Describe()` reports backend state; implementations must not throw, but must not hide failure either.
- `Assets/Scripts/Analytics/UnityAnalyticsSink.cs` — live UGS sink (`#if ANALYTICS`).
- `Assets/Scripts/Analytics/TelemetryService.cs` — singleton: `StartIfEnabled`, `Record*` (all funnelled through
  `Emit`, which counts accepted/refused), snapshot loop, lifecycle flush.
- Flags: `analytics.enabled` / `analytics.phase` in `FeatureFlags.cs`.
- Taps: `SaveManager.InitialCloudReconcile` (start), `AchievementService.Notify{BuildingPlaced,
  ResearchCompleted,TierReached}`, `PrestigeSystem.OnUpdate` (rich pre-reset event), `MegastructureService.Deduct`.
- Tests: `Assets/Scripts/Tests/TelemetryServiceTests.cs`.
- Dashboard + data dictionary: `docs/analytics/`.

## Events (v1)

Custom events must also be registered as schemas in the UGS dashboard (snake_case). Every event additionally
carries `player_id` + `collection_phase`.

Types below are the UGS schema types to register (`Int` covers C# `int` and `long`; `Float` covers `float`).
**The schema must match these names and types exactly** — the dashboard rejects an event whose name is not
registered, and marks it *invalid* when a param's type disagrees. Both failures look identical from inside the
game: the SDK reports success and nothing appears in the dashboard.

Every event also carries `player_id` (String) + `collection_phase` (String).

| Event | Params (type) | Tap |
|---|---|---|
| `prestige_completed` | run_count (Int), networth_before (Float), prestige_currency_earned (Int), playtime_run_sec (Int), building_count (Int), highest_tier (Int) | `PrestigeSystem.OnUpdate` (values captured pre-reset) |
| `building_placed` | building_id (String), building_count_after (Int) | `AchievementService.NotifyBuildingPlaced` |
| `research_completed` | research_id (String), entropy_spent_total (Int) | `AchievementService.NotifyResearchCompleted` |
| `tier_reached` | tier (Int) | `AchievementService.NotifyTierReached`, driven by `TierProgress.NotifyItemProduced` from both production paths (manual craft + automated recipe output) |
| `megastructure_stage` | stage (Int) | `MegastructureService.Deduct` on stage completion |
| `game_update_notice` | data_version (Int) | `HUDController.MaybeShowGameUpdateNotice` when the update modal is shown (newer `gamedata.updatedUtc` than the saved marker) |
| `player_snapshot` | networth (Float), base_currency (Int), entropy_per_sec (Float), prestige_currency (Int), paid_currency (Int), prestige_count (Int), building_count (Int), highest_tier (Int), megastructure_stage (Int), research_unlocked_count (Int), playtime_total_sec (Int), field_collections (Int), field_cooldown_sec (Float), power_nodes_total (Int), power_nodes_linked (Int) | snapshot loop (5 min) + each prestige |

### Nothing arriving in the dashboard?

**Start with `analytics status` in the dev console.** It reports the whole client-side chain in one line —
collecting or not, the phase, the UGS `services=` / `signedIn=` state, the environment the events are being
sent to, and how many events the backend has *accepted vs refused* this session. Everything below the client
boundary is silent by design, so let the counters tell you which half of the pipe is broken:

- **Accepted > 0 but nothing in the dashboard** ⇒ the events left the device and were dropped server-side.
  That is a **schema problem** (link 2) or an **environment problem** (link 3), not a game problem.
- **Refused > 0** ⇒ the SDK itself rejected the call; the `[Telemetry] event '<name>' was NOT accepted`
  warning and the sink's own exception message name the cause (link 1 / link 4).

1. **`analytics.enabled` is false by default.** `TelemetryService.StartIfEnabled` stays inert and logs
   `[Telemetry] Disabled (analytics.enabled=false)`. Set the Remote Config key for the environment under test.
   `analytics fire` bypasses the flag, so use it to isolate this link.
2. **Schemas not registered** in Analytics → Event Manager, or a name/type mismatch against the table above.
   Unregistered events are rejected outright; mismatched params count as *invalid*. The Event Manager's
   "valid / invalid received (last 24h)" counters are the only place this is visible. Note the client cannot
   see this at all — `analytics fire` reporting "accepted" says only that the *SDK* took the event.
3. **Environment mismatch.** Editor + `DEV_ENVIRONMENT` builds report to `development` (see `UgsEnvironment`);
   a `production` dashboard view will read empty no matter what the game does. `analytics status` prints the
   environment actually in use.
4. **Not signed in / UGS not initialized.** Collection starts from `SaveManager.InitialCloudReconcile`, after
   UGS init + auth. If auth fails, `StartDataCollection` returns false and logs
   `[Telemetry] Backend refused StartCollection — events will NOT reach the dashboard`.

**Buffering:** UGS batches events in memory and uploads on its own cadence. `TelemetryService` flushes on
`OnApplicationPause(true)` and `OnApplicationQuit` (the same boundaries `SaveManager` saves at), so a
backgrounded or killed app doesn't lose the tail of the session. Anything recorded and *not* flushed is still
subject to that upload delay before it appears — don't read an empty dashboard in the first minute as a failure.

`field_collections` (Integer) = session-cumulative manual field taps that yielded an item, bumped via
`TelemetryService.NotifyFieldCollected` from `ManualFieldCollector`. `field_cooldown_sec` (Float) = the current
effective field tap cooldown (base × research mult × (1 − prestige reduction)); the lever for tuning the
manual-collection loop. `power_nodes_total` / `power_nodes_linked` (Integer) = placed power buildings vs how many
chain back to a generator (read from the `PowerGridState` singleton); the gap is stranded relays, the signal that
power-building link ranges are mistuned.

`entropy_per_sec` = NetWorth growth rate over the snapshot interval — a clean income-rate proxy, since
spending entropy moves BaseCurrency→TotalEntropySpent without changing NetWorth (only production raises it).
`playtime_*_sec` are session-relative (`realtimeSinceStartup`), not lifetime totals.

## Reading the data
Two paths, split by what Data Explorer v2 can do:
- **Data Explorer v2 (free, in-dashboard)** — measures are built-in metrics + **event counts**; custom
  parameters are **dimensions/filters only — you cannot aggregate a parameter value**. So it's for
  count-by-parameter distributions (building_id, research_id, stage, run_count, highest_tier), funnels, and
  retention, segmented by `collection_phase`.
- **CSV → `docs/analytics/dashboard.html`** — for the value curves Data Explorer can't produce (net worth per
  run, `entropy_per_sec` over playtime, run-length, currency balances). One-click **Export CSV** from Data
  Explorer (free dashboard export, not the paid Snowflake stream), then load it in the HTML page.

The metric→balancing-lever map is in `docs/analytics/data-dictionary.md`; that doc also flags which metrics
are Data-Explorer-native vs CSV-only.

## Editor / device smoke test
Dev console (`DevConsoleController`, `#if UNITY_EDITOR || DEVELOPMENT_BUILD`):

- **`analytics fire`** (`TelemetryService.DevFireAll`) — force-starts collection (ignores the flag), sends one of
  every event with sample data, `Flush()`es so they upload immediately, and reports **how many the backend
  accepted vs refused** plus the backend's own state. It reports counters, not a canned string: an earlier
  version printed "Fired: ..." unconditionally, which made a fully broken pipeline look identical to a healthy one.
- **`analytics status`** (`TelemetryService.DevStatus`) — collecting?, phase, player, UGS state, environment, and
  the session's accepted/refused counts.

"Accepted" means the SDK took the event, **not** that it will appear in the dashboard — an unregistered schema is
dropped server-side and the client never learns about it. See `docs/analytics/README.md`.

## Out of scope (v1)
- GDPR/consent gating + sampling/batching (needed before store-scale release).
- Automated/live HTML feed (would need paid UGS Data Access or a second sink).
- Same-session phase switching (flags are once-per-launch).
