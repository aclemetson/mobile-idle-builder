# Scenario 1: stability-sweep

**Purpose.** Prove that every system works from a fresh install and that nothing throws, softlocks or
loses state. This is a functional sweep, not a player simulation, and it isn't timed.

**Setup.** README "Scenario runs" with `fresh_start` scope `all`. Clock `realtime`; `time_scale` up to 20.
Dev-console cheats are allowed (`add currency`, `add item`, `unlock research`, `tutorial skip`,
`iap grant`, `away`) to reach a system quickly. Once a system is reached, exercise it **through the UI with
`playtest_input`**: the sweep tests what a player can do, not what the console can do.

**After every checklist item:** `read_console` (errors and warnings since the last check). Any exception,
error log or softlock is a finding. Take a `playtest_screenshot` of every new panel.

## Checklist (in order; mark each PASS, FAIL or BLOCKED)

1. **First launch.** Intro dialogue plays, first steps work with real taps: collect the first electron,
   sell in Maxwell's Demon, close the panel.
2. **Tutorial skip.** `tutorial skip place_first_building`, then place the building and a conveyor by hand.
3. **Research.** Buy a research with entropy; let one finish on its timer; skip one with crystals
   (`iap grant 1` first).
4. **Manual crafting.** Recipes panel: craft protons, neutrons, hydrogen.
5. **Placement.** Place one of each building in the Builder panel (rotate and flip at least once), using
   the confirm popup; cancel one placement.
6. **Conveyors.** Paint a straight run, an elbow and a single belt; remove a belt; route output into an
   input and confirm the target's `inputsSatisfied` goes true.
7. **Deconstruct.** Remove a building (confirm prompt) and a conveyor tile.
8. **Power.** Place a generator and a relay; check coverage rings, the power label, and the `⚡` overlay
   toggle; leave one consumer unpowered and confirm it shows.
9. **Building inspector.** Upgrade speed, storage and input; change recipe; empty buffers.
10. **Managers.** Hire, assign, unassign, upgrade a star.
11. **Shop.** `iap grant 2`; buy a speed boost, a time warp and an entropy pack; check the balances.
12. **Daily and achievements.** Claim the login reward; complete and claim an achievement.
13. **Codex and settings.** Open the codex; toggle every setting and confirm it persists after reload.
14. **Offline return.** `away` 7200; the welcome-back modal shows plausible earnings.
15. **Save round trip.** `save_fixture stability-mid`, keep playing, `load_fixture stability-mid`;
    buildings, conveyors, research and currency match.
16. **Sites and worlds.** Unlock and travel to a second site, then a second world; travel back; the
    grid restores each time.
17. **Prestige.** `add currency` to the wall, prestige through the UI, buy a prestige-shop upgrade, and
    confirm what should survive (recipe knowledge, unlocked sites) survives.
18. **Megastructure and PVP.** Open each panel and do one action if reachable.
19. **Stress.** `spawn all buildings`, time_scale 20 for 60 s, no errors or frame stalls.

**Report.** `reports/<date>-stability-sweep.md`: the checklist as a table (item, result, notes,
screenshot), then findings in the README format. Don't delete `fixtures/stability-mid.json`.
