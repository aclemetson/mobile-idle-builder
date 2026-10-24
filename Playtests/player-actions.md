# Player actions and stall fixes

What a player can do in the game, and what to try when a run stops moving. Drivers: read Part 1
once, and go to Part 2 when the state hasn't changed for 3 actions. This sheet is built from
`tutorial-walkthrough.md`, `docs/agents/ui-toolkit.md` and `docs/agents/economy-balance.md`. If the
game disagrees with it, the game is right: log the difference as a finding.

## Part 1: Common actions

Element names in `code` are the `GameHUD.uxml` names for `playtest_input tap_element`.

### Dialogue and hints
| Action | How | Tutorial event |
|---|---|---|
| Advance dialogue | Tap the dialogue box once per line, until it closes | `dialogue_complete` |
| Read the current goal | `playtest_state` tutorial hint, or the hint banner at the bottom | |
| Restore a minimized hint | Tap the "Show hint" pill, bottom right | |
| Read gather progress | The line under the hint ("Up Quark 3/4, ...") on hold-N-items steps | |

### Fields (manual collection)
| Action | How |
|---|---|
| Collect from a field | `playtest_input collect target=<field id> count=N`. Each tile has a cooldown (`grid.fields[].cooldownLeft`) |
| Quark field | Gives Up and Down Quarks in equal amounts |
| Lepton field | Gives Electrons |
| Buy a new field | Build menu, after Field Synthesis. A quark pair costs 300e, then 3,000e, then 30,000e (x10 each); a lepton field costs 200e, then 2,000e |

A tap on a blocked target shows a message ("Collect electrons first!"). That message tells you what
the step wants.

### Maxwell's Demon (selling for entropy)
| Action | How | Tutorial event |
|---|---|---|
| Open | Tap the Demon (3x3 building) | `demon_opened` |
| Sell | Drag items to the right side, or `btn-demon-deposit-all`; use `btn-sell-decrease` / `btn-sell-increase` / `btn-sell-confirm` for a partial sale | |
| Close | `btn-demon-close` | `demon_closed` |

The camera is locked while the Demon is open.

### Drawer and panels
| Action | How | Tutorial event |
|---|---|---|
| Open the menu | `btn-drawer-handle`. Taps on the drawer are ignored for 250 ms while it slides in | `drawer_opened` |
| Recipes (hand crafting) | `btn-recipes` | `recipe_panel_opened` |
| Builder (place buildings) | `btn-buildings` | |
| Research | `btn-research` | |
| Other panels | `btn-codex`, `btn-upgrades` (prestige shop), `btn-managers`, `btn-daily`, `btn-achievements`, `btn-sites`, `btn-worlds`, `btn-shop`, `btn-settings` | |
| Close a panel | Its `btn-close-*` button. Opening any panel closes the others and the drawer | |

### Crafting by hand
| Action | How |
|---|---|
| Craft | Recipes panel, tap the recipe. It needs the inputs in the inventory. A disabled Craft button means inputs are missing |
| Proton | 2 Up Quark + 1 Down Quark |
| Neutron | 1 Up Quark + 2 Down Quark |
| Hydrogen | 1 Proton + 1 Electron |

Heavier items (Helium-4 and up) need a building, not the recipe panel.

### Research
| Action | How |
|---|---|
| Start | Research panel, tap the node. It costs entropy up front, then a real-time timer runs |
| One at a time | The lab is busy until the timer finishes |
| Wait it out | `playtest_control time_scale` (10x or less on benchmark runs) and `wait` |
| Tutorial costs | Recombination I 100e / 10 s, Hydrogen Synthesis 50e / 15 s, Automation I 25e / 20 s, Field Synthesis 250e / 20 s, Atomic Assembly 500e / 30 s |

Skipping a timer costs crystals, so it's not allowed in no-crystal briefs.

