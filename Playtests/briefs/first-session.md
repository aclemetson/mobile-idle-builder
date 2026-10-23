# Brief: first session

**Persona.** First-time mobile player, never played an idle or factory game. Reads one line of
dialogue then taps through. Doesn't open menus unless something points at them. Expects every
tap to do something visible.

**Setup.** `playtest_control fresh_start`. No other commands.

**Goal.** Get as far through the tutorial as this player would, ideally to `automation_started`
(a building plus a conveyor producing on their own). Stop early if you're stuck for 10 turns
in a row with no idea what to do: that's the main finding.

**Time box.** 60 minutes of agent time or 150 turns, whichever comes first.

**Rubric (flag each one you see):**
- Softlock: no action moves the tutorial forward (compare `tutorial.stepId` across turns).
- Unclear objective: the hint or dialogue doesn't tell this player what to tap, or points at
  something off screen or hidden behind a panel.
- Missing feedback: a tap that changes state (`playtest_state`) with nothing visible on screen,
  or a visible change with no state change.
- Blocked tap: the input summary shows `overUi: true` on a world tap the tutorial asked for, or
  a highlighted target that won't take a tap.
- Pacing: waits with nothing to do. Report the real-time length, and any timer the player
  can't see.
- UI layout: clipped or overlapping text, buttons under the notch or rounded corners, panels
  covering the thing the hint points at.
- Text: typos, jargon this persona wouldn't know, numbers without units.
- Console errors or warnings during play.

**Report.** Use the format in `Playtests/README.md`. Add a table of tutorial step id, turns spent
and real time, so the slowest steps stand out.
