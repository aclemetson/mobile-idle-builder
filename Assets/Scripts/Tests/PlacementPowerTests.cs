using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Unit tests for the placement-time "is this footprint powered?" check (#302).</summary>
    [TestFixture]
    public class PlacementPowerTests
    {
        static PowerConnectionGraph.PowerNode Node(int x, int y, int w, int h, float radius, bool linked) =>
            new PowerConnectionGraph.PowerNode
            {
                AnchorX = x, AnchorY = y, Width = w, Height = h,
                InfluenceRadius = radius, IsGridLinked = linked, IsGenerator = linked
            };

        [Test]
        public void InsideLinkedGeneratorRadius_IsPowered()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode> { Node(0, 0, 1, 1, 3f, true) };
            Assert.IsTrue(PlacementPower.IsFootprintPowered(nodes, 2, 0, 1, 1));
        }

        [Test]
        public void JustOutsideRadius_IsUnpowered()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode> { Node(0, 0, 1, 1, 3f, true) };
            Assert.IsFalse(PlacementPower.IsFootprintPowered(nodes, 5, 0, 1, 1));
        }

        [Test]
        public void UnlinkedRelayInRange_DoesNotPower()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode> { Node(0, 0, 1, 1, 3f, false) };
            Assert.IsFalse(PlacementPower.IsFootprintPowered(nodes, 1, 0, 1, 1));
        }

        [Test]
        public void PartlyOverlapping2x2Footprint_CountsAsPowered()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode> { Node(0, 0, 1, 1, 3f, true) };
            // Near cell (3,0) is inside the radius; the far column (4,x) is not.
            Assert.IsTrue(PlacementPower.IsFootprintPowered(nodes, 3, 0, 2, 2));
        }

        [Test]
        public void NoNodes_IsUnpowered()
        {
            Assert.IsFalse(PlacementPower.IsFootprintPowered(new List<PowerConnectionGraph.PowerNode>(), 0, 0, 1, 1));
        }
    }
}
