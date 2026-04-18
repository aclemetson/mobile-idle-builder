using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Routes tap input to character movement when not in building placement mode.
    /// Taps that land on the HUD are ignored — the UI intercepts them first.
    /// When IsPlacing is true, BuildingPlacementController owns the tap — this router
    /// does nothing so there's no conflict.
    ///
    /// Scene setup: attach to any GameObject, wire placementController, characterMover,
    /// and hudDocument (the UIDocument used by the HUD).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class PlayerInputRouter : MonoBehaviour
    {
        [SerializeField] private BuildingPlacementController  placementController;
        [SerializeField] private ConveyorPlacementController  conveyorController;
        [SerializeField] private DeconstructController        deconstructController;
        [SerializeField] private BuildingInspectorController  buildingInspector;
        [SerializeField] private ManualFieldCollector         fieldCollector;
        [SerializeField] private CharacterMover               characterMover;
        [SerializeField] private GridRenderer                 gridRenderer;
        [SerializeField] private UIDocument                   hudDocument;

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();
            if (buildingInspector == null)
                buildingInspector = FindAnyObjectByType<BuildingInspectorController>();
        }

        void Update()
        {
            UpdateFieldHover();

            if (!InputUtils.WasPointerPressed()) return;
            if (placementController   != null && placementController.IsPlacing)         return;
            if (conveyorController    != null && conveyorController.IsPlacing)          return;
            if (deconstructController != null && deconstructController.IsDeconstructing) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            if (IsPointerOverUI(screenPos)) return;

            // Tapping a placed building opens the inspector instead of moving the character.
            if (buildingInspector != null && buildingInspector.TrySelectBuildingAt(screenPos))
                return;

            // Tapping a field tile or its particle collider collects from it.
            // Grid-cell path is primary (works for the whole visible tile area);
            // raycast fallback handles taps directly on the 3D particle object.
            bool collectedByCell = false;
            if (fieldCollector != null && gridRenderer != null)
            {
                if (ScreenToGridCell(screenPos, out int cx, out int cy))
                    collectedByCell = fieldCollector.TryCollectAtGridCell(cx, cy);
            }
            if (!collectedByCell && fieldCollector != null && fieldCollector.TryCollect(screenPos))
                return;
            if (collectedByCell)
                return;

            // Tapping empty ground clears any building selection and moves the character.
            buildingInspector?.ClearSelection();

            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

            // Intersect the ray with the Y=0 ground plane
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return;
            float   t        = (0f - ray.origin.y) / ray.direction.y;
            Vector3 worldPos = ray.origin + ray.direction * t;

            characterMover.SetMoveTarget(worldPos);
        }

        // ---- Field hover highlight ----

        private void UpdateFieldHover()
        {
            if (gridRenderer == null) return;

            // Don't show field hover during any exclusive placement/deconstruct mode
            bool busy = (placementController   != null && placementController.IsPlacing)          ||
                        (conveyorController    != null && conveyorController.IsPlacing)           ||
                        (deconstructController != null && deconstructController.IsDeconstructing);
            if (busy) { gridRenderer.ClearFieldHoverCell(); return; }

            Vector2 screenPos = InputUtils.GetPointerPosition();
            if (screenPos == Vector2.zero) { gridRenderer.ClearFieldHoverCell(); return; }

            if (!ScreenToGridCell(screenPos, out int cx, out int cy)) { gridRenderer.ClearFieldHoverCell(); return; }

            bool isField    = FieldGenerator.GetFieldAt(cx, cy) != null;
            bool isOccupied = GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(cx, cy);

            if (isField && !isOccupied)
                gridRenderer.SetFieldHoverCell(cx, cy);
            else
                gridRenderer.ClearFieldHoverCell();
        }

        /// <summary>
        /// Maps a screen position to a grid cell on the Y=0 ground plane.
        /// Returns false if the ray is nearly horizontal (degenerate) or there is no camera.
        /// </summary>
        private bool ScreenToGridCell(Vector2 screenPos, out int cx, out int cy)
        {
            cx = cy = -1;
            if (Camera.main == null || gridRenderer == null) return false;
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float   t     = -ray.origin.y / ray.direction.y;
            Vector3 world = ray.origin + ray.direction * t;
            // Tile centers sit at x*cellSize (not (x+0.5)*cellSize), so +0.5 before floor
            // snaps to the nearest tile center rather than the lower-left cell corner.
            float cs = gridRenderer.CellSize;
            cx = Mathf.FloorToInt(world.x / cs + 0.5f);
            cy = Mathf.FloorToInt(world.z / cs + 0.5f);
            return true;
        }

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (hudDocument == null) return false;
            var panel = hudDocument.rootVisualElement?.panel;
            if (panel == null) return false;

            // UIToolkit panel space has Y=0 at the top; Unity screen space has Y=0 at the bottom
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screenPos.x, Screen.height - screenPos.y));

            return panel.Pick(panelPos) != null;
        }
    }
}
