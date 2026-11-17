# Feature: Multiple Grids / Sites (Idle Space Miner-style planets)

**Status:** ALL FOUR PHASES IMPLEMENTED. Phase 1 PR #57 (`feature/multi-grids-p1`) — data + save model. Phase 2 PR #58 (`feature/multi-grids-p2`) — `SiteService` switch/unlock + per-site idle aggregation + dev console. Phase 3 PR #59 (`feature/multi-grids-p3`) — `sites-panel` UI + `SitesSubController`. Phase 4 (`feature/multi-grids-p4`, stacked on #59) — prestige clears all grids + `siteSnapshots` + resets `activeSiteIndex`, keeps `unlockedSites`; unlock costs verified against `economy-balance.md` (250K between ⑬/⑭, 10M after ⑭). Phase 5 (`feature/multi-grids-p5`, stacked on p4) — discoverability: one-shot `intro_quantum_domains` dialogue when `materials_science` (150K gate) unlocks, runtime dialogue lookup via `DialogueDatabaseSO`, `domainsIntroSeen` guard, `domains intro` dev command; **scene wiring done** (SiteService on Services, SitesSubController on HUD). PRs stack #57←#58←#59←p4←p5; merge oldest-first.
**Required reading:** `docs/agents/architecture.md`, `docs/agents/save-system.md`, `docs/agents/ecs-patterns.md`, `docs/agents/ui-toolkit.md`, `docs/agents/economy-balance.md`
**Scope estimate:** LARGEST of the four specs. 4 phases, **each phase ends with "stop, run full suite, commit, PR"** — do NOT attempt this in one pass. A session should pick up the next incomplete phase.
**Branch:** one branch per phase: `feature/multi-grids-p1` … `-p4` → PR into the current release branch (confirm with user).

## Design summary

Additional build sites ("Quantum Domains") with their own grid, unique field distributions, and escalating unlock costs — ISM's planet model. Exactly ONE site is "active" (live ECS simulation + rendering); inactive sites keep producing via the existing idle-snapshot mechanism. The player switches sites through a new panel; switching saves the current grid and loads the target.

**Load-bearing constraint (do not revisit):** inactive sites are NEVER live ECS. They are simulated exactly like offline progress — `IdleGraphAnalyzer` snapshots per site, paid out on switch-in or continuously aggregated. This reuses `OfflineCollectionService`/`GridSaveService.RebuildIdleSnapshot` wholesale and avoids multi-world ECS entirely. If a requirement seems to need live inactive sites, stop and ask the user.

**Prestige semantics (pinned):** prestige resets ALL sites' grids and the active site back to site 0, but site *unlocks* survive prestige (they're expensive; this mirrors how recipe knowledge survives). Unlock costs are entropy, so re-buying each run would be punitive.

## Balance numbers (final — do not derive your own)

v1 ships 2 additional sites (3 total including the starting grid).

| Site | id | Unlock cost (entropy) | Size | Field distribution |
|---|---|---|---|---|
| 0 | `site_origin` | free (start) | current default | current default field set |
| 1 | `site_quark_sea` | 250,000e | same as origin | quark fields ×2 density, no lepton fields |
| 2 | `site_lepton_storm` | 10,000,000e | same as origin | lepton/electron fields ×2 density, no quark fields |

(250K lands between the ⑬ 150K and ⑭ 5M research gates; 10M after ⑭ — see `economy-balance.md` phase table. Field ids must reference existing entries in `game_data.json` `fields`.)

---

## Phase 1 — Data + save model (no behavior change)

**Goal:** saves support N grids; game still plays identically on grids[0].

### Schema (`game_data.json`): new top-level `"sites"` array; bump `_meta.version`. Importer → `SiteSO` assets (after Fields import — cross-refs field ids). Model classes in `Editor/GameDataModel.cs`.

```json
"sites": [
  { "id": "site_origin", "display_name": "Origin Domain", "unlock_cost": 0,
    "field_overrides": [] },
  { "id": "site_quark_sea", "display_name": "Quark Sea", "unlock_cost": 250000,
    "field_overrides": [ { "field": "quark_field", "density_multiplier": 2.0 },
                          { "field": "electron_field", "density_multiplier": 0.0 } ] }
]
```

### Save (`SaveData.cs` — additive with migration shim):

```csharp
// in CurrentRunData (grids reset on prestige):
public List<GridSaveData> grids = new();        // index = site index; grids[0] = legacy 'grid'
public int activeSiteIndex;                      // default 0
// in SaveData root (survives prestige):
public List<string> unlockedSites = new();       // site ids; site_origin implicit
```

**Migration shim:** keep the legacy `CurrentRunData.grid` field untouched. Read-preference everywhere: `grids.Count > 0 ? grids[activeSiteIndex] : grid`. On first save after load, populate `grids[0]` from `grid` and keep writing BOTH (`grid` mirrors `grids[0]`) so a rollback to an older build still reads saves. Centralize in a helper `SaveData.ActiveGrid` property — all call sites go through it (`GridSaveService`, `IdleGraphAnalyzer` call sites, `RebuildIdleSnapshot`).

