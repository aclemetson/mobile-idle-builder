using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class PowerGridSystemTests
    {
        private World         _world;
        private EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("PowerGridTest");
            _em    = _world.EntityManager;

            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<PowerGridSystem>());
            simGroup.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Tick() => _world.Update();

        private Entity MakeNode(float maxEV, float currentEV)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new PowerNodeData { MaxEV = maxEV, CurrentEV = currentEV });
            return e;
        }

        private float GetCurrentEV(Entity e) =>
            _em.GetComponentData<PowerNodeData>(e).CurrentEV;

        // ── Clamping ─────────────────────────────────────────────────────────

        [Test]
        public void WhenCurrentEVExceedsMax_ThenClampedToMax()
        {
            var e = MakeNode(maxEV: 100f, currentEV: 150f);

            Tick();

            Assert.AreEqual(100f, GetCurrentEV(e), 0.001f,
                "CurrentEV above MaxEV must be clamped down to MaxEV");
        }

        [Test]
        public void WhenCurrentEVBelowZero_ThenClampedToZero()
        {
            var e = MakeNode(maxEV: 100f, currentEV: -10f);

            Tick();

            Assert.AreEqual(0f, GetCurrentEV(e), 0.001f,
                "CurrentEV below 0 must be clamped up to 0");
        }

        [Test]
        public void WhenCurrentEVWithinRange_ThenUnchanged()
        {
            var e = MakeNode(maxEV: 100f, currentEV: 60f);

            Tick();

            Assert.AreEqual(60f, GetCurrentEV(e), 0.001f,
                "CurrentEV within [0, MaxEV] must not be modified");
        }

        [Test]
        public void WhenCurrentEVEqualsMax_ThenUnchanged()
        {
            var e = MakeNode(maxEV: 100f, currentEV: 100f);

            Tick();

            Assert.AreEqual(100f, GetCurrentEV(e), 0.001f,
                "CurrentEV exactly at MaxEV must not be modified");
        }

        [Test]
        public void WhenCurrentEVEqualsZero_ThenUnchanged()
        {
            var e = MakeNode(maxEV: 100f, currentEV: 0f);

            Tick();

            Assert.AreEqual(0f, GetCurrentEV(e), 0.001f,
                "CurrentEV exactly at 0 must not be modified");
        }

        // ── Multiple entities ─────────────────────────────────────────────────

        [Test]
        public void WhenMultipleEntities_ThenEachClampedIndependently()
        {
            var overMax  = MakeNode(maxEV: 100f, currentEV: 200f);
            var belowMin = MakeNode(maxEV: 100f, currentEV: -5f);
            var inRange  = MakeNode(maxEV:  50f, currentEV: 25f);

            Tick();

            Assert.AreEqual(100f, GetCurrentEV(overMax),  0.001f, "overMax entity");
            Assert.AreEqual(  0f, GetCurrentEV(belowMin), 0.001f, "belowMin entity");
            Assert.AreEqual( 25f, GetCurrentEV(inRange),  0.001f, "inRange entity");
        }

        // ── Proximity connection ──────────────────────────────────────────────

        private Entity MakeGenerator(int x, int y, float outputEV, float radius, float linkRadius = 0f)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new PowerNodeData
            {
                MaxEV = outputEV, CurrentEV = outputEV, InfluenceRadius = radius, LinkRadius = linkRadius
            });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, y) });
            return e;
        }

        private bool IsGridLinked(Entity e) => _em.GetComponentData<PowerNodeData>(e).IsGridLinked;

        private Entity MakeConsumer(int x, int y, float drawEV)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new PowerConsumer { DrawEV = drawEV });
            _em.AddComponentData(e, new PowerStatus  { IsConnected = 0, ThrottleRatio = 0f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(x, y) });
            return e;
        }

        private PowerStatus Status(Entity e) => _em.GetComponentData<PowerStatus>(e);

        private PowerGridState GridState()
        {
            using var q = _em.CreateEntityQuery(typeof(PowerGridState));
            return q.GetSingleton<PowerGridState>();
        }

        [Test]
        public void ConsumerWithinRadius_IsConnectedAtFullSpeed()
        {
            MakeGenerator(0, 0, outputEV: 50f, radius: 3f);
            var consumer = MakeConsumer(3, 0, drawEV: 10f); // square well inside the r3 power circle

            Tick();

            Assert.AreEqual(1, Status(consumer).IsConnected, "consumer within radius must be connected");
            Assert.AreEqual(1f, Status(consumer).ThrottleRatio, 0.001f, "supply covers draw -> full speed");
        }

        [Test]
        public void ConsumerSquarePartiallyOverlapsRadius_IsConnected()
        {
            // 1x1 generator radius 3 -> power circle Rc = 3.5. Cell (4,0)'s near edge sits at x=3.5, on the
            // ring: the square only partially reaches into the circle but must now count as within.
            MakeGenerator(0, 0, outputEV: 50f, radius: 3f);
            var consumer = MakeConsumer(4, 0, drawEV: 10f);

            Tick();

            Assert.AreEqual(1, Status(consumer).IsConnected,
                "a footprint that only partially overlaps the power area must be powered");
        }

        [Test]
        public void ConsumerFullyOutsideRadius_IsDisconnectedAndStalled()
        {
            MakeGenerator(0, 0, outputEV: 50f, radius: 3f);
            var consumer = MakeConsumer(5, 0, drawEV: 10f); // near edge x=4.5 > Rc 3.5 -> no overlap

            Tick();

            Assert.AreEqual(0, Status(consumer).IsConnected, "consumer outside radius must be disconnected");
            Assert.AreEqual(0f, Status(consumer).ThrottleRatio, 0.001f, "disconnected -> no power");
        }

        [Test]
        public void OverCapacity_ThrottlesConnectedConsumersProportionally()
        {
            MakeGenerator(0, 0, outputEV: 50f, radius: 5f);
            var a = MakeConsumer(1, 0, drawEV: 40f);
            var b = MakeConsumer(2, 0, drawEV: 40f); // total draw 80 > supply 50

            Tick();

            float expected = 50f / 80f;
            Assert.AreEqual(1, Status(a).IsConnected);
            Assert.AreEqual(1, Status(b).IsConnected);
            Assert.AreEqual(expected, Status(a).ThrottleRatio, 0.001f, "brownout throttle = supply/draw");
            Assert.AreEqual(expected, Status(b).ThrottleRatio, 0.001f, "all connected consumers share the throttle");
        }

        [Test]
        public void PowerDiscount_ReducesDrawAndEasesBrownout()
        {
            MakeGenerator(0, 0, outputEV: 30f, radius: 5f);
            var consumer = MakeConsumer(1, 0, drawEV: 40f); // 40 > 30 would brownout...
            // ...but a PowerDiscount manager keeps only 50% of the draw -> effective 20 <= 30.
            _em.AddComponentData(consumer, new ManagerAssignmentData
            {
                AppliedOutputMult = 1f, AppliedPowerMult = 0.5f
            });

            Tick();

            Assert.AreEqual(1f, Status(consumer).ThrottleRatio, 0.001f, "discounted draw fits supply -> full speed");
            Assert.AreEqual(20f, GridState().Draw, 0.001f, "draw must reflect the manager PowerDiscount");
        }

        // ── Power Relay (spreader: adds no eV, extends coverage) ───────────────

        [Test]
        public void RelayLinkedToGenerator_ExtendsCoverage_ConsumerReachableOnlyViaRelay_IsPowered()
        {
            // Generator far from the consumer (out of its small radius); a relay (MaxEV 0) sits next to the
            // consumer and re-radiates the shared pool's reach. The relay's link range reaches the generator
            // (gap 9), so it is grid-linked and its coverage counts.
            var gen   = MakeGenerator(0, 0, outputEV: 50f, radius: 1f);              // small reach, far away
            var relay = MakeGenerator(9, 0, outputEV:  0f, radius: 3f, linkRadius: 10f); // 0 output, wide radius, links back
            var consumer = MakeConsumer(10, 0, drawEV: 10f);  // gap 10 from gen (out), gap 1 from relay (in)

            Tick();

            Assert.AreEqual(1, IsGridLinked(gen)   ? 1 : 0, "the generator is always grid-linked");
            Assert.AreEqual(1, IsGridLinked(relay) ? 1 : 0, "the relay links back to the generator");
            Assert.AreEqual(1, Status(consumer).IsConnected, "relay coverage must connect the consumer");
            Assert.AreEqual(1f, Status(consumer).ThrottleRatio, 0.001f,
                "the generator's 50 eV covers the 10 eV draw carried through the relay");
            Assert.AreEqual(50f, GridState().Supply, 0.001f, "the relay adds nothing to supply");
        }

        [Test]
        public void StrandedRelay_OutOfLinkRange_ProvidesNoCoverage_ConsumerDisconnected()
        {
            // A relay whose link range does NOT reach the generator (gap 9, link range 3) is stranded: it is
            // not grid-linked, so it provides no coverage and the consumer under it gets no power at all.
            var gen   = MakeGenerator(0, 0, outputEV: 50f, radius: 1f, linkRadius: 3f);
            var relay = MakeGenerator(9, 0, outputEV:  0f, radius: 3f, linkRadius: 3f);
            var consumer = MakeConsumer(10, 0, drawEV: 10f);

            Tick();

            Assert.AreEqual(1, IsGridLinked(gen)   ? 1 : 0, "the generator is always grid-linked");
            Assert.AreEqual(0, IsGridLinked(relay) ? 1 : 0, "a relay out of link range must NOT be grid-linked");
            Assert.AreEqual(0, Status(consumer).IsConnected,
                "a stranded relay provides no coverage, so the consumer is disconnected");
            Assert.AreEqual(0f, Status(consumer).ThrottleRatio, 0.001f, "disconnected -> no power");
        }

        [Test]
        public void RelayWithNoGenerator_ProvidesNoCoverage_ConsumerDisconnected()
        {
            var relay = MakeGenerator(0, 0, outputEV: 0f, radius: 3f, linkRadius: 6f); // relay only, no real source
            var consumer = MakeConsumer(1, 0, drawEV: 10f);

            Tick();

            Assert.AreEqual(0, IsGridLinked(relay) ? 1 : 0,
                "a relay with no generator anywhere in the grid is never linked");
            Assert.AreEqual(0, Status(consumer).IsConnected,
                "an unlinked relay provides no coverage, so the consumer is disconnected");
            Assert.AreEqual(0f, Status(consumer).ThrottleRatio, 0.001f,
                "supply is 0 with no generator, and the stranded relay covers nothing");
            Assert.AreEqual(0f, GridState().Supply, 0.001f);
        }

        [Test]
        public void GridState_ReportsSupplyDrawAndCounts()
        {
            MakeGenerator(0, 0, outputEV: 50f, radius: 3f);
            MakeConsumer(1, 0, drawEV: 10f); // connected
            MakeConsumer(9, 9, drawEV: 10f); // far -> disconnected

            Tick();

            var state = GridState();
            Assert.AreEqual(50f, state.Supply, 0.001f);
            Assert.AreEqual(10f, state.Draw,   0.001f, "only connected consumers add to draw");
            Assert.AreEqual(1, state.ConnectedCount);
            Assert.AreEqual(1, state.DisconnectedCount);
            Assert.AreEqual(1f, state.Ratio, 0.001f);
        }

        [Test]
        public void GridState_ReportsPowerNodeCounts_LinkedVsStranded()
        {
            MakeGenerator(0, 0, outputEV: 50f, radius: 2f, linkRadius: 6f);  // generator (always linked)
            MakeGenerator(5, 0, outputEV:  0f, radius: 3f, linkRadius: 6f);  // relay linked to the generator
            MakeGenerator(30, 0, outputEV: 0f, radius: 3f, linkRadius: 6f);  // stranded relay (far away)

            Tick();

            var state = GridState();
            Assert.AreEqual(3, state.TotalNodeCount,  "all three power nodes are counted");
            Assert.AreEqual(2, state.LinkedNodeCount, "the generator + the near relay are grid-linked; the far relay is not");
        }
    }
}
