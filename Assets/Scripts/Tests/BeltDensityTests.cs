using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Belt density: a tile holds <see cref="BeltSettings.ItemsPerTile"/> items, so a belt carries
    /// speed x itemsPerTile items/s and a blocked belt packs at that spacing. Before this a tile held one
    /// item and an item waited mid-tile for the next tile to empty, so a belt carried only ~0.66 items/s.
    /// </summary>
    [TestFixture]
    public class BeltDensityTests
    {
        private const int East = 1;
        private const int ItemId = 7;
        private const float Dt = 1f / 60f;

        private World _world;
        private EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("BeltDensityTest");
            _em = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            sim.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Settings(int itemsPerTile, float speedMult = 1f)
        {
            var e = _em.CreateEntity(typeof(BeltSettings), typeof(ResearchEffects));
            _em.SetComponentData(e, new BeltSettings { ItemsPerTile = itemsPerTile });
            _em.SetComponentData(e, new ResearchEffects { ConveyorSpeedMult = speedMult });
        }

        /// <summary>
        /// A straight East-running belt of <paramref name="length"/> cells from x = 1. A source building at
        /// x = 0 outputs into the head; when <paramref name="withSink"/> a building at x = length + 1 takes
        /// everything at the tail. Returns (head, tail, source, sink).
        /// </summary>
        private (Entity[] segs, Entity source, Entity sink) Line(int length, bool withSink)
        {
            var segs = new Entity[length];
            for (int i = 0; i < length; i++)
            {
                segs[i] = _em.CreateEntity();
                _em.AddComponentData(segs[i], new GridPosition { Cell = new int2(i + 1, 0) });
            }
            for (int i = 0; i < length; i++)
                _em.AddComponentData(segs[i], new ConveyorSegmentData
                {
                    Cell = new int2(i + 1, 0), EntryDir = East, ExitDir = East, TransportTime = 1f,
                    NextSegment = i < length - 1 ? segs[i + 1] : Entity.Null,
                    PrevSegment = i > 0 ? segs[i - 1] : Entity.Null,
                    IsChainHead = i == 0
                });

            var source = Building(new int2(0, 0), PortType.Output, East);
            Entity sink = Entity.Null;
            if (withSink) sink = Building(new int2(length + 1, 0), PortType.Input, East);
            return (segs, source, sink);
        }

        private Entity Building(int2 cell, PortType type, int facing)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = cell });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData
                { PortType = (int)type, CellX = cell.x, CellY = cell.y, Facing = facing });
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig { OutputCapacity = 1000, InputCapacity = 1000 });
            return e;
        }

        /// <summary>Runs for <paramref name="seconds"/> with an endless source; returns items/s delivered after warm-up.</summary>
        private float MeasureThroughput(int itemsPerTile, float speedMult, float seconds = 30f)
        {
            Settings(itemsPerTile, speedMult);
            var (_, source, sink) = Line(6, withSink: true);
            int delivered = 0, warmupFrames = (int)(10f / Dt), frames = (int)(seconds / Dt);
            for (int f = 0; f < warmupFrames + frames; f++)
            {
                var outBuf = _em.GetBuffer<BuildingOutputSlot>(source);
                if (SlotBufferUtils.TotalInOutputBuffer(outBuf) < 10)
                    SlotBufferUtils.AddToOutputBuffer(outBuf, ItemId, 10);

                _world.SetTime(new TimeData(elapsedTime: f * Dt, deltaTime: Dt));
                _world.Update();

                var inBuf = _em.GetBuffer<BuildingInputSlot>(sink);
                if (f >= warmupFrames) delivered += SlotBufferUtils.TotalInInputBuffer(inBuf);
                inBuf.Clear();
            }
            return delivered / seconds;
        }

        [TestCase(1, 1f, 1f)]
        [TestCase(2, 1f, 2f)]
        [TestCase(4, 1f, 4f)]
        [TestCase(2, 2f, 4f)]
        public void Throughput_IsSpeedTimesItemsPerTile(int itemsPerTile, float speedMult, float expected) =>
            Assert.AreEqual(expected, MeasureThroughput(itemsPerTile, speedMult), expected * 0.02f);

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BlockedBelt_PacksItemsPerTile_AtEvenSpacing(int itemsPerTile)
        {
            Settings(itemsPerTile);
            var (segs, source, _) = Line(4, withSink: false);
            for (int f = 0; f < (int)(40f / Dt); f++)
            {
                var outBuf = _em.GetBuffer<BuildingOutputSlot>(source);
                if (SlotBufferUtils.TotalInOutputBuffer(outBuf) < 10)
                    SlotBufferUtils.AddToOutputBuffer(outBuf, ItemId, 10);
                _world.SetTime(new TimeData(elapsedTime: f * Dt, deltaTime: Dt));
                _world.Update();
            }

            float spacing = BeltSettings.Spacing(itemsPerTile);
            float? prev = null; // position along the belt of the item ahead, in tiles
            for (int i = segs.Length - 1; i >= 0; i--)
            {
                var items = _em.GetBuffer<ConveyorItem>(segs[i]);
                Assert.AreEqual(itemsPerTile, items.Length, $"cell {i} holds a full tile of items");
                for (int k = 0; k < items.Length; k++)
                {
                    float pos = i + items[k].Progress;
                    if (prev != null) Assert.AreEqual(spacing, prev.Value - pos, 1e-3f, "items queue side by side");
                    prev = pos;
                }
            }
            Assert.AreEqual(BeltMotion.TailRest(spacing), _em.GetBuffer<ConveyorItem>(segs[^1])[0].Progress, 1e-4f,
                "the front item rests mid-slot");
        }

        [Test]
        public void DefaultsToTwoPerTile_WithoutSettings()
        {
            var (segs, source, _) = Line(2, withSink: false);
            for (int f = 0; f < (int)(20f / Dt); f++)
            {
                var outBuf = _em.GetBuffer<BuildingOutputSlot>(source);
                if (SlotBufferUtils.TotalInOutputBuffer(outBuf) < 10)
                    SlotBufferUtils.AddToOutputBuffer(outBuf, ItemId, 10);
                _world.SetTime(new TimeData(elapsedTime: f * Dt, deltaTime: Dt));
                _world.Update();
            }
            Assert.AreEqual(BeltSettings.DefaultItemsPerTile, _em.GetBuffer<ConveyorItem>(segs[0]).Length);
        }

        // ── Pure motion rules ────────────────────────────────────────────────

        [Test]
        public void Advance_StopsAtTheLimit_AndNeverSlidesBack()
        {
            Assert.AreEqual(0.3f, BeltMotion.Advance(0.2f, 0.1f, 0.9f), 1e-6f);
            Assert.AreEqual(0.5f, BeltMotion.Advance(0.45f, 0.1f, 0.5f), 1e-6f);
            Assert.AreEqual(0.8f, BeltMotion.Advance(0.8f, 0.1f, 0.5f), 1e-6f, "space closed ahead: it waits");
        }

        [Test]
        public void FrontLimit_KeepsSpacingAcrossTheTileEdge()
        {
            Assert.AreEqual(BeltMotion.Open, BeltMotion.FrontLimitBehind(null, 0.5f), "empty ahead: no limit");
            Assert.AreEqual(1.1f, BeltMotion.FrontLimitBehind(0.6f, 0.5f), 1e-6f, "room ahead: may pass the hand-off");
            Assert.AreEqual(0.75f, BeltMotion.FrontLimitBehind(0.25f, 0.5f), 1e-6f);
        }

        [Test]
        public void HasRoom_NeedsOneSpacing()
        {
            Assert.IsTrue(BeltMotion.HasRoom(null, 0f, 0.5f));
            Assert.IsTrue(BeltMotion.HasRoom(0.5f, 0f, 0.5f));
            Assert.IsFalse(BeltMotion.HasRoom(0.4f, 0f, 0.5f));
        }
    }
}
