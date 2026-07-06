# Feature: Chemistry & Biology Tracks (Worlds)

**Status:** IN PROGRESS — **Phases 1–3 DONE** (2026-07-06): P1 World data model + save partition (PR #119); P2 field-type string refactor + `OrganicCompound` items + element fields + `site_chem_lab` (PR #120); P3 `WorldService` (unlock + prereq gate + travel via `SiteService`) + `world` dev commands (branch `feat/worlds-chem-phase3`). Chemistry-focused; Biology content deferred. Phases 4–6 not started. Discussion progression map (v0.1) rendered as an HTML artifact; balance numbers below are first-pass anchors, not committed.
**Required reading:** `docs/agents/architecture.md`, `docs/agents/chemistry-biology.md` (content model), `docs/agents/data-pipeline.md`, `docs/agents/save-system.md`, `docs/agents/ecs-patterns.md`, `docs/agents/economy-balance.md`, `docs/agents/ui-toolkit.md`
**Scope estimate:** XL. 5 build phases + 1 deferred follow-up; **each phase ends "stop, run full suite, commit, PR."** Do NOT attempt in one pass. A session picks up the next incomplete phase.
**Branch:** one branch per phase → PR into the current integration/release branch (confirm target with user).

## Why

The game is a single **Particle Physics** track (subatomic → elements → molecules → materials → components → Dyson Sphere). Heavy elements unlock far too early (`heavy_elements` = depth-2, 3,000e, right after the tutorial), flattening the mid-game. We open two new thematic tracks in the **middle** of the lifecycle so the heavy/cosmic content can move to the back:

1. **Chemistry** — its own map whose fields *yield elements* (mined, not synthesized); research tree refines elements → compounds, culminating in **basic organic compounds**.
2. **Biology** — its own map whose fields yield **organic compounds**; research tree grows biomolecules → cells → organisms.

Target arc: *matter → chemistry → life → heavy/cosmic physics → megastructure.*

## Design decisions (locked — do not revisit)

- **Architecture: a Track/World layer ABOVE the existing Site system.** Each World (Physics / Chemistry / Biology) owns its own set of Sites + research tree + map theme. Reuse the multi-Site machinery (`SiteService`, per-site save grids, per-site idle snapshots, `FieldGenerator.GenerateForSite`, field-introduction-via-override). See `chemistry-biology.md` for the model.
- **Gating: pure economic.** A World opens by paying entropy + having the prerequisite unlocks in the prior World. **No prestige-count hook** (the `PrestigeRunMin` ConditionType stays tutorial-only). "After a few prestiges" emerges from wall sizes.
- **Fields:** Chemistry fields drop **existing Element items**; Biology fields drop a **new `OrganicCompound` item category**. Biology needs a new `ResearchBranch.Biology` enum value (Chemistry branch already exists).
- **Heavy-element re-tiering: design now, implement later.** The whole heavy/cosmic tail moves behind Biology conceptually, but the actual gate/cost moves ship in Phase 6, coordinated with the in-flight `elements-and-isotopes.md` redesign.

## Load-bearing constraints (mirror the Site design — do not revisit)

- Exactly ONE world's ONE site is live ECS. Every other site/world produces via the existing **idle-snapshot** mechanism (`OfflineCollectionService` / `GridSaveService.RebuildIdleSnapshot`). **No multi-world ECS.** If a requirement seems to need live inactive worlds, stop and ask.
- **Shared across all worlds:** prestige currency (✦), crystals (◆), managers, prestige shop, achievements, the single run-wide entropy wallet, net worth (active world live + inactive via snapshots).
- **Prestige** resets all worlds' grids + research (research already resets per run); **World *unlocks* survive prestige** exactly like Site unlocks (mirror `unlockedSites`). Research is re-climbed each run, cheaper via `memory_resonance` / per-node `prestigeMemoryDiscount`.

## Progression & walls (first-pass numbers — tune with the map before committing)

Costs anchored to the `economy-balance.md` phase table so they slot correctly. See `chemistry-biology.md` for full tier/field/recipe content.

| World | Opening wall (research → cost) | Tiers | Ends at |
|---|---|---|---|
| **W1 Physics** (existing) | start | P1 Subatomic · P2 Atomic · P3 Molecules | (heavy tail → Act IV) |
| **W2 Chemistry** | `chemistry_lab` ~150,000e, prereq `mid_elements` | C1 Inorganic · C2 Reactions/Catalysis · C3 Organic · C4 Biochemistry | **basic organic compounds** (feed W3) |
| **W3 Biology** | `biology_lab` ~10–25M e, prereq `biochem_precursors` | B1 Biomolecules · B2 Cellular · B3 Multicellular · B4 Ecosystems | organism / ecosystem (~1B e) |
| **Act IV** (deferred re-tier) | re-cost above Biology | Heavy elements → Transuranics → Materials/Components → Megastructure | Dyson Sphere |

**Known risk:** Biology gates (50M–1B) currently overlap Megastructure's 50M gate. Phase 6 must lift the heavy-physics endgame above Biology, and re-check the flat-log ✦ payout (~+50✦ per ×10 net worth, `economy-balance.md` §4) so deep runs aren't ✦-starved (consider raising `prestige_scale` or adding ✦ faucets).

---

## Phase 1 — World data model + save partition (no behavior change) — DONE (2026-07-06)

**Goal:** saves support N worlds grouping the existing sites; game still plays identically as World 0 = Physics.

**As-built note:** Worlds are a **logical grouping over the existing FLAT site list** (not a nested save restructure). `WorldSO.siteIds` names member sites; `grids[]`/`siteSnapshots[]` stay flat/global. The save change is just two additive fields: `SaveData.unlockedWorlds` (survives prestige, mirrors `unlockedSites`) and `CurrentRunData.activeWorldIndex` (default 0). `WorldSO`/`WorldDatabaseSO` mirror the Site pair; importer pass at Step 12.51 (after sites); `WorldDatabase.asset` in Resources; `worlds` section in `game_data.json` (`_meta.version` 0.3.9). Centralized world↔site mapping in `Services/WorldLayout.cs`. `world_chemistry` is a stub (150K/`mid_elements`, empty `site_ids`) until its site lands in Phase 2/3.

- New `ScriptableObjects/WorldSO.cs` + `WorldDatabaseSO.cs` (copy `SiteSO`/`SiteDatabaseSO` end-to-end): `id`, `displayName`, `unlockCost` (entropy), prerequisite unlock ids (research/item), member site ids, theme fields. New `"worlds"` array in `game_data.json`; bump `_meta.version`. Model classes in `Editor/GameDataModel.cs`; import pass in `Editor/GameDataImporter.cs` **after Sites** (cross-refs site ids).
- Extend `SaveSystem/SaveData.cs`: group sites/grids by world; `activeWorldIndex`; `unlockedWorlds` (survives prestige — mirror `unlockedSites`). **Additive migration shim:** existing saves become World 0 = Physics; keep reading legacy `grids[]`/`activeSiteIndex` as World 0. Centralize behind a helper like the existing `SaveData.ActiveGrid`.
- **Tests:** legacy single-world save loads and plays as World 0; round-trip; `SOSchemaTests` extension for `WorldSO`.
### GATE: full suite green → commit → PR. Stop here.

## Phase 2 — Field taxonomy + new resources — DONE (2026-07-06)

**Goal:** element fields + the `OrganicCompound` items exist and place on a map.

**As-built (Chemistry-focused per user direction — organic/Biology fields DEFERRED to the Biology build):**
- **DECIDED: field-type is a data-driven string** (not enum extension). The `FieldType` enum is deleted; `FieldSO.fieldType`/`ItemSO.fieldType`/`TutorialFlowSO.collectionFilter` are `string`, `BuildingSO.compatibleFields` is `string[]`. New `FieldTypes` helper (`Enums/GameEnums.cs`) holds the `"None"` sentinel + `IsUnrestricted`/`Normalize`. Touched: importer (stop parsing to enum), `ManualFieldCollector` (`FieldTypeToTriggerId` removed — toast now uses `field.id`, identical for tutorial fields), `TutorialOverlayController`, `BuildingPlacementController` (string equality), `GameDataEditorWindow` (dropdown is string). JSON was already string-valued, so no content churn beyond regeneration.
- `ItemCategory.OrganicCompound` + 4 forward-declared items (`glucose`/`fatty_acid`/`amino_acid`/`nucleotide`, item_id 150–153, tier_3). **Ran Generate Element Icons** for their tiles (else `ItemIconTests` fails).
- Element fields `element_field_light`/`metal`/`mineral` (type `"Element"`, drop existing elements) + site `site_chem_lab` (introduces them via density-override, `unlock_cost:0`; world-gated in Phase 3), wired to `world_chemistry.site_ids`.
- **Tests:** `Tests/ChemistryContentTests.cs` (OrganicCompound items, chem site introduces element fields with string type, world owns chem site) + updated `WorldSchemaTests` (sites partitioned across worlds), `ItemBalanceTests` (149→153), `SOSchemaTests` (FieldTypes/OrganicCompound). Full suite green (734).
- **Not done (deferred):** organic fields (`amino_acid_field` etc.) — Biology feedstock, land with Biology.
### GATE: full suite green → commit → PR. Stop here.

## Phase 3 — WorldService + switching + economic gate — DONE (2026-07-06)

**Goal:** `WorldService.SwitchTo(index)` and economic unlock work (dev-console first).

**As-built:**
- `Services/WorldService.cs` mirrors `SiteService`: static `IsUnlocked` (index 0 physics implicit; others in `unlockedWorlds`) + static `PrereqsMet` (all `prereqUnlockIds` in `save.unlockedResearch` — pure/testable); `CanUnlock` = !unlocked && prereqs && affordable. `UnlockWorld` deducts entropy, records `unlockedWorlds` (survives prestige), and **unlocks member sites** (`unlockedSites` + `EnsureSiteGrid`) so travel works. `SwitchTo` resolves the world's entry site and **delegates the grid handoff to `SiteService.SwitchTo`** (no duplicated flush/teardown logic).
- **Self-bootstrapped** (`[RuntimeInitializeOnLoadMethod]`, no scene placement — no serialized fields, can't be lost in a scene refactor; mirrors `FeatureFlagService`). ECS access is lazy (`EnsureEcs`) since it's created before GameScene's ECS world.
- `SiteService.SwitchTo` now keeps `activeWorldIndex` in sync via `WorldLayout.WorldIndexForSite` (SiteService is the single writer of `activeSiteIndex`), so world/site stay consistent whether switched via the world or site path. SiteService loads `WorldDatabase` for this.
- **Idle aggregation across worlds: already satisfied** — `OfflineCollectionService` iterates the flat `save.siteSnapshots` (all unlocked sites across all worlds); no change needed.
- Dev console `world list / switch / unlock` (shows prereq status), mirroring the `site` commands.
- **Tests:** `PlayModeTests/WorldServiceTests.cs` (pure gate: `IsUnlocked` + `PrereqsMet`). The ECS switch handoff is covered by `SiteServiceTests` (grid round-trip) + manual playtest.
### GATE: full suite green → commit → PR. Stop here.

## Phase 4 — Research branches + buildings + recipes

**Goal:** the chem/bio content is playable and correctly gated.

- Chemistry sub-branch nodes (`chemistry_lab`, `reaction_engineering`, `organic_chemistry`, `biochem_precursors`) extending the existing `ResearchBranch.Chemistry`; add `ResearchBranch.Biology` enum value + its nodes (`biology_lab`, `cell_biology`, `multicellular_life`, `ecosystems`). Wire each World's unlock to its opening research node.
- New buildings (Compound Synthesizer, Catalytic Reactor, Organic Synthesizer, Biosynthesizer, Cell Assembler, …) with `requiredResearch`, `compatibleFields` (new types), `supportedRecipes` whose `outputItem` matches the new fields' drops; new recipes for all C/B tier products. All data-driven in `game_data.json`.
- **Tests:** research gates unlock the right buildings/recipes; `SOSchemaTests` for new content.
### GATE: full suite green → commit → PR. Stop here.

## Phase 5 — World-select UI + map theming + discoverability

**Goal:** player-facing worlds panel + distinct-looking maps.

- `worlds-panel` in `Assets/UI/GameHUD.uxml` + `UI/WorldsSubController.cs` (copy `SitesSubController` / `prestige-panel`): one row per world (locked/unlocked, unlock cost + prereq status, "Travel", active badge); confirm dialog on unlock. Cancel placement overlays on switch (same path the deconstruct toggle uses).
- Per-world **map theming** (the one net-new subsystem): extend `WorldSO` with a palette/theme; make `Grid/GridRenderer.cs` accept a per-world palette (today all Inspector-fixed). MVP = tile/background palette + field tint per world.
- One-shot intro dialogue when each World first unlocks (copy the `intro_quantum_domains` dialogue pattern from `feature-multi-grids.md` Phase 5).
- **Tests:** `HUDControllerTests`-style wiring; manual editor play-mode pass. Flag component-wiring to user.
### GATE: full suite green → commit → PR. Done (v1).

## Phase 6 — (separate follow-up) Heavy-element re-tiering

Coordinate with `elements-and-isotopes.md`: move heavy-element / transuranic / materials / component gates + costs above Biology; re-cost Megastructure; re-check ✦ payout vs new sink depth. **Out of scope for phases 1–5.**

---

## Reuse map (copy, don't reinvent)

- **Multi-map backbone:** `Services/SiteService.cs`, `ScriptableObjects/SiteSO.cs` + `SiteDatabaseSO.cs`, `Gameplay/FieldGenerator.cs` (`GenerateForSite`, `IntroducedFields`).
- **Economic unlock:** `SiteService.UnlockSite` / `CanUnlock` / `DeductEntropy` via `PlayerProgressData` (also `ResearchService.DeductEntropy`, `ResearchService.cs:168`).
- **Save partition + migration shim:** `SaveData.ActiveGrid` accessor; `GridSaveService.EnsureSiteGrid`.
- **New content type pipeline:** `data-pipeline.md` recipe (SO → `GameDataModel.cs` field → JSON section → import pass → Resources DB → EditMode test).
- **Research/field/building gating:** `ResearchService.IsUnlocked`, `BuildingSO.requiredResearch`, `RecipeSO.requiredResearch`, `BuildingPlacementController.IsCellValidForPending` (`compatibleFields` + `supportedRecipes` matching field drops).
- **New-panel UI:** `SitesSubController` + `sites-panel`, prestige-panel confirm pattern.

## Definition of done (every phase)

- [ ] JSON parses; importer run; full suite green BOTH platforms (`scripts/test-local.ps1`), results XML reported
- [ ] Legacy save (single-world) loads and plays as World 0
- [ ] Phase-specific manual check in editor play-mode (component-wiring flagged to user for Phase 5)
- [ ] Docs updated + `Verified against` stamps refreshed (`architecture.md`, `economy-balance.md`, `chemistry-biology.md`, this task's Status line)
- [ ] No generated assets hand-edited; no edits beyond the phase's scope

## Out of scope (phases 1–5)

Heavy-element re-tiering (Phase 6); inter-world logistics/transfers; per-world prestige; world-specific managers; live simulation of inactive worlds; net-worth aggregation redesign; more than the fields/recipes listed in `chemistry-biology.md`.
