using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for TutorialSystem.
    ///
    /// Each test builds a minimal TutorialFlowSO programmatically and injects it via
    /// TutorialFlowSO.SetCurrentForTesting so TutorialSystem can read step definitions
    /// without requiring the full editor import pipeline.
    ///
    /// Item IDs match recipes.json:
    ///   1 = Up Quark | 2 = Down Quark | 3 = Electron
    ///   4 = Proton   | 5 = Neutron    | 6 = Hydrogen
    /// </summary>
    [TestFixture]
    public class TutorialSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _playerEntity;

        [SetUp]
        public void Setup()
        {
            _world = new World("TutorialTest");
            _em = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<TutorialSystem>());
            simGroup.SortSystems();

            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddComponent<TutorialStateData>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
        }

        [TearDown]
        public void Teardown()
        {
            TutorialFlowSO.ClearCurrentForTesting();
            if (_world.IsCreated) _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal TutorialFlowSO with the given steps and injects it as current.
        /// </summary>
        private static TutorialFlowSO MakeFlow(params TutorialStepDef[] steps)
        {
            var so = ScriptableObject.CreateInstance<TutorialFlowSO>();
            so.steps = steps;
            TutorialFlowSO.SetCurrentForTesting(so);
            return so;
        }

        private static TutorialStepDef InventoryMinStep(string id, int itemId, int qty,
            bool anyOf = false, bool isSubstep = false)
            => new TutorialStepDef
            {
                id = id,
                isSubstep = isSubstep,
                advanceCondition = new TutorialConditionDef
                {
                    type  = ConditionType.InventoryMin,
                    anyOf = anyOf,
                    items = new List<ItemCountReq> { new ItemCountReq { itemId = itemId, quantity = qty } }
                },
                onEnter = new TutorialOnEnter()
            };

        private static TutorialStepDef InventoryMinAnyStep(string id,
            (int itemId, int qty)[] requirements)
        {
            var items = new List<ItemCountReq>();
            foreach (var r in requirements)
                items.Add(new ItemCountReq { itemId = r.itemId, quantity = r.qty });
            return new TutorialStepDef
            {
                id = id,
                advanceCondition = new TutorialConditionDef
                    { type = ConditionType.InventoryMin, anyOf = true, items = items },
                onEnter = new TutorialOnEnter()
            };
        }

        private static TutorialStepDef UiEventStep(string id, string eventId)
            => new TutorialStepDef
            {
                id = id,
                advanceCondition = new TutorialConditionDef
                    { type = ConditionType.UiEvent, uiEventId = eventId },
                onEnter = new TutorialOnEnter()
            };

        private static TutorialStepDef BuildingMinStep(string id, int minCount)
            => new TutorialStepDef
            {
                id = id,
                advanceCondition = new TutorialConditionDef
                    { type = ConditionType.BuildingMin, minCount = minCount },
                onEnter = new TutorialOnEnter()
            };

        private static TutorialStepDef BuildingMinTypeStep(string id, int minCount, int buildingType)
            => new TutorialStepDef
            {
                id = id,
                advanceCondition = new TutorialConditionDef
                    { type = ConditionType.BuildingMin, minCount = minCount, buildingType = buildingType },
                onEnter = new TutorialOnEnter()
            };

        private Entity CreateBuilding(int buildingType)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { BuildingType = buildingType });
            return e;
        }

        private static TutorialStepDef PrestigeAvailableStep(string id)
            => new TutorialStepDef
            {
                id = id,
                advanceCondition = new TutorialConditionDef { type = ConditionType.PrestigeAvailable },
                onEnter = new TutorialOnEnter()
            };

        private void SetStepIndex(int index)
        {
            _em.SetComponentData(_playerEntity, new TutorialStateData
            {
                CurrentStepIndex = index,
                IsActive = true
            });
        }

        private int GetStepIndex() =>
            _em.GetComponentData<TutorialStateData>(_playerEntity).CurrentStepIndex;

        private bool GetIsActive() =>
            _em.GetComponentData<TutorialStateData>(_playerEntity).IsActive;

        private void AddToInventory(int itemId, int qty)
        {
            var inv = _em.GetBuffer<InventorySlot>(_playerEntity);
            inv.Add(new InventorySlot { ItemID = itemId, Quantity = qty });
        }

        // ── Inactive guard ────────────────────────────────────────────────────

        [Test]
        public void WhenInactive_StepDoesNotAdvance()
        {
            MakeFlow(InventoryMinStep("craft_quarks", itemId: 1, qty: 1));
            _em.SetComponentData(_playerEntity, new TutorialStateData
            {
                CurrentStepIndex = 0,
                IsActive = false
            });
            AddToInventory(itemId: 1, qty: 5);

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "Inactive tutorial must not advance steps");
        }

        // ── InventoryMin (all items required) ────────────────────────────────

        [Test]
        public void InventoryMin_NotMet_DoesNotAdvance()
        {
            MakeFlow(
                InventoryMinStep("step_a", itemId: 1, qty: 5),
                InventoryMinStep("step_b", itemId: 2, qty: 1)
            );
            SetStepIndex(0);
            // Inventory empty

            _world.Update();

            Assert.AreEqual(0, GetStepIndex());
        }

        [Test]
        public void InventoryMin_Met_Advances()
        {
            MakeFlow(
                InventoryMinStep("step_a", itemId: 1, qty: 5),
                InventoryMinStep("step_b", itemId: 2, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 1, qty: 5);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        // ── InventoryMin anyOf ────────────────────────────────────────────────

        [Test]
        public void InventoryMin_AnyOf_NeitherMet_DoesNotAdvance()
        {
            MakeFlow(
                InventoryMinAnyStep("craft_quarks",
                    new[] { (itemId: 1, qty: 1), (itemId: 2, qty: 1) }),
                InventoryMinStep("next", itemId: 4, qty: 1)
            );
            SetStepIndex(0);
            // Inventory empty

            _world.Update();

            Assert.AreEqual(0, GetStepIndex());
        }

        [Test]
        public void InventoryMin_AnyOf_FirstMet_Advances()
        {
            MakeFlow(
                InventoryMinAnyStep("craft_quarks",
                    new[] { (itemId: 1, qty: 1), (itemId: 2, qty: 1) }),
                InventoryMinStep("next", itemId: 4, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 1, qty: 3); // up quark

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void InventoryMin_AnyOf_SecondMet_Advances()
        {
            MakeFlow(
                InventoryMinAnyStep("craft_quarks",
                    new[] { (itemId: 1, qty: 1), (itemId: 2, qty: 1) }),
                InventoryMinStep("next", itemId: 4, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 2, qty: 2); // down quark

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        // ── InventoryMin proton / neutron / hydrogen chain ────────────────────

        [Test]
        public void InventoryMin_Proton_NotMet_DoesNotAdvance()
        {
            MakeFlow(
                InventoryMinStep("craft_proton", itemId: 4, qty: 1),
                InventoryMinStep("craft_neutron", itemId: 5, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 1, qty: 5); // quarks, not proton

            _world.Update();

            Assert.AreEqual(0, GetStepIndex());
        }

        [Test]
        public void InventoryMin_Proton_Met_Advances()
        {
            MakeFlow(
                InventoryMinStep("craft_proton",  itemId: 4, qty: 1),
                InventoryMinStep("craft_neutron", itemId: 5, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 4, qty: 1);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void InventoryMin_Neutron_Met_Advances()
        {
            MakeFlow(
                InventoryMinStep("craft_neutron",  itemId: 5, qty: 1),
                InventoryMinStep("craft_hydrogen", itemId: 6, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 5, qty: 1);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void InventoryMin_Hydrogen_Met_Advances()
        {
            MakeFlow(
                InventoryMinStep("craft_hydrogen",    itemId: 6, qty: 1),
                BuildingMinStep("place_building", minCount: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 1);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        // ── UiEvent — never advances from ECS side ───────────────────────────

        [Test]
        public void UiEvent_NeverAdvancesFromECS()
        {
            MakeFlow(
                UiEventStep("wait_for_open", "demon_opened"),
                InventoryMinStep("next", itemId: 1, qty: 1)
            );
            SetStepIndex(0);

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "UiEvent steps must not advance from the ECS system");
        }

        // ── BuildingMin ───────────────────────────────────────────────────────

        [Test]
        public void BuildingMin_NoBuildings_DoesNotAdvance()
        {
            MakeFlow(
                BuildingMinStep("place_building", minCount: 1),
                PrestigeAvailableStep("automation")
            );
            SetStepIndex(0);

            _world.Update();

            Assert.AreEqual(0, GetStepIndex());
        }

        [Test]
        public void BuildingMin_BuildingExists_Advances()
        {
            MakeFlow(
                BuildingMinStep("place_building", minCount: 1),
                PrestigeAvailableStep("automation")
            );
            SetStepIndex(0);

            var building = _em.CreateEntity();
            _em.AddComponent<BuildingData>(building);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void BuildingMin_TypeFilter_WrongTypeDoesNotAdvance()
        {
            // The step needs a specific building type (e.g. Strong Force Combiner). A building
            // of a different type must NOT satisfy it — this is what stops place_sfc and friends
            // from auto-advancing on any pre-existing building.
            MakeFlow(
                BuildingMinTypeStep("place_sfc", minCount: 1, buildingType: 5),
                PrestigeAvailableStep("next")
            );
            SetStepIndex(0);
            CreateBuilding(buildingType: 3); // different type

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "A different building type must not satisfy a type-filtered BuildingMin");
        }

        [Test]
        public void BuildingMin_TypeFilter_MatchingTypeAdvances()
        {
            MakeFlow(
                BuildingMinTypeStep("place_sfc", minCount: 1, buildingType: 5),
                PrestigeAvailableStep("next")
            );
            SetStepIndex(0);
            CreateBuilding(buildingType: 3); // decoy of a different type
            CreateBuilding(buildingType: 5); // the required type

            _world.Update();

            Assert.AreEqual(1, GetStepIndex(), "A matching building type must satisfy a type-filtered BuildingMin");
        }

        // ── PrestigeAvailable ─────────────────────────────────────────────────

        [Test]
        public void PrestigeAvailable_NotSet_DoesNotAdvance()
        {
            MakeFlow(
                PrestigeAvailableStep("automation"),
                UiEventStep("done", "prestige_spent")
            );
            SetStepIndex(0);

            var pe = _em.CreateEntity();
            _em.AddComponentData(pe, new PlayerProgressData { PrestigeAvailable = false });

            _world.Update();

            Assert.AreEqual(0, GetStepIndex());
        }

        [Test]
        public void PrestigeAvailable_Set_Advances()
        {
            MakeFlow(
                PrestigeAvailableStep("automation"),
                UiEventStep("done", "prestige_spent")
            );
            SetStepIndex(0);

            var pe = _em.CreateEntity();
            _em.AddComponentData(pe, new PlayerProgressData { PrestigeAvailable = true });

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        // ── Nucleon arc (new tutorial steps) ─────────────────────────────────

        [Test]
        public void CollectQuarksForNucleons_AdvancesWhen8UpAnd8DownPresent()
        {
            MakeFlow(
                new TutorialStepDef
                {
                    id = "collect_quarks_for_nucleons",
                    advanceCondition = new TutorialConditionDef
                    {
                        type  = ConditionType.InventoryMin,
                        anyOf = false,
                        items = new List<ItemCountReq>
                        {
                            new ItemCountReq { itemId = 1, quantity = 8 },
                            new ItemCountReq { itemId = 2, quantity = 8 }
                        }
                    },
                    onEnter = new TutorialOnEnter()
                },
                InventoryMinStep("next", itemId: 1, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 1, qty: 8);
            AddToInventory(itemId: 2, qty: 8);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void CraftTwoProtons_AdvancesWhenTwoProtonsInInventory()
        {
            MakeFlow(
                InventoryMinStep("craft_two_protons",  itemId: 4, qty: 2),
                InventoryMinStep("craft_two_neutrons", itemId: 5, qty: 2)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 4, qty: 2);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void CraftTwoNeutrons_AdvancesWhenTwoNeutronsInInventory()
        {
            MakeFlow(
                InventoryMinStep("craft_two_neutrons", itemId: 5, qty: 2),
                InventoryMinStep("next",               itemId: 1, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 5, qty: 2);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        [Test]
        public void SellNucleons_DoesNotAdvanceUntilBothProtonsAndNeutronsGone()
        {
            MakeFlow(
                new TutorialStepDef
                {
                    id = "sell_protons_and_neutrons",
                    advanceCondition = new TutorialConditionDef
                    {
                        type  = ConditionType.InventoryZero,
                        items = new List<ItemCountReq>
                        {
                            new ItemCountReq { itemId = 4 },
                            new ItemCountReq { itemId = 5 }
                        }
                    },
                    onEnter = new TutorialOnEnter()
                },
                InventoryMinStep("next", itemId: 1, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 4, qty: 1); // proton still present

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "Must not advance while protons remain in inventory");
        }

        [Test]
        public void BuyHydrogenSynthesis_DoesNotAdvanceWhenResearchUnavailable()
        {
            // ResearchUnlocked reads the live unlock state (ResearchService, falling back to
            // SaveManager). Neither singleton exists in this bare ECS world, so the research
            // counts as locked and the step must hold. See ResearchServiceTests for the
            // positive case where a purchase advances a research-gated step.
            MakeFlow(
                new TutorialStepDef
                {
                    id = "buy_hydrogen_synthesis",
                    advanceCondition = new TutorialConditionDef
                    {
                        type       = ConditionType.ResearchUnlocked,
                        researchId = "hydrogen_synthesis"
                    },
                    onEnter = new TutorialOnEnter()
                },
                InventoryMinStep("next", itemId: 1, qty: 1)
            );
            SetStepIndex(0);

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "ResearchUnlocked must not advance from the ECS system");
        }

        [Test]
        public void CraftHydrogen_AdvancesWhenTwoHydrogenInInventory()
        {
            MakeFlow(
                InventoryMinStep("craft_hydrogen",       itemId: 6, qty: 2),
                BuildingMinStep("place_first_building",  minCount: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 2);

            _world.Update();

            Assert.AreEqual(1, GetStepIndex());
        }

        // ── Building upgrade tutorial (steps 48–50) ──────────────────────────

        [Test]
        public void UpgradeTutorial_AllThreeSteps_HoldUntilUiEvents()
        {
            // Mirrors the real steps 48–50: all three are UiEvent gates — none auto-advance.
            MakeFlow(
                UiEventStep("intro_building_upgrades",     "dialogue_complete"),
                UiEventStep("upgrade_atom_generator_speed","atom_generator_speed_upgraded"),
                UiEventStep("upgrade_context",             "dialogue_complete")
            );
            SetStepIndex(0);

            _world.Update();
            Assert.AreEqual(0, GetStepIndex(), "intro_building_upgrades must not advance from ECS");

            SetStepIndex(1);
            _world.Update();
            Assert.AreEqual(1, GetStepIndex(), "upgrade_atom_generator_speed must not advance from ECS");

            SetStepIndex(2);
            _world.Update();
            Assert.AreEqual(2, GetStepIndex(), "upgrade_context must not advance from ECS");
        }

        [Test]
        public void UpgradeAtomGenerator_WaitsForSpecificEventId()
        {
            // The step uses "atom_generator_speed_upgraded", NOT the generic "dialogue_complete".
            // Verifies the event ID is wired to the right string — a wrong ID would keep the
            // tutorial stuck even after the upgrade UI calls the generic event.
            var upgradeStep = UiEventStep("upgrade_atom_generator_speed", "atom_generator_speed_upgraded");
            Assert.AreEqual("atom_generator_speed_upgraded",
                upgradeStep.advanceCondition.uiEventId,
                "upgrade_atom_generator_speed step must use 'atom_generator_speed_upgraded' event id");
        }

        [Test]
        public void UpgradeTutorial_SequenceDoesNotSkipUpgradeGate()
        {
            // Even if an earlier step's inventory/auto condition is met, the upgrade gate
            // (UiEvent) must not be bypassed by the ECS system.
            MakeFlow(
                InventoryMinStep("hydrogen_loop_complete", itemId: 6, qty: 1),
                UiEventStep("intro_building_upgrades",     "dialogue_complete"),
                UiEventStep("upgrade_atom_generator_speed","atom_generator_speed_upgraded"),
                UiEventStep("upgrade_context",             "dialogue_complete"),
                InventoryMinStep("post_tutorial",          itemId: 6, qty: 1)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 5); // hydrogen present — step 0 should advance

            _world.Update();
            Assert.AreEqual(1, GetStepIndex(), "Step 0 (InventoryMin) should advance");

            _world.Update();
            Assert.AreEqual(1, GetStepIndex(), "Step 1 (UiEvent: dialogue_complete) must not advance from ECS");
        }

        // ── Smart progression: forward substep skip ──────────────────────────

        [Test]
        public void ForwardSubstepSkip_SkipsCurrentWhenLaterSubstepSatisfied()
        {
            // Mirrors the hydrogen chain: craft_protons_for_hydrogen (proton>=2) then
            // craft_hydrogen (hydrogen>=2). The protons were already consumed into hydrogen,
            // so the proton step's own condition can never read >=2 — but craft_hydrogen is
            // satisfied, so the proton substep must be skipped rather than soft-locking.
            MakeFlow(
                InventoryMinStep("craft_protons_for_hydrogen", itemId: 4, qty: 2, isSubstep: true),
                InventoryMinStep("craft_hydrogen",             itemId: 6, qty: 2, isSubstep: true),
                BuildingMinStep("hydrogen_crafted", minCount: 1) // major boundary (non-substep)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 2); // 2 hydrogen, 0 protons

            _world.Update(); // skips the proton substep (later substep satisfied)
            Assert.AreEqual(1, GetStepIndex(), "Proton substep should be skipped when hydrogen already exists");

            _world.Update(); // craft_hydrogen advances on its own satisfied condition
            Assert.AreEqual(2, GetStepIndex(), "Should cascade to the satisfied substep and past it");
        }

        [Test]
        public void ForwardSubstepSkip_DoesNotCrossMajorStep()
        {
            // A satisfied substep sits AFTER a non-substep (major) step. The forward scan must
            // stop at the major step and NOT skip the current substep.
            MakeFlow(
                InventoryMinStep("gather", itemId: 4, qty: 2, isSubstep: true),
                UiEventStep("major_dialogue", "dialogue_complete"),          // non-substep boundary
                InventoryMinStep("later_substep", itemId: 6, qty: 2, isSubstep: true)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 2); // later substep IS satisfied, but behind a major step

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(),
                "Must not skip across a major step even if a later substep is satisfied");
        }

        [Test]
        public void ForwardSubstepSkip_NonSubstepStepIsNotForceSkipped()
        {
            // The current step is NOT a substep, so even though a later substep is satisfied,
            // it must hold on its own (unmet) condition.
            MakeFlow(
                InventoryMinStep("major_craft", itemId: 4, qty: 2, isSubstep: false),
                InventoryMinStep("later_substep", itemId: 6, qty: 2, isSubstep: true)
            );
            SetStepIndex(0);
            AddToInventory(itemId: 6, qty: 2); // later substep satisfied, current not

            _world.Update();

            Assert.AreEqual(0, GetStepIndex(), "Non-substep steps must not be forward-skipped");
        }

        // ── Completion ────────────────────────────────────────────────────────

        [Test]
        public void LastStep_Auto_SetsInactiveAndDoesNotThrow()
        {
            MakeFlow(
                new TutorialStepDef
                {
                    id = "final",
                    advanceCondition = new TutorialConditionDef { type = ConditionType.Auto },
                    onEnter = new TutorialOnEnter()
                }
            );
            SetStepIndex(0);

            Assert.DoesNotThrow(() => _world.Update());

            Assert.IsFalse(GetIsActive(), "Tutorial should be inactive after final step");
        }
    }
}
