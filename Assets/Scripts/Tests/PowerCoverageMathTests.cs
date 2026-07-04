using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Unit tests for the shared power-coverage overlap test. A footprint is powered if <b>any part of
    /// its square overlaps the source's circular power area</b> (touching counts) — a geometric
    /// circle-vs-rectangle test, not a cell-centre gap. The circle is centred on the source footprint
    /// with radius Rc = InfluenceRadius + half the source's larger dimension, so along an axis it reaches
    /// exactly InfluenceRadius tiles past the source edge (matching the ring the player sees). This is the
    /// single spec every proximity call site agrees on (GridRenderer, BuildingVisualizer, and — by an
    /// identical inlined copy — the Burst PowerGridSystem).
    /// </summary>
    [TestFixture]
    public class PowerCoverageMathTests
    {
        // Single-cell source at (sx,sy) vs single-cell target at (tx,ty).
        static bool CellToCell(int sx, int sy, int tx, int ty, float radius) =>
            PowerCoverageMath.FootprintWithinRadius(sx, sy, sx, sy, tx, ty, tx, ty, radius);

        [Test]
        public void SameCell_IsWithin()
        {
            Assert.IsTrue(CellToCell(2, 2, 2, 2, radius: 1f), "the source's own cell is always within a positive radius");
        }

        [Test]
        public void AdjacentCell_IsWithin()
        {
            Assert.IsTrue(CellToCell(0, 0, 1, 0, radius: 1f), "an adjacent square overlaps the power area");
        }

        [Test]
        public void PartiallyOverlappingSquare_CountsAsWithin()
        {
            // 1x1 source, radius 2 -> Rc = 2.5. Cell (3,0)'s near edge sits at x=2.5, exactly on the ring:
            // the square partially reaches into the circle, so it now counts (the OLD cell-centre gap of 3
            // would have excluded it).
            Assert.IsTrue(CellToCell(0, 0, 3, 0, radius: 2f),
                "a square that only partially reaches into the radius must count as within");
        }

        [Test]
        public void SquareFullyOutside_IsNotWithin()
        {
            // radius 2 -> Rc 2.5; cell (4,0) near edge at x=3.5 > 2.5.
            Assert.IsFalse(CellToCell(0, 0, 4, 0, radius: 2f), "a square entirely outside the circle is not powered");
        }

        [Test]
        public void Diagonal_UsesEuclideanCircle()
        {
            // radius 3 -> Rc 3.5. Cell (3,3) closest corner (2.5,2.5): dist ~3.54 > 3.5 -> outside.
            Assert.IsFalse(CellToCell(0, 0, 3, 3, radius: 3f), "a diagonal square just past the circle is not powered");
            // Cell (2,2) closest corner (1.5,1.5): dist ~2.12 <= 3.5 -> inside.
            Assert.IsTrue(CellToCell(0, 0, 2, 2, radius: 3f), "a diagonal square inside the circle is powered");
        }

        [Test]
        public void MultiTileSource_UsesFootprintCentreAndSize()
        {
            // 2x2 source (0,0)-(1,1): centre (0.5,0.5), Rc = radius(2) + 1 = 3. Target (4,0) near edge x=3.5:
            // distance from centre = 3.0 == Rc -> within (touching). One cell further is out.
            Assert.IsTrue(PowerCoverageMath.FootprintWithinRadius(0, 0, 1, 1, 4, 0, 4, 0, radius: 2f),
                "coverage grows with the source footprint size and is centred on it");
            Assert.IsFalse(PowerCoverageMath.FootprintWithinRadius(0, 0, 1, 1, 5, 0, 5, 0, radius: 2f),
                "one cell beyond the enlarged circle is out of range");
        }

        [Test]
        public void MultiTileTarget_MeasuresFromNearestEdge()
        {
            // 1x1 source radius 1 -> Rc 1.5. A 2x1 target spanning cells (1,0)-(2,0) has its near edge at
            // x=0.5 -> within; a 2x1 target at (3,0)-(4,0) has near edge x=2.5 -> out.
            Assert.IsTrue(PowerCoverageMath.FootprintWithinRadius(0, 0, 0, 0, 1, 0, 2, 0, radius: 1f),
                "a large target counts if its nearest edge reaches the circle");
            Assert.IsFalse(PowerCoverageMath.FootprintWithinRadius(0, 0, 0, 0, 3, 0, 4, 0, radius: 1f),
                "a large target whose nearest edge is outside does not count");
        }

        [Test]
        public void ZeroOrNegativeRadius_NeverWithin()
        {
            Assert.IsFalse(CellToCell(2, 2, 2, 2, radius: 0f), "a non-positive radius never connects");
            Assert.IsFalse(CellToCell(2, 2, 2, 2, radius: -1f), "a negative radius never connects");
        }
    }
}
