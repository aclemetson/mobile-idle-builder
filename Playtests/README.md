# AI Playtests

An AI agent (Claude Code) plays the game in the Unity Editor through MCP for Unity, then writes up
what felt wrong: softlocks, unclear goals, missing feedback, pacing walls, UI overlap. This finds
problems. The EditMode/PlayMode suites keep them fixed once found.

Each run follows a **brief** in `briefs/`. A brief sets a persona, a goal, a rubric and a time box.
Reports go to `reports/` (gitignored); benchmark timings go to `benchmarks/` (committed).

| Brief | What it tests | Timed |
|---|---|---|
| `stability-sweep` | Every system works from a fresh install; no exceptions, softlocks or lost state | no |
| `casual-idler` | F2P player who checks in 5 times a day and waits for timers | yes |
| `active-grinder` | F2P player who keeps the app open and optimizes; no crystals | yes |
| `whale-speedrun` | Fastest first prestige with simulated top-spender purchases | yes |
| `first-session` | UX: first-time player through the tutorial | no |
| `automation-loop` | UX: first production chain, power and belts | no |

Run one with `/playtest <brief>` in Claude Code (optionally `for 20 minutes`), or
`scripts/run-playtest.ps1 -Brief <brief>`. The skill picks the models; you don't switch them.

## One-time setup

1. Unity Editor open on this project, on the branch you want to test.
2. Start the MCP server in Unity: **Window > MCP for Unity > Start Server** (HTTP, `127.0.0.1:8080`).
3. Claude Code must see `UnityMCP` in `/mcp`. If it doesn't, the server is registered under the
   forward-slash project key only; add it under the backslash key too, then restart the session.
4. Set the Game View to a phone resolution in portrait or landscape (for example 1080x2400 or
   2400x1080). Screenshots and tap coordinates use the Game View's pixel size.
5. The `playtest_*` tools register automatically when the MCP bridge connects. If they're missing,
   check the Editor console for compile errors and reconnect the bridge.

You don't need to change any project setting. While Play Mode is on, the harness turns on run in
background and routes injected input to the game even when the Game View isn't focused. Both
revert when Play Mode ends. **Don't click inside the Editor during a run:** your real mouse is
routed to the game too.

## Tools

Built-in MCP for Unity tools:

| Need | Tool |
|---|---|
| Enter / exit Play Mode | `manage_editor` action `play` / `stop` |
| Errors and warnings | `read_console` |

**Don't use the built-in screenshot (`manage_camera` / `manage_scene` screenshot) during Play Mode.**
With the Editor in the background it falls back to a camera render without the HUD, and it
steps the player loop, which logs "PlayerLoop internal function has been called recursively".
Use `playtest_screenshot`.

Project tools (`Assets/Scripts/Playtest/`):

| Tool | Use |
|---|---|
| `playtest_screenshot` | What the player sees (3D + HUD), saved to `reports/screenshots/` at 800 px (about 400 tokens); open the PNG with Read. Image px x `scale` = screen px |
| `playtest_state` | JSON truth: entropy, tutorial step + hint, open panels, dialogue, placement modes, research, buildings (crafting / power), inventory |
| `playtest_ui` | Visible buttons and text with screen rects. `list` (with `filter`, `interactive_only`) or `find` |
| `playtest_input` | `tap`, `tap_element`, `tap_cell`, `drag`, `drag_cells`, `collect`. Real Input System events through a virtual mouse. Returns only the summary fields that changed since the last call |
| `playtest_input collect` | `target` field id (`quark_field`, `electron_field`, ...), `count` taps. Pans to the field if needed, taps its tiles in turn as each comes off cooldown, waits out cooldowns. Returns items gained and missed taps. One call instead of a pan plus one call per item |
| `playtest_control` | `status`, `time_scale`, `clock`, `wait`, `away`, `console`, `list_fixtures`, `load_fixture`, `save_fixture`, `fresh_start` (`scope` local, cloud or all) |
| `playtest_benchmark` | `start`, `mark`, `note`, `status`, `end`: milestone timings in player time; `note` (with `severity` and `screenshot`) logs a finding for the review pass |

`playtest_state` also lists every field tile (`grid.fields`: id, cell, `cooldownLeft`), so fields
can be found without panning around. `playtest_control status` returns the full summary and resets
the baseline the change-only summaries compare against.

If a tool rejects a parameter as "Unexpected keyword argument", the MCP server still has the schema
from before a code change. Restart the Claude Code session, or call the tool through
`execute_custom_tool` (`tool_name` plus a `parameters` object), which passes parameters straight through.

