using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for TutorialSystem.
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
            if (_world.IsCreated) _world.Dispose();
        }

        // ── Initial step ─────────────────────────────────────────────────────

        [Test]
        public void WhenInactive_StepDoesNotAdvance()
        {
            _em.SetComponentData(_playerEntity, new TutorialStateData
            {
                CurrentStep = TutorialStep.CraftFirstQuarks,
                IsActive = false
            });

            _world.Update();

            var state = _em.GetComponentData<TutorialStateData>(_playerEntity);
            Assert.AreEqual(TutorialStep.CraftFirstQuarks, state.CurrentStep,
                "Inactive tutorial must not advance steps");
        }

        [Test]
        public void WhenStepIsNone_AdvancesToIntroDialogue()
        {
            _em.SetComponentData(_playerEntity, new TutorialStateData
            {
                CurrentStep = TutorialStep.None,
                IsActive = true
            });

            _world.Update();

            var state = _em.GetComponentData<TutorialStateData>(_playerEntity);
            Assert.AreEqual(TutorialStep.IntroDialogue, state.CurrentStep);
        }

        // ── Step: CraftFirstQuarks → CraftFirstProton ────────────────────────

        [Test]
        public void WhenCraftFirstQuarks_AndNoQuarks_DoesNotAdvance()
        {
            SetStep(TutorialStep.CraftFirstQuarks);
            // Inventory empty

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstQuarks, GetStep());
        }

        [Test]
        public void WhenCraftFirstQuarks_AndHasUpQuark_AdvancesToCraftFirstProton()
        {
            SetStep(TutorialStep.CraftFirstQuarks);
            AddToInventory(itemId: 1, qty: 3); // up quark

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstProton, GetStep());
        }

        [Test]
        public void WhenCraftFirstQuarks_AndHasDownQuark_AdvancesToCraftFirstProton()
        {
            SetStep(TutorialStep.CraftFirstQuarks);
            AddToInventory(itemId: 2, qty: 2); // down quark

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstProton, GetStep());
        }

        // ── Step: CraftFirstProton → CraftFirstNeutron ───────────────────────

        [Test]
        public void WhenCraftFirstProton_AndNoProton_DoesNotAdvance()
        {
            SetStep(TutorialStep.CraftFirstProton);
            AddToInventory(itemId: 1, qty: 5); // has quarks, not proton

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstProton, GetStep());
        }

        [Test]
        public void WhenCraftFirstProton_AndHasProton_AdvancesToCraftFirstNeutron()
        {
            SetStep(TutorialStep.CraftFirstProton);
            AddToInventory(itemId: 4, qty: 1); // proton

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstNeutron, GetStep());
        }

        // ── Step: CraftFirstNeutron → CraftFirstHydrogen ────────────────────

        [Test]
        public void WhenCraftFirstNeutron_AndHasNeutron_AdvancesToCraftFirstHydrogen()
        {
            SetStep(TutorialStep.CraftFirstNeutron);
            AddToInventory(itemId: 5, qty: 1); // neutron

            _world.Update();

            Assert.AreEqual(TutorialStep.CraftFirstHydrogen, GetStep());
        }

        // ── Step: CraftFirstHydrogen → PlaceFirstBuilding ───────────────────

        [Test]
        public void WhenCraftFirstHydrogen_AndHasHydrogen_AdvancesToPlaceFirstBuilding()
        {
            SetStep(TutorialStep.CraftFirstHydrogen);
            AddToInventory(itemId: 6, qty: 1); // hydrogen

            _world.Update();

            Assert.AreEqual(TutorialStep.PlaceFirstBuilding, GetStep());
        }

        // ── Step: PlaceFirstBuilding → AutomationStarted ────────────────────

        [Test]
        public void WhenPlaceFirstBuilding_AndNoBuildings_DoesNotAdvance()
        {
            SetStep(TutorialStep.PlaceFirstBuilding);
            // No BuildingData entities

            _world.Update();

            Assert.AreEqual(TutorialStep.PlaceFirstBuilding, GetStep());
        }

        [Test]
        public void WhenPlaceFirstBuilding_AndBuildingExists_AdvancesToAutomationStarted()
        {
            SetStep(TutorialStep.PlaceFirstBuilding);

            var building = _em.CreateEntity();
            _em.AddComponent<BuildingData>(building);

            _world.Update();

            Assert.AreEqual(TutorialStep.AutomationStarted, GetStep());
        }

        // ── Step: AutomationStarted → ReachPrestigeWall ─────────────────────

        [Test]
        public void WhenAutomationStarted_AndPrestigeNotAvailable_DoesNotAdvance()
        {
            SetStep(TutorialStep.AutomationStarted);

            var progressEntity = _em.CreateEntity();
            _em.AddComponentData(progressEntity, new PlayerProgressData { PrestigeAvailable = false });

            _world.Update();

            Assert.AreEqual(TutorialStep.AutomationStarted, GetStep());
        }

        [Test]
        public void WhenAutomationStarted_AndPrestigeAvailable_AdvancesToReachPrestigeWall()
        {
            SetStep(TutorialStep.AutomationStarted);

            var progressEntity = _em.CreateEntity();
            _em.AddComponentData(progressEntity, new PlayerProgressData { PrestigeAvailable = true });

            _world.Update();

            Assert.AreEqual(TutorialStep.ReachPrestigeWall, GetStep());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void SetStep(TutorialStep step)
        {
            _em.SetComponentData(_playerEntity, new TutorialStateData
            {
                CurrentStep = step,
                IsActive = true
            });
        }

        private TutorialStep GetStep() =>
            _em.GetComponentData<TutorialStateData>(_playerEntity).CurrentStep;

        private void AddToInventory(int itemId, int qty)
        {
            var inv = _em.GetBuffer<InventorySlot>(_playerEntity);
            inv.Add(new InventorySlot { ItemID = itemId, Quantity = qty });
        }
    }
}
