# Dev Console & Test Mode — Live Testing Checklist

This guide covers manual verification of every dev-console testing feature. Work through each section top to bottom. Check off items as you go. Sections marked **⚠️ PENDING** require the Test Mode feature (GameBootstrap field-seed + save suppression) to be implemented first.

---

## How to Use

- **Open dev console**: Press the backtick/tilde key `` ` `` while in Play Mode (or shake the device in a dev build)
- **Submit a command**: Type it and press Enter (or click Submit)
- **Close console**: Click × or press `` ` `` again
- **Commands are case-insensitive**: `Tutorial Skip INTRO_DIALOGUE` works the same as `tutorial skip intro_dialogue`
- **Exact expected output** is shown after each command — match it character-for-character (minus the leading `>` echo line)

---

## Before Every Test Session

- [ ] **Start with a clean save.** Open the console and run:
  ```
  clear save
  ```
  Expected: `Save cleared. Reloading...` — scene reloads, tutorial starts at step 0

- [ ] **Confirm fresh state.** After reload, open console and run:
  ```
  show progress
  ```
  Verify: `BaseCurrency` is near the starting value (baked default, roughly 0–10e before tutorial grants entropy), `CurrentTier` is 0 or 1

---

## Part 1 — Dev Console Basics

### 1.1 Console Opens Correctly

- [ ] Press `` ` `` — console overlay appears, input field is focused
- [ ] Console log shows: `Dev console ready. Type 'help' for commands.`
- [ ] Press `` ` `` again — console closes

### 1.2 Help Command

- [ ] Type `help` → command list appears; confirm these entries are present:
  - `tutorial list`
  - `tutorial skip <id>`
  - `skip tutorial`
  - `add currency <amount>`
  - `show progress`
  - `save`
  - `clear save`

### 1.3 Unknown Command

- [ ] Type `foo bar baz`
  - Expected output (red): `Unknown command. Type 'help' for a list of commands.`

---

## Part 1.5 — Building Spawn (visual / structure testing)

Spawns buildings through the real `BuildingPlacer`, so they get the same components, ports, holes, and
procedural structure as a player placement. Use this to eyeball every building's art without grinding
research. Spawned buildings persist in the save (use `clear save` to wipe).

### Commands

| Command | What it does |
|---------|--------------|
| `list buildings` | Lists every placeable building: `name  #id  WxH footprint  structureKind` |
| `spawn building <id>` | Spawns one building at the first free cell. `<id>` = asset name (e.g. `atomic_assembler`) or numeric `#id` |
| `spawn building <id> <x> <y>` | Spawns one building at grid cell (x, y) |
| `spawn all buildings` | Spawns one of every building in `availableBuildings` for a one-shot visual sweep |

Notes:
- Only buildings wired into the HUD's `BuildingPlacementController.availableBuildings` can be spawned —
  `list buildings` shows exactly what's available.
- `structureKind` in the listing tells you which procedural form to expect: `AtomGenerator` (orbital
  nucleus), `None` (placeholder cube — not yet given a custom form). The harvester (spindle) and
  Maxwell's Demon (torus) are driven by their gameplay components, so they show `None` here but still
  render their bespoke structure.
- Field collectors are spawned with a South output direction; they may render with the default (no-field)
  colour if dropped on a bare cell.
- Production buildings only fire their completion flare when inputs are satisfied + powered. To see the
  Atom Generator's flare, also `spawn building basic_generator` next to it and `add item proton 99` etc.

### What to verify per structure

- [ ] `spawn building atomic_assembler` → round glowing **nucleus** with 3 tilted electrons orbiting it
- [ ] Atom Generator shows a lit **output aperture** on one edge and recessed **intake mouths** on the others
- [ ] `spawn building harvester` → tapering **spindle** with a front emission door
- [ ] `spawn building maxwells_demon` → flat gold **torus** filling its 3x3 footprint
- [ ] `spawn all buildings` → every building shows its bespoke structure (no placeholder cubes remain):
  generator spire, combiner rounded prism, isotopic breathing nucleus, molecular cluster, forge crucible,
  fabricator precision core, containment lead dome

