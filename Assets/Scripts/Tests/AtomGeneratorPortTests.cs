using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The 2x1 Atom Generator takes belts on its short ends as well as its long side (#306). Uses the
    /// imported BuildingSO ports (so a data change that drops an end intake fails here), places them the
    /// way BuildingPlacer does for each rotation, and runs ConveyorSystem to hand an item over.
    /// </summary>
    [TestFixture]
    public class AtomGeneratorPortTests
    {
        private const int ItemId = 7;
        private static readonly int2 Anchor = new(5, 5);

        private World         _world;
        private EntityManager _em;
        private BuildingSO    _atomGen;

        [SetUp]
        public void SetUp()
        {
            var db = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            Assert.IsNotNull(db, "BuildingDatabase must be imported");
            foreach (var b in db.allBuildings)
                if (b != null && b.id == "atomic_assembler") _atomGen = b;
            Assert.IsNotNull(_atomGen, "atomic_assembler missing from the BuildingDatabase");

            _world = new World("AtomGeneratorPortTest");
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

        // Mirrors BuildingPlacer: footprint and ports after rotation, anchored at the bottom-left cell.
        private Entity PlaceAtomGenerator(int rotation, out Vector2Int footprint)
        {
            var baseFp = _atomGen.footprint;
            footprint  = PortUtils.RotatedFootprint(baseFp, rotation);

            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = Anchor });
            _em.AddComponentData(e, new BuildingFootprint { Width = footprint.x, Height = footprint.y });
            var ports = _em.AddBuffer<PlacedPortData>(e);
            foreach (var port in _atomGen.ports)
            {
                var (rel, dir) = PortUtils.TransformPort(port, baseFp, flipped: false, rotation);
                ports.Add(new PlacedPortData
                {
                    PortType = (int)port.portType,
                    CellX    = Anchor.x + rel.x,
                    CellY    = Anchor.y + rel.y,
                    Facing   = (int)dir
                });
            }
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig { OutputCapacity = 10, InputCapacity = 10 });
            return e;
        }

        private void LoadedTail(int2 cell)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell = cell, EntryDir = 0, ExitDir = 0,
                NextSegment = Entity.Null, PrevSegment = Entity.Null,
                TransportTime = 1f, IsChainHead = true
            });
            ConveyorItems.Add(_em, e, ItemId, 1f);
        }

        private int Delivered(Entity building)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: 1f));
            _world.Update();
            return SlotBufferUtils.TotalInInputBuffer(_em.GetBuffer<BuildingInputSlot>(building));
        }

        // The cell just past each short end, along the long axis.
        private static int2 EndCell(Vector2Int fp, bool far)
        {
            bool horizontal = fp.x > fp.y;
            if (horizontal) return far ? new int2(Anchor.x + fp.x, Anchor.y) : new int2(Anchor.x - 1, Anchor.y);
            return far ? new int2(Anchor.x, Anchor.y + fp.y) : new int2(Anchor.x, Anchor.y - 1);
        }

        [Test]
        public void Footprint_Is2x1() => Assert.AreEqual(new Vector2Int(2, 1), _atomGen.footprint);

        [Test]
        public void BeltAtNearEnd_FeedsTheAtomGenerator([Values(0, 1, 2, 3)] int rotation)
        {
            var gen = PlaceAtomGenerator(rotation, out var fp);
            LoadedTail(EndCell(fp, far: false));
            Assert.AreEqual(1, Delivered(gen), $"rotation {rotation}: a belt at the near short end must feed an input");
        }

        [Test]
        public void BeltAtFarEnd_FeedsTheAtomGenerator([Values(0, 1, 2, 3)] int rotation)
        {
            var gen = PlaceAtomGenerator(rotation, out var fp);
            LoadedTail(EndCell(fp, far: true));
            Assert.AreEqual(1, Delivered(gen), $"rotation {rotation}: a belt at the far short end must feed an input");
        }
    }
}
