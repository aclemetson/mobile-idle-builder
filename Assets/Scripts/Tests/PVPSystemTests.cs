using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for PVPSystem.
    ///
    /// PVPSystem resets the current run (inventory, buildings, currency) when
    /// PVPRunRequested is set, but — unlike PrestigeSystem — does NOT award prestige
    /// currency or increment the run counter.
    /// </summary>
    [TestFixture]
    public class PVPSystemTests
    {
        private World         _world;
        private EntityManager _em;
        private Entity        _playerEntity;

        [SetUp]
        public void SetUp()
        {
            _world = new World("PVPTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<PVPSystem>());
            simGroup.SortSystems();

            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddComponent<PlayerProgressData>(_playerEntity);
            _em.AddComponent<PrestigeData>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        // ── PVPRunRequested = false ───────────────────────────────────────────

        [Test]
        public void WhenNotRequested_StateIsUnchanged()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth        = 999f,
                PVPRunRequested = false
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(999f, progress.NetWorth, "State must not change when PVPRunRequested is false");
        }

        // ── PVPRunRequested = true ────────────────────────────────────────────

        [Test]
        public void WhenRequested_NoCurrencyAwarded()
        {
            _em.SetComponentData(_playerEntity, new PrestigeData  { PrestigeCurrency = 0L });
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth        = 5000f,
                PVPRunRequested = true
            });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(0L, prestige.PrestigeCurrency,
                "PVP run must NOT award prestige currency (unlike a prestige)");
        }

        [Test]
        public void WhenRequested_RunCountDoesNotIncrement()
        {
            _em.SetComponentData(_playerEntity, new PrestigeData  { RunCount = 3 });
            _em.SetComponentData(_playerEntity, new PlayerProgressData { PVPRunRequested = true });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(3, prestige.RunCount, "PVP run must not increment prestige run count");
        }

        [Test]
        public void WhenRequested_PlayerProgressIsReset()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                BaseCurrency      = 9999L,
                NetWorth          = 5000f,
                CurrentTier       = 4,
                PrestigeAvailable = true,
                PVPRunRequested   = true
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(0L,  progress.BaseCurrency,       "BaseCurrency must reset to 0");
            Assert.AreEqual(0f,  progress.NetWorth,           "NetWorth must reset to 0");
            Assert.AreEqual(1,   progress.CurrentTier,        "CurrentTier must reset to 1");
            Assert.IsFalse(progress.PrestigeAvailable,        "PrestigeAvailable must reset to false");
            Assert.IsFalse(progress.PVPRunRequested,          "PVPRunRequested must clear after processing");
        }

        [Test]
        public void WhenRequested_InventoryIsCleared()
        {
            var inv = _em.GetBuffer<InventorySlot>(_playerEntity);
            inv.Add(new InventorySlot { ItemID = 1, Quantity = 50 });
            inv.Add(new InventorySlot { ItemID = 4, Quantity = 20 });

            _em.SetComponentData(_playerEntity, new PlayerProgressData { PVPRunRequested = true });

            _world.Update();

            var invAfter = _em.GetBuffer<InventorySlot>(_playerEntity);
            Assert.AreEqual(0, invAfter.Length, "Inventory must be fully cleared on PVP run start");
        }

        [Test]
        public void WhenRequested_BuildingEntitiesAreDestroyed()
        {
            var b1 = _em.CreateEntity(); _em.AddComponent<BuildingData>(b1);
            var b2 = _em.CreateEntity(); _em.AddComponent<BuildingData>(b2);

            _em.SetComponentData(_playerEntity, new PlayerProgressData { PVPRunRequested = true });

            _world.Update();

            Assert.IsFalse(_em.Exists(b1), "Building b1 must be destroyed on PVP run start");
            Assert.IsFalse(_em.Exists(b2), "Building b2 must be destroyed on PVP run start");
        }
    }
}
