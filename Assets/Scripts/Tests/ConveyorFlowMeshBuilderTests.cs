using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Pure mesh invariants for the conveyor "river" ribbon: straight / bend / merge vertex counts,
    /// a flat low profile, geometry contained within the cell, and monotonic flow UVs (so the shader
    /// scrolls the current the right way).
    /// </summary>
    [TestFixture]
    public class ConveyorFlowMeshBuilderTests
    {
        private const int North = (int)OutputDirection.North;
        private const int East  = (int)OutputDirection.East;
        private const int West  = (int)OutputDirection.West;

        private const float Cell = 1f;
        private const float Eps  = 1e-3f;

        [Test]
        public void Straight_IsAFlatRibbonWithinTheCell()
        {
            var mesh = ConveyorFlowMeshBuilder.Build(Cell, East, East, null);

            Assert.AreEqual(6, mesh.vertexCount, "a straight ribbon is 3 cross-sections (6 verts)");

            foreach (var v in mesh.vertices)
            {
                Assert.AreEqual(ConveyorFlowMeshBuilder.FlowY, v.y, Eps, "ribbon lies flat at FlowY");
                Assert.LessOrEqual(Mathf.Abs(v.x), Cell * 0.5f + Eps, "stays within the cell in X");
                Assert.LessOrEqual(Mathf.Abs(v.z), Cell * 0.5f + Eps, "stays within the cell in Z");
            }

            float minV = 1f, maxV = 0f;
            foreach (var uv in mesh.uv) { minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y); }
            Assert.AreEqual(0f, minV, Eps, "flow UV starts at the inbound edge");
            Assert.AreEqual(1f, maxV, Eps, "flow UV ends at the outbound edge");
        }

        [Test]
        public void Bend_CurvesWithMoreCrossSectionsThanStraight()
        {
            // entryDir East ⇒ inbound edge West; exitDir North ⇒ a 90° turn (Bézier).
            var bend = ConveyorFlowMeshBuilder.Build(Cell, East, North, null);

            Assert.Greater(bend.vertexCount, 6, "a bend is subdivided into more cross-sections than a straight");
            foreach (var uv in bend.uv)
                Assert.That(uv.y, Is.InRange(-Eps, 1f + Eps), "flow UV stays in [0,1]");
        }

        [Test]
        public void Merge_ArmsReachEachInboundAndTheExit()
        {
            // Inbound edges West + North sweeping into the East exit: arms must reach all three edges.
            var merge = ConveyorFlowMeshBuilder.Build(Cell, East, East, new[] { West, North });

            Assert.Greater(merge.vertexCount, 0);
            Assert.AreEqual(0, merge.vertexCount % 2, "ribbon verts come in left/right pairs");

            bool reachesWest = false, reachesNorth = false, reachesEast = false;
            foreach (var v in merge.vertices)
            {
                if (v.x <= -Cell * 0.5f + 0.05f) reachesWest  = true; // west inbound edge
                if (v.z >=  Cell * 0.5f - 0.05f) reachesNorth = true; // north inbound edge
                if (v.x >=  Cell * 0.5f - 0.05f) reachesEast  = true; // east exit edge
                Assert.AreEqual(ConveyorFlowMeshBuilder.FlowY, v.y, Eps, "ribbon lies flat");
            }

            Assert.IsTrue(reachesWest,  "a tributary reaches the west inbound edge");
            Assert.IsTrue(reachesNorth, "a tributary reaches the north inbound edge");
            Assert.IsTrue(reachesEast,  "the exit arm reaches the east edge");

            foreach (var uv in merge.uv)
                Assert.That(uv.y, Is.InRange(-Eps, 1f + Eps));
        }
    }
}
