using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages deconstruct mode.
    ///
    /// Flow:
    ///   1. HUDController calls BeginDeconstructMode() from the Buildings panel.
    ///   2. While IsDeconstructing, tapping any occupied cell destroys the building or
    ///      conveyor segment there. Mode stays active for repeated removals.
    ///   3. CancelDeconstructMode() exits (cancel button or Escape).
    ///
    /// When a conveyor segment is removed its chain neighbors are re-linked:
    ///   - The predecessor becomes a tail (NextSegment = Null).
    ///   - The successor becomes a chain head (PrevSegment = Null, IsChainHead = true).
    /// </summary>
    public class DeconstructController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private GridRenderer       gridRenderer;
        [SerializeField] private BuildingVisualizer buildingVisualizer;
        [SerializeField] private ConveyorVisualizer conveyorVisualizer;
        [SerializeField] private UIDocument         hudDocument;

        public bool IsDeconstructing { get; private set; }
        public event Action<bool> OnDeconstructingChanged;

        private EntityManager _em;
        private EntityQuery   _buildingQuery;
        private EntityQuery   _conveyorQuery;
        private bool          _ecsReady;
        private Vector2Int    _hoveredCell = new(-1, -1);

        // ================================================================
        // Unity lifecycle
        // ================================================================

        void Awake()
        {
            if (gridRenderer       == null) gridRenderer       = FindAnyObjectByType<GridRenderer>();
            if (buildingVisualizer == null) buildingVisualizer = FindAnyObjectByType<BuildingVisualizer>();
            if (conveyorVisualizer == null) conveyorVisualizer = FindAnyObjectByType<ConveyorVisualizer>();
            if (hudDocument        == null) hudDocument        = FindAnyObjectByType<UIDocument>();
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em = world.EntityManager;
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _conveyorQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<ConveyorSegmentData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _ecsReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
            {
                _buildingQuery.Dispose();
                _conveyorQuery.Dispose();
            }
        }

        // ================================================================
        // Public API
        // ================================================================

        public void BeginDeconstructMode()
        {
            IsDeconstructing = true;
            OnDeconstructingChanged?.Invoke(true);
        }

        public void CancelDeconstructMode()
        {
            gridRenderer?.ClearDeconstructHover();
            _hoveredCell = new(-1, -1);
            IsDeconstructing = false;
            OnDeconstructingChanged?.Invoke(false);
        }

        // ================================================================
        // Update loop
        // ================================================================

        void Update()
        {
            if (!IsDeconstructing) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelDeconstructMode();
                return;
            }

            Vector2 screenPos = InputUtils.GetPointerPosition();
            bool    overUI    = IsPointerOverUI(screenPos);

            // Update hover highlight every frame based on pointer position
            UpdateHoverHighlight(overUI || gridRenderer == null
                ? new Vector2Int(-1, -1)
                : WorldToCell(screenPos));

            if (!InputUtils.WasPointerPressed() || overUI) return;

            if (gridRenderer == null) return;
            Vector2Int cell = WorldToCell(screenPos);
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y)) return;

            TryDeconstructAt(cell.x, cell.y);
        }

        private void UpdateHoverHighlight(Vector2Int cell)
        {
            if (cell == _hoveredCell) return;

            gridRenderer?.ClearDeconstructHover();
            _hoveredCell = cell;

            if (gridRenderer == null) return;
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y)) return;

            var (fx, fy, fw, fh) = FindBuildingFootprintAt(cell.x, cell.y);
            gridRenderer.SetDeconstructHover(fx, fy, fw, fh);
        }

        /// <summary>
        /// Returns the origin and size of the building whose footprint contains (x, y).
        /// Falls back to 1×1 for conveyors or if ECS isn't ready.
        /// </summary>
        private (int fx, int fy, int fw, int fh) FindBuildingFootprintAt(int x, int y)
        {
            if (!_ecsReady) return (x, y, 1, 1);

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e   = entities[i];
                var pos = _em.GetComponentData<GridPosition>(e);
                int fx = pos.Cell.x, fy = pos.Cell.y;
                int fw = 1, fh = 1;

                if (_em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = _em.GetComponentData<BuildingFootprint>(e);
                    fw = fp.Width;
                    fh = fp.Height;
                }

                if (x >= fx && x < fx + fw && y >= fy && y < fy + fh)
                {
                    entities.Dispose();
                    return (fx, fy, fw, fh);
                }
            }
            entities.Dispose();

            return (x, y, 1, 1); // conveyor or unknown — 1×1
        }

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (hudDocument == null) return false;
            var panel = hudDocument.rootVisualElement?.panel;
            if (panel == null) return false;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.Pick(panelPos) != null;
        }

        // ================================================================
        // Private helpers
        // ================================================================

        private void TryDeconstructAt(int x, int y)
        {
            if (!_ecsReady) return;

            // ---- Buildings ----
            var bldgEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bldgEntities.Length; i++)
            {
                var e   = bldgEntities[i];
                var pos = _em.GetComponentData<GridPosition>(e);
                int fx = pos.Cell.x, fy = pos.Cell.y;
                int fw = 1, fh = 1;

                if (_em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = _em.GetComponentData<BuildingFootprint>(e);
                    fw = fp.Width;
                    fh = fp.Height;
                }

                // Check if the tapped cell falls within this building's footprint
                if (x < fx || x >= fx + fw || y < fy || y >= fy + fh) continue;

                _em.DestroyEntity(e);
                GridOccupancy.Instance.ReleaseRect(fx, fy, fw, fh);
                buildingVisualizer?.RemoveBuilding(fx, fy);
                gridRenderer?.ClearDeconstructHover();
                _hoveredCell = new(-1, -1);
                bldgEntities.Dispose();
                Debug.Log($"[Deconstruct] Removed building at ({fx},{fy}).");
                return;
            }
            bldgEntities.Dispose();

            // ---- Conveyor segments ----
            var convEntities = _conveyorQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < convEntities.Length; i++)
            {
                var e   = convEntities[i];
                var pos = _em.GetComponentData<GridPosition>(e);
                if (pos.Cell.x != x || pos.Cell.y != y) continue;

                var seg = _em.GetComponentData<ConveyorSegmentData>(e);

                // Unlink from predecessor — it becomes the new tail
                if (seg.PrevSegment != Entity.Null && _em.Exists(seg.PrevSegment))
                {
                    var prev = _em.GetComponentData<ConveyorSegmentData>(seg.PrevSegment);
                    prev.NextSegment = Entity.Null;
                    _em.SetComponentData(seg.PrevSegment, prev);
                }

                // Unlink from successor — it becomes the new chain head
                if (seg.NextSegment != Entity.Null && _em.Exists(seg.NextSegment))
                {
                    var next = _em.GetComponentData<ConveyorSegmentData>(seg.NextSegment);
                    next.PrevSegment = Entity.Null;
                    next.IsChainHead = true;
                    _em.SetComponentData(seg.NextSegment, next);
                }

                _em.DestroyEntity(e);
                GridOccupancy.Instance.Release(x, y);
                conveyorVisualizer?.RemoveBelt(x, y);
                gridRenderer?.ClearDeconstructHover();
                _hoveredCell = new(-1, -1);
                convEntities.Dispose();
                Debug.Log($"[Deconstruct] Removed conveyor segment at ({x},{y}).");
                return;
            }
            convEntities.Dispose();
        }

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);

            float t     = -ray.origin.y / ray.direction.y;
            var   world = ray.origin + ray.direction * t;

            int cx = Mathf.FloorToInt(world.x / gridRenderer.CellSize);
            int cy = Mathf.FloorToInt(world.z / gridRenderer.CellSize);
            return new(cx, cy);
        }
    }
}
