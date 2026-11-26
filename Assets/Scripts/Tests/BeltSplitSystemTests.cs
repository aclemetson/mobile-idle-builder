using System.Collections.Generic;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The live <see cref="ConveyorSystem"/> on a belt split, in a scratch ECS world: items fed onto a split tile
    /// leave by its output sides in the weighted share, travel down each branch, and none are lost or duplicated.
    /// </summary>
    public class BeltSplitSystemTests
    {
        const int N = 0, E = 1;
        World _world;
        EntityManager _em;
        ConveyorSystem _system;
        readonly Dictionary<int2, Entity> _cells = new();

        [SetUp]
        public void SetUp()
        {
            _world  = new World("BeltSplitSystemTests");
            _em     = _world.EntityManager;
            _system = _world.GetOrCreateSystemManaged<ConveyorSystem>();
            _cells.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        Entity Belt(int x, int y, int exit)
        {
            var e = _em.CreateEntity(typeof(ConveyorSegmentData), typeof(GridPosition), typeof(ConveyorItem));
            _em.SetComponentData(e, new GridPosition { Cell = new int2(x, y) });
            _em.SetComponentData(e, new ConveyorSegmentData { Cell = new int2(x, y), EntryDir = exit, ExitDir = exit, TransportTime = 1f });
            _cells[new int2(x, y)] = e;
            return e;
        }

        // Links every belt the way ConveyorPlacer.RelinkAll does, from the pure topology.
        void Relink()
        {
            var exits  = new Dictionary<int2, int>();
            var splits = new Dictionary<int2, int>();
            foreach (var kv in _cells)
            {
                exits[kv.Key] = _em.GetComponentData<ConveyorSegmentData>(kv.Value).ExitDir;
                if (_em.HasComponent<BeltSplit>(kv.Value)) splits[kv.Key] = _em.GetComponentData<BeltSplit>(kv.Value).Mask;
            }
            var links = ConveyorTopology.Compute(exits, null, splits);
            foreach (var kv in _cells)
            {
                var s = _em.GetComponentData<ConveyorSegmentData>(kv.Value);
                var l = links[kv.Key];
                s.NextSegment = l.HasOutput ? _cells[l.OutputCell] : Entity.Null;
                s.IsChainHead = l.IsHead;
                s.PrevSegment = l.HasPrimaryInbound ? _cells[l.PrimaryInboundCell] : Entity.Null;
                s.EntryDir    = l.IsHead ? s.ExitDir : l.PrimaryInboundExitDir;
                _em.SetComponentData(kv.Value, s);
            }
        }

        int ItemsOn(IEnumerable<Entity> segs)
        {
            int n = 0;
            foreach (var e in segs) n += _em.GetBuffer<ConveyorItem>(e).Length;
            return n;
        }

        [Test]
        public void ItemsLeaveBySide_InTheWeightedShare_NoneLost()
        {
            // Split at (0,0): east 2, north 1. East branch (1,0)..(6,0); north branch (0,1)..(0,6).
            var split = Belt(0, 0, E);
            var east  = new List<Entity>();
            var north = new List<Entity>();
            for (int i = 1; i <= 6; i++) east.Add(Belt(i, 0, E));
            for (int i = 1; i <= 6; i++) north.Add(Belt(0, i, N));
            var bs = new BeltSplit { Mask = (1 << E) | (1 << N) };
            bs.SetWeight(E, 2);
            bs.SetWeight(N, 1);
            _em.AddComponentData(split, bs);
            Relink();
            Assert.IsFalse(_em.GetComponentData<ConveyorSegmentData>(north[0]).IsChainHead, "the branch is fed by the split");

            const int total = 9;
            const float dt = 0.05f;
            int fed = 0;
            double time = 0;
            for (int frame = 0; frame < 900; frame++)
            {
                var buf = _em.GetBuffer<ConveyorItem>(split);
                bool room = buf.Length == 0 || buf[buf.Length - 1].Progress >= 0.5f;
                if (fed < total && room)
                {
                    buf.Add(new ConveyorItem { ItemID = 7, Progress = 0f });
                    fed++;
                }
                time += dt;
                _world.SetTime(new TimeData(time, dt));
                _system.Update();
            }

            Assert.AreEqual(total, fed);
            Assert.AreEqual(0, _em.GetBuffer<ConveyorItem>(split).Length, "the split emptied");
            Assert.AreEqual(6, ItemsOn(east), "two thirds went east");
            Assert.AreEqual(3, ItemsOn(north), "one third went north");
        }

        [Test]
        public void ABackedUpBranch_SendsEverythingToTheOther()
        {
            // North branch is a single tile already full; the split keeps flowing east.
            var split = Belt(0, 0, E);
            var east  = new List<Entity>();
            for (int i = 1; i <= 8; i++) east.Add(Belt(i, 0, E));
            var northTile = Belt(0, 1, N);
            var nb = _em.GetBuffer<ConveyorItem>(northTile);
            nb.Add(new ConveyorItem { ItemID = 3, Progress = 0.9f });
            nb.Add(new ConveyorItem { ItemID = 3, Progress = 0.4f });
            var bs = new BeltSplit { Mask = (1 << E) | (1 << N) };
            bs.SetWeight(E, 1);
            bs.SetWeight(N, 1);
            _em.AddComponentData(split, bs);
            Relink();

            const float dt = 0.05f;
            int fed = 0;
            double time = 0;
            for (int frame = 0; frame < 600; frame++)
            {
                var buf = _em.GetBuffer<ConveyorItem>(split);
                if (fed < 6 && (buf.Length == 0 || buf[buf.Length - 1].Progress >= 0.5f))
                {
                    buf.Add(new ConveyorItem { ItemID = 7, Progress = 0f });
                    fed++;
                }
                time += dt;
                _world.SetTime(new TimeData(time, dt));
                _system.Update();
            }

            Assert.AreEqual(6, ItemsOn(east), "the full branch never blocked the split");
            Assert.AreEqual(2, _em.GetBuffer<ConveyorItem>(northTile).Length, "nothing squeezed onto the full branch");
        }
    }
}
