using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Nucleon Harvester's output must line up with its visuals: items leave from one cell of a 2x1
    /// footprint, not from its middle. These pin the door position to the placed port for each rotation.
    /// </summary>
    [TestFixture]
    public class NucleonHarvesterStructureTests
    {
        private const int North = 0, East = 1, South = 2, West = 3;

        // A 2x1 anchored at (10, 5) occupies cells (10,5) and (11,5).
        [Test]
        public void LeftCellSouthPort_IsOffCentreTowardsThatCell()
        {
            var edge = NucleonHarvesterStructure.PortEdgeLocal(10, 5, South, 10, 5, 2, 1);
            Assert.AreEqual(-0.5f, edge.x, 1e-5f, "the left cell's centre, not the footprint's centre");
            Assert.AreEqual(-0.5f, edge.y, 1e-5f);
        }

        [Test]
        public void RightCellNorthPort_IsOnTheOtherCell()
        {
            var edge = NucleonHarvesterStructure.PortEdgeLocal(11, 5, North, 10, 5, 2, 1);
            Assert.AreEqual(0.5f, edge.x, 1e-5f);
            Assert.AreEqual(0.5f, edge.y, 1e-5f);
        }

        // Rotated a quarter turn, the footprint is 1x2: cells (10,5) and (10,6).
        [Test]
        public void RotatedFootprint_UsesTheOtherAxis()
        {
            var west = NucleonHarvesterStructure.PortEdgeLocal(10, 6, West, 10, 5, 1, 2);
            Assert.AreEqual(-0.5f, west.x, 1e-5f);
            Assert.AreEqual(0.5f, west.y, 1e-5f);

            var east = NucleonHarvesterStructure.PortEdgeLocal(10, 5, East, 10, 5, 1, 2);
            Assert.AreEqual(0.5f, east.x, 1e-5f);
            Assert.AreEqual(-0.5f, east.y, 1e-5f);
        }
    }
}