### Files: `game_data.json`, `GameDataModel.cs`, `GameDataImporter.cs`, `ScriptableObjects/SiteSO.cs` (copy `FieldSO.cs` style), `SaveData.cs`, `GridSaveService.cs` (switch to `ActiveGrid` property).
### Tests: legacy-save load (old JSON with only `grid` → plays on it, `grids[0]` populated on save); round-trip; `SOSchemaTests` extension for SiteSO.
### GATE: full suite green → commit → PR. Stop here.

---

## Phase 2 — Site switching + idle aggregation

**Goal:** `SiteService.SwitchTo(index)` works (dev-console only; no UI yet).

- New `Assets/Scripts/Services/SiteService.cs` (copy `PersistentUpgradeService.cs` service shape): `UnlockSite(id)` (deduct entropy via `ECSLoadBridge`-style singleton write, pattern `ResearchService.DeductEntropy`, `ResearchService.cs:168`), `SwitchTo(index)`:
  1. `SaveManager.SaveLocal()` (flushes ECS → `grids[current]`, rebuilds its idle snapshot — `GridSaveService.cs:335`).
  2. Set `activeSiteIndex`; destroy current grid building/conveyor entities (copy the destruction loop in `PrestigeSystem.cs:72-77` — keep `EntropySinkTag` fixtures).
  3. `GridSaveService.LoadGrid(forceApply: true)` for the new index; `FieldGenerator` regenerates fields per the site's `field_overrides` (deterministic seed per site index — extend the existing test-mode seeding in `Assets/Scripts/Gameplay/FieldGenerator.cs`).
- Per-site idle snapshots: `IdleCollectionSnapshot` gains a sibling list `List<IdleCollectionSnapshot> siteSnapshots` (additive). `OfflineCollectionService.CalculateAndApply` iterates ALL unlocked sites' snapshots, not just the active one. Inactive-site production = the same capped idle math; the `idleCollectionApplied` guard stays global.
- Dev console commands: `site list / site switch <n> / site unlock <id>` (register in `Assets/Scripts/DevConsole/DevConsoleController.cs`, copy the `set multiplier` registration pattern `:417-440`).

### Tests: switch round-trip (place building on site 0 → switch → switch back → building intact); idle aggregation across 2 sites (extend `OfflineCollectionServiceTests.cs`); unlock deduction.
### Pitfalls for this phase
- `GridOccupancy` (`Assets/Scripts/Grid/GridOccupancy.cs`) must be cleared and rebuilt on switch — grep its reset path; tutorial-skip presets already rebuild grids, copy that flow (`GridSaveService.cs:101-111` region).
- `NetWorthSystem` only sees the active site's entities. Decision: net worth = active site + `Σ` inactive sites' snapshot-derived inventory value is OUT of scope; net worth stays active-site (+ global inventory), document in code comment.
- Conveyor entities are separate from buildings — destroy both (grep `ConveyorData` destruction handling in deconstruct flow).
### GATE: full suite green → commit → PR. Stop here.

---

## Phase 3 — UI

**Goal:** player-facing site panel.

- `sites-panel` in `GameHUD.uxml` (copy `prestige-panel` `:152` structure) + `btn-sites` nav button; `SitesSubController.cs` (copy `PrestigeShopSubController.cs`): one row per site — name, locked/unlocked, unlock button with entropy cost (affordability check like research), "Travel" button on unlocked non-active sites, active badge.
- Switching mid-placement/conveyor mode must cancel placement overlays first — call the same cancel path the deconstruct toggle uses in `HUDController`.
- Confirm dialog before unlock (large entropy spend) — reuse the prestige confirm pattern if one exists in `prestige-panel`, else a simple two-tap (button becomes "Confirm?" for 3s).

### Tests: `HUDControllerTests.cs`-style wiring test. Manual: panel visible, travel works, placement overlay cancelled on switch.
### GATE: full suite green → commit → PR. Stop here.

---

## Phase 4 — Prestige integration + balance pass

- `PrestigeSystem.OnUpdate`: building destruction already clears the live site; ALSO clear `save.currentRun.grids` (all sites) and `activeSiteIndex = 0` in the SaveData reset block (`PrestigeSystem.cs:103-110`). `unlockedSites` is NOT touched (survives).
- `PrestigeSaveWatcher` clears idle snapshot on prestige — extend to clear `siteSnapshots` too.
- Verify unlock costs against the phase table in `economy-balance.md` (250K / 10M as specified — no invention).

### Tests: prestige clears all grids + resets active site, keeps unlocks; extend `PrestigeSystemTests.cs`.
### GATE: full suite green → commit → PR. Done.

---

## Definition of done (every phase)

- [ ] JSON parses; importer run; full suite green BOTH platforms (`scripts/test-local.ps1`), results XML reported
- [ ] Legacy save (single-grid) loads and plays
- [ ] Phase-specific manual check done in editor play mode (and component-wiring flagged to user for Phase 3)
- [ ] No generated assets hand-edited; no edits beyond the phase's scope

## Out of scope (all phases)

Inter-site logistics/transfers, per-site prestige, more than 2 extra sites, site-specific buildings/recipes, live simulation of inactive sites, site visuals beyond field distribution, net worth aggregation across sites.
