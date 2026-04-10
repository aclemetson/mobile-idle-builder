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
    ///   2. A conveyor overlay appears. Player presses down on any free cell OR any existing
    ///      conveyor cell — this becomes the START point.
    ///   3. While held, the END point tracks the pointer. The path is recalculated every frame
    ///      as an L-shape (start → corner → end), with at most one 90° turn.
    ///      The end point may also land on an existing conveyor cell.
    ///      All intermediate cells must be free.
    ///   4. On release: the displayed path is placed. Existing conveyor cells at the endpoints
    ///      have their ExitDir / EntryDir updated for any 90° turn, and any old chain links at
    ///      those endpoints are severed (the cut-off segments become independent chains).
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
        private List<Vector2Int>   _currentPath = new();
        private bool               _pathValid;

        private bool               _orientationLocked;
        private bool               _horizontalFirst;

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

            if (InputUtils.WasCancelPressed())
            {
                CancelConveyorMode();
                return;
            }

            Vector2Int cell = WorldToCell(InputUtils.GetPointerPosition());

            // ---- Pointer DOWN: fix start point ----
            if (InputUtils.WasPointerPressed())
            {
                gridRenderer.ClearConveyorHoverCell();
                if (IsFreecell(cell) || IsConveyorCell(cell))
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
                if (gridRenderer.IsInBounds(cell.x, cell.y) && (IsFreecell(cell) || IsConveyorCell(cell)))
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

                ResetDraw();
            }
        }

        // ----------------------------------------------------------------
        // Path calculation
        // ----------------------------------------------------------------

        private void UpdatePath()
        {
            gridRenderer.ClearConveyorGhost();

            int dx = _endCell.x - _startCell.x;
            int dy = _endCell.y - _startCell.y;

            if (dx == 0 && dy == 0)
                _orientationLocked = false;
            else if (!_orientationLocked)
            {
                _horizontalFirst   = Mathf.Abs(dx) >= Mathf.Abs(dy);
                _orientationLocked = true;
            }

            var primary   = BuildLPath(_startCell, _endCell, preferHorizontalFirst: _horizontalFirst);
            var alternate = BuildLPath(_startCell, _endCell, preferHorizontalFirst: !_horizontalFirst);

            bool primaryValid   = IsPathFree(primary);
            bool alternateValid = IsPathFree(alternate);

            List<Vector2Int> chosen;
            if (primaryValid)
                chosen = primary;
            else if (alternateValid)
                chosen = alternate;
            else
                chosen = primary;

            _currentPath = chosen;
            _pathValid   = primaryValid || alternateValid;

            // Paint ghost tiles; skip cells that are existing conveyors at the endpoints
            for (int i = 0; i < _currentPath.Count; i++)
            {
                var  c              = _currentPath[i];
                bool isExistingEndpoint =
                    (i == 0                      && IsConveyorCell(c)) ||
                    (i == _currentPath.Count - 1 && IsConveyorCell(c));
                if (!isExistingEndpoint)
                    gridRenderer.AddConveyorGhostCell(c.x, c.y);
            }

            if (_currentPath.Count > 0)
                gridRenderer.PaintConveyorEndpoints(_startCell.x, _startCell.y, _endCell.x, _endCell.y);
        }

        private static List<Vector2Int> BuildLPath(Vector2Int start, Vector2Int end, bool preferHorizontalFirst)
        {
            int dx = end.x - start.x;
            int dy = end.y - start.y;

            if (dx == 0 || dy == 0)
                return BuildStraightLine(start, end);

            Vector2Int corner = preferHorizontalFirst
                ? new Vector2Int(end.x,   start.y)
                : new Vector2Int(start.x, end.y);

            var path = new List<Vector2Int>();
            AddLineCells(path, start,  corner, skipFirst: false);
            AddLineCells(path, corner, end,    skipFirst: true);
            return path;
        }

        private static List<Vector2Int> BuildStraightLine(Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            AddLineCells(path, start, end, skipFirst: false);
            return path;
        }

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

        /// <summary>
        /// True if the path is placeable: start and end cells may be existing conveyors;
        /// all intermediate cells must be free.
        /// </summary>
        private bool IsPathFree(List<Vector2Int> path)
        {
            for (int i = 0; i < path.Count; i++)
            {
                var  c       = path[i];
                bool isFirst = (i == 0);
                bool isLast  = (i == path.Count - 1);

                if ((isFirst || isLast) && IsConveyorCell(c)) continue;

                if (!IsFreecell(c)) return false;
            }
            return true;
        }

        private bool IsFreecell(Vector2Int c) => IsFreecell(c.x, c.y);
        private bool IsFreecell(int x, int y)
        {
            if (!gridRenderer.IsInBounds(x, y)) return false;
            if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y)) return false;
            return true;
        }

        private bool IsConveyorCell(Vector2Int c) =>
            GridOccupancy.Instance != null && GridOccupancy.Instance.IsConveyorCell(c.x, c.y);

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