Coordinates in `playtest_input` and `playtest_ui` are Game View screen pixels with a top-left
origin. Screenshots are downscaled; multiply image pixels by the returned `scale`.
`tap_cell` takes grid cells on the active site; it fails if the cell is off screen, so drag to
pan first. `playtest_ui` doesn't detect one element covering another; a button under an open
drawer can still be listed.

## The run loop

1. Read this README and the brief. Note the start time; respect the brief's time box.
2. `manage_editor` play. Entering Play Mode reloads the domain, so the next call can fail with
   "disconnected"; retry it. Then `playtest_control wait` until `loaded` is true (10 to 20 s).
   Never trigger a script recompile while playing: it breaks the ECS world (load hangs at 0%,
   NullReferenceExceptions from EntityQuery). Stop, recompile, then play again.
3. Set up as the brief says (`fresh_start`, `load_fixture`, or nothing). Setup may use the dev
   console. **After setup, play only through `playtest_input`.** If you have to cheat to get
   unstuck, that's a finding: log it as a softlock and say which command you used.
4. Each turn:
   - Read the state (change-only summaries, `playtest_state` when needed); take a
     `playtest_screenshot` when the screen changes (see "Driver rules").
   - Decide as the persona would, **from the screenshot first**. The state shows what's really
     happening; the screenshot shows what the player can tell. Gaps between the two are findings.
   - Act with one `playtest_input` call and check the returned summary.
5. Record a finding when it happens with `playtest_benchmark note` (severity, screenshot path).
6. At the end: `playtest_benchmark end`, `manage_editor` stop. The reviewer writes the report.

Tips:
- Prefer `tap_element` (by name or visible text) for HUD buttons, and `tap` with
  screenshot coordinates for anything else.
- Idle waits: raise `time_scale` and `wait`. Game time, research timers and cooldowns all speed
  up together (see "Time"); keep benchmark runs at 10x or less.
- If a tap did nothing, look again before retrying. "The button didn't respond" is a finding,
  not something to work around.
- `read_console` after anything surprising; errors and warnings are findings too.

## Driver rules (token budget)

`/playtest` runs a cheap **driver** model to play the game and a stronger **reviewer** model to
write the report (see `.claude/skills/playtest/SKILL.md` for which models run each scenario).
Drivers keep context small:

- **State first.** `playtest_state` and the change-only summaries are the main view. Take a
  `playtest_screenshot` only when a new screen or panel appears, when the summary shows something
  you can't place, or when something looks wrong. Never take two screenshots of an unchanged screen.
- **Use `collect`** for every field-gathering step, and `tap_element` for HUD buttons. Use `tap`
  with coordinates only when neither fits.
- **Log findings as they happen** with `playtest_benchmark note`: one line, severity, and the
  screenshot path. The reviewer only sees what you logged, plus the auto-detected milestones.
- **Don't write prose.** No running commentary between tool calls; act, check the summary, move on.
- **Stuck for 5 actions** with no state change: note it as S1/S2 with a screenshot, then try one
  different approach. If that fails too, end the run with outcome `stuck`.
- **Don't edit code or the project.** If Unity misbehaves (load hangs, exceptions in the console),
  stop Play Mode, note it, and end with outcome `crashed`.

## Reviewer rules

- **Inputs:** the brief, the run's `.json`/`.jsonl`, the driver's notes, and at most 8 screenshots
  (those cited by S1/S2 findings first).
- **Verify before reporting.** Each finding needs evidence: a screenshot, an event, or a state value.
  Downgrade or drop anything unsupported, and mark uncertain ones `[verify]`.
- **Write `reports/<date>-<scenario>.md`** in the README format. For timed briefs, include the
  `benchmark-report.py` table and the forced idle stretches (gaps between milestones where `mark`
  says idle, or long `wait`s).

## Fixtures (checkpoints)

`fixtures/<name>.json` are full save files. `load_fixture` copies one into the dev snapshot
folder and runs `snapshot load`, which reloads the scene. `save_fixture` captures the current
game into this folder; commit fixtures that briefs rely on. Before the first load or wipe each
day, the harness saves your own state as the dev snapshot `pre_playtest_<yyyymmdd>`. To get it
back: dev console `snapshot load pre_playtest_<yyyymmdd>` (snapshots survive every wipe). Playtests
write the local save and autosave pushes it to your UGS Cloud Save. `clear cloud save` and
`clear all save` can't be typed into `console`; use `fresh_start` with `scope` `cloud` or `all`, which
backs up first.