---

## Part 1.6 — Snapshots (named save-states)

Capture the **entire current game state** (grid buildings/conveyors/fields, currency, inventory,
tutorial progress, prestige, research) to a named file, then jump back to it any time. Unlike
`tutorial skip <id>` (which restores scripted checkpoints), snapshots capture whatever you have set up
right now — place buildings, wire conveyors, grind some resources, then snapshot it as a reusable
starting point for testing.

Snapshots are stored in `Application.persistentDataPath/snapshots/<name>.json`, separate from the real
`save.json`, so they never collide with normal play. Names are a single word (`a-z`, `0-9`, `-`, `_`).

### Commands

| Command | What it does |
|---------|--------------|
| `snapshot save <name>` | Flushes live ECS + grid into the save, then writes `snapshots/<name>.json` |
| `snapshot load <name>` | Swaps the in-memory save to the snapshot and reloads the scene (full re-apply) |
| `snapshot list` | Lists every saved snapshot |
| `snapshot delete <name>` | Removes a snapshot file |

### How to verify

- [ ] Place a couple of buildings + a conveyor, `add currency 5000`, then `snapshot save mytest`
  - Expected: `Snapshot 'mytest' saved — N building(s), 5000e, tutorial '<step>'.`
- [ ] `snapshot list` → shows `mytest`
- [ ] `add currency 999999`, spawn more buildings, then `snapshot load mytest`
  - Expected: `Loading snapshot 'mytest'...`, scene reloads
  - After reload, `show progress` reports the **snapshot's** currency (not the 999999), and the grid
    matches what you had when you saved
- [ ] `snapshot delete mytest` → `Snapshot 'mytest' deleted.`; `snapshot list` no longer shows it

Notes:
- `snapshot load` reloads the scene (~1s), so the full save→ECS path re-applies the state cleanly.
- Snapshots are editor / development-build only (the whole dev console is gated behind
  `UNITY_EDITOR || DEVELOPMENT_BUILD`).
- The auto-save loop will persist the loaded snapshot as the live save; if cloud save is on, it will
  also push to cloud on the next interval. Use `clear save` to get back to a clean slate.

---

## Part 2 — tutorial list

### 2.1 Basic Output

- [ ] Type `tutorial list`
  - First line: `Tutorial steps (71 total):`
  - First entry: `  [ 0]  intro_dialogue`
  - Last entry:  `  [70]  particles_loop_complete`
  - Scroll through — all 71 lines present, no gaps or repeated indices

### 2.2 Spot-Check Key Step IDs

Scan the list and confirm each of these appears at the correct index:

| [ ] | Index | ID |
|-----|-------|----|
| [ ] | `[11]` | `buy_recombination_i` |
| [ ] | `[20]` | `buy_hydrogen_synthesis` |
| [ ] | `[29]` | `buy_automation_i` |
| [ ] | `[30]` | `place_first_building` |
| [ ] | `[31]` | `place_conveyor` |
| [ ] | `[41]` | `nucleon_loop_complete` |
| [ ] | `[43]` | `place_atom_generator` |
| [ ] | `[51]` | `buy_atomic_assembly` |
| [ ] | `[59]` | `buy_isotope_engineering` |
| [ ] | `[64]` | `buy_radioactive_isotopes` |
| [ ] | `[70]` | `particles_loop_complete` |

---

## Part 3 — tutorial skip: Phase 1 (Steps 0–10, Pre-Any-Research)

Steps in this range have no research gates. Entropy stays at 250. No research unlocks. No grid changes.

### 3.1 Start of Tutorial

