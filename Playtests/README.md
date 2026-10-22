# AI Playtests

An AI agent (Claude Code) plays the game in the Unity Editor through MCP for Unity, then writes up
what felt wrong: softlocks, unclear goals, missing feedback, pacing walls, UI overlap. This finds
problems. The EditMode/PlayMode suites keep them fixed once found.

Each run follows a **brief** in `briefs/`. A brief sets a persona, a goal, a rubric and a time box.
Reports go to `reports/` (gitignored).

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
| Screenshot of what the player sees (HUD included) | `manage_scene` action `screenshot`, `capture_source: game_view`, `include_image: true` |
| Errors and warnings | `read_console` |

Project tools (`Assets/Scripts/Playtest/`):

| Tool | Use |
|---|---|
| `playtest_state` | JSON truth: entropy, tutorial step + hint, open panels, dialogue, placement modes, research, buildings (crafting / power), inventory |
| `playtest_ui` | Visible buttons and text with screen rects. `list` (with `filter`, `interactive_only`) or `find` |
| `playtest_input` | `tap`, `tap_element`, `tap_cell`, `drag`, `drag_cells`. Real Input System events through a virtual mouse. Returns a short state summary after `settle_ms` |
| `playtest_control` | `status`, `time_scale`, `wait`, `console`, `list_fixtures`, `load_fixture`, `save_fixture`, `fresh_start` |

Coordinates are Game View screenshot pixels with a top-left origin. `tap_cell` takes grid
cells on the active site; it fails if the cell is off screen, so drag to pan first.

## The run loop

1. Read this README and the brief. Note the start time; respect the brief's time box.
2. `manage_editor` play. Then `playtest_control status` until `loaded` is true.
3. Set up as the brief says (`fresh_start`, `load_fixture`, or nothing). Setup may use the dev
   console. **After setup, play only through `playtest_input`.** If you have to cheat to get
   unstuck, that's a finding: log it as a softlock and say which command you used.
4. Each turn:
   - Take a screenshot and read `playtest_state`.
   - Decide as the persona would, **from the screenshot first**. The state shows what's really
     happening; the screenshot shows what the player can tell. Gaps between the two are findings.
   - Act with one `playtest_input` call and check the returned summary.
5. Record a finding when it happens, with the screenshot path, the state values, and the steps
   just taken.
6. At the end: `manage_editor` stop, then write the report.

Tips:
- Prefer `tap_element` (by name or visible text) for HUD buttons, and `tap` with
  screenshot coordinates for anything else.
- Idle waits: raise `time_scale` (up to 50x) and `wait`. Research timers run on wall-clock time
  and don't speed up. Note how long a real player would wait.
- If a tap did nothing, look again before retrying. "The button didn't respond" is a finding,
  not something to work around.
- `read_console` after anything surprising; errors and warnings are findings too.

## Fixtures (checkpoints)

`fixtures/<name>.json` are full save files. `load_fixture` copies one into the dev snapshot
folder and runs `snapshot load`, which reloads the scene. `save_fixture` captures the current
game into this folder; commit fixtures that briefs rely on. Before the first load or wipe each
day, the harness saves your own state as the dev snapshot `pre_playtest_<yyyymmdd>`. To get it
back: dev console `snapshot load pre_playtest_<yyyymmdd>`. Playtests write the local save and
autosave pushes it to your UGS Cloud Save; `clear cloud save` and `clear all save` are blocked.

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

`scripts/run-playtest.ps1 -Brief first-session` checks that the Editor and the MCP server are up,
then runs Claude Code headless on the brief. It needs a real GPU and desktop session, because
screenshots render the Game View, so it can't run on the CI runner or in batchmode.
