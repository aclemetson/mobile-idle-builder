# Agent Context Pack

Layered, token-efficient context for AI coding sessions on this repo. Read this file + `architecture.md` first (always), then ONLY the deep-dive docs the routing table names for your task. Do not re-explore the codebase for facts these docs already state. CLAUDE.md (auto-loaded) holds the workflow rules and recurring pitfalls — it is not duplicated here.

> Verified against: `deea0a4`, 2026-06-11.

## Routing table

| Your task involves… | Read |
|---|---|
| anything (always) | `architecture.md` |
| items / recipes / buildings / research / new content type / game_data.json | `data-pipeline.md` |
| save fields, persistence, cloud, offline earnings, timed effects | `save-system.md` |
| HUD, panels, UXML/USS, sub-controllers | `ui-toolkit.md` |
| ECS systems, components, authoring, Mono↔ECS, multipliers/bonuses | `ecs-patterns.md` |
| balance numbers, currencies, costs, formulas | `economy-balance.md` |
| any code change (before committing) | `testing.md` |

## Feature assignments (`tasks/`)

Each task file opens with its own Required-reading line and a Status line — update Status when work lands.

| Task | Status | Size |
|---|---|---|
| `tasks/feature-daily-events.md` — login rewards + daily challenges | done (PRs #54-56) | S |
| `tasks/feature-managers.md` — hireable building managers | done (PR #63) | M |
| `tasks/feature-manager-upgrades.md` — manager star tiers (✦) | done (PR #65) | M |
| `tasks/feature-megastructure.md` — Dyson Sphere endgame project | pending | M |
| `tasks/feature-multi-grids.md` — multiple build sites | done (PRs #58-61) | XL (4 phased PRs) |

Session prompt format: *"Read docs/agents/README.md, then implement docs/agents/tasks/feature-X.md."*

## Maintenance

- Any PR that changes a system covered by a doc here MUST update that doc and refresh its `Verified against` stamp in the same PR.
- If code contradicts a doc, trust the code and fix the doc.
- Line citations are paired with symbol names — if a line number drifted, grep the symbol.
- Task files: update the Status line (pending / in-progress / done — PR #) when work starts/lands.
