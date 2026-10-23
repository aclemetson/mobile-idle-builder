# Brief: automation loop

**Persona.** Player who finished the guided basics and is now building their first production
chain alone. Comfortable with menus. Wants to know why a machine isn't producing.

**Setup.** `playtest_control load_fixture` with name `post-automation` if it exists. If it
doesn't: `fresh_start`, then `console` with `tutorial skip intro_sfc`, then `save_fixture` with
name `post-automation` so later runs start from the same state. Say in the report which path
you took.

**Goal.** Follow the tutorial from `intro_sfc` through
`hydrogen_loop_complete`: generator, power links, conveyor routing, recipe setup. After the
tutorial, add one more producing building of your choice.

**Time box.** 60 minutes of agent time or 150 turns.

**Rubric (on top of the first-session rubric):**
- Production you can't diagnose: a building in `playtest_state` with `crafting: false`, or with
  `power` `unpowered` or `brownout`. Can the player tell why from the screen (inspector, tint,
  power label)?
- Conveyor routing: belts that look connected but don't deliver (the target building's
  `inputsSatisfied` stays false). Belts that need many attempts to place.
- Placement: the confirm popup off screen or under the HUD; rotate and flip results that don't
  match the preview.
- Power: whether coverage rings and link lines explain why something is unpowered.
- Economy: a stretch where nothing is affordable and the next goal isn't shown. Give the real
  time it lasted at 1x speed (scale up the time you waited at higher `time_scale`).

**Report.** Use the README format. Add the final building list from `playtest_state` with each
building's status.
