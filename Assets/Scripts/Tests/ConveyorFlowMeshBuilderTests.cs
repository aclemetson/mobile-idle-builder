using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Pure mesh invariants for the conveyor belt ribbon: straight / bend / merge vertex counts,
    /// a flat low profile, geometry contained within the cell, and monotonic flow UVs (so the shader
    /// scrolls the current the right way).
    /// </summary>
    [TestFixture]
    public class ConveyorFlowMeshBuilderTests
    {
        private const int North = (int)OutputDirection.North;
        private const int East  = (int)OutputDirection.East;
        private const int South = (int)OutputDirection.South;
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
            // entryDir East ⇒ inbound edge West; exitDir North ⇒ a 90° turn (quarter circle).
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

        [Test]
        public void Bend_InnerEdgeNeverPinches()
        {
            // The corner's inner edge must keep a real radius about the shared cell corner (the old Bézier
            // pinched it to a point, crowding the lattice lines). Corner for West→North is (-½, +½).
            var bend   = ConveyorFlowMeshBuilder.Build(Cell, East, North, null);
            var corner = new Vector3(-Cell * 0.5f, 0f, Cell * 0.5f);
            float hw   = Cell * ConveyorFlowMeshBuilder.WaterWidthFrac * 0.5f;
            float minR = float.MaxValue;
            foreach (var v in bend.vertices)
                minR = Mathf.Min(minR, Vector2.Distance(new Vector2(v.x, v.z), new Vector2(corner.x, corner.z)));
            Assert.AreEqual(Cell * 0.5f - hw, minR, 0.01f, "inner edge radius is half a cell minus the half-width");
        }

        [Test]
        public void CenterlinePoint_RunsEdgeMidToEdgeMid_OnAQuarterCircle()
        {
            Vector3 start = ConveyorFlowMeshBuilder.CenterlinePoint(Cell, West, North, 0f);
            Vector3 end   = ConveyorFlowMeshBuilder.CenterlinePoint(Cell, West, North, 1f);
            Assert.That(Vector3.Distance(start, new Vector3(-0.5f, 0f, 0f)), Is.LessThan(Eps), "starts mid west edge");
            Assert.That(Vector3.Distance(end,   new Vector3(0f, 0f, 0.5f)),  Is.LessThan(Eps), "ends mid north edge");

            var corner = new Vector3(-0.5f, 0f, 0.5f);
            for (int i = 0; i <= 8; i++)
            {
                var p = ConveyorFlowMeshBuilder.CenterlinePoint(Cell, West, North, i / 8f);
                Assert.AreEqual(Cell * 0.5f, Vector3.Distance(p, corner), Eps, "every point is half a cell from the corner");
            }

            // Straight pieces stay a straight line through the centre.
            Assert.That(Vector3.Distance(ConveyorFlowMeshBuilder.CenterlinePoint(Cell, West, East, 0.5f), Vector3.zero), Is.LessThan(Eps));
        }

        [Test]
        public void PathLength_IsOneCellStraight_AndAQuarterCircleOnATurn()
        {
            Assert.AreEqual(1f, ConveyorFlowMeshBuilder.PathLengthCells(West, East), Eps);
            Assert.AreEqual(Mathf.PI / 4f, ConveyorFlowMeshBuilder.PathLengthCells(West, North), Eps);
        }

        [Test]
        public void Bend_CarriesItsLengthForCrossLineSpacing()
        {
            var bend = ConveyorFlowMeshBuilder.Build(Cell, East, North, null);
            var arms = new System.Collections.Generic.List<Vector4>();
            bend.GetUVs(1, arms);
            Assert.AreEqual(bend.vertexCount, arms.Count);
            foreach (var a in arms)
            {
                Assert.AreEqual(0f, a.x, "a plain bend has no secondary arm");
                Assert.AreEqual(Mathf.PI / 4f, a.y, Eps, "uv1.y = centreline length in cells");
            }
        }

        [Test]
        public void Merge_MarksSecondaryArmsWithTheMainBeltAxis()
        {
            // West is straight-through into the East exit (main); North turns in (secondary).
            var merge = ConveyorFlowMeshBuilder.Build(Cell, East, East, new[] { West, North });
            var arms  = new System.Collections.Generic.List<Vector4>();
            var mains = new System.Collections.Generic.List<Vector4>();
            merge.GetUVs(1, arms);
            merge.GetUVs(2, mains);
            Assert.AreEqual(merge.vertexCount, arms.Count);
            Assert.AreEqual(merge.vertexCount, mains.Count);

            float hw = Cell * ConveyorFlowMeshBuilder.WaterWidthFrac * 0.5f;
            bool sawMain = false, sawSecondary = false;
            for (int i = 0; i < arms.Count; i++)
            {
                if (arms[i].x > 0.5f)
                {
                    sawSecondary = true;
                    // Main belt runs along X, so its sideways axis is Z, scaled by 1/half-width.
                    Assert.AreEqual(0f, mains[i].x, Eps);
                    Assert.AreEqual(1f / hw, Mathf.Abs(mains[i].y), Eps);
                }
                else
                {
                    sawMain = true;
                    Assert.AreEqual(Vector4.zero, mains[i], "the main arm never hands over");
                }
            }
            Assert.IsTrue(sawMain && sawSecondary);
        }

        [Test]
        public void Merge_WithoutAStraightInput_HasNoMainAxis()
        {
            // Both inputs turn into the North exit: no straight main arm, so the shader falls back.
            var merge = ConveyorFlowMeshBuilder.Build(Cell, North, North, new[] { West, East });
            var mains = new System.Collections.Generic.List<Vector4>();
            merge.GetUVs(2, mains);
            foreach (var m in mains) Assert.AreEqual(Vector4.zero, m);
        }

        [Test]
        public void Fork_KeepsTheStraightCrossLineCount()
        {
            var fork = ConveyorFlowMeshBuilder.BuildFork(Cell, West, new[] { North, South });
            var arms = new System.Collections.Generic.List<Vector4>();
            fork.GetUVs(1, arms);
            Assert.AreEqual(fork.vertexCount, arms.Count);
            foreach (var a in arms) Assert.AreEqual(1f, a.y, Eps);
        }
    }
}
