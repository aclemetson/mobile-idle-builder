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

        private static TutorialStepDef InventoryMinStep(string id, int itemId, int qty, bool anyOf = false)
            => new TutorialStepDef
            {
                id = id,
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
