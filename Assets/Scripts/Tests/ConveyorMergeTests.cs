using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for ConveyorSystem's merge handling: when 2-3 segments feed one cell, only
    /// one item enters per frame and the inbound directions take turns (round-robin via MergeCursor)
    /// so no branch starves.
    /// </summary>
    [TestFixture]
    public class ConveyorMergeTests
    {
        private World         _world;
        private EntityManager _em;

        private const int North = (int)OutputDirection.North;
        private const int East  = (int)OutputDirection.East;
        private const int South = (int)OutputDirection.South;
        private const int West  = (int)OutputDirection.West;

        private const int ItemId = 5;

        [SetUp]
        public void SetUp()
        {
            _world = new World("ConveyorMergeTest");
            _em    = _world.EntityManager;
            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            simGroup.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        private void Tick(float dt = 1f)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: dt));
            _world.Update();
        }

        private Entity Seg(int2 cell, int exitDir, Entity next, int cursor = 0)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell          = cell,
                EntryDir      = exitDir,
                ExitDir       = exitDir,
                NextSegment   = next,
                PrevSegment   = Entity.Null,
                TransportTime = 1f,
                IsChainHead   = false,
                MergeCursor   = cursor
            });
            return e;
        }

        private void PutItem(Entity e, float progress = 1f) =>
            _em.AddComponentData(e, new ConveyorItemData { ItemID = ItemId, Progress = progress });

        private bool HasItem(Entity e) => _em.HasComponent<ConveyorItemData>(e);

        // ── Two inbounds alternate (round-robin) ─────────────────────────────

        [Test]
        public void TwoInbounds_TakeTurns_NoBranchStarves()
        {
            var merge = Seg(new int2(1, 0), East, Entity.Null);          // tail merge cell
            var west  = Seg(new int2(0, 0), East,  merge);               // flows E into merge
            var north = Seg(new int2(1, 1), South, merge);               // flows S into merge
            PutItem(west);
            PutItem(north);

            // Cursor 0 scans N first → the north branch goes first.
            Tick();
            Assert.IsTrue(HasItem(merge),  "an item entered the merge cell");
            Assert.IsFalse(HasItem(north), "the north branch delivered this frame");
            Assert.IsTrue(HasItem(west),   "the west branch waits its turn");
            Assert.AreEqual(1, _em.GetComponentData<ConveyorSegmentData>(merge).MergeCursor,
                "the merge cursor advanced past the branch it served");

            // Drain the merge cell and re-arm the served branch; the OTHER branch must go next.
            _em.RemoveComponent<ConveyorItemData>(merge);
            PutItem(north);

            Tick();
            Assert.IsTrue(HasItem(merge));
            Assert.IsFalse(HasItem(west),  "the west branch is served on the second turn");
            Assert.IsTrue(HasItem(north),  "the north branch now waits");
        }

        // ── Three inbounds: cell accepts from a third side ───────────────────

        [Test]
        public void ThreeInbounds_AcceptOnePerFrame()
        {
            var merge = Seg(new int2(1, 0), South, Entity.Null);         // tail merge cell, 3 inbounds
            var west  = Seg(new int2(0, 0), East,  merge);
            var north = Seg(new int2(1, 1), South, merge);
            var east  = Seg(new int2(2, 0), West,  merge);
            PutItem(west);
            PutItem(north);
            PutItem(east);

            Tick();

            Assert.IsTrue(HasItem(merge), "the 3-input merge cell accepted an item");
            int delivered = (HasItem(west) ? 0 : 1) + (HasItem(north) ? 0 : 1) + (HasItem(east) ? 0 : 1);
            Assert.AreEqual(1, delivered, "exactly one branch delivers per frame");
            Assert.IsFalse(HasItem(north), "cursor 0 serves the north branch first");
        }
    }
}
