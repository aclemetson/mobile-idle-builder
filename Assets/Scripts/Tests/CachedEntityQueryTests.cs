using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// CachedEntityQuery (#285): one query per world, reused across calls, rebuilt when the world
    /// changes or has been disposed.
    /// </summary>
    [TestFixture]
    public class CachedEntityQueryTests
    {
        World _a, _b;

        [TearDown]
        public void TearDown()
        {
            if (_a != null && _a.IsCreated) _a.Dispose();
            if (_b != null && _b.IsCreated) _b.Dispose();
            _a = _b = null;
        }

        [Test]
        public void SameWorld_ReturnsTheSameQuery()
        {
            _a = new World("CachedQueryA");
            var cache = new CachedEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());

            var q1 = cache.For(_a.EntityManager);
            var q2 = cache.For(_a.EntityManager);

            Assert.IsTrue(q1 == q2, "Repeated calls in one world must reuse the query, not create new ones");
        }

        [Test]
        public void SeesEntitiesCreatedAfterTheQuery()
        {
            _a = new World("CachedQueryA");
            var cache = new CachedEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            Assert.AreEqual(0, cache.For(_a.EntityManager).CalculateEntityCount());

            _a.EntityManager.CreateEntity(typeof(PlayerProgressData));
            Assert.AreEqual(1, cache.For(_a.EntityManager).CalculateEntityCount());
        }

        [Test]
        public void AfterWorldDisposed_NewWorldGetsAWorkingQuery()
        {
            var cache = new CachedEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());

            _a = new World("CachedQueryA");
            _a.EntityManager.CreateEntity(typeof(PlayerProgressData));
            Assert.AreEqual(1, cache.For(_a.EntityManager).CalculateEntityCount());
            _a.Dispose();

            _b = new World("CachedQueryB");
            _b.EntityManager.CreateEntity(typeof(PlayerProgressData));
            _b.EntityManager.CreateEntity(typeof(PlayerProgressData));
            Assert.AreEqual(2, cache.For(_b.EntityManager).CalculateEntityCount(),
                "The cache must rebuild for the new world, not reuse the disposed world's query");
        }

        [Test]
        public void TwoLiveWorlds_EachGetsItsOwnResults()
        {
            var cache = new CachedEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            _a = new World("CachedQueryA");
            _b = new World("CachedQueryB");
            _a.EntityManager.CreateEntity(typeof(PlayerProgressData));

            Assert.AreEqual(1, cache.For(_a.EntityManager).CalculateEntityCount());
            Assert.AreEqual(0, cache.For(_b.EntityManager).CalculateEntityCount());
            Assert.AreEqual(1, cache.For(_a.EntityManager).CalculateEntityCount());
        }
    }
}
