using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests for GridOccupancy.
    ///
    /// GridOccupancy stores its state in HashSets initialized at field declaration,
    /// so Awake is not required — methods work correctly on a freshly added component.
    /// Tests call methods directly on the component reference to stay isolated from
    /// the singleton Instance.
    /// </summary>
    [TestFixture]
    public class GridOccupancyTests
    {
        private GameObject  _go;
        private GridOccupancy _occ;

        [SetUp]
        public void SetUp()
        {
            _go  = new GameObject("GridOccupancyTest");
            _occ = _go.AddComponent<GridOccupancy>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        // ── Single-cell TryOccupy / IsOccupied / Release ─────────────────────

        [Test]
        public void TryOccupy_FreeCell_ReturnsTrueAndMarksCellOccupied()
        {
            bool result = _occ.TryOccupy(3, 5);

            Assert.IsTrue(result, "TryOccupy must return true for a free cell");
            Assert.IsTrue(_occ.IsOccupied(3, 5), "Cell must be occupied after TryOccupy");
        }

        [Test]
        public void TryOccupy_AlreadyOccupiedCell_ReturnsFalse()
        {
            _occ.TryOccupy(1, 1);
            bool result = _occ.TryOccupy(1, 1);

            Assert.IsFalse(result, "TryOccupy must return false when cell is already occupied");
        }

        [Test]
        public void Release_ClearsOccupancy()
        {
            _occ.TryOccupy(2, 4);
            _occ.Release(2, 4);

            Assert.IsFalse(_occ.IsOccupied(2, 4), "Release must clear the cell");
        }

        [Test]
        public void IsOccupied_UnoccupiedCell_ReturnsFalse()
        {
            Assert.IsFalse(_occ.IsOccupied(0, 0), "Fresh grid must report all cells as free");
        }

        // ── Register ─────────────────────────────────────────────────────────

        [Test]
        public void Register_MarksCell()
        {
            _occ.Register(5, 5);

            Assert.IsTrue(_occ.IsOccupied(5, 5), "Register must mark cell as occupied");
        }

        // ── Multi-cell rect ──────────────────────────────────────────────────

        [Test]
        public void TryOccupyRect_AllFree_ReturnsTrueAndAllCellsOccupied()
        {
            bool result = _occ.TryOccupyRect(0, 0, 3, 2);

            Assert.IsTrue(result, "TryOccupyRect must return true when all cells are free");
            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 2; dy++)
                    Assert.IsTrue(_occ.IsOccupied(dx, dy), $"Cell ({dx},{dy}) must be occupied");
        }

        [Test]
        public void TryOccupyRect_PartiallyOccupied_ReturnsFalseAndNoPartialWrite()
        {
            _occ.TryOccupy(1, 0); // block one cell inside the target rect

            bool result = _occ.TryOccupyRect(0, 0, 3, 1);

            Assert.IsFalse(result, "TryOccupyRect must fail if any cell is occupied");
            // The cells that were free inside the rect must not have been written
            Assert.IsFalse(_occ.IsOccupied(0, 0), "No partial write: (0,0) must remain free");
            Assert.IsFalse(_occ.IsOccupied(2, 0), "No partial write: (2,0) must remain free");
        }

        [Test]
        public void IsRectFree_WhenOneCellOccupied_ReturnsFalse()
        {
            _occ.TryOccupy(2, 2);

            Assert.IsFalse(_occ.IsRectFree(1, 1, 3, 3), "IsRectFree must return false when any cell in the rect is occupied");
        }

        [Test]
        public void IsRectFree_WhenAllFree_ReturnsTrue()
        {
            Assert.IsTrue(_occ.IsRectFree(0, 0, 4, 4), "IsRectFree must return true on an empty grid");
        }

        [Test]
        public void ReleaseRect_ClearsAllCells()
        {
            _occ.TryOccupyRect(0, 0, 2, 2);
            _occ.ReleaseRect(0, 0, 2, 2);

            Assert.IsTrue(_occ.IsRectFree(0, 0, 2, 2), "ReleaseRect must free all cells in the rect");
        }

        [Test]
        public void RegisterRect_MarksAllCells()
        {
            _occ.RegisterRect(1, 1, 2, 3);

            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 3; dy++)
                    Assert.IsTrue(_occ.IsOccupied(1 + dx, 1 + dy), $"RegisterRect cell ({1+dx},{1+dy}) must be occupied");
        }

        // ── Conveyor tracking ────────────────────────────────────────────────

        [Test]
        public void RegisterConveyor_SetsOccupiedAndConveyorCell()
        {
            _occ.RegisterConveyor(4, 4);

            Assert.IsTrue(_occ.IsOccupied(4, 4),     "RegisterConveyor must mark cell as occupied");
            Assert.IsTrue(_occ.IsConveyorCell(4, 4), "RegisterConveyor must mark cell as conveyor");
        }

        [Test]
        public void UnregisterConveyor_ClearsOccupiedAndConveyorTracking()
        {
            _occ.RegisterConveyor(3, 7);
            _occ.UnregisterConveyor(3, 7);

            Assert.IsFalse(_occ.IsOccupied(3, 7),     "UnregisterConveyor must clear occupancy");
            Assert.IsFalse(_occ.IsConveyorCell(3, 7), "UnregisterConveyor must clear conveyor flag");
        }

        [Test]
        public void IsConveyorCell_RegularOccupiedCell_ReturnsFalse()
        {
            _occ.TryOccupy(1, 1);

            Assert.IsFalse(_occ.IsConveyorCell(1, 1), "A building-occupied cell must not be reported as a conveyor cell");
        }
    }
}
