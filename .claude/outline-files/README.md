# Mobile Crafting Game — Project Plan

## Vision
A mobile idle/crafting game inspired by **Shapez 2**, **Idle Planet Miner**, and **Dyson Sphere Program**. Players start from the absolute bottom — subatomic particles — and work their way up through atoms, molecules, alloys, and beyond. The game is educational by design, with crafting steps reflecting real-world science as closely as possible.

Players begin crafting manually, then unlock **buildings** that automate production, gradually scaling into complex automated pipelines.

## Tech Stack
- **Engine:** Unity (DOTS — Data-Oriented Technology Stack)
- **Platform:** Mobile (iOS & Android)

---

## Chapter Files

| # | File | Topic |
|---|------|-------|
| 01 | [01-game-design.md](01-game-design.md) | Core gameplay loop, progression, tutorial |
| 02 | [02-crafting-system.md](02-crafting-system.md) | Crafting hierarchy & real-world science mapping |
| 03 | [03-buildings-automation.md](03-buildings-automation.md) | Buildings, automation, and unlocks |
| 04 | [04-unity-dots-architecture.md](04-unity-dots-architecture.md) | Unity DOTS setup & architecture |
| 05 | [05-test-environment.md](05-test-environment.md) | Test environment & QA strategy |
| 06 | [06-ci-cd.md](06-ci-cd.md) | CI/CD pipeline |
| 07 | [07-reusable-components.md](07-reusable-components.md) | Reusable UI & gameplay components |
| 08 | [08-online-storage.md](08-online-storage.md) | Cloud save & online storage |
| 09 | [09-achievements.md](09-achievements.md) | Achievement system |
| 10 | [10-pvp.md](10-pvp.md) | PVP / competitive features |
| 11 | [11-graphics-visuals.md](11-graphics-visuals.md) | Graphics & visual style |
| 12 | [12-music-sound.md](12-music-sound.md) | Music, sound design & audio |
| 13 | [13-beta-testing.md](13-beta-testing.md) | Beta testing plan |
| 14 | [14-deployment-monetization.md](14-deployment-monetization.md) | Full deployment & monetization strategy |
| 15 | [15-research-system.md](15-research-system.md) | Research system & content gating |
| 16 | [16-narrative-dialogue.md](16-narrative-dialogue.md) | Narrative layer & in-game dialogue |

---

## Project Status
- [x] Plan phase started
- [x] Architecture defined (Chapter 04 — DOTS setup, asmdef, core ECS components, Authoring, Systems skeleton)
- [x] Crafting data foundation (Chapter 02 — ScriptableObjects, RecipeDatabase, recipes.json Tier 0–2)
- [x] Buildings & Automation core (Chapter 03 — GridPosition, InventorySlot, RecipeInputSlot/RecipeOutputSlot, PlayerInventoryAuthoring, BuildingAuthoring recipe baking, complete ProductionSystem)
- [x] Test infrastructure (Chapter 05 — Tests asmdef, RecipeValidationTests for Tier 0–2 science)
- [ ] Prototype
- [ ] Alpha
- [ ] Beta
- [ ] Launch