## Time

Gameplay timers (research, field cooldowns, speed boosts, offline earnings, daily resets) read
`GameClock`, which the harness keeps in step with the game time it lets run. Release builds always
read the wall clock.

- **`clock realtime`** (default): the game runs continuously, like a normal session. Agent thinking
  time passes in the game too. Fine for functional and UX runs.
- **`clock turn_based`**: the game is frozen (time scale 0, timers held) except while it is let run.
  Each `playtest_input` lets `action_seconds` of player time pass (the player's reaction and reading
  time), `wait` lets the given player seconds pass, and `away` jumps the given seconds as a closed
  app. The agent's thinking costs nothing, so **timings measure the player, not the agent**. Required
  for benchmarks.
- **`time_scale`** only changes how fast real time maps to player time during a `wait`; player-time
  results are the same at 1x and 10x. Above 10x, crafts shorter than one frame can lose output.
- **`away <seconds>`** (at least 60) runs the game's real background and resume path with the clock
  moved forward: offline earnings with the idle cap, finished research, ready cooldowns, expired
  boosts, and the welcome-back modal. Also a dev console command.
- **`iap grant <1-4>`** (dev console) simulates buying crystal pack 1 to 4 ($0.99 / $4.99 / $9.99 /
  $19.99) through the store's award path, and logs the dollars to the benchmark.

Dialogue that pauses the game counts its real reading time as player time.

## Scenario runs (briefs 1-4)

1. `manage_editor` play, then `playtest_control wait` until `loaded` is true.
2. `playtest_control fresh_start` with `scope` `all`: deletes local and cloud saves, settings, recipe
   knowledge and prefs for a true first launch (it backs up your save first). `scope` `cloud` keeps
   recipe knowledge and settings, so recipes from earlier runs would show as known.
3. `playtest_control clock` with the brief's mode and `action_seconds`.
4. Timed briefs: `playtest_benchmark start` with `scenario` set to the brief's name. Milestones are
   then recorded automatically; use `mark` for anything else worth timing.
5. Play the brief. "Agent turns" in a time box means `playtest_input` actions (the benchmark's
   `actions` count). To save context on long runs, a screenshot every turn is not required once a
   screen is familiar: read `playtest_state`, and take a screenshot whenever the screen changes or
   something looks wrong. When it ends: `playtest_benchmark end` with an `outcome`, `manage_editor` stop,
   write the report, and commit the files under `benchmarks/<scenario>/`.

## Benchmarks

Each timed run writes `benchmarks/<scenario>/<run>.jsonl` (events as they happen) and `<run>.json`
(summary: commit, branch, app version, screen, clock settings, outcome, totals, milestones). Every
milestone carries player time, agent actions and dollars spent at that moment.

Compare runs: `python scripts/benchmark-report.py` (latest run of each scenario side by side), or
`python scripts/benchmark-report.py casual-idler` (every run of one scenario, to track a build
over time). Add `--all` for every milestone, `--csv` for a spreadsheet.

Limits: per-building milestones only cover the active site's live grid (inactive sites run on
idle snapshots); agent decisions differ between runs, so compare trends across several runs, not
single numbers.

## Report format

Write `reports/<yyyy-mm-dd>-<brief-name>.md`:

```markdown
# <brief name>, <date>
Build: <git short sha> on <branch>. Game View <w>x<h>. Duration <min>. Furthest point: <tutorial step / goal>.

## Summary
Three to five sentences: how the session felt for this persona, and the top problems.

## Findings
### [S1|S2|S3] <short title>
- Category: softlock | unclear objective | missing feedback | pacing | UI layout | bug | text
- Where: <tutorial step id / panel / screen>
- What happened: <observed>
- Why it matters for this persona: <the reasoning; this is the valuable part>
- Repro: numbered steps from a fixture or fresh start
- Evidence: screenshot path(s), relevant playtest_state fields, console lines
- Suggestion (optional)

## Cheats used
Setup commands, plus any mid-run command and why (each mid-run one is also a finding).
```

Severity: **S1** blocks progress or loses progress. **S2** a typical player would get confused or
quit here. **S3** polish.

## Unattended runs

`scripts/run-playtest.ps1 -Brief casual-idler` checks that the Editor and the MCP server are up,
then runs Claude Code headless on the brief. It needs a real GPU and desktop session, because
screenshots render the Game View, so it can't run on the CI runner or in batchmode.
