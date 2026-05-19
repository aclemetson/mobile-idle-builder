using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for CollectorSystem.
    ///
    /// Collector buildings produce items autonomously at a fixed rate (OutputRate items/sec)
    /// into their BuildingOutputSlot. The timer remainder is preserved across frames so
    /// fractional intervals don't lose time.
    /// </summary>
    [TestFixture]
    public class CollectorSystemTests
    {
        private World         _world;
        private EntityManager _em;
        private Entity        _buildingEntity;

        private const int OutputItemId = 3;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CollectorTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<CollectorSystem>());
            simGroup.SortSystems();

            _buildingEntity = _em.CreateEntity();
            _em.AddComponentData(_buildingEntity, new CollectorData { OutputRate = 1f, Timer = 0f });
            _em.AddComponentData(_buildingEntity, new BuildingData  { IsActive = true });
            var recipeOut = _em.AddBuffer<RecipeOutputSlot>(_buildingEntity);
            recipeOut.Add(new RecipeOutputSlot { ItemID = OutputItemId, Quantity = 1 });
            _em.AddBuffer<BuildingOutputSlot>(_buildingEntity);
            _em.AddComponentData(_buildingEntity, new BuildingInventoryConfig { OutputCapacity = 20 });
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Tick(float deltaTime)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: deltaTime));
            _world.Update();
        }

        private int OutputCount() =>
            SlotBufferUtils.CountInOutputBuffer(_em.GetBuffer<BuildingOutputSlot>(_buildingEntity), OutputItemId);

        // ── Guard conditions ─────────────────────────────────────────────────

        [Test]
        public void WhenBuildingInactive_NothingProduced()
        {
            _em.SetComponentData(_buildingEntity, new BuildingData { IsActive = false });

            Tick(deltaTime: 10f); // way past interval

            Assert.AreEqual(0, OutputCount(), "Inactive building must produce nothing");
        }

        [Test]
        public void WhenNoRecipeOutputSlots_NothingProduced()
        {
            // Replace the buffer with an empty one
            _em.GetBuffer<RecipeOutputSlot>(_buildingEntity).Clear();

            Tick(deltaTime: 10f);

            Assert.AreEqual(0, OutputCount(), "Building with no RecipeOutputSlots must produce nothing");
        }

        // ── Rate-based production ────────────────────────────────────────────

        [Test]
        public void AtOutputRate2_ProducesOneItemEvery0p5Seconds()
        {
            _em.SetComponentData(_buildingEntity, new CollectorData { OutputRate = 2f, Timer = 0f });

            Tick(deltaTime: 0.5f); // exactly one interval

            Assert.AreEqual(1, OutputCount(), "OutputRate=2 should produce 1 item after 0.5s");
        }

        [Test]
        public void BelowInterval_NothingProduced()
        {
            _em.SetComponentData(_buildingEntity, new CollectorData { OutputRate = 2f, Timer = 0f });

            Tick(deltaTime: 0.4f); // 0.4 < 0.5 interval

            Assert.AreEqual(0, OutputCount(), "Tick below interval must not produce an item");
        }

        [Test]
        public void TimerRemainder_PreservedAcrossFrames()
        {
            // OutputRate=2 → interval=0.5s; two ticks of 0.3s each = 0.6s total = 1 item
            _em.SetComponentData(_buildingEntity, new CollectorData { OutputRate = 2f, Timer = 0f });

            Tick(deltaTime: 0.3f);
            Tick(deltaTime: 0.3f);

            Assert.AreEqual(1, OutputCount(),
                "Remainder must be preserved: 0.3+0.3=0.6 > 0.5 interval → exactly 1 item");
        }

        // ── Output capacity ──────────────────────────────────────────────────

        [Test]
        public void WhenOutputAtCapacity_ItemNotAdded()
        {
            var config  = _em.GetComponentData<BuildingInventoryConfig>(_buildingEntity);
            var outBuf  = _em.GetBuffer<BuildingOutputSlot>(_buildingEntity);
            outBuf.Add(new BuildingOutputSlot { ItemID = OutputItemId, Quantity = config.OutputCapacity });

            Tick(deltaTime: 10f);

            Assert.AreEqual(config.OutputCapacity,
                SlotBufferUtils.TotalInOutputBuffer(outBuf),
                "Full output buffer must not overflow");
        }
    }
}
