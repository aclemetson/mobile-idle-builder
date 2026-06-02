using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for EntropySinkSystem (Maxwell's Demon building).
    ///
    /// EntropySinkSystem reads ItemDatabase.Instance for per-item sell values, falling back
    /// to 1f per item when null. Since ItemDatabase is a managed MonoBehaviour not available
    /// in isolated-World tests, all tests use the fallback path (sellValue=1f), which still
    /// gives full coverage of the accounting logic and buffer clearing behaviour.
    /// </summary>
    [TestFixture]
    public class EntropySinkSystemTests
    {
        private World         _world;
        private EntityManager _em;
        private Entity        _playerEntity;
        private Entity        _sinkEntity;

        [SetUp]
        public void SetUp()
        {
            _world = new World("EntropySinkTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<EntropySinkSystem>());
            simGroup.SortSystems();

            // Player entity — holds PlayerProgressData singleton
            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddComponentData(_playerEntity, new PlayerProgressData { BaseCurrency = 0 });

            // Entropy sink building
            _sinkEntity = _em.CreateEntity();
            _em.AddComponent<EntropySinkTag>(_sinkEntity);
            _em.AddComponentData(_sinkEntity, new BuildingData { IsActive = true });
            _em.AddBuffer<BuildingInputSlot>(_sinkEntity);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Tick() => _world.Update();

        private long GetCurrency() =>
            _em.GetComponentData<PlayerProgressData>(_playerEntity).BaseCurrency;

        private void AddInputItems(int itemId, int qty)
        {
            var buf = _em.GetBuffer<BuildingInputSlot>(_sinkEntity);
            SlotBufferUtils.AddToInputBuffer(buf, itemId, qty);
        }

        // ── Guard conditions ─────────────────────────────────────────────────

        [Test]
        public void WhenInputBufferEmpty_CurrencyUnchanged()
        {
            Tick();
            Assert.AreEqual(0L, GetCurrency(), "Empty input buffer must not change currency");
        }

        [Test]
        public void WhenBuildingInactive_CurrencyUnchanged()
        {
            _em.SetComponentData(_sinkEntity, new BuildingData { IsActive = false });
            AddInputItems(itemId: 1, qty: 5);

            Tick();

            Assert.AreEqual(0L, GetCurrency(), "Inactive sink must not consume items or award currency");
        }

        // ── Accounting (null ItemDatabase → fallback sellValue=1f) ────────────

        [Test]
        public void WithNullItemDatabase_EachItemAddsFallbackCurrencyOf1()
        {
            AddInputItems(itemId: 1, qty: 5);

            Tick();

            Assert.AreEqual(5L, GetCurrency(),
                "5 items × fallback sellValue=1 must award 5 BaseCurrency (ItemDatabase is null in isolated tests)");
        }

        [Test]
        public void WhenItemsPresent_ClearsInputBuffer()
        {
            AddInputItems(itemId: 1, qty: 3);

            Tick();

            int remaining = SlotBufferUtils.TotalInInputBuffer(_em.GetBuffer<BuildingInputSlot>(_sinkEntity));
            Assert.AreEqual(0, remaining, "EntropySink must clear its input buffer after each tick");
        }

        // ── Multiple buildings ────────────────────────────────────────────────

        [Test]
        public void MultipleBuildings_CurrencyAccumulatesAcrossAll()
        {
            // Second entropy sink
            var sink2 = _em.CreateEntity();
            _em.AddComponent<EntropySinkTag>(sink2);
            _em.AddComponentData(sink2, new BuildingData { IsActive = true });
            var buf2 = _em.AddBuffer<BuildingInputSlot>(sink2);
            SlotBufferUtils.AddToInputBuffer(buf2, 2, 4);

            AddInputItems(itemId: 1, qty: 3); // first sink: 3 × 1f = 3

            Tick();

            Assert.AreEqual(7L, GetCurrency(),
                "Two sinks: 3 + 4 items × fallback sellValue=1 must total 7 BaseCurrency");
        }
    }
}
