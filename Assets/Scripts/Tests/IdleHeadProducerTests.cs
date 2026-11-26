using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #535: Time Warp (and offline return) delivered nothing to the Demon on a working factory. A belt's head
    /// touched two harvesters (a pair side by side), and the offline graph credited the belt to whichever it found
    /// first, not the one whose output faces it, as ConveyorSystem pulls live. The synth that belt feeds then
    /// looked starved, its inputs were sold as surplus, and no product reached the Demon.
    /// </summary>
    [TestFixture]
    public class IdleHeadProducerTests
    {
        const int Collector = 1, Demon = 9;
        const int Quark = 1, Electron = 3;
        const int North = (int)OutputDirection.North, South = (int)OutputDirection.South;

        static GridSaveData Grid()
        {
            // Quark harvester at (0,0), west of the head, output facing north (onto its own belt elsewhere).
            // Electron harvester at (1,1), north of the head, output facing south onto the head (1,0).
            // Belt (1,0) (2,0) -> Demon at (3,0).
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = Collector, recipeId = Quark,    position = new[] { 0, 0 }, outputDirection = North });
            grid.buildings.Add(new BuildingSaveData { buildingId = Collector, recipeId = Electron, position = new[] { 1, 1 }, outputDirection = South });
            grid.buildings.Add(new BuildingSaveData { buildingId = Demon, recipeId = -1, position = new[] { 3, 0 }, outputDirection = -1 });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 1, 0, 2, 0 } });
            return grid;
        }

        static IEnumerable<(Vector2Int, int)> Ports(BuildingSaveData b)
        {
            if (b.buildingId == Collector) yield return (new Vector2Int(b.position[0], b.position[1]), b.outputDirection);
        }

        static Dictionary<int, float> Delivered(bool withPorts)
        {
            var snap = IdleGraphAnalyzer.BuildSnapshot(Grid(),
                id => id == Collector, id => id == Demon, _ => Vector2Int.one, (_, recipe) => recipe, _ => 2f, _ => 1f,
                1f, "t", getOutputPorts: withPorts ? Ports : null);
            return snap.chains.Where(c => c.endsAtEntropySink && !c.notDelivered)
                       .GroupBy(c => c.itemId).ToDictionary(g => g.Key, g => g.Sum(c => c.itemsPerSecond));
        }

        [Test]
        public void TheBeltIsFedByTheHarvesterFacingIt()
        {
            var delivered = Delivered(withPorts: true);
            Assert.AreEqual(2f, delivered.TryGetValue(Electron, out var e) ? e : 0f, 1e-4f, "electrons reach the Demon");
            Assert.IsFalse(delivered.ContainsKey(Quark), "the quark harvester faces away from this belt");
        }

        [Test]
        public void WithoutPortData_AnAdjacentProducerStillFeeds() =>
            Assert.AreEqual(2f, Delivered(withPorts: false).Values.Sum(), 1e-4f);

        // ── GridSaveService.OutputPorts ───────────────────────────────────

        static BuildingSO Building(PlacementRule rule, params BuildingPort[] ports)
        {
            var b = ScriptableObject.CreateInstance<BuildingSO>();
            b.placementRule = rule;
            b.ports = ports;
            return b;
        }

        [Test]
        public void AHarvesterReportsItsSavedOutputDirection()
        {
            var so = Building(PlacementRule.MustBeOnField);
            var ports = GridSaveService.OutputPorts(
                new BuildingSaveData { position = new[] { 6, 8 }, outputDirection = North }, so).ToList();
            Object.DestroyImmediate(so);
            CollectionAssert.AreEqual(new[] { (new Vector2Int(6, 8), North) }, ports);
        }

        [Test]
        public void ABuildingWithPorts_IgnoresAnOldSavesZeroOutputDirection()
        {
            var port = new BuildingPort { portType = PortType.Output, localCell = Vector2Int.zero, localFacing = (OutputDirection)South };
            var so = Building(PlacementRule.Anywhere, port);
            so.footprint = Vector2Int.one;
            var ports = GridSaveService.OutputPorts(
                new BuildingSaveData { position = new[] { 2, 2 }, outputDirection = 0 }, so).ToList();
            Object.DestroyImmediate(so);
            Assert.AreEqual(1, ports.Count);
            Assert.AreEqual(South, ports[0].facing);
        }
    }
}
