# Agent Context Pack

Layered, token-efficient context for AI coding sessions on this repo. Read this file + `architecture.md` first (always), then ONLY the deep-dive docs the routing table names for your task. Do not re-explore the codebase for facts these docs already state. CLAUDE.md (auto-loaded) holds the workflow rules and recurring pitfalls — it is not duplicated here.

> Verified against: `6c38e7e`, 2026-09-13.

## Routing table

| Your task involves… | Read |
|---|---|
| anything (always) | `architecture.md` |
| items / recipes / buildings / research / new content type / game_data.json | `data-pipeline.md` |
| elements / isotopes / particles / fusion / fission / fissile fields / periodic table | `elements-and-isotopes.md` |
| worlds / tracks / chemistry / biology / organic compounds / a second or third map | `chemistry-biology.md` |
| save fields, persistence, cloud, offline earnings, timed effects | `save-system.md` |
| HUD, panels, UXML/USS, sub-controllers | `ui-toolkit.md` |
| ECS systems, components, authoring, Mono↔ECS, multipliers/bonuses | `ecs-patterns.md` |
| balance numbers, currencies, costs, formulas | `economy-balance.md` (values and research costs are generated: "Derived economy") |
| remote toggles, feature gating, kill-switches, env-specific behavior | `feature-flags.md` |
| telemetry, analytics events, player data collection, balancing dashboards | `analytics.md` |
| crash/exception reporting, Cloud Diagnostics, debug data to Unity Cloud, symbol upload | `../cloud-diagnostics-setup.md` |
| building/structure visuals, procedural meshes, shaders, art direction | `visual-design.md` |
| rewarded ads, ad SDK, LevelPlay go-live | `../levelplay-ads-setup.md` |
| any code change (before committing) | `testing.md` |
| AI playtests (agent plays in the Editor via MCP), playtest briefs, fixtures, `playtest_*` tools | `../../Playtests/README.md` |

## Feature assignments (`tasks/`)

Each task file opens with its own Required-reading line and a Status line — update Status when work lands.

| Task | Status | Size |
|---|---|---|
| `tasks/feature-daily-events.md` — login rewards + daily challenges | done (PRs #54-56) | S |
| `tasks/feature-managers.md` — hireable building managers | done (PR #63) | M |
| `tasks/feature-manager-upgrades.md` — manager star tiers (✦) | done (PR #65) | M |
| `tasks/feature-megastructure.md` — Dyson Sphere endgame project | done (branch `feature/megastructure`) | M |
| `tasks/feature-power-draw.md` — proximity eV power grid (makes PowerDiscount live) | done (feature/power-draw) | M–L |
| `tasks/feature-multi-grids.md` — multiple build sites | done (PRs #58-61) | XL (4 phased PRs) |
| `tasks/feature-worlds-chem-bio.md` — Chemistry & Biology tracks (worlds) | Phases 1–5 done (PRs #119–128); only P6 economy re-tier remains | XL (5 phases + deferred re-tier) |
| `tasks/fix-341-recipe-switch-outputs.md` — recipe switch moves old-recipe outputs to inventory | done (PR #346) | S |
| `tasks/feature-339-research-effects.md` - research effects (Transportation I = +10% belt speed) | done (PR #347) | M |
| `tasks/feature-302-placement-power-preview.md` - power range while placing a powered building | done (PR #348) | M |
| `tasks/fix-303-tutorial-polish.md` - tutorial polish from the 2026-10-31 playtests | done (PR #349) | S |
| `tasks/feature-336a-recipe-names.md` - recipes named after their product, picker method line | done (PR #350) | M |
| `tasks/feature-336b-recipes-tab-grouping.md` - Recipes tab on game_data.json, grouped by item (#287 step 2) | done (PR #351) | M-L |
| `tasks/feature-301-belt-drawing-ux.md` - belt drawing feedback (301a) and router cost (301b) | done (PRs #352, #353) | L |

Session prompt format: *"Read docs/agents/README.md, then implement docs/agents/tasks/feature-X.md."*

## Maintenance

- Any PR that changes a system covered by a doc here MUST update that doc and refresh its `Verified against` stamp in the same PR.
- If code contradicts a doc, trust the code and fix the doc.
- Line citations are paired with symbol names — if a line number drifted, grep the symbol.
- Task files: update the Status line (pending / in-progress / done — PR #) when work starts/lands.
