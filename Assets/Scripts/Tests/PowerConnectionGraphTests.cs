using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Unit tests for the managed power-connection graph: which power nodes link (edges, via
    /// <see cref="PowerCoverageMath.NodesLinked"/>) and which chain back to a generator (connectivity flood).
    /// This is the read-only mirror of the Burst connectivity pass in <see cref="PowerGridSystem"/>; it takes
    /// plain data so it needs no ECS World.
    /// </summary>
    [TestFixture]
    public class PowerConnectionGraphTests
    {
        static PowerConnectionGraph.PowerNode Node(int x, int y, float linkRange, bool isGenerator,
                                                   int w = 1, int h = 1) =>
            new PowerConnectionGraph.PowerNode
            {
                AnchorX = x, AnchorY = y, Width = w, Height = h,
                LinkRange = linkRange, IsGenerator = isGenerator
            };

        // ── Edges ─────────────────────────────────────────────────────────────

        [Test]
        public void BuildEdges_LinksNodesWithinMaxRange()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>
            {
                Node(0, 0, linkRange: 4f, isGenerator: true),
                Node(4, 0, linkRange: 6f, isGenerator: false), // 4 apart, max range 6 -> linked
            };

            var edges = PowerConnectionGraph.BuildEdges(nodes);

            Assert.AreEqual(1, edges.Count, "the two in-range nodes must produce exactly one edge");
            Assert.AreEqual((0, 1), edges[0]);
        }

        [Test]
        public void BuildEdges_NoEdgeBeyondRange()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>
            {
                Node(0, 0, linkRange: 3f, isGenerator: true),
                Node(10, 0, linkRange: 3f, isGenerator: false), // 10 apart, max range 3 -> no link
            };

            Assert.AreEqual(0, PowerConnectionGraph.BuildEdges(nodes).Count,
                "nodes beyond the larger link range form no edge");
        }

        [Test]
        public void BuildEdges_EmptyOrNull_NoEdges()
        {
            Assert.AreEqual(0, PowerConnectionGraph.BuildEdges(null).Count);
            Assert.AreEqual(0, PowerConnectionGraph.BuildEdges(new List<PowerConnectionGraph.PowerNode>()).Count);
        }

        // ── Connectivity flood ────────────────────────────────────────────────

        [Test]
        public void Connectivity_RelayChainedThroughRelayToGenerator_IsLinked()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>
            {
                Node(0, 0, linkRange: 4f, isGenerator: true),   // 0: generator
                Node(4, 0, linkRange: 6f, isGenerator: false),  // 1: relay, links to gen (4 apart)
                Node(9, 0, linkRange: 6f, isGenerator: false),  // 2: relay, links to relay 1 (5 apart), not gen (9)
            };

            var edges     = PowerConnectionGraph.BuildEdges(nodes);
            var connected = PowerConnectionGraph.ComputeConnectivity(nodes, edges);

            Assert.IsTrue(connected[0], "the generator is always connected");
            Assert.IsTrue(connected[1], "a relay in range of the generator is connected");
            Assert.IsTrue(connected[2], "a relay chained through another relay to the generator is connected");
        }

        [Test]
        public void Connectivity_StrandedRelay_IsNotLinked()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>
            {
                Node(0, 0, linkRange: 4f, isGenerator: true),    // generator
                Node(4, 0, linkRange: 6f, isGenerator: false),   // linked relay
                Node(30, 0, linkRange: 6f, isGenerator: false),  // stranded relay (far from everything)
            };

            var edges     = PowerConnectionGraph.BuildEdges(nodes);
            var connected = PowerConnectionGraph.ComputeConnectivity(nodes, edges);

            Assert.IsTrue(connected[1], "the near relay is connected");
            Assert.IsFalse(connected[2], "a relay out of range of any generator (directly or chained) is stranded");
        }

        [Test]
        public void Connectivity_TwoSeparateGenerators_FormIndependentClusters()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>
            {
                Node(0, 0,  linkRange: 4f, isGenerator: true),   // generator A
                Node(30, 0, linkRange: 4f, isGenerator: true),   // generator B (far from A)
            };

            var edges     = PowerConnectionGraph.BuildEdges(nodes);
            var connected = PowerConnectionGraph.ComputeConnectivity(nodes, edges);

            Assert.AreEqual(0, edges.Count, "two distant generators share no edge");
            Assert.IsTrue(connected[0], "generator A is connected to itself");
            Assert.IsTrue(connected[1], "generator B is connected to itself");
        }

        [Test]
        public void Connectivity_Empty_ReturnsEmpty()
        {
            var nodes = new List<PowerConnectionGraph.PowerNode>();
            Assert.AreEqual(0, PowerConnectionGraph.ComputeConnectivity(nodes, PowerConnectionGraph.BuildEdges(nodes)).Length);
        }
    }
}
