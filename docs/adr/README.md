# Architecture Decision Records

Short records of the decisions that shape this codebase: the context at the time, what was chosen, and what it costs. They were written down retroactively in November 2026 from the project history, and new decisions get a new numbered file.

| # | Decision | Status |
|---|---|---|
| [0001](0001-dots-ecs-simulation.md) | Run the factory simulation in DOTS/ECS, everything else in MonoBehaviours | Accepted |
| [0002](0002-unity-gaming-services-backend.md) | Use Unity Gaming Services instead of a custom AWS backend | Accepted (supersedes the AWS/Terraform backend) |
| [0003](0003-json-single-source-of-truth.md) | One `game_data.json` as the source of truth for all content | Accepted |
| [0004](0004-derived-economy.md) | Derive item values and research costs from a progression curve | Accepted |
| [0005](0005-single-gameplay-scene.md) | A single gameplay scene, with every menu as a HUD panel | Accepted |

Format: Context, Decision, Consequences. Keep each one under a page.
