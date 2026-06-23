# Analytics Dashboard

A zero-cost, zero-server way to chart the balancing telemetry collected by `TelemetryService`
(see `docs/agents/analytics.md` for the collection side and `data-dictionary.md` for what each metric means).

## The free workflow

1. **Open a collection phase.** In the Unity Dashboard → Remote Config, set `analytics.enabled = true` and
   `analytics.phase = "<phase-name>"` (e.g. `prestige-tuning-v1`) for the environment you're testing
   (`development` for editor/internal). Testers pick this up on their **next app launch**.
2. **Play / let testers play.** Events stream to UGS Analytics (free up to 50k MAU; ~50 here). Dev-environment
   events appear in the dashboard within a few minutes.
3. **Analyze in-dashboard (free):** Unity Dashboard → Analytics → **Data Explorer**. Build queries, segment by
   `collection_phase`, view funnels/retention. This alone covers most balancing questions.
4. **Custom charts (optional, free):** in Data Explorer, run a query and click **Export → CSV** (this is the
   *free* dashboard export, **not** the paid Snowflake "Data Access" raw stream). Save it under
   `docs/analytics/data/`, then open `dashboard.html` and load the CSV.

## Using `dashboard.html`

- Open the file directly in a browser (no server needed), or publish `docs/analytics/` via GitHub Pages.
- Click **Load CSV** and pick an exported file. The page auto-detects which event the CSV is (by columns) and
  renders the relevant charts:
  - `prestige_completed` → net-worth-per-run curve, prestige-currency-per-run, run-length histogram.
  - `player_snapshot` → entropy/sec over playtime, prestige-currency growth, building-count trend.
  - `building_placed` → building-type distribution.
- Pure client-side (Chart.js + PapaParse from CDN). No keys, no data leaves your browser.

## Smoke-testing all 6 events in the editor

Three events (`tier_reached`, `megastructure_stage`, `prestige_completed`) are hard or impossible to
trigger by normal play, so use the dev console (backtick `` ` `` to open, or shake on device):

1. Register all 6 event schemas in the UGS dashboard first (see above / `docs/agents/analytics.md`) —
   unregistered events are rejected as **invalid**.
2. Enter Play mode and let GameScene load (the ECS world must exist for snapshots).
3. Open the dev console and run **`analytics fire`**. It force-starts collection (so you don't even need
   `analytics.enabled` set for the smoke test), sends one of each of the 6 events with sample data, and
   **flushes** so they upload immediately instead of waiting for the batch interval.
4. Run **`analytics status`** to confirm it's collecting and see the active phase.
5. In the dashboard (**development** environment) → **Analytics → Event Manager**, watch each event's
   "valid received (last 24h)" count rise. Any **invalid** count = a name/type mismatch in the schema.
6. Then chart via Data Explorer, or export CSV and open `dashboard.html`.

> The editor uses the `development` environment, so smoke-test data lands there, not in `production`.
> Real (non-forced) collection still requires `analytics.enabled = true` — `analytics fire` only bypasses
> the flag for this manual test.

## Notes
- Custom events must be registered as schemas in the UGS dashboard before they show up (snake_case names +
  params — see the table in `docs/agents/analytics.md`).
- `data/` is for local CSV exports; avoid committing large/raw player CSVs.
