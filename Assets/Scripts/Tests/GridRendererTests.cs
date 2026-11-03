using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for GridRenderer.WorldToCell — the single source of truth for
    /// mapping a world-space ground point to a grid cell. Tiles are rendered CENTERED at
    /// (x*cellSize, z*cellSize), so the mapping must be round-to-nearest; plain FloorToInt
    /// selects the cell a half-tile down-left of the point (the "touch is a bit off" bug).
    /// These tests lock the convention so the placement/conveyor/deconstruct/inspector
    /// call sites can never silently diverge again.
    /// </summary>
    [TestFixture]
    public class GridRendererTests
    {
        // ── Tile centre maps to its own cell ─────────────────────────────────

        [Test]
        public void WorldToCell_ExactCentre_MapsToThatCell()
        {
            Assert.AreEqual(new Vector2Int(0, 0), GridRenderer.WorldToCell(new Vector3(0f, 0f, 0f), 1f));
            Assert.AreEqual(new Vector2Int(3, 5), GridRenderer.WorldToCell(new Vector3(3f, 0f, 5f), 1f));
        }

        // ── Anywhere inside the visible tile maps to that tile ───────────────

        [Test]
        public void WorldToCell_RightOfCentreWithinTile_StaysOnTile()
        {
            // Cell 2 spans world [1.5, 2.5); 2.4 is still cell 2.
            Assert.AreEqual(new Vector2Int(2, 2), GridRenderer.WorldToCell(new Vector3(2.4f, 0f, 2.4f), 1f));
        }

        [Test]
        public void WorldToCell_LeftOfCentreWithinTile_StaysOnTile()
        {
            // 1.6 is inside cell 2's range [1.5, 2.5) — the case plain FloorToInt got wrong (it returned 1).
            Assert.AreEqual(new Vector2Int(2, 2), GridRenderer.WorldToCell(new Vector3(1.6f, 0f, 1.6f), 1f));
        }

        // ── Boundary rounds up to the next cell ──────────────────────────────

        [Test]
        public void WorldToCell_HalfCellBoundary_RoundsToNextCell()
        {
            // The boundary between cell 0 and 1 sits at 0.5 — it belongs to cell 1.
            Assert.AreEqual(new Vector2Int(1, 1), GridRenderer.WorldToCell(new Vector3(0.5f, 0f, 0.5f), 1f));
        }

        // ── Negative coordinates (just inside / just outside cell 0) ─────────

        [Test]
        public void WorldToCell_SlightlyNegativeWithinCellZero_IsZero()
        {
            Assert.AreEqual(new Vector2Int(0, 0), GridRenderer.WorldToCell(new Vector3(-0.4f, 0f, -0.4f), 1f));
        }

        [Test]
        public void WorldToCell_PastNegativeBoundary_IsNegativeOne()
        {
            // -0.6 is past the -0.5 boundary → cell -1 (out of bounds, caught by IsInBounds).
            Assert.AreEqual(new Vector2Int(-1, -1), GridRenderer.WorldToCell(new Vector3(-0.6f, 0f, -0.6f), 1f));
        }

        // ── Non-unit cell size scales correctly ──────────────────────────────

        [Test]
        public void WorldToCell_NonUnitCellSize_Scales()
        {
            // cellSize 2: cell 2 centre = 4.0, cell 3 centre = 6.0.
            Assert.AreEqual(new Vector2Int(2, 3), GridRenderer.WorldToCell(new Vector3(4f, 0f, 6f), 2f));
            // 4.9 is within cell 2's range [3, 5).
            Assert.AreEqual(new Vector2Int(2, 2), GridRenderer.WorldToCell(new Vector3(4.9f, 0f, 4.9f), 2f));
        }

        // ── Uses world.z (ground plane), not world.y ─────────────────────────

        [Test]
        public void WorldToCell_UsesZForRow_IgnoresY()
        {
            // y is the up-axis height of the ground hit and must not affect the cell row.
            Assert.AreEqual(new Vector2Int(1, 4), GridRenderer.WorldToCell(new Vector3(1f, 99f, 4f), 1f));
        }
    }
}
