# Fix #303: tutorial polish from the 2026-10-31 playtests

**Required reading:** `docs/agents/README.md`, `architecture.md`, `ui-toolkit.md`, `data-pipeline.md` (tutorial
steps/dialogues section only), `testing.md`.
**Status:** done (PR pending, integration/2026-11-07). Item 7 (list swipe) needs an on-device check.
**Size:** S. Six small independent items; do them in order and keep each one minimal.
**Branch:** `fix/303-tutorial-polish` from `integration/2026-11-07`.

Repro notes: `Playtests/reports/2026-10-31-first-session.md` and `2026-10-31-active-grinder.md` (local files). Step
numbers in those reports predate the current tutorial; go by step **ids**.

## 1. Placement confirm popup clips at the screen edge
- `HUDController.UpdatePlacementConfirmPopup` (`Assets/Scripts/UI/HUDController.cs` ~:2011-2033) sets
  `left = panelPoint.x - w/2` and `top = panelPoint.y - h - 24` with no clamp.
- **Fix:**
  - Clamp `left` to `[margin, panelWidth - w - margin]` and `top` to `[margin, panelHeight - h - margin]`, with
    `margin = 8`.
  - Get the panel size from `_placementConfirmPopup.panel.visualTree.layout`, or the popup's parent `resolvedStyle`,
    whichever already resolves at runtime.
  - Also respect the safe area if a helper exists: grep `SafeArea`.
  - If the popup would go above the top edge, place it **below** the cell (`panelPoint.y + 24`).
- **Testability:** extract the math into a pure `static Vector2 ClampPopup(Vector2 desired, Vector2 size, Vector2 panelSize, float margin)`
  and unit-test it.

## 2. Closing Research takes two taps when a dialogue starts at that moment
- `DialogueController.SetBoxVisible(true)` (`Assets/Scripts/UI/DialogueController.cs` ~:168) shows `_blocker`
  (`dialogue-blocker`, full screen, GameHUD.uxml ~:487). The player's tap on `btn-close-research` then lands on the
  blocker.
- **Fix:** for **0.3 s** of real time after the blocker appears, set `_blocker.pickingMode = PickingMode.Ignore` so a
  tap already on its way reaches the button underneath. After that, restore `PickingMode.Position`.
  - Use `schedule.Execute(...).StartingIn(300)` or a timestamp check.
  - Time is paused during dialogues (`pause_game`), so use real time; `schedule` runs on real time.
- Check that the dialogue box itself still advances on tap.

## 3. Panning from the left edge opens the drawer
- The drawer opens only through the `btn-drawer-handle` click (`HUDController.cs` ~:501, `ToggleDrawer`/`OpenDrawer`
  ~:2389-2407). `DrawerTapGuard.cs` already guards taps on rows while the drawer slides in.
- A pan that starts on the handle ends as a click.
- **Fix:** on the handle, record the pointer-down position (`PointerDownEvent`, TrickleDown if needed). On click,
  ignore it when the pointer moved more than ~12 px since down: that was a pan. A pure
  `static bool IsTap(Vector2 down, Vector2 up, float threshold)` helper makes it testable.

## 4. No completion moment at the end of the tutorial
- The last step is `tutorial_complete` (game_data.json). It plays dialogue `tutorial_complete_context` and advances
  on `dialogue_complete`.
- `TutorialOverlayController` (`Assets/Scripts/UI/TutorialOverlayController.cs` ~:152-166) then only hides the hint
  and dialogue.
- **Fix:** when the tutorial goes from active to complete in that block, and **only on a real transition, not when a
  save loads with the tutorial already complete**, show a celebration through the existing notification banner:
  `HUDController.ShowNotification(icon, msg, style)`. Grep the signature and use an existing style such as
  `"success"`, or the closest one. Text: "Tutorial complete! The Foundry is yours."
  - If there's an existing confetti or particle effect (grep `Celebrat|Confetti|Burst` in `Assets/Scripts/UI`),
    trigger it too. **Don't build a new one.**
- Guard against the load race the same way the controller already does: `ECSLoadBridge.IsLoaded` (see
  `project_tutorial_race_fix`).

## 5. Hints should mention the belt overlay's Destroy mode
- Two hints in `Assets/Data/game_data.json` `tutorial_steps`:
  - `reroute_neutrons_to_atom_gen`: "...(Build menu, Deconstruct)...";
  - `route_deuterium_to_reactor`: "...(Builder, Deconstruct)...".
- The belt overlay has a Create/Destroy toggle (`ConveyorPlacementController.ToggleMode`). **Check the button's
  visible label** in GameHUD.uxml or the controller before writing the hint.
- New wording, e.g. "Remove the belt from the Atom Generator to Maxwell's Demon (belt tool, Destroy, or Builder,
  Deconstruct), then ...". Keep everything else in the hints the same.
- Re-import game data and commit the regenerated tutorial asset (`tutorial_flow.asset` or wherever the importer
  writes steps).
- Regenerate `Playtests/tutorial-walkthrough.md` with `python scripts/generate-tutorial-walkthrough.py`. Check the
  script's usage line.

## 6. Quark tap yields are random
- `ManualFieldCollector.PickWeightedItem(FieldSO field)` (`Assets/Scripts/Gameplay/ManualFieldCollector.cs` ~:196)
  rolls `Random.Range` over `field.drops[].weight` (`quark_field` is 0.5/0.5). One playtest got 4 Down and 2 Up
  from 6 taps, so the "8 of each" steps took extra taps.
- **Fix:** keep the long-run ratio but deal the drops from a **shuffled bag**, one bag per field id, held in a
  static or per-collector dictionary.
  - A bag holds each drop `round(weight / minWeight)` times, capped at 20 entries total. If the weights can't be
    expressed that way, fall back to the current roll.
  - When the bag is empty, refill and shuffle (Fisher-Yates with `UnityEngine.Random`).
  - For 0.5/0.5 the bag is [Up, Down] shuffled, so every 2 taps give one of each.
- **Testability:** make the bag a small pure class, `DropBag`, with an injectable `System.Random` or `Func<int,int>`.
  - Test: 0.5/0.5 over 2N picks gives exactly N each;
  - test: a 3:1 weighting over 4N picks gives 3N:N;
  - test: a single drop always returns it.
- Statics must survive a domain-reload-off Play Mode: reset them in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
  method (see #289).

## 7. List swipe: no code change
ScrollView drag-scroll is touch-only, so a mouse drag in the harness doesn't scroll (see `ListTapRouter.cs`
comments). Put a note in the PR body: "harness artefact; verify swipe on a device". Leave the issue's checkbox
for the device check.

## Tests (EditMode)
- `ClampPopup` (item 1): inside, left edge, right edge, flip below at the top.
- `IsTap` (item 3).
- `DropBag` (item 6).
- `TutorialDataFlowTests` still passes. If it checks hint text, add an assertion that the two hints mention
  "Destroy".

## Done when
- All six items are done (or skipped with a reason).
- The tests pass and the full EditMode suite passes.
- The game data is re-imported and the walkthrough regenerated.
- Set Status to done with the PR number.
