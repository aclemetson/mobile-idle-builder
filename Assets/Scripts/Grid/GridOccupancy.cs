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

        private readonly HashSet<(int, int)> _occupied = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public bool IsOccupied(int x, int y) => _occupied.Contains((x, y));

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
    }
}