### Placing buildings
| Action | How |
|---|---|
| Start | Builder panel, then the building's `btn-place-<id>` |
| Choose a cell | One tap sets the candidate. A drag pans the map and never places |
| Rotate | ↻ on the popup (`btn-rotate-candidate`) or `btn-rotate-output`. Rotate before confirming, because the output direction matters for belts |
| Flip | `btn-flip-building` on the bottom bar |
| Confirm / clear | ✓ `btn-confirm-place` (disabled when the cell is invalid) / ✕ `btn-cancel-candidate` |
| Field output | A Harvester on a quark field asks Up or Down after ✓ |
| Leave placement | `btn-cancel-placement` |

Harvesters go on a field tile. A powered building must be inside a generator's radius: 3 tiles at
level 1, then 4, 5 and 6. While you place a generator, its coverage shows as blue tiles. A building
costs `base x 1.5^(n-1)` for each copy you own (1.4 and 1.3 for higher tiers).

### Conveyors
| Action | How |
|---|---|
| Start | Builder panel, Conveyor Belt (`btn-place-conveyor`) |
| Draw | Tap the start tile (the tile the source's output faces), then the end tile (the tile in front of the target's input). The route is filled in for you |
| Bend | `btn-conveyor-rotate` swaps the route's corner |
| Place / clear | ✓ `btn-conveyor-confirm` / ✕ `btn-conveyor-cancel-candidate` |
| Start a new belt | `btn-conveyor-new-start` |
| Erase belts | `btn-conveyor-mode` switches Create to Destroy. Removing a belt is free and asks nothing |
| Finish | `btn-cancel-conveyor` ("Done") |

In tutorial steps the right start and end tiles glow. A belt that links nothing shows "Belt not
linked" about half a second after ✓.

### Building inspector
| Action | How |
|---|---|
| Open | Tap a placed building |
| Set a recipe | Inspector recipe list, for example the Combiner set to Proton (`combiner_recipe_set`) |
| Empty buffers | The Empty buttons (disabled when the buffer is empty) |
| Upgrade | Speed is always available. Output and input capacity need research (`surplus_containment`, `feedstock_buffers`) |
| Power | Output, draw and status rows. A selected generator shows its coverage |
| Close | `btn-close-inspector` |

### Removing buildings
| Action | How |
|---|---|
| Deconstruct | Builder panel `btn-deconstruct`, tap the building (asks to confirm) or belt (no prompt). `btn-cancel-deconstruct` to leave |

### Camera
| Action | How |
|---|---|
| Pan | `playtest_input drag`, starting on empty map, never on UI. Use several short drags |
| Zoom / rotate | Two fingers or the scroll wheel / middle mouse. Few drivers need this |

### Power
| Action | How |
|---|---|
| Read | Top bar `⚡ draw / supply eV`. A `⚠` with `(N unpowered)` means a brownout or a building with no power. Unpowered buildings are tinted red |
| Fix supply | Place or upgrade a Basic Generator (50, 100, 200 then 400 eV) |

Combiner draw is 10 eV and Atom Generator draw is 20 eV. Harvesters, the Demon and generators
draw nothing.

### Time and idle
| Action | How |
|---|---|
| Wait for entropy | `time_scale` + `wait`. Production and timers speed up together |
| Close the app | `playtest_control away <seconds>`. Offline earnings are 20% of the rate, capped at 2 h. The idle-return modal (`btn-idle-return-claim`) appears on return |

## Part 2: When a run stalls

Work down the rungs in order. Each rung costs more realism than the one before. Log what you used
with `playtest_benchmark note`:

- Rung 1 needs no note unless it took more than 3 tries; then it is an S3 UX finding.
- Rung 2 is S2: the player could not have found it.
- Rung 3 is S1: a softlock or bug.

### Rung 0: Look first (every stall)
1. `playtest_state`: the tutorial step id, hint, open panels, dialogue, placement mode.
2. Read that step in `tutorial-walkthrough.md`: what advances it, what it blocks, how to get its items.
3. `read_console` for errors. Exceptions mean outcome `crashed`, not "stuck".
4. One `playtest_screenshot`, if the screen changed since the last one.

