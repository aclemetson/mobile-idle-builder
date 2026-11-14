using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The Belt Splitter deals every item out to its output belts in turn (1 in, 2 or 3 out).</summary>
    [TestFixture]
    public class SplitterTests
    {
        const int North = 0, East = 1, South = 2;
        const int Proton = 5;

        // ── Rotation (pure) ──────────────────────────────────────────────────

        [Test]
        public void Pick_TakesTurns_AndSkipsSidesThatCantTakeAnItem()
        {
            var order = new List<int> { North, East, South };
            var all   = new HashSet<int> { North, East, South };

            Assert.AreEqual(North, SplitterRotation.Pick(order, -1, all), "no turn yet: first side");
            Assert.AreEqual(East,  SplitterRotation.Pick(order, East, all));
            Assert.AreEqual(South, SplitterRotation.Pick(order, East, new HashSet<int> { North, South }),
                "East's belt is full: the turn goes on to South");
            Assert.AreEqual(North, SplitterRotation.Pick(order, East, new HashSet<int> { North }), "wraps around");
            Assert.AreEqual(-1,    SplitterRotation.Pick(order, North, new HashSet<int>()), "nobody ready");
        }

        [Test]
        public void After_PassesTheTurnOn_Wrapping()
        {
            var order = new List<int> { North, South };
            Assert.AreEqual(South, SplitterRotation.After(order, North));
            Assert.AreEqual(North, SplitterRotation.After(order, South));
        }

        // ── Live: SplitterSystem + ConveyorSystem ────────────────────────────

        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("SplitterTest");
            _em    = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystemManaged<SplitterSystem>());
            sim.AddSystemToUpdateList(_world.CreateSystem<ConveyorSystem>());
            sim.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        void Tick()
        {
            _world.SetTime(new TimeData(0, 0.1f));
            _world.Update();
        }

        /// <summary>A Splitter at (1,0) fed from the west (input facing East) with the given output sides.</summary>
        Entity CreateSplitter(params int[] outputs)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(1, 0) });
            _em.AddComponentData(e, new BuildingInventoryConfig { InputCapacity = 10, OutputCapacity = 10 });
            _em.AddComponent<SplitterTag>(e);
            _em.AddComponentData(e, new SplitterState { NextFacing = -1 });
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddBuffer<PlacedPortData>(e);

            var ports = _em.GetBuffer<PlacedPortData>(e);
            ports.Add(new PlacedPortData { PortType = (int)PortType.Input, CellX = 1, CellY = 0, Facing = East });
            foreach (int f in outputs)
                ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 1, CellY = 0, Facing = f });
            return e;
        }

        Entity CreateHead(int2 cell, int exitDir)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new ConveyorSegmentData
            {
                Cell = cell, EntryDir = exitDir, ExitDir = exitDir, TransportTime = 1f, IsChainHead = true,
                NextSegment = Entity.Null, PrevSegment = Entity.Null
            });
            return e;
        }

        bool HasItem(Entity seg) => ConveyorItems.Has(_em, seg);
        void Clear(Entity seg) { if (HasItem(seg)) ConveyorItems.Clear(_em, seg); }

        [Test]
        public void TwoWay_AlternatesBetweenLeftAndRight()
        {
            var splitter = CreateSplitter(North, South);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(splitter), Proton, 6);
            var north = CreateHead(new int2(1, 1), North);
            var south = CreateHead(new int2(1, -1), South);

            int n = 0, s = 0;
            for (int t = 0; t < 6; t++)
            {
                Tick();
                if (HasItem(north)) n++;
                if (HasItem(south)) s++;
                Clear(north);
                Clear(south);
            }

            Assert.AreEqual(3, n, "North gets every other item");
            Assert.AreEqual(3, s, "South gets every other item");
        }

        [Test]
        public void ThreeWay_SharesEvenly_AndSkipsAFullBranch()
        {
            var splitter = CreateSplitter(North, East, South);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(splitter), Proton, 9);
            var north = CreateHead(new int2(1, 1), North);
            var east  = CreateHead(new int2(2, 0), East);
            var south = CreateHead(new int2(1, -1), South);

            var counts = new Dictionary<Entity, int> { [north] = 0, [east] = 0, [south] = 0 };
            for (int t = 0; t < 9; t++)
            {
                Tick();
                foreach (var h in counts.Keys.ToList()) { if (HasItem(h)) counts[h]++; Clear(h); }
            }
            Assert.AreEqual(3, counts[north]);
            Assert.AreEqual(3, counts[east]);
            Assert.AreEqual(3, counts[south]);

            // East's belt stays full: the others keep flowing instead of the splitter stalling.
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(splitter), Proton, 4);
            ConveyorItems.Add(_em, east, Proton, 0f);
            int others = 0;
            for (int t = 0; t < 4; t++)
            {
                Tick();
                if (HasItem(north)) others++;
                if (HasItem(south)) others++;
                Clear(north);
                Clear(south);
            }
            Assert.AreEqual(4, others, "every item went to North or South while East was blocked");
        }

        // ── Offline: the flow is shared over the output belts ────────────────

        [Test]
        public void IdleGraph_SplitsAProducersFlowEvenly()
        {
            // Collector (item 10, 1/s) belts into a Splitter at (5,5); North belt -> Demon, South belt -> nowhere.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 10, position = new[] { 0, 5 } },
                    new BuildingSaveData { buildingId = 27, recipeId = -1, position = new[] { 5, 5 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 5, 9 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,5, 2,5, 3,5, 4,5 } },
                    new ConveyorSaveData { cells = new[] { 5,6, 5,7, 5,8 } },
                    new ConveyorSaveData { cells = new[] { 5,4, 5,3, 5,2 } },
                }
            };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid, id => id == 1, id => id == 2, _ => Vector2Int.one, (_, recipe) => recipe,
                _ => 1f, _ => 4f, 1f, DateTime.UtcNow.ToString("O"), isSplitter: id => id == 27);

            Assert.AreEqual(2, snap.chains.Count, "one chain per output belt");
            Assert.AreEqual(0.5f, snap.chains.Single(c => c.endsAtEntropySink).itemsPerSecond, 1e-4f, "half to the Demon");
            Assert.AreEqual(0.5f, snap.chains.Single(c => !c.endsAtEntropySink).itemsPerSecond, 1e-4f, "half to inventory");
        }

        // ── Data ─────────────────────────────────────────────────────────────

        [Serializable] class GdPort     { public string port_type; }
        [Serializable] class GdBuilding { public string id; public bool is_splitter; public bool requires_power; public string required_research; public List<GdPort> ports; }
        [Serializable] class GdResearch { public string id; public List<string> unlocks_buildings; }
        [Serializable] class GameData   { public List<GdBuilding> buildings; public List<GdResearch> research; }

        [TestCase("splitter_2", 2)]
        [TestCase("splitter_3", 3)]
        public void Data_SplitterHasOneInputAndItsOutputs(string id, int outputs)
        {
            var gd = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            var b  = gd.buildings.Single(x => x.id == id);
            Assert.IsTrue(b.is_splitter);
            Assert.IsFalse(b.requires_power);
            Assert.AreEqual(1, b.ports.Count(p => p.port_type == "Input"));
            Assert.AreEqual(outputs, b.ports.Count(p => p.port_type == "Output"));
            Assert.AreEqual("automation_i", b.required_research);
            CollectionAssert.Contains(gd.research.Single(r => r.id == "automation_i").unlocks_buildings, id);
        }
    }
}
