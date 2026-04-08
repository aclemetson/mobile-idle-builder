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
  - [x] Added `GameConfigSO`, `TierSO`, `FieldSO`, `ResearchSO` — per ScriptableObject-Schemas.md
- [x] Crafting data foundation (Chapter 02 — ScriptableObjects, RecipeDatabase, recipes.json Tier 0–2)
  - [x] `ItemSO`: full schema fields (symbol, charge, category, scientific data, harvesting, economy)
  - [x] `RecipeSO`: full schema fields (byproducts, power cost, validBuildings, unlock, simplification)
- [x] Buildings & Automation core (Chapter 03 — GridPosition, InventorySlot, RecipeInputSlot/RecipeOutputSlot, PlayerInventoryAuthoring, BuildingAuthoring recipe baking, complete ProductionSystem)
  - [x] `BuildingSO`: full schema fields (power source, upgrade levels, placement rule, decay collection)
  - [x] Added `PersistentUpgradeSO`, `AchievementSO`, `CosmeticSO`
- [x] Test infrastructure (Chapter 05 — Tests asmdef, RecipeValidationTests for Tier 0–2 science)
- [ ] Prototype (in progress)
  - [x] `GameBootstrap` — startup order, `GameConfigSO` holder
  - [x] `ItemDatabase` — string id → int itemId runtime lookup
  - [x] `ManualCraftService` — managed ECS bridge (CanCraft / TryCraft / GetInventoryCounts)
  - [x] `BuildingPlacer` — runtime DOTS entity creation for placing buildings
  - [x] `GridRenderer` — procedural placeholder tile grid
  - [x] `HUDController` — UIDocument driver (inventory bar, recipe panel, button wiring)
  - [x] `GameHUD.uxml` / `GameHUD.uss` — sci-fi HUD layout with design tokens
  - [ ] Scene wiring (set up GameObjects + SubScene in Unity Editor — see below)
- [ ] Alpha
- [ ] Beta
- [ ] Launch