### Rung 1: Player fixes (normal input only)

| Symptom | Likely cause | Fix |
|---|---|---|
| Nothing reacts to taps on the map | A dialogue, panel, drawer, confirm prompt or the dev console is open | Finish the dialogue, close panels (`btn-close-*`), answer the prompt |
| Tap shows a message like "Collect quarks first!" | The step allows only one kind of target | Do what the message says; it names the target |
| A tap on a drawer row did nothing | It came less than 250 ms after the drawer opened | Wait a moment and tap again |
| Can't see the target | It's off screen or under a panel | Close panels, `drag` the map in short moves |
| `collect` returns `blocked: true` or misses every tap | The field is under HUD chrome | Close the drawer or panel, drag the field into the open, retry |
| Hint is gone | It's minimized, or a menu is open (the banner collapses to a pill) | Close menus, tap "Show hint" |
| Gather step won't advance | Short of one item | Read the progress line under the hint; collect the missing item |
| A hold-N-items step won't advance and Craft is disabled | Missing inputs | Check the recipe; craft the intermediate first (protons before hydrogen) |
| Research won't start | Not enough entropy, or another research is running | Sell stock at the Demon, or wait out the running timer |
| Research step done but nothing happens | A dialogue or "unlocked" step follows | Tap through the dialogue |
| ✓ is disabled while placing | Invalid cell: occupied, not a field (Harvester), out of bounds, or not enough entropy | Tap a different cell. For powered buildings, pick a cell inside the blue coverage. Check the cost against entropy |
| Building placed but the step didn't advance | The step counts a different building, or needs more of them (step 34 needs 3 Harvesters) | Re-read "Advances when" |
| "Belt not linked" warning | Belt is off the glowing tiles: one short, at the Demon's corner, or backwards | Extend from its end onto the glowing tile, or Destroy mode, erase, and redraw output to input |
| Belt placed, no warning, no advance | The step needs 2 links (steps 39 and 45) | Draw the second belt |
| Belts linked but nothing flows | No recipe set, unpowered, output buffer full, or the wrong input | Inspector: set the recipe, check power, Empty the buffer, check which input the belt feeds |
| Building tinted red / `⚠` in the power label | Out of generator range, or draw is more than supply | Move it into range (deconstruct and re-place) or add or upgrade a generator |
| Atom Generator output points away from the Demon | Not rotated before ✓ | Draw the belt from the side it does face, or deconstruct, re-place and rotate |
| Waiting for entropy feels endless | Normal idle pacing | `time_scale` and `wait`. Log the gap in minutes; that's the pacing data |

### Rung 2: Harness fixes (no game state is changed by hand)
| Fix | When |
|---|---|
| `playtest_control status` | The change-only summaries look wrong; this resets their baseline |
| Stop and replay Play Mode (`manage_editor` stop, then play, then `wait` for `loaded`) | The UI is in a state no tap clears. The save reloads where it left off; check the tutorial step afterwards. If it went back to the intro, something wiped the save (before #265, every test run did): log it as S1 |
| `load_fixture` for the nearest checkpoint | Only if the brief allows fixtures |

### Rung 3: Cheats (dev console; each one is a softlock finding)
Use the smallest cheat that unsticks the run, then go back to normal play.

| Stall | Cheat |
|---|---|
| A step that blocks collection asks for items you no longer have (steps 16, 17, 23, 24, **[check stock]**) | `add item <itemId> <qty>` for just the shortfall |
| A research step blocks collection and entropy is below the cost | `add currency <shortfall>` |
| One research is stuck or won't unlock | `unlock research <id>` |
| One tutorial step can't be finished by any means above | `tutorial skip <next step id>` (`tutorial list` for ids) |
| Everything else fails | End the run with outcome `stuck` and the last state |

Never use `skip tutorial`, `unlock all research` or `iap grant` to get unstuck in a no-crystal or
timed brief. They end the test.
