using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #206: a building whose output faces a belt running through its tile (a belt merged in at a harvester's
    /// exit) feeds it live (ConveyorSystem pass 4), so the offline graph must count that flow too.
    /// </summary>
    [TestFixture]
    public class IdleMergeFeedTests
    {
        const int Collector = 1, Demon = 9;
        const int South = (int)OutputDirection.South, East = (int)OutputDirection.East;

        static GridSaveData Grid()
        {
            // Collector A at (0,0) -> belt (1,0) (2,0) (3,0) -> Demon at (4,0).
            // Collector B at (2,1), output facing south onto the belt's middle tile.
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = Collector, position = new[] { 0, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = Collector, position = new[] { 2, 1 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = Demon,     position = new[] { 4, 0 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 1, 0, 2, 0, 3, 0 } });
            return grid;
        }

        static IEnumerable<(Vector2Int, int)> Ports(BuildingSaveData b)
        {
            var at = new Vector2Int(b.position[0], b.position[1]);
            if (b.buildingId != Collector) yield break;
            yield return (at, at == new Vector2Int(2, 1) ? South : East);
        }

        static float DemonFlow(System.Func<BuildingSaveData, IEnumerable<(Vector2Int, int)>> ports, bool withPorts)
        {
            var snap = IdleGraphAnalyzer.BuildSnapshot(Grid(),
                id => id == Collector, id => id == Demon, _ => Vector2Int.one, (_, __) => 7, _ => 3f, _ => 1f, 1f, "t",
                getOutputPorts: withPorts ? ports : null);
            return snap.chains.Where(c => c.endsAtEntropySink).Sum(c => c.itemsPerSecond);
        }

        [Test]
        public void ACollectorFeedingTheBeltMidway_ReachesTheDemon() =>
            Assert.AreEqual(6f, DemonFlow(Ports, withPorts: true), 1e-4f, "both collectors' 3/s");

        [Test]
        public void WithoutPortData_OnlyTheHeadFeeds() =>
            Assert.AreEqual(3f, DemonFlow(Ports, withPorts: false), 1e-4f);

        [Test]
        public void AnOutputFacingAway_FeedsNothing()
        {
            static IEnumerable<(Vector2Int, int)> Away(BuildingSaveData b)
            {
                if (b.buildingId == Collector) yield return (new Vector2Int(b.position[0], b.position[1]), East);
            }
            Assert.AreEqual(3f, DemonFlow(Away, withPorts: true), 1e-4f);
        }
    }
}
