using NUnit.Framework;
using Unity.Entities;

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
    }
}
