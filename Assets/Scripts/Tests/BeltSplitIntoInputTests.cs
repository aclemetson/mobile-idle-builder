using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A split placed right in front of a building's input used to send everything down its belt branches: only
    /// sides with a belt counted as outputs, so the building got nothing. A side facing an input now takes its
    /// share (straight into the input, as at a belt's tail), and the offline graph counts it too.
    /// </summary>
    [TestFixture]
    public class BeltSplitIntoInputTests
    {
        World _world;
        EntityManager _em;

        const int North = (int)OutputDirection.North, East = (int)OutputDirection.East;
        const int South = (int)OutputDirection.South, West = (int)OutputDirection.West;
        const int ItemId = 5;

        [SetUp]
        public void SetUp()
        {
            _world = new World("BeltSplitIntoInput");
            _em    = _world.EntityManager;
            var group = _world.CreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            group.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        void Tick(float dt = 1f)
        {
            _world.SetTime(new TimeData(elapsedTime: 0, deltaTime: dt));
            _world.Update();
        }

        Entity Seg(int2 cell, int exitDir, Entity next)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell = cell, EntryDir = exitDir, ExitDir = exitDir, NextSegment = next,
                PrevSegment = Entity.Null, TransportTime = 1f,
            });
            _em.AddBuffer<ConveyorItem>(e);
            return e;
        }

        /// <summary>A building at <paramref name="cell"/> whose input faces <paramref name="facing"/> (belt toward it).</summary>
        Entity BuildingWithInput(int2 cell, int facing, int capacity = 50)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = cell });
            _em.AddBuffer<PlacedPortData>(e).Add(new PlacedPortData
            {
                PortType = (int)PortType.Input, CellX = cell.x, CellY = cell.y, Facing = facing,
            });
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddComponentData(e, new BuildingInventoryConfig { InputCapacity = capacity, OutputCapacity = 10 });
            return e;
        }

        int InputCount(Entity b) => SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(b), ItemId);

        // Split at (1,0): its straight side (East) is a building's input at (2,0); its South side a belt at (1,-1).
        (Entity split, Entity branch, Entity building) Layout()
        {
            var branch   = Seg(new int2(1, -1), South, Entity.Null);
            var split    = Seg(new int2(1, 0), East, Entity.Null);
            var building = BuildingWithInput(new int2(2, 0), East);
            var s = new BeltSplit { Mask = (byte)((1 << East) | (1 << South)) };
            s.SetWeight(East, 1);
            s.SetWeight(South, 1);
            _em.AddComponentData(split, s);
            return (split, branch, building);
        }

        [Test]
        public void ASplitInFrontOfAnInput_SharesWithTheBuilding()
        {
            var (split, branch, building) = Layout();
            int toBuilding = 0, toBranch = 0;
            for (int i = 0; i < 6; i++)
            {
                ConveyorItems.Clear(_em, branch);
                int before = InputCount(building);
                ConveyorItems.Add(_em, split, ItemId, 1f);
                Tick();
                if (InputCount(building) > before) toBuilding++;
                if (ConveyorItems.Has(_em, branch)) toBranch++;
            }
            Assert.AreEqual(3, toBuilding, "even weights: half the items go into the building");
            Assert.AreEqual(3, toBranch, "and half down the belt branch");
        }

        [Test]
        public void AFullInput_SendsItsTurnDownTheBranch()
        {
            var (split, branch, building) = Layout();
            _em.SetComponentData(building, new BuildingInventoryConfig { InputCapacity = 0, OutputCapacity = 10 });

            for (int i = 0; i < 4; i++)
            {
                ConveyorItems.Clear(_em, branch);
                ConveyorItems.Add(_em, split, ItemId, 1f);
                Tick();
                Assert.IsTrue(ConveyorItems.Has(_em, branch), $"item {i}: the branch takes it while the input is full");
            }
            Assert.AreEqual(0, InputCount(building));
        }

        [Test]
        public void Offline_ASplitInFrontOfAnInput_SharesTheFlow()
        {
            // Collector (id 1) at (0,0) -> belt (1,0) (2,0); the split at (2,0) faces a Demon (id 9) at (3,0) on its
            // East side and a branch (2,-1) (2,-2) to a second Demon at (2,-3) on its South side.
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = 1, position = new[] { 0, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 3, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 2, -3 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 1, 0, 2, 0 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 2, -1, 2, -2 } });
            grid.splits = BeltSplitRules.Flatten(new List<BeltSplitData>
            {
                new BeltSplitData { X = 2, Y = 0, Mask = (1 << East) | (1 << South), W1 = 1, W2 = 1 },
            });

            var snap = IdleGraphAnalyzer.BuildSnapshot(grid,
                id => id == 1, id => id == 9, _ => Vector2Int.one, (_, __) => 7, _ => 4f, _ => 1f, 1f, "t");

            var flows = snap.chains.Where(c => c.endsAtEntropySink).Select(c => c.itemsPerSecond).OrderBy(x => x).ToList();
            Assert.AreEqual(2, flows.Count, "both Demons are fed");
            Assert.AreEqual(2f, flows[0], 1e-4f);
            Assert.AreEqual(2f, flows[1], 1e-4f, "the Demon in front of the split gets its half");
        }
    }
}
