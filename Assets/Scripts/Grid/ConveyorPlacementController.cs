using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages conveyor belt placement mode.
    ///
    /// Interaction model:
    ///   1. Player opens the Buildings panel and taps "Place" on the Conveyor Belt card.
    ///   2. A conveyor overlay appears. Player presses down on any free cell — this becomes
    ///      the START point.
    ///   3. While held, the END point tracks the pointer. The path is recalculated every frame
    ///      as an L-shape (start → corner → end), with at most one 90° turn.
    ///      The corner is placed so the longer axis comes first. If that path is blocked, the
    ///      alternate orientation is tried automatically.
    ///   4. On release: the displayed path is placed. Draw state resets so the player can
    ///      immediately start the next segment (still in conveyor mode).
    ///   5. Escape / Cancel button exits conveyor mode entirely.
    /// </summary>
    public class ConveyorPlacementController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private GridRenderer   gridRenderer;
        [SerializeField] private ConveyorPlacer conveyorPlacer;

        // ----------------------------------------------------------------
        // Public state
        // ----------------------------------------------------------------

        public bool IsPlacing { get; private set; }
        public event Action<bool> OnPlacingChanged;

        // ----------------------------------------------------------------
        // Private draw state
        // ----------------------------------------------------------------

        private bool               _isDragging;
        private Vector2Int         _startCell;
        private Vector2Int         _endCell;
        private List<Vector2Int>   _currentPath = new(); // live-calculated, may be empty if blocked
        private bool               _pathValid;

        // Orientation lock: set on the first step away from the start cell;
        // cleared when the end returns to the start so the player can restart in a new direction.
        private bool               _orientationLocked;
        private bool               _horizontalFirst; // true = EW leg first, false = NS leg first

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        public void BeginConveyorMode()
        {
            if (IsPlacing) return;
            ResetDraw();
            IsPlacing = true;
            OnPlacingChanged?.Invoke(true);
        }

        public void CancelConveyorMode()
        {
            ResetDraw();
            IsPlacing = false;
            OnPlacingChanged?.Invoke(false);
        }

        // ----------------------------------------------------------------
        // Update loop
        // ----------------------------------------------------------------

        void Update()
        {
            if (!IsPlacing) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelConveyorMode();
                return;
            }

            Vector2Int cell = WorldToCell(InputUtils.GetPointerPosition());

            // ---- Pointer DOWN: fix start point ----
            if (InputUtils.WasPointerPressed())
            {
                gridRenderer.ClearConveyorHoverCell();
                if (IsFreecell(cell))
                {
                    ResetDraw();
                    _isDragging = true;
                    _startCell  = cell;
                    _endCell    = cell;
                    UpdatePath();
                }
                return;
            }

            // ---- Pre-drag hover: show green on valid start cells ----
            if (!_isDragging)
            {
                if (gridRenderer.IsInBounds(cell.x, cell.y) && IsFreecell(cell))
                    gridRenderer.SetConveyorHoverCell(cell.x, cell.y);
                else
                    gridRenderer.ClearConveyorHoverCell();
                return;
            }

            // ---- Pointer HELD: move end point ----
            if (IsPointerHeld())
            {
                if (cell != _endCell && gridRenderer.IsInBounds(cell.x, cell.y))
                {
                    _endCell = cell;
                    UpdatePath();
                }
            }

            // ---- Pointer RELEASED: place if valid ----
            if (WasPointerReleased())
            {
                _isDragging = false;

                if (_pathValid && _currentPath.Count >= 1)
                    conveyorPlacer.PlaceConveyorChain(_currentPath);

                ResetDraw(); // clear ghost + path, stay in conveyor mode
            }
        }

        // ----------------------------------------------------------------
        // Path calculation
        // ----------------------------------------------------------------

        /// <summary>
        /// Recalculates _currentPath from _startCell to _endCell and refreshes the ghost.
        /// Tries the primary orientation first; falls back to the alternate if it is blocked.
        /// </summary>
        private void UpdatePath()
        {
            gridRenderer.ClearConveyorGhost();

            // Lock orientation on the first step away from start; unlock if back at start.
            int dx = _endCell.x - _startCell.x;
            int dy = _endCell.y - _startCell.y;

            if (dx == 0 && dy == 0)
            {
                // End is back at start — release the lock so the next move picks a fresh direction
                _orientationLocked = false;
            }
            else if (!_orientationLocked)
            {
                // First movement away from start: whichever axis moved more determines the first leg
                _horizontalFirst   = Mathf.Abs(dx) >= Mathf.Abs(dy);
                _orientationLocked = true;
            }
            // else: orientation stays locked — moving far in the other axis won't flip it

            var primary   = BuildLPath(_startCell, _endCell, preferHorizontalFirst: _horizontalFirst);
            var alternate = BuildLPath(_startCell, _endCell, preferHorizontalFirst: !_horizontalFirst);

            bool primaryValid   = IsPathFree(primary);
            bool alternateValid = IsPathFree(alternate);

            // Prefer primary unless it is blocked and alternate is free
            List<Vector2Int> chosen;
            if (primaryValid)
                chosen = primary;
            else if (alternateValid)
                chosen = alternate;
            else
                chosen = primary; // both blocked — show primary as visual feedback

            _currentPath = chosen;
            _pathValid   = primaryValid || alternateValid;

            // Paint ghost tiles in orange, then overlay start/end in green
            foreach (var c in _currentPath)
                gridRenderer.AddConveyorGhostCell(c.x, c.y);

            if (_currentPath.Count > 0)
                gridRenderer.PaintConveyorEndpoints(_startCell.x, _startCell.y, _endCell.x, _endCell.y);
        }

        /// <summary>
        /// Builds an L-shaped path from <paramref name="start"/> to <paramref name="end"/>.
        /// <paramref name="preferHorizontalFirst"/>: if true, the horizontal leg comes first
        /// (corner at (end.x, start.y)); otherwise the vertical leg comes first
        /// (corner at (start.x, end.y)).
        /// Returns a straight line when start and end share a row or column.
        /// </summary>
        private static List<Vector2Int> BuildLPath(Vector2Int start, Vector2Int end, bool preferHorizontalFirst)
        {
            int dx = end.x - start.x;
            int dy = end.y - start.y;

            // Straight line — no turn needed
            if (dx == 0 || dy == 0)
                return BuildStraightLine(start, end);

            // Determine elbow cell
            Vector2Int corner = preferHorizontalFirst
                ? new Vector2Int(end.x,   start.y)  // horizontal first
                : new Vector2Int(start.x, end.y);    // vertical first

            var path = new List<Vector2Int>();
            AddLineCells(path, start,  corner, skipFirst: false);
            AddLineCells(path, corner, end,    skipFirst: true);   // skip corner (already added)
            return path;
        }

        private static List<Vector2Int> BuildStraightLine(Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            AddLineCells(path, start, end, skipFirst: false);
            return path;
        }

        /// <summary>
        /// Appends each cell on the straight line from <paramref name="a"/> to
        /// <paramref name="b"/> (which must share a row or column) into <paramref name="path"/>.
        /// </summary>
        private static void AddLineCells(List<Vector2Int> path, Vector2Int a, Vector2Int b, bool skipFirst)
        {
            int dx    = b.x - a.x;
            int dy    = b.y - a.y;
            int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
            int sx    = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            int sy    = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

            int iStart = skipFirst ? 1 : 0;
            for (int i = iStart; i <= steps; i++)
                path.Add(new Vector2Int(a.x + sx * i, a.y + sy * i));
        }

        // ----------------------------------------------------------------
        // Validity helpers
        // ----------------------------------------------------------------

        /// <summary>True if every cell in the path is free (bounds + not occupied).</summary>
        private bool IsPathFree(List<Vector2Int> path)
        {
            foreach (var c in path)
                if (!IsFreecell(c)) return false;
            return true;
        }

        private bool IsFreecell(Vector2Int c) => IsFreecell(c.x, c.y);
        private bool IsFreecell(int x, int y)
        {
            if (!gridRenderer.IsInBounds(x, y)) return false;
            if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y)) return false;
            return true;
        }

        // ----------------------------------------------------------------
        // Reset helper
        // ----------------------------------------------------------------

        private void ResetDraw()
        {
            gridRenderer.ClearConveyorGhost();
            gridRenderer.ClearConveyorHoverCell();
            _isDragging        = false;
            _currentPath       = new List<Vector2Int>();
            _pathValid         = false;
            _orientationLocked = false;
        }

        // ----------------------------------------------------------------
        // Input helpers
        // ----------------------------------------------------------------

        private static bool IsPointerHeld()
        {
            if (Touchscreen.current != null)
                return Touchscreen.current.primaryTouch.press.isPressed;
            if (Mouse.current != null)
                return Mouse.current.leftButton.isPressed;
            return false;
        }

        private static bool WasPointerReleased()
        {
            if (Touchscreen.current != null)
                return Touchscreen.current.primaryTouch.press.wasReleasedThisFrame;
            if (Mouse.current != null)
                return Mouse.current.leftButton.wasReleasedThisFrame;
            return false;
        }

        // ----------------------------------------------------------------
        // Grid helper
        // ----------------------------------------------------------------

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);

            float t     = -ray.origin.y / ray.direction.y;
            var   world = ray.origin + ray.direction * t;

            int x = Mathf.FloorToInt(world.x / gridRenderer.CellSize);
            int y = Mathf.FloorToInt(world.z / gridRenderer.CellSize);
            return new(x, y);
        }
    }
}