- [ ] `tutorial skip intro_dialogue`
  - Expected: `Skipped to 'intro_dialogue' [0]. Entropy: 250e. 0 research node(s) unlocked.`
  - In-game: HUD hint bar is **empty** (intro dialogue fires), intro dialogue overlay appears, entropy shows **250**
  - Research panel: all nodes locked / no purchases shown

### 3.2 First Collection Step

- [ ] `tutorial skip collect_first_electron`
  - Expected: `Skipped to 'collect_first_electron' [1]. Entropy: 250e. 0 research node(s) unlocked.`
  - In-game: Hint bar shows **"Tap the electron field to collect 5 electrons."**
  - Electron field is highlighted; quark field and buildings are locked (cannot interact)

### 3.3 Quark Collection

- [ ] `tutorial skip collect_quarks`
  - Expected: `Skipped to 'collect_quarks' [6]. Entropy: 250e. 0 research node(s) unlocked.`
  - In-game: Hint bar shows **"Tap the quark field to collect 5 Up Quarks and 5 Down Quarks."**
  - Quark field is highlighted; collection filter active for Quarks

### 3.4 Research Drawer Prompt

- [ ] `tutorial skip open_research_drawer`
  - Expected: `Skipped to 'open_research_drawer' [10]. Entropy: 250e. 0 research node(s) unlocked.`
  - In-game: Hint bar shows **"Open the menu on the left to find the Research panel."**
  - Drawer handle button is pulsing; building interactions are blocked

---

## Part 4 — tutorial skip: Research Gate 1 (Recombination I)

### 4.1 AT the Purchase Step (Research Not Yet Unlocked)

- [ ] `tutorial skip buy_recombination_i`
  - Expected: `Skipped to 'buy_recombination_i' [11]. Entropy: 265e. 0 research node(s) unlocked.`
  - In-game: Hint bar shows **"Purchase Recombination I to unlock nucleon synthesis."**
  - Entropy display: **265**
  - Research panel: **Recombination I is available to purchase** (not yet unlocked, shows 100e cost)
  - Research panel: Hydrogen Synthesis is locked/unavailable

### 4.2 PAST the Purchase Step (Research Now Unlocked)

- [ ] `tutorial skip recombination_unlocked`
  - Expected: `Skipped to 'recombination_unlocked' [12]. Entropy: 165e. 1 research node(s) unlocked.`
  - In-game: Entropy display: **165** (100e was "spent")
  - Research panel: **Recombination I shows as purchased/unlocked**
  - Build menu: Proton and Neutron recipes are now visible (unlocked by recombination_i)

### 4.3 Nucleon Gathering (Research Persists)

- [ ] `tutorial skip collect_quarks_for_nucleons`
  - Expected: `Skipped to 'collect_quarks_for_nucleons' [13]. Entropy: 165e. 1 research node(s) unlocked.`
  - In-game: Hint bar shows **"Collect 8 Up Quarks and 8 Down Quarks..."**
  - Research panel: Recombination I still shows as purchased ✓

---

## Part 5 — tutorial skip: Research Gate 2 (Hydrogen Synthesis)

### 5.1 AT the Purchase Step

- [ ] `tutorial skip buy_hydrogen_synthesis`
  - Expected: `Skipped to 'buy_hydrogen_synthesis' [20]. Entropy: 177e. 1 research node(s) unlocked.`
  - In-game: Hint bar shows **"Purchase Hydrogen Synthesis in the Research panel..."**
  - Entropy: **177**
  - Research panel: Recombination I = purchased ✓; **Hydrogen Synthesis = available** (50e cost)

### 5.2 PAST the Purchase Step

- [ ] `tutorial skip gather_for_hydrogen`
  - Expected: `Skipped to 'gather_for_hydrogen' [21]. Entropy: 127e. 2 research node(s) unlocked.`
  - In-game: Entropy: **127**
  - Research panel: Recombination I ✓, **Hydrogen Synthesis ✓**
  - Build menu: Hydrogen recipe now visible

---

## Part 6 — tutorial skip: Research Gate 3 (Automation I)

