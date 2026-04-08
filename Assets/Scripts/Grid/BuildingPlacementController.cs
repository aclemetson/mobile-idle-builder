using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages building placement mode.
    ///
    /// Flow:
    ///   1. HUDController calls BeginPlacement(entry) when player taps a building card.
    ///   2. While IsPlacing, a ghost tile follows the pointer on the grid.
    ///      Cyan = valid empty cell.  Red = occupied or out of bounds.
    ///   3. Player taps a valid cell → building is placed, mode ends.
    ///      Player taps an invalid cell → nothing placed, mode continues.
    ///   4. CancelPlacement() exits placement mode (called by cancel button or Escape).
    ///
    /// Scene setup: place on any GameObject, assign gridRenderer, buildingPlacer,
    ///              buildingVisualizer references in the Inspector.
    /// </summary>
    public class BuildingPlacementController : MonoBehaviour
    {
        [Serializable]
        public struct BuildingEntry
        {
            public BuildingSO building;
            public RecipeSO   defaultRecipe;
        }

        [Header("Available buildings (shown in the selection panel)")]
        public BuildingEntry[] availableBuildings;

        [Header("Scene references")]
        [SerializeField] private GridRenderer       gridRenderer;
        [SerializeField] private BuildingPlacer     buildingPlacer;
        [SerializeField] private BuildingVisualizer buildingVisualizer;

        public bool IsPlacing { get; private set; }

        private BuildingEntry _pending;
        private Vector2Int    _lastGhostCell = new(-1, -1);

        // Raised when placement mode begins/ends so HUDController can show/hide the overlay
        public event Action<bool> OnPlacingChanged;

        public void BeginPlacement(BuildingEntry entry)
        {
            _pending       = entry;
            _lastGhostCell = new(-1, -1);
            IsPlacing      = true;
            OnPlacingChanged?.Invoke(true);
        }

        public void CancelPlacement()
        {
            gridRenderer.HideGhost();
            _lastGhostCell = new(-1, -1);
            IsPlacing      = false;
            OnPlacingChanged?.Invoke(false);
        }

        void Update()
        {
            if (!IsPlacing) return;

            // Escape cancels
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            Vector2 pointerPos = GetPointerPosition();
            Vector2Int cell    = WorldToCell(pointerPos);

            // Update ghost when the hovered cell changes
            if (cell != _lastGhostCell)
            {
                bool valid = gridRenderer.IsInBounds(cell.x, cell.y) &&
                             (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y));
                gridRenderer.ShowGhost(cell.x, cell.y, valid);
                _lastGhostCell = cell;
            }

            // Confirm placement on click/tap (re-check IsPlacing in case Cancel fired this frame)
            if (IsPlacing && WasPointerPressed())
            {
                bool inBounds = gridRenderer.IsInBounds(cell.x, cell.y);
                bool free     = GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y);

                if (inBounds && free)
                {
                    gridRenderer.HideGhost();
                    bool placed = buildingPlacer.PlaceBuilding(cell.x, cell.y, _pending.building, _pending.defaultRecipe);
                    if (placed)
                    {
                        gridRenderer.SetTileHighlight(cell.x, cell.y, true);
                        buildingVisualizer.Refresh();
                    }
                    IsPlacing = false;
                    OnPlacingChanged?.Invoke(false);
                }
                // Invalid cell — ghost stays red, player must move to a valid cell
            }
        }

        // ---- Input helpers — delegate to shared InputUtils ----

        private static Vector2 GetPointerPosition() => InputUtils.GetPointerPosition();
        private static bool    WasPointerPressed()  => InputUtils.WasPointerPressed();

        // ---- Grid helpers ----

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
