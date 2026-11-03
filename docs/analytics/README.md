# Analytics Dashboard

A zero-cost, zero-server way to chart the balancing telemetry collected by `TelemetryService`
(see `docs/agents/analytics.md` for the collection side and `data-dictionary.md` for what each metric means).

## The free workflow

1. **Open a collection phase.** In the Unity Dashboard → Remote Config, set `analytics.enabled = true` and
   `analytics.phase = "<phase-name>"` (e.g. `prestige-tuning-v1`) for the environment you're testing
   (`development` for editor/internal). Testers pick this up on their **next app launch**.
2. **Play / let testers play.** Events stream to UGS Analytics (free up to 50k MAU; ~50 here). Dev-environment
   events appear in the dashboard within a few minutes.
3. **Distributions / engagement (free, in-dashboard):** Unity Dashboard → Analytics → **Data Explorer v2**.
   See "What Data Explorer can and can't do" below — use it for event-count breakdowns, funnels, and
   retention, segmented by `collection_phase`.
4. **Value curves (free, CSV → local):** for the actual balancing curves (net worth per run, income rate,
   run length, currency balances) Data Explorer **can't** help — export CSV (Data Explorer → **Export → CSV**,
   the *free* dashboard export, **not** the paid Snowflake "Data Access" raw stream), save it under
   `docs/analytics/data/`, then open `dashboard.html` and load it.

## What Data Explorer v2 can and can't do

Data Explorer's **measures** are built-in metrics (DAU/MAU, retention, sessions) **plus event counts**.
Custom event **parameters can only be used as dimensions/filters — you cannot aggregate a parameter value**
(no avg/sum/max of `networth_before`, `entropy_per_sec`, etc.).

- **Good for** (count of an event, broken down by a *low-cardinality* parameter, optionally filtered by
  `collection_phase`):
  - `building_placed` count by `building_id` — building usage distribution
  - `research_completed` count by `research_id` — what's researched / skipped
  - `megastructure_stage` count by `stage` — endgame reach
  - `prestige_completed` count by `run_count` — how far into prestige players get
  - `player_snapshot` count by `highest_tier` or `prestige_count` — player-state distribution
  - `prestige_completed` count by Day — prestige activity; plus built-in DAU/retention/sessions
- **Not possible** (route to the CSV dashboard / Looker Studio / Sheets instead): average or trend of any
  numeric value — net worth per run, prestige currency per run, `entropy_per_sec` over playtime, run-length,
  `base_currency` / `paid_currency` balances. These are continuous, so they aren't aggregatable *and* are
  useless as a dimension (every value is unique).

## Using `dashboard.html`

- Open the file directly in a browser (no server needed), or publish `docs/analytics/` via GitHub Pages.
- Click **Load CSV** and pick an exported file. The page auto-detects which event the CSV is (by columns) and
  renders the relevant charts:
  - `prestige_completed` → net-worth-per-run curve, prestige-currency-per-run, run-length histogram.
  - `player_snapshot` → entropy/sec over playtime, prestige-currency growth, building-count trend.
  - `building_placed` → building-type distribution.
- Pure client-side (Chart.js + PapaParse from CDN). No keys, no data leaves your browser.

## Smoke-testing every event (editor or device)

Several events (`tier_reached`, `megastructure_stage`, `prestige_completed`) are hard or impossible to
trigger by normal play, so use the dev console (backtick `` ` `` to open, or shake on device):

1. **Register every event schema in the UGS dashboard first** (see above / `docs/agents/analytics.md`).
   This is the step that most often gets missed, and it is invisible from inside the game: an unregistered
   event is dropped server-side while the client still reports it as sent.
2. Enter Play mode and let GameScene load (the ECS world must exist for snapshots).
3. Open the dev console and run **`analytics fire`**. It force-starts collection (so you don't even need
   `analytics.enabled` set for the smoke test), sends one of each event with sample data, **flushes** so they
   upload immediately, and then reports what actually happened:

   ```
   Accepted by backend: 7 event(s). Refused: 0. Flush: ok.
   UGS Analytics — services=Initialized, signedIn=True, environment=development
   phase='initial_test', player='4enx…'
   ```

   `Refused: 0` with `services=Initialized, signedIn=True` means the events left the device — from there on,
   any problem is dashboard-side (schema or environment). A non-zero **Refused** count, or `signedIn=False`,
   means the client never got them out, and the `[Telemetry] …` warnings in the log say why.
4. Run **`analytics status`** any time for the same state plus the session's running accepted/refused counts.
5. In the dashboard (**development** environment) → **Analytics → Event Manager**, watch each event's
   "valid received (last 24h)" count rise. Any **invalid** count = a name/type mismatch in the schema.
6. Then chart via Data Explorer, or export CSV and open `dashboard.html`.

> Editor and `DEV_ENVIRONMENT` device builds use the `development` environment, so smoke-test data lands
> there, not in `production`. Real (non-forced) collection still requires `analytics.enabled = true` —
> `analytics fire` only bypasses the flag for this manual test.
> Ingestion is not instant: give the Event Manager counters a few minutes before concluding anything.

## Notes
- Custom events must be registered as schemas in the UGS dashboard before they show up (snake_case names +
  params — see the table in `docs/agents/analytics.md`).
- `data/` is for local CSV exports; avoid committing large/raw player CSVs.