### 6.1 AT the Purchase Step

- [ ] `tutorial skip buy_automation_i`
  - Expected: `Skipped to 'buy_automation_i' [29]. Entropy: 137e. 2 research node(s) unlocked.`
  - In-game: Hint bar shows **"Purchase Automation I in the Research panel to unlock the Harvester."**
  - Entropy: **137**
  - Research panel: 2 nodes purchased; **Automation I = available** (25e cost)
  - Build menu: Harvester NOT yet available

### 6.2 PAST the Purchase Step — Pre-Building Placement

- [ ] `tutorial skip place_first_building`
  - Expected: `Skipped to 'place_first_building' [30]. Entropy: 112e. 3 research node(s) unlocked.`
  - In-game: Hint bar shows **"Place a Harvester on the quark or lepton field to begin automatic collection."**
  - Entropy: **112** (enough to afford the 100e Harvester)
  - Research panel: All 3 tutorial research nodes purchased ✓
  - Build menu: **Harvester is available** to place
  - Build button in nav is pulsing

---

## Part 7 — tutorial skip: First Grid Step (Step 31+)

From step 31 onward, grid presets have not yet been captured from a playthrough. The skip sets ECS state, entropy, and research correctly — but **does not place buildings**. The console will warn you. You'll need to place buildings manually after skipping.

### 7.1 Grid Warning Fires Correctly

- [ ] `tutorial skip place_conveyor`
  - Expected: `Skipped to 'place_conveyor' [31]. Entropy: 12e. 3 research node(s) unlocked. (grid preset not yet captured — place buildings manually if needed)`
  - Output is shown in **green** (success, not error) despite the warning note
  - In-game: Hint bar shows **"Connect the Harvester to Maxwell's Demon with a conveyor belt."**
  - Entropy: **12**
  - Grid: **No buildings placed by the skip** — grid is empty (buildings must be placed manually)

### 7.2 SFC Phase

- [ ] `tutorial skip intro_sfc`
  - Expected: `Skipped to 'intro_sfc' [33]. Entropy: 50e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Hint bar shows **"Save up entropy to place the Strong Force Combiner."**

- [ ] `tutorial skip place_sfc`
  - Expected: `Skipped to 'place_sfc' [34]. Entropy: 200e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Entropy: **200** (enough to buy the 200e SFC); build menu shows SFC available

### 7.3 Nucleon Loop Complete

- [ ] `tutorial skip nucleon_loop_complete`
  - Expected: `Skipped to 'nucleon_loop_complete' [41]. Entropy: 100e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Hint bar shows **"Your nucleon pipeline is running automatically."**

### 7.4 Atom Generator

- [ ] `tutorial skip place_atom_generator`
  - Expected: `Skipped to 'place_atom_generator' [43]. Entropy: 350e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Entropy: **350** (exact cost of Atom Generator); build menu shows Atom Generator available

### 7.5 Building Upgrades

