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
    /// <summary>#321: the Filter sends each item of a mixed belt out of the side its rules pick.</summary>
    [TestFixture]
    public class FilterTests
    {
        const int North = 0, East = 1, South = 2;
        const int Helium = 7, Deuterium = 14, Carbon = 9;

        // ── Rules ────────────────────────────────────────────────────────────

        [Test]
        public void Route_NamedItemWins_OthersRideEverythingElse_NoRuleWaits()
        {
            var rules = new List<(int, int)> { (East, Helium), (North, PortFilterRules.Everything) };
            Assert.AreEqual(East,  PortFilterRules.Route(rules, Helium));
            Assert.AreEqual(North, PortFilterRules.Route(rules, Deuterium));

            var strict = new List<(int, int)> { (East, Helium) };
            Assert.AreEqual(PortFilterRules.NoRoute, PortFilterRules.Route(strict, Deuterium), "no catch-all: it waits");
        }

        [Test]
        public void Assign_MovesAnItemToOneSideOnly_AndWaitClearsIt()
        {
            var rules = PortFilterRules.Default(East);
            PortFilterRules.Assign(rules, Helium, North);
            PortFilterRules.Assign(rules, Helium, South);
            Assert.AreEqual(1, rules.Count(r => r.itemId == Helium));
            Assert.AreEqual(South, PortFilterRules.Route(rules, Helium));

            PortFilterRules.Assign(rules, Helium, PortFilterRules.NoRoute);
            Assert.AreEqual(East, PortFilterRules.Route(rules, Helium), "cleared: back to everything else");
        }

        [Test]
        public void FlattenAndParse_RoundTrip()
        {
            var rules = new List<(int, int)> { (East, Helium), (North, PortFilterRules.Everything) };
            CollectionAssert.AreEqual(rules, PortFilterRules.Parse(PortFilterRules.Flatten(rules)));
            Assert.IsNull(PortFilterRules.Flatten(new List<(int, int)>()));
            Assert.IsEmpty(PortFilterRules.Parse(null));
        }

        // ── Live: FilterSystem + ConveyorSystem ──────────────────────────────

        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("FilterTest");
            _em    = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystemManaged<FilterSystem>());
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

        /// <summary>A Filter at (1,0): fed from the west (input facing East), outputs North, East and South.</summary>
        Entity CreateFilter(params (int facing, int item)[] rules)
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddComponentData(e, new GridPosition { Cell = new int2(1, 0) });
            _em.AddComponentData(e, new BuildingInventoryConfig { InputCapacity = 10, OutputCapacity = 10 });
            _em.AddComponent<ItemFilterTag>(e);
            _em.AddBuffer<BuildingInputSlot>(e);
            _em.AddBuffer<BuildingOutputSlot>(e);
            _em.AddBuffer<FilterSeenItem>(e);
            _em.AddBuffer<OutputPortFilter>(e);
            _em.AddBuffer<PlacedPortData>(e);

            var ports = _em.GetBuffer<PlacedPortData>(e);
            ports.Add(new PlacedPortData { PortType = (int)PortType.Input, CellX = 1, CellY = 0, Facing = East });
            foreach (int f in new[] { North, East, South })
                ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 1, CellY = 0, Facing = f });
            FilterSystem.WriteRules(_em.GetBuffer<OutputPortFilter>(e), rules.ToList());
            return e;
        }

        /// <summary>An empty one-cell belt at <paramref name="cell"/>, ready to take an item.</summary>
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

        int ItemOn(Entity seg) => _em.HasComponent<ConveyorItemData>(seg) ? _em.GetComponentData<ConveyorItemData>(seg).ItemID : -1;

        [Test]
        public void MixedInput_EachSideGetsOnlyItsItems()
        {
            var filter = CreateFilter((East, Helium), (North, PortFilterRules.Everything));
            var input  = _em.GetBuffer<BuildingInputSlot>(filter);
            SlotBufferUtils.AddToInputBuffer(input, Helium, 1);
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(filter), Deuterium, 1);

            var east  = CreateHead(new int2(2, 0), East);
            var north = CreateHead(new int2(1, 1), North);
            var south = CreateHead(new int2(1, -1), South);

            Tick();

            Assert.AreEqual(Helium,    ItemOn(east),  "Helium is named for the East side");
            Assert.AreEqual(Deuterium, ItemOn(north), "Deuterium rides 'everything else' (North)");
            Assert.AreEqual(-1,        ItemOn(south), "South has no rule and gets nothing");
        }

        [Test]
        public void ItemNoSideTakes_WaitsInTheInput()
        {
            var filter = CreateFilter((East, Helium));
            SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(filter), Carbon, 3);
            var east = CreateHead(new int2(2, 0), East);

            Tick();

            Assert.AreEqual(-1, ItemOn(east));
            Assert.AreEqual(3, SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(filter), Carbon),
                "the Filter never deletes an item it can't route");
            Assert.AreEqual(Carbon, _em.GetBuffer<FilterSeenItem>(filter)[0].ItemID, "but it lists it so the player can assign it");
        }

        // ── Offline: the Filter is see-through ───────────────────────────────

        [Test]
        public void IdleGraph_RoutesEachProducersItemThroughTheFilter()
        {
            // Collector A (item 10) and B (item 20) both belt into a Filter at (5,5). 10 -> East -> Demon,
            // 20 -> North -> a belt to nowhere (inventory).
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 10, position = new[] { 0, 5 } },
                    new BuildingSaveData { buildingId = 1, recipeId = 20, position = new[] { 5, 0 } },
                    new BuildingSaveData { buildingId = 9, recipeId = -1, position = new[] { 5, 5 },
                                           portFilters = new[] { East, 10, North, 20 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 9, 5 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,5, 2,5, 3,5, 4,5 } },
                    new ConveyorSaveData { cells = new[] { 5,1, 5,2, 5,3, 5,4 } },
                    new ConveyorSaveData { cells = new[] { 6,5, 7,5, 8,5 } },
                    new ConveyorSaveData { cells = new[] { 5,6, 5,7, 5,8 } },
                }
            };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid, id => id == 1, id => id == 2, _ => Vector2Int.one, (_, recipe) => recipe,
                _ => 1f, _ => 4f, 1f, DateTime.UtcNow.ToString("O"), isFilter: id => id == 9);

            Assert.AreEqual(2, snap.chains.Count);
            Assert.IsTrue(snap.chains.Single(c => c.itemId == 10).endsAtEntropySink, "10 is filtered East into the Demon");
            Assert.IsFalse(snap.chains.Single(c => c.itemId == 20).endsAtEntropySink, "20 is filtered North to a belt to nowhere");
        }

        [Test]
        public void IdleGraph_ItemTheFilterWontTake_EarnsNothing()
        {
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 20, position = new[] { 0, 5 } },
                    new BuildingSaveData { buildingId = 9, recipeId = -1, position = new[] { 5, 5 },
                                           portFilters = new[] { East, 10 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 9, 5 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,5, 2,5, 3,5, 4,5 } },
                    new ConveyorSaveData { cells = new[] { 6,5, 7,5, 8,5 } },
                }
            };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid, id => id == 1, id => id == 2, _ => Vector2Int.one, (_, recipe) => recipe,
                _ => 1f, _ => 4f, 1f, DateTime.UtcNow.ToString("O"), isFilter: id => id == 9);

            Assert.IsEmpty(snap.chains, "the item waits on its belt, as it does live");
        }

        // ── Data ─────────────────────────────────────────────────────────────

        [Serializable] class GdPort     { public string port_type; public string local_facing; }
        [Serializable] class GdBuilding { public string id; public bool is_item_filter; public bool requires_power; public string required_research; public List<GdPort> ports; }
        [Serializable] class GameData   { public List<GdBuilding> buildings; }

        [Test]
        public void Data_FilterHasOneInputAndThreeOutputs()
        {
            var gd = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            var f  = gd.buildings.Single(b => b.id == "filter");
            Assert.IsTrue(f.is_item_filter);
            Assert.IsFalse(f.requires_power);
            Assert.AreEqual(1, f.ports.Count(p => p.port_type == "Input"));
            Assert.AreEqual(3, f.ports.Count(p => p.port_type == "Output"));
            Assert.AreEqual("automation_i", f.required_research);
        }
    }
}
