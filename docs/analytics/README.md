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

## Notes
- Custom events must be registered as schemas in the UGS dashboard before they show up (snake_case names +
  params — see the table in `docs/agents/analytics.md`).
- `data/` is for local CSV exports; avoid committing large/raw player CSVs.