- [ ] `tutorial skip upgrade_atom_generator_speed`
  - Expected: `Skipped to 'upgrade_atom_generator_speed' [48]. Entropy: 1000e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Entropy: **1000** (needed for L2 speed upgrade); Atom Generator interaction is the only building gate

---

## Part 8 — tutorial skip: Post-Tutorial Research Gates (Steps 51–70)

### 8.1 Atomic Assembly

- [ ] `tutorial skip buy_atomic_assembly`
  - Expected: `Skipped to 'buy_atomic_assembly' [51]. Entropy: 500e. 3 research node(s) unlocked. (grid preset not yet captured...)`
  - In-game: Research panel: 3 previous nodes purchased; **Atomic Assembly = available** (500e)

- [ ] `tutorial skip atomic_assembly_unlocked`
  - Expected: `Skipped to 'atomic_assembly_unlocked' [52]. Entropy: 50e. **4** research node(s) unlocked.`
  - In-game: **Atomic Assembly now shows as purchased** ✓
  - Atom Generator recipe panel should now include Helium-4 and other elements

### 8.2 Heavy Elements

- [ ] `tutorial skip buy_heavy_elements`
  - Expected: `Skipped to 'buy_heavy_elements' [55]. Entropy: 3000e. 4 research node(s) unlocked. (grid preset...)`
  - In-game: Entropy: **3000**; Heavy Elements shows as available (3000e cost); 4 prior nodes purchased

- [ ] `tutorial skip heavy_elements_unlocked`
  - Expected: `Skipped to 'heavy_elements_unlocked' [56]. Entropy: 50e. **5** research node(s) unlocked.`

### 8.3 Isotope Engineering

- [ ] `tutorial skip buy_isotope_engineering`
  - Expected: `Skipped to 'buy_isotope_engineering' [59]. Entropy: 1500e. 5 research node(s) unlocked. (grid preset...)`

- [ ] `tutorial skip isotopes_unlocked`
  - Expected: `Skipped to 'isotopes_unlocked' [60]. Entropy: 50e. **6** research node(s) unlocked.`

### 8.4 Radioactive Isotopes (Final Gate)

- [ ] `tutorial skip buy_radioactive_isotopes`
  - Expected: `Skipped to 'buy_radioactive_isotopes' [64]. Entropy: 5000e. 6 research node(s) unlocked. (grid preset...)`
  - In-game: Entropy: **5000**; both Isotope Engineering and Heavy Elements nodes show as purchased

- [ ] `tutorial skip particles_loop_complete`
  - Expected: `Skipped to 'particles_loop_complete' [70]. Entropy: 50e. **7** research node(s) unlocked.`
  - This is the **last** step in the flow (index 70)

---

## Part 9 — Error Handling & Edge Cases

### 9.1 Unknown Step ID

- [ ] `tutorial skip not_a_real_step`
  - Expected output (red): `Unknown step 'not_a_real_step'. Type 'tutorial list' to see all IDs.`

### 9.2 Partial Step Name (No Match)

- [ ] `tutorial skip intro`
  - Expected output (red): `Unknown step 'intro'. Type 'tutorial list' to see all IDs.`
  - Confirms partial matching is not supported — IDs must be exact

### 9.3 Wrong Token Count (No Arg)

- [ ] `tutorial skip` (no ID after skip)
  - Expected output (red): `Unknown command. Type 'help' for a list of commands.`
  - Confirms the registry correctly rejects a 2-token input against the 3-token pattern

### 9.4 Case Insensitivity

- [ ] `TUTORIAL SKIP BUY_RECOMBINATION_I`
  - Expected: `Skipped to 'buy_recombination_i' [11]. Entropy: 265e. 0 research node(s) unlocked.`
  - Upper-case input resolves to the canonical lower-case step ID

### 9.5 tutorial list When Flow Not Loaded

> Skip this unless you've deliberately unloaded the tutorial flow asset. If TutorialFlowSO.Current is null, both `tutorial list` and `tutorial skip` should return red error messages starting with `Error: TutorialFlowSO not loaded`.

---

## Part 10 — State Persistence

These checks confirm that `tutorial skip` correctly persists state to SaveData so that a manual scene reload arrives at the right step.

### 10.1 Skip → Save → Reload → Verify

- [ ] Run: `tutorial skip collect_quarks_for_nucleons`
- [ ] Run: `save`
  - Expected: `Saved.`
- [ ] Run: `reload`
  - Scene reloads
- [ ] After reload: open console, run `show progress`
  - Verify `BaseCurrency` ≈ **165** (loaded from save)
- [ ] Confirm tutorial hint bar shows **"Collect 8 Up Quarks and 8 Down Quarks..."** after load
- [ ] Open Research panel: **Recombination I is purchased** ✓

### 10.2 Skip to Research Gate → Reload → Research Persists

- [ ] Run: `tutorial skip place_first_building`
- [ ] Run: `save` → `reload`
- [ ] After reload: Research panel shows all **3 nodes purchased** (recombination_i, hydrogen_synthesis, automation_i)
- [ ] Entropy ≈ **112**
- [ ] Hint bar shows **"Place a Harvester..."**

### 10.3 Explicit save Command Still Works in Test Mode
> **⚠️ Run this AFTER Test Mode is implemented.** But record expected behavior here:
> - Background saves suppressed → running `save` from the console should still write to disk
> - After `save` + `reload`, state should be exactly as above

---

## Part 11 — ⚠️ PENDING: Test Mode — Field Determinism

> **Requires:** Test Mode checkbox on GameBootstrap + `FieldGenerator` seed injection implemented.

### Setup

- [ ] In the Editor (not Play Mode), find **Managers** GameObject in Hierarchy → select **GameBootstrap** component
- [ ] Confirm **"Test Mode — Editor Only"** header is visible with two fields:
  - `Test Mode Enabled` — default **unchecked**
  - `Test Mode Field Seed` — default **42**

### 11.1 Default Behavior Is Unchanged

- [ ] With `Test Mode Enabled` = **unchecked**: run `clear save` then enter Play
- [ ] Observe field positions on the grid
- [ ] Exit Play, re-enter Play → `clear save` again
- [ ] Observe field positions: **they should differ** from the first run (random placement still active)

### 11.2 Seed Produces Consistent Layout

- [ ] Check `Test Mode Enabled`, leave seed = **42**
- [ ] Enter Play → `clear save` → note all three field positions (approximate tile coords)
- [ ] Exit Play → Enter Play again → `clear save`
- [ ] Field positions must be **identical** to the first run
- [ ] Repeat a third time — fields must be in **exactly the same positions** again
- [ ] Write down the canonical test layout:
  - Electron field: `(__, __)`
  - Quark field 1:  `(__, __)`
  - Quark field 2:  `(__, __)` ← adjacent to Quark field 1

### 11.3 Different Seed Produces Different Layout

- [ ] Change `Test Mode Field Seed` to **99** → Enter Play → `clear save`
- [ ] Note field positions → they should differ from seed 42 layout
- [ ] Change back to **42** → verify original layout returns

### 11.4 Non-Test-Mode Unaffected

- [ ] Uncheck `Test Mode Enabled` → Enter Play → `clear save`
- [ ] Fields randomize again (multiple runs produce different positions)
- [ ] Confirms the flag is the exclusive control of determinism

---

## Part 12 — ⚠️ PENDING: Test Mode — Save Suppression

> **Requires:** Test Mode save-guard in `SaveManager` implemented.

### Setup

- [ ] Check `Test Mode Enabled` = **true**, seed = **42**

### 12.1 Auto-Save Does Not Fire

- [ ] Enter Play → wait **90 seconds** (longer than the 60s auto-save interval)
- [ ] Check the save file timestamp: `Application.persistentDataPath/save.json`
  - It should **not** be updated during this wait
  - Alternatively: run `show progress`, make a visible change (e.g. `add currency 999`), wait 90s, exit Play, re-enter Play — entropy should **not** be 999 (save never fired)

### 12.2 Pause Does Not Save

- [ ] Enter Play → run `add currency 999` → alt-tab away (triggers OnApplicationPause)
- [ ] Return to Unity → exit Play → re-enter Play
- [ ] Entropy should **not** be 999 — pause-triggered save was suppressed

### 12.3 Quit Does Not Save

- [ ] Enter Play → run `add currency 999` → exit Play (triggers OnApplicationQuit)
- [ ] Re-enter Play → entropy should **not** be 999

### 12.4 Explicit Console save Still Works

- [ ] Enter Play → run `add currency 999` → run `save`
- [ ] Exit Play → re-enter Play
- [ ] Entropy should be **≈ 1249** (starting default + 999) — explicit save respected ✓

### 12.5 tutorial skip Reload Cycle Works

- [ ] `tutorial skip gather_for_hydrogen` → (skip internally calls SaveLocal before reloading)
- [ ] Scene reloads automatically
- [ ] After reload: entropy ≈ **127**, 2 research unlocked ✓
- [ ] Confirms `tutorial skip` can still write save data even with background saves off

---

## Part 13 — ⚠️ PENDING: Combined Workflow

> **Full test of the intended testing workflow once Test Mode is implemented.**

### Canonical Testing Session

- [ ] Enable Test Mode (seed 42)
- [ ] Enter Play → run `clear save`
- [ ] Note field positions — should match seed 42 layout every time
- [ ] Run `tutorial skip buy_automation_i` → entropy 137, 2 research unlocked
- [ ] Open research panel → **Automation I available** (25e) ✓
- [ ] Purchase Automation I manually → hint advances to "Place a Harvester..."
- [ ] Run `tutorial skip nucleon_loop_complete` → entropy 100, 3 unlocked
  - Note: grid is empty (preset pending), but all research is correct for that phase
- [ ] Run `add currency 200` to simulate idle income
- [ ] Manually place SFC + Generator + Harvesters to test step conditions
- [ ] Confirm tutorial advances through each step's condition normally

### Confirm No State Bleed Between Test Runs

- [ ] While still in the above session, run `clear save`
- [ ] State resets to fresh game — fields appear at same seed-42 positions ✓
- [ ] Confirm research panel is empty again (no research purchased)
- [ ] Confirm entropy is back to starting default

---

## Quick Reference — Research Count by Skip Target

| Skip target | Research unlocked | Nodes that are purchased |
|---|---|---|
| Steps 0–10 | 0 | *(none)* |
| Step 11 `buy_recombination_i` | 0 | *(none — about to buy it)* |
| Steps 12–19 | 1 | recombination_i |
| Step 20 `buy_hydrogen_synthesis` | 1 | recombination_i |
| Steps 21–28 | 2 | + hydrogen_synthesis |
| Step 29 `buy_automation_i` | 2 | recombination_i, hydrogen_synthesis |
| Steps 30–50 | 3 | + automation_i |
| Step 51 `buy_atomic_assembly` | 3 | recombination_i, hydrogen_synthesis, automation_i |
| Steps 52–54 | 4 | + atomic_assembly |
| Step 55 `buy_heavy_elements` | 4 | + atomic_assembly |
| Steps 56–58 | 5 | + heavy_elements |
| Step 59 `buy_isotope_engineering` | 5 | + heavy_elements |
| Steps 60–63 | 6 | + isotopes |
| Step 64 `buy_radioactive_isotopes` | 6 | + isotopes |
| Steps 65–70 | 7 | + radioactive_isotopes |

## Quick Reference — Entropy by Phase Entry Point

| Phase entry point | Entropy granted | Why |
|---|---|---|
| Tutorial start | 250 | Starting default |
| `buy_recombination_i` | 265 | After selling e/q (+15) |
| After recombination_i | 165 | After −100e purchase |
| `buy_hydrogen_synthesis` | 177 | After selling P/N (+12) |
| After hydrogen_synthesis | 127 | After −50e purchase |
| `buy_automation_i` | 137 | After selling H (+10) |
| After automation_i | 112 | After −25e purchase |
| `place_conveyor` | 12 | After −100e Harvester |
| `place_sfc` | 200 | Idle income; enough for SFC |
| `nucleon_loop_complete` | 100 | Pipeline running |
| `place_atom_generator` | 350 | Idle; exact Atom Gen cost |
| `upgrade_atom_generator_speed` | 1000 | Idle; exact L2 speed cost |
| `buy_atomic_assembly` | 500 | Idle; exact research cost |
| `buy_heavy_elements` | 3000 | Idle; exact research cost |
| `buy_isotope_engineering` | 1500 | Idle; exact research cost |
| `buy_radioactive_isotopes` | 5000 | Idle; exact research cost |
