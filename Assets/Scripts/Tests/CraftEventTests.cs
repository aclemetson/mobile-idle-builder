using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for the craft-reporting path: ProductionSystem records each completed recipe
    /// output into CraftedOutputEvent, and ProductionAchievementBridge drains it.
    ///
    /// The bridge used to diff BuildingOutputSlot totals frame to frame instead, which dropped any craft a
    /// conveyor drained before it sampled (ConveyorSystem and the bridge are both merely
    /// UpdateAfter(ProductionSystem), so nothing orders them) and could not name the item that was made.
    /// DrainedCraft_SurvivesTheOutputBufferBeingEmptied is the regression guard for the first half;
    /// recording ItemID at the source is the fix for the second.
    /// </summary>
    [TestFixture]
    public class CraftEventTests
    {
        World         _world;
        World         _previousDefaultWorld;
        EntityManager _em;
        Entity        _playerEntity;
        Entity        _buildingEntity;

        const int   InputItemId  = 1;
        const int   OutputItemId = 4;   // stubbed as tier 3 below
        const int   InputQty     = 2;
        const float CraftTime    = 2f;

        [SetUp]
        public void SetUp()
        {
            _previousDefaultWorld = World.DefaultGameObjectInjectionWorld;

            _world = new World("CraftEventTest");
            _em    = _world.EntityManager;
            World.DefaultGameObjectInjectionWorld = _world;   // TierProgress resolves the singleton through this

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<ProductionSystem>());
            simGroup.AddSystemToUpdateList(_world.CreateSystemManaged<ProductionAchievementBridge>());
            simGroup.SortSystems();

            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
            _em.AddComponentData(_playerEntity, new PlayerProgressData { CurrentTier = 1 });

            _buildingEntity = _em.CreateEntity();
            _em.AddComponentData(_buildingEntity, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(_buildingEntity, new RecipeProcessData
            {
                RecipeID = 1, CraftTime = CraftTime, Progress = 0f, IsCrafting = false
            });
            _em.AddBuffer<RecipeInputSlot>(_buildingEntity)
               .Add(new RecipeInputSlot { ItemID = InputItemId, Quantity = InputQty });
            _em.AddBuffer<RecipeOutputSlot>(_buildingEntity)
               .Add(new RecipeOutputSlot { ItemID = OutputItemId, Quantity = 1 });
            _em.AddBuffer<BuildingInputSlot>(_buildingEntity);
            _em.AddBuffer<BuildingOutputSlot>(_buildingEntity);
            _em.AddBuffer<CraftedOutputEvent>(_buildingEntity);
            _em.AddComponentData(_buildingEntity, new BuildingInventoryConfig
            {
                OutputCapacity = 20, InputCapacity = 20
            });

            // ItemDatabase fills its tables in Awake and this assembly is editor-only, so stub the tier.
            TierProgress.TierLookupOverrideForTests = id => id == OutputItemId ? 3 : 0;
        }

        [TearDown]
        public void TearDown()
        {
            TierProgress.TierLookupOverrideForTests = null;
            World.DefaultGameObjectInjectionWorld   = _previousDefaultWorld;
            if (_world.IsCreated) _world.Dispose();
        }

        void Tick(float deltaTime = 1f)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: deltaTime));
            _world.Update();
        }

        void AddInputs(int qty)
        {
            var buf = _em.GetBuffer<InventorySlot>(_playerEntity);
            SlotBufferUtils.AddToInventory(ref buf, InputItemId, qty);
        }

        int CurrentTier() =>
            _em.GetComponentData<PlayerProgressData>(_playerEntity).CurrentTier;

        int CraftedEventCount() =>
            _em.GetBuffer<CraftedOutputEvent>(_buildingEntity).Length;

        [Test]
        public void CompletingARecipe_RaisesTierFromTheRecordedCraft()
        {
            AddInputs(10);

            Tick(CraftTime + 0.1f);   // production completes, bridge drains in the same frame

            Assert.AreEqual(3, CurrentTier(),
                "The recorded craft names its item, so its tier can be read — that is what makes tier progression work.");
        }

        [Test]
        public void TheBridgeDrainsTheCraftBuffer()
        {
            AddInputs(10);

            Tick(CraftTime + 0.1f);

            Assert.AreEqual(0, CraftedEventCount(),
                "Events must be consumed, or every craft would be re-counted on the next frame.");
        }

        [Test]
        public void DrainedCraft_SurvivesTheOutputBufferBeingEmptied()
        {
            // The old bridge inferred crafts from BuildingOutputSlot totals. Simulate a conveyor pulling the
            // output away in the same frame it was produced: with total-diffing this craft vanished.
            AddInputs(10);

            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: CraftTime + 0.1f));
            _world.GetExistingSystem<ProductionSystem>().Update(_world.Unmanaged);

            _em.GetBuffer<BuildingOutputSlot>(_buildingEntity).Clear();   // the belt drains it

            _world.GetExistingSystemManaged<ProductionAchievementBridge>().Update();

            Assert.AreEqual(3, CurrentTier(),
                "A craft must still count when a belt empties the output buffer before the bridge runs.");
            Assert.AreEqual(0, CraftedEventCount());
        }

        [Test]
        public void NoCraft_ProducesNoEvents()
        {
            // No inputs available — nothing should be recorded.
            Tick(CraftTime + 0.1f);

            Assert.AreEqual(0, CraftedEventCount());
            Assert.AreEqual(1, CurrentTier(), "Tier must not move without production.");
        }

        [Test]
        public void ProductionStillWorks_WhenTheCraftBufferIsAbsent()
        {
            // The buffer is optional (ProductionSystem probes with HasBuffer) so an entity created by an
            // older archetype cannot have its production broken by the recording.
            _em.RemoveComponent<CraftedOutputEvent>(_buildingEntity);
            AddInputs(10);

            Tick(CraftTime + 0.1f);

            var output = _em.GetBuffer<BuildingOutputSlot>(_buildingEntity);
            Assert.AreEqual(1, output.Length, "Output must still be deposited without the craft buffer.");
            Assert.AreEqual(OutputItemId, output[0].ItemID);
        }
    }
}
