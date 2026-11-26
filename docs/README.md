# Documentation

Start with the project [README](../README.md). This page indexes everything under `docs/`.

## Architecture and design

| Doc | Contents |
|---|---|
| [agents/architecture.md](agents/architecture.md) | Scenes, the three layers (ECS, services, UI), data flow, init-order rules, key singletons |
| [adr/](adr/README.md) | Architecture decision records: DOTS, UGS, the JSON pipeline, the derived economy, the single scene |
| [agents/ecs-patterns.md](agents/ecs-patterns.md) | How systems, components, authoring and the Mono/ECS bridge fit together |
| [agents/save-system.md](agents/save-system.md) | `SaveData`, ECS sync, cloud reconciliation, offline earnings |
| [agents/data-pipeline.md](agents/data-pipeline.md) | `game_data.json` to ScriptableObjects, with the conventions the importer enforces |
| [agents/ui-toolkit.md](agents/ui-toolkit.md) | GameHUD, sub-controllers, panels |
| [agents/feature-flags.md](agents/feature-flags.md) | Remote Config flags, kill-switches, maintenance mode, update prompt |
| [agents/analytics.md](agents/analytics.md) | Telemetry events and snapshots |
| [agents/visual-design.md](agents/visual-design.md) | Art direction for buildings and procedural meshes |
| [agents/elements-and-isotopes.md](agents/elements-and-isotopes.md), [agents/chemistry-biology.md](agents/chemistry-biology.md) | The science content model |
| [design/ipm-research.md](design/ipm-research.md) | Genre research: balance reference from a comparable idle game |

The `agents/` folder is also the context pack for AI coding sessions; [agents/README.md](agents/README.md) explains how it's used.

## Economy and balance

| Doc | Contents |
|---|---|
| [agents/economy-balance.md](agents/economy-balance.md) | Currencies, formulas, progression gates, the derived economy |
| [progression-curve.html](progression-curve.html) | Generated progression curve: target hours per research block |
| [balance/prestige-pacing.html](balance/prestige-pacing.html) | Generated prestige pacing plan, active and idle |
| [research-tree.html](research-tree.html) | Interactive research-tree editor |
| [progression-map.html](progression-map.html), [progression-deck.html](progression-deck.html) | Whole-game progression map and overview deck |
| [gameplay-loop.html](gameplay-loop.html), [balance-editor.html](balance-editor.html), [crystal-economy.html](crystal-economy.html) | Earlier design references (may lag the code; `game_data.json` is authoritative) |

The HTML pages are served by GitHub Pages. The game is Quantum Foundry. The repository and the app package keep the original `mobile-idle-builder` name.

## Testing

| Doc | Contents |
|---|---|
| [agents/testing.md](agents/testing.md) | Test layout, how to run the suites, what each change needs |
| [../Playtests/README.md](../Playtests/README.md) | AI playtest harness: briefs, fixtures, MCP tools |
| [testing/beta-plan.md](testing/beta-plan.md) | Beta phases, goals and exit criteria, metrics, tools, recruiting post and surveys |
| [testing/dev-testing-guide.md](testing/dev-testing-guide.md) | Dev console and test-mode checklist |
| [testing/device-checklist-0.4.html](testing/device-checklist-0.4.html) | On-device test checklist (0.4) |

## Runbooks

| Doc | Contents |
|---|---|
| [runbooks/ci-setup.md](runbooks/ci-setup.md) | The CI/CD pipeline (paused): runner, signing, Play internal track, TestFlight, re-enabling |
| [runbooks/google-play-iap-testing.md](runbooks/google-play-iap-testing.md) | Testing in-app purchases on Google Play |
| [runbooks/app-store-iap-testing.md](runbooks/app-store-iap-testing.md) | Setting up and testing in-app purchases on the App Store |
| [runbooks/levelplay-ads-setup.md](runbooks/levelplay-ads-setup.md) | Rewarded ads go-live |
| [runbooks/cloud-diagnostics-setup.md](runbooks/cloud-diagnostics-setup.md) | Crash and exception reporting |
| [runbooks/store-privacy-declarations.md](runbooks/store-privacy-declarations.md) | Play Data safety and App Store privacy answers |
| [store/store-listing.md](store/store-listing.md) | Store listing text, tags, keywords and graphics |
| [community/discord-server.md](community/discord-server.md) | Discord server: roles, channels, permissions, bots, incentives, rules, set-up checklist |
| [analytics/README.md](analytics/README.md) | Telemetry dashboard (CSV export) |

## Legal

- [privacy-policy.html](privacy-policy.html): the privacy policy linked from the store listings
