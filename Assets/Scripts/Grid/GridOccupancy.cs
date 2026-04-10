using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Tracks which grid cells are occupied by buildings.
    /// Populated at startup by BuildingVisualizer (for pre-baked SubScene buildings)
    /// and updated by BuildingPlacer when buildings are placed at runtime.
    /// </summary>
    public class GridOccupancy : MonoBehaviour
    {
        public static GridOccupancy Instance { get; private set; }

        private readonly HashSet<(int, int)> _occupied      = new();
        private readonly HashSet<(int, int)> _conveyorCells = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public bool IsOccupied(int x, int y)    => _occupied.Contains((x, y));
        public bool IsConveyorCell(int x, int y) => _conveyorCells.Contains((x, y));

        /// <summary>Marks the cell as occupied. Returns false if already taken.</summary>
        public bool TryOccupy(int x, int y)
        {
            if (_occupied.Contains((x, y))) return false;
            _occupied.Add((x, y));
            return true;
        }

        public void Release(int x, int y) => _occupied.Remove((x, y));

        /// <summary>Used by BuildingVisualizer to register pre-baked buildings.</summary>
        public void Register(int x, int y) => _occupied.Add((x, y));

        /// <summary>Marks a cell as occupied by a conveyor segment (tracked separately from buildings).</summary>
        public void RegisterConveyor(int x, int y)
        {
            _occupied.Add((x, y));
            _conveyorCells.Add((x, y));
        }

        /// <summary>Removes a conveyor cell from all occupancy tracking.</summary>
        public void UnregisterConveyor(int x, int y)
        {
            _occupied.Remove((x, y));
            _conveyorCells.Remove((x, y));
        }

        // ---- Multi-cell helpers ----

        /// <summary>Returns true if every cell in the rect is free.</summary>
        public bool IsRectFree(int x, int y, int w, int h)
        {
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    if (_occupied.Contains((x + dx, y + dy))) return false;
            return true;
        }

        /// <summary>Marks all cells in the rect as occupied. Returns false if any cell is taken (no partial writes).</summary>
        public bool TryOccupyRect(int x, int y, int w, int h)
        {
            if (!IsRectFree(x, y, w, h)) return false;
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    _occupied.Add((x + dx, y + dy));
            return true;
        }

        public void ReleaseRect(int x, int y, int w, int h)
        {
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    _occupied.Remove((x + dx, y + dy));
        }

        public void RegisterRect(int x, int y, int w, int h)
        {
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    _occupied.Add((x + dx, y + dy));
        }
    }
}
