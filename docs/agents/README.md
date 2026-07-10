# Agent Context Pack

Layered, token-efficient context for AI coding sessions on this repo. Read this file + `architecture.md` first (always), then ONLY the deep-dive docs the routing table names for your task. Do not re-explore the codebase for facts these docs already state. CLAUDE.md (auto-loaded) holds the workflow rules and recurring pitfalls — it is not duplicated here.

> Verified against: `3c264f2`, 2026-07-06.

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
| balance numbers, currencies, costs, formulas | `economy-balance.md` |
| remote toggles, feature gating, kill-switches, env-specific behavior | `feature-flags.md` |
| telemetry, analytics events, player data collection, balancing dashboards | `analytics.md` |
| crash/exception reporting, Cloud Diagnostics, debug data to Unity Cloud, symbol upload | `../cloud-diagnostics-setup.md` |
| building/structure visuals, procedural meshes, shaders, art direction | `visual-design.md` |
| rewarded ads, ad SDK, LevelPlay go-live | `../levelplay-ads-setup.md` |
| any code change (before committing) | `testing.md` |

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
| `tasks/feature-worlds-chem-bio.md` — Chemistry & Biology tracks (worlds) | Phases 1–4 + full Biology B1–B4 done (PRs #119–125+); next P5 worlds UI, P6 re-tier | XL (5 phases + deferred re-tier) |

Session prompt format: *"Read docs/agents/README.md, then implement docs/agents/tasks/feature-X.md."*

## Maintenance

- Any PR that changes a system covered by a doc here MUST update that doc and refresh its `Verified against` stamp in the same PR.
- If code contradicts a doc, trust the code and fix the doc.
- Line citations are paired with symbol names — if a line number drifted, grep the symbol.
- Task files: update the Status line (pending / in-progress / done — PR #) when work starts/lands.
