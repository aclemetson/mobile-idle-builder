using System.Linq;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Several Springs and several streams (2026-11-27): a branch from a second Spring that merges into a stream adds
    /// its flow there, streams that never join are tracked apart, and a Spring beside no stream feeds nothing. The HUD
    /// keeps the site total and warns when any one stream draws more than its own Springs give.
    /// </summary>
    public class StreamNetworkTests
    {
        const int North = 0, East = 1, South = 2;

        // ── Rules ─────────────────────────────────────────────────────────────

        [Test]
        public void Networks_SeparateChainsStayApart_AMergeJoinsThem()
        {
            var n = new StreamNetworks<int>();
            n.Link(1, 2); n.Link(2, 3);     // stream A
            n.Link(10, 11);                 // stream B
            Assert.AreEqual(2, n.Count);
            Assert.AreNotEqual(n.Find(1), n.Find(10));

            n.Link(11, 3);                  // B merges into A
            Assert.AreEqual(1, n.Count);
            Assert.AreEqual(n.Find(1), n.Find(10));
        }

        [Test]
        public void FlowLabel_KeepsTheTotal_AndWarnsAboutAnOverdrawnStream()
        {
            Assert.AreEqual("Flow 12 / 20 L/s", HUDStatusBarController.FormatFlowLabel(12f, 20f, 0, 0));
            string warn = HUDStatusBarController.FormatFlowLabel(12f, 20f, 0, 1);
            StringAssert.StartsWith("⚠ Flow 12 / 20 L/s", warn, "the total stays, with a warning");
            StringAssert.Contains("1 stream over its Springs", warn);
            StringAssert.Contains("2 streams over their Springs", HUDStatusBarController.FormatFlowLabel(1f, 2f, 0, 2));
        }

        [Test]
        public void InspectorStreamTotals_WarnsWhenItsStreamIsOverdrawn()
        {
            Assert.AreEqual("This stream: 6 / 20 L/s from 2 Springs",
                HUDBuildingInspectorSubController.StreamTotalsText(new StreamStatus { OnStream = 1, StreamDraw = 6f, StreamSupply = 20f, StreamSprings = 2 }));
            StringAssert.StartsWith("⚠", HUDBuildingInspectorSubController.StreamTotalsText(
                new StreamStatus { OnStream = 1, StreamDraw = 12f, StreamSupply = 10f, StreamSprings = 1 }));
        }

        // ── ECS ───────────────────────────────────────────────────────────────

        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("StreamNetworkTest");
            _em    = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystem<StreamFlowSystem>());
            sim.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        void Tick()
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: 0.02f));
            _world.Update();
        }

        void Spring(int x, int y, int facing, float flow = 10f)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, y) });
            _em.AddComponentData(e, new StreamSource { Flow = flow });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = x, CellY = y, Facing = facing });
        }

        /// <summary>A stream run through <paramref name="cells"/>, each flowing into the next; the last flows into <paramref name="into"/> if given.</summary>
        Entity[] Run(int2[] cells, int exitDir, Entity into = default)
        {
            var segs = new Entity[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                segs[i] = _em.CreateEntity();
                _em.AddComponentData(segs[i], new GridPosition { Cell = cells[i] });
                _em.AddComponentData(segs[i], new StreamSegment());
                _em.AddBuffer<ConveyorItem>(segs[i]);
            }
            for (int i = 0; i < cells.Length; i++)
                _em.AddComponentData(segs[i], new ConveyorSegmentData
                {
                    Cell = cells[i], EntryDir = exitDir, ExitDir = exitDir, TransportTime = 1f,
                    NextSegment = i + 1 < cells.Length ? segs[i + 1] : into,
                    PrevSegment = i > 0 ? segs[i - 1] : Entity.Null,
                    IsChainHead = i == 0,
                });
            return segs;
        }

        Entity[] EastRun(int y, int length) =>
            Run(Enumerable.Range(0, length).Select(x => new int2(x, y)).ToArray(), East);

        Entity Bank(int x, int y, float draw)
        {
            // Sits north of the stream at (x, y - 1); input faces it.
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, y) });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData { PortType = (int)PortType.Input, CellX = x, CellY = y, Facing = North });
            _em.AddComponentData(e, new StreamConsumer { Draw = draw });
            _em.AddComponentData(e, new StreamStatus());
            return e;
        }

        StreamStatus Status(Entity e) => _em.GetComponentData<StreamStatus>(e);

        StreamGridState Grid()
        {
            using var q = _em.CreateEntityQuery(typeof(StreamGridState));
            return q.GetSingleton<StreamGridState>();
        }

        [Test]
        public void TwoSprings_MergingBranches_AddTheirFlow()
        {
            // Main stream along y = 0 from a Spring at (0,1); a branch from a second Spring at (2,3) runs south and
            // joins it at (2,0). A building past the merge draws 15: more than either Spring, not more than both.
            var main = EastRun(0, 5);
            Spring(0, 1, South);
            Run(new[] { new int2(2, 2), new int2(2, 1) }, South, into: main[2]);
            Spring(2, 3, South);
            var b = Bank(4, 1, 15f);

            Tick();

            Assert.AreEqual(1f, Status(b).Ratio, 1e-5f, "past the merge both Springs' water is there");
            Assert.AreEqual(20f, Status(b).StreamSupply, 1e-5f);
            Assert.AreEqual(2, Status(b).StreamSprings);
            Assert.AreEqual(1, Grid().StreamCount, "the branch is part of the stream it joins");
            Assert.AreEqual(0, Grid().OverdrawnCount);
        }

        [Test]
        public void TwoSeparateStreams_TrackTheirFlowApart()
        {
            // Stream A (y = 0) has a Spring and draws 4; stream B (y = 5) has a Spring and draws 15. The site total
            // (19 of 20) looks fine, but B is over its own Spring and its building runs short.
            EastRun(0, 3); Spring(0, 1, South);
            EastRun(5, 3); Spring(0, 6, South);
            var a = Bank(1, 1, 4f);
            var b = Bank(1, 6, 15f);

            Tick();

            Assert.AreEqual(1f, Status(a).Ratio, 1e-5f);
            Assert.AreEqual(10f / 15f, Status(b).Ratio, 1e-5f, "B cannot use A's spare water");
            Assert.AreEqual(4f,  Status(a).StreamDraw, 1e-5f);
            Assert.AreEqual(15f, Status(b).StreamDraw, 1e-5f);
            Assert.AreEqual(10f, Status(b).StreamSupply, 1e-5f);
            var g = Grid();
            Assert.AreEqual(20f, g.Supply, 1e-5f, "the HUD total still adds every stream");
            Assert.AreEqual(19f, g.Draw, 1e-5f);
            Assert.AreEqual(2, g.StreamCount);
            Assert.AreEqual(1, g.OverdrawnCount, "one stream is over its Springs");
        }

        [Test]
        public void ASpringBesideNoStream_FeedsNothing()
        {
            EastRun(0, 3); Spring(0, 1, South);
            Spring(10, 10, South);   // pours onto an empty tile
            var a = Bank(1, 1, 4f);

            Tick();

            Assert.AreEqual(10f, Grid().Supply, 1e-5f, "the lone Spring adds nothing");
            Assert.AreEqual(10f, Status(a).StreamSupply, 1e-5f);
            Assert.AreEqual(1, Status(a).StreamSprings);
        }
    }
}
