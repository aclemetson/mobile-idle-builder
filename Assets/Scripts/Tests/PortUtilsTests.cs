using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class PortUtilsTests
    {
        // ── RotatedFootprint ─────────────────────────────────────────────────

        [Test]
        public void RotatedFootprint_ZeroRotation_ReturnsSame()
        {
            Assert.AreEqual(new Vector2Int(3, 1), PortUtils.RotatedFootprint(new Vector2Int(3, 1), 0));
        }

        [Test]
        public void RotatedFootprint_OneRotation_SwapsDimensions()
        {
            Assert.AreEqual(new Vector2Int(1, 3), PortUtils.RotatedFootprint(new Vector2Int(3, 1), 1));
        }

        [Test]
        public void RotatedFootprint_TwoRotations_ReturnsSame()
        {
            Assert.AreEqual(new Vector2Int(3, 1), PortUtils.RotatedFootprint(new Vector2Int(3, 1), 2));
        }

        [Test]
        public void RotatedFootprint_FourRotations_ReturnsSame()
        {
            Assert.AreEqual(new Vector2Int(3, 1), PortUtils.RotatedFootprint(new Vector2Int(3, 1), 4));
        }

        // ── RotateDir ────────────────────────────────────────────────────────

        [Test]
        public void RotateDir_NorthOneStep_IsEast()
        {
            Assert.AreEqual(OutputDirection.East, PortUtils.RotateDir(OutputDirection.North, 1));
        }

        [Test]
        public void RotateDir_NorthTwoSteps_IsSouth()
        {
            Assert.AreEqual(OutputDirection.South, PortUtils.RotateDir(OutputDirection.North, 2));
        }

        [Test]
        public void RotateDir_NorthThreeSteps_IsWest()
        {
            Assert.AreEqual(OutputDirection.West, PortUtils.RotateDir(OutputDirection.North, 3));
        }

        [Test]
        public void RotateDir_FourSteps_FullCycle_ReturnsOriginal()
        {
            Assert.AreEqual(OutputDirection.North, PortUtils.RotateDir(OutputDirection.North, 4));
            Assert.AreEqual(OutputDirection.East,  PortUtils.RotateDir(OutputDirection.East,  4));
            Assert.AreEqual(OutputDirection.South, PortUtils.RotateDir(OutputDirection.South, 4));
            Assert.AreEqual(OutputDirection.West,  PortUtils.RotateDir(OutputDirection.West,  4));
        }

        [Test]
        public void RotateDir_WestOneStep_IsNorth()
        {
            Assert.AreEqual(OutputDirection.North, PortUtils.RotateDir(OutputDirection.West, 1));
        }

        // ── FlipDir ──────────────────────────────────────────────────────────

        [Test]
        public void FlipDir_East_BecomesWest()
        {
            Assert.AreEqual(OutputDirection.West, PortUtils.FlipDir(OutputDirection.East));
        }

        [Test]
        public void FlipDir_West_BecomesEast()
        {
            Assert.AreEqual(OutputDirection.East, PortUtils.FlipDir(OutputDirection.West));
        }

        [Test]
        public void FlipDir_North_Unchanged()
        {
            Assert.AreEqual(OutputDirection.North, PortUtils.FlipDir(OutputDirection.North));
        }

        [Test]
        public void FlipDir_South_Unchanged()
        {
            Assert.AreEqual(OutputDirection.South, PortUtils.FlipDir(OutputDirection.South));
        }

        // ── AdjacentCell ─────────────────────────────────────────────────────

        [Test]
        public void AdjacentCell_North_IncrementsY()
        {
            Assert.AreEqual(new Vector2Int(2, 4), PortUtils.AdjacentCell(new Vector2Int(2, 3), OutputDirection.North));
        }

        [Test]
        public void AdjacentCell_East_IncrementsX()
        {
            Assert.AreEqual(new Vector2Int(3, 3), PortUtils.AdjacentCell(new Vector2Int(2, 3), OutputDirection.East));
        }

        [Test]
        public void AdjacentCell_South_DecrementsY()
        {
            Assert.AreEqual(new Vector2Int(2, 2), PortUtils.AdjacentCell(new Vector2Int(2, 3), OutputDirection.South));
        }

        [Test]
        public void AdjacentCell_West_DecrementsX()
        {
            Assert.AreEqual(new Vector2Int(1, 3), PortUtils.AdjacentCell(new Vector2Int(2, 3), OutputDirection.West));
        }

        // ── TransformPort ────────────────────────────────────────────────────

        [Test]
        public void TransformPort_NoFlipNoRotation_Unchanged()
        {
            var port = new BuildingPort { localCell = new Vector2Int(0, 0), localFacing = OutputDirection.North };
            var fp   = new Vector2Int(1, 1);

            var (cell, facing) = PortUtils.TransformPort(port, fp, flipped: false, rotation: 0);

            Assert.AreEqual(new Vector2Int(0, 0), cell);
            Assert.AreEqual(OutputDirection.North, facing);
        }

        [Test]
        public void TransformPort_FlipOnly_MirrorsXAndFacing()
        {
            // 2-wide footprint, port at (0,0) facing East → after flip: x=(2-1-0)=1, facing West
            var port = new BuildingPort { localCell = new Vector2Int(0, 0), localFacing = OutputDirection.East };
            var fp   = new Vector2Int(2, 1);

            var (cell, facing) = PortUtils.TransformPort(port, fp, flipped: true, rotation: 0);

            Assert.AreEqual(new Vector2Int(1, 0), cell);
            Assert.AreEqual(OutputDirection.West, facing);
        }

        [Test]
        public void TransformPort_RotationOnly_RotatesCellAndFacing()
        {
            // Port at (1,0) in 2×1 footprint, rotated 1× CW:
            // nx = (h-1-y) = (1-1-0) = 0, ny = x = 1 → cell=(0,1), facing=South
            var port = new BuildingPort { localCell = new Vector2Int(1, 0), localFacing = OutputDirection.East };
            var fp   = new Vector2Int(2, 1);

            var (cell, facing) = PortUtils.TransformPort(port, fp, flipped: false, rotation: 1);

            Assert.AreEqual(new Vector2Int(0, 1), cell);
            Assert.AreEqual(OutputDirection.South, facing);
        }
    }
}
