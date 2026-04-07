# Mobile Crafting Game — Project Plan

## Vision
A mobile idle/crafting game inspired by **Shapez 2**, **Idle Planet Miner**, and **Dyson Sphere Program**. Players start from the absolute bottom — subatomic particles — and work their way up through atoms, molecules, alloys, and beyond. The game is educational by design, with crafting steps reflecting real-world science as closely as possible.

Players begin crafting manually, then unlock **buildings** that automate production, gradually scaling into complex automated pipelines.

## Tech Stack
- **Engine:** Unity (DOTS — Data-Oriented Technology Stack)
- **Platform:** Mobile (iOS & Android)

---

## Design Docs
Full chapter files are in [`.claude/outline-files/`](.claude/outline-files/README.md).

| # | Topic |
|---|-------|
| 01 | Core gameplay loop, progression, tutorial |
| 02 | Crafting hierarchy & real-world science mapping |
| 03 | Buildings, automation, and unlocks |
| 04 | Unity DOTS setup & architecture |
| 05 | Test environment & QA strategy |
| 06–16 | CI/CD, UI components, cloud save, achievements, PVP, graphics, audio, beta, monetization, research, narrative |
| SO | [ScriptableObject field schemas](.claude/outline-files/ScriptableObject-Schemas.md) |

---

## Project Status

- [x] Plan phase started
- [x] Architecture defined (Chapter 04 — DOTS setup, asmdef, core ECS components, Authoring, Systems skeleton)
  - [ ] (pending) Add `GameConfigSO` (craft time multiplier, power economy, prestige, environment config) — per ScriptableObject-Schemas.md
  - [ ] (pending) Add `TierSO`, `FieldSO` SO classes — per ScriptableObject-Schemas.md
  - [ ] (pending) Add `ResearchSO` (research tree nodes, prerequisites, unlock cascades) — per ScriptableObject-Schemas.md
- [x] Crafting data foundation (Chapter 02 — ScriptableObjects, RecipeDatabase, recipes.json Tier 0–2)
  - [ ] (pending) `ItemSO`: add `symbol`, `charge`, `category`, scientific data fields, `isRadioactive`, `decayType`, `isHarvested`, `fieldType`, `isSecondaryParticle`, `baseSellValue` — per ScriptableObject-Schemas.md
  - [ ] (pending) `RecipeSO`: add `byproducts`, `powerCostIsDynamic`, `fixedPowerCostEV`, `validBuildings`, `knownFromStart`, `requiredResearch`, `unlocksResearch`, `simplificationNote` — per ScriptableObject-Schemas.md
- [x] Buildings & Automation core (Chapter 03 — GridPosition, InventorySlot, RecipeInputSlot/RecipeOutputSlot, PlayerInventoryAuthoring, BuildingAuthoring recipe baking, complete ProductionSystem)
  - [ ] (pending) `BuildingSO`: add `isPowerSource`, `baseOutputEV`, `influenceRadiusTiles`, `linkRadiusTiles`, `placementRule`, `upgradeLevels`, `hasSpecialUpgrade`, `collectsDecayParticles` — per ScriptableObject-Schemas.md
  - [ ] (pending) Add `PersistentUpgradeSO`, `AchievementSO`, `CosmeticSO` — per ScriptableObject-Schemas.md
- [x] Test infrastructure (Chapter 05 — Tests asmdef, RecipeValidationTests for Tier 0–2 science)
- [ ] Prototype
- [ ] Alpha
- [ ] Beta
- [ ] Launch
