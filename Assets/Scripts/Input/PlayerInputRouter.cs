using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Routes confirmed tap input to building selection, field collection, and
    /// ARCH's presence anchor. Swipe-to-pan is handled entirely by CameraController.
    ///
    /// Tap vs. swipe discrimination:
    ///   - On pointer DOWN  : record start position.
    ///   - While held       : accumulate drag distance.
    ///   - On pointer UP    : if drag distance &lt; CameraController.TapThreshold → treat as tap.
    ///
    /// Taps fire on RELEASE (not press) so swipes never accidentally trigger gameplay.
    ///
    /// Scene setup: attach to any GameObject, wire placementController, buildingInspector,
    /// gridRenderer, and hudDocument. CharacterMover is no longer needed.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class PlayerInputRouter : MonoBehaviour
    {
        [SerializeField] private BuildingPlacementController  placementController;
        [SerializeField] private ConveyorPlacementController  conveyorController;
        [SerializeField] private DeconstructController        deconstructController;
        [SerializeField] private BuildingInspectorController  buildingInspector;
        private ManualFieldCollector         fieldCollector;
        [SerializeField] private GridRenderer                 gridRenderer;
        [SerializeField] private UIDocument                   hudDocument;
        [SerializeField] private Transform                    tapAnchor;

        private Vector2 _pressStart;
        private float   _dragAccum;
        private bool    _pressWasOnUI;

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();
            if (buildingInspector == null)
                buildingInspector = FindAnyObjectByType<BuildingInspectorController>();
            if (fieldCollector == null)
                fieldCollector = FindAnyObjectByType<ManualFieldCollector>()
                                 ?? gameObject.AddComponent<ManualFieldCollector>();
        }

        void Update()
        {
            UpdateFieldHover();

            // Track gesture on press start
            if (InputUtils.WasPointerPressed())
            {
                _pressStart    = InputUtils.GetPointerPosition();
                _dragAccum     = 0f;
                _pressWasOnUI  = IsPointerOverUI(_pressStart);
            }

            // Accumulate drag while held
            if (InputUtils.IsPointerHeld())
                _dragAccum = Vector2.Distance(InputUtils.GetPointerPosition(), _pressStart);

            // Only act on confirmed tap (release + small drag)
            if (!InputUtils.WasPointerReleased()) return;
            if (_dragAccum > CameraController.TapThreshold) return;

            // Exclusive modes consume all taps
            if (placementController   != null && placementController.IsPlacing)          return;
            if (conveyorController    != null && conveyorController.IsPlacing)           return;
            if (deconstructController != null && deconstructController.IsDeconstructing) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            // Block the tap if the press started on UI (handles click-through when panels
            // close on the same frame as the release) or if UI still covers the release pos.
            if (_pressWasOnUI || IsPointerOverUI(screenPos)) return;

            // Building inspector — tapping a placed building opens it
            if (buildingInspector != null && buildingInspector.TrySelectBuildingAt(screenPos))
            {
                fieldCollector?.DeactivateField();
                AnchorPresence(screenPos);
                return;
            }

            // Field collection — tapping a field tile or its particle collider
            bool collectedByCell = false;
            if (fieldCollector != null && gridRenderer != null)
            {
                if (ScreenToGridCell(screenPos, out int cx, out int cy))
                {
                    GameLogger.Develop($"[InputRouter] Tap → grid cell ({cx},{cy}), field={FieldGenerator.GetFieldAt(cx,cy)?.displayName ?? "none"}");
                    collectedByCell = fieldCollector.TryCollectAtGridCell(cx, cy);
                    GameLogger.Develop($"[InputRouter] TryCollectAtGridCell={collectedByCell}");
                }
            }
            else
            {
                GameLogger.Warning($"[InputRouter] Field collection skipped — fieldCollector={fieldCollector}, gridRenderer={gridRenderer}");
            }

            if (!collectedByCell && fieldCollector != null && fieldCollector.TryCollect(screenPos))
            {
                AnchorPresence(screenPos);
                return;
            }

            if (collectedByCell)
            {
                AnchorPresence(screenPos);
                return;
            }

            // Tap on empty ground — clear any building selection and anchor presence
            fieldCollector?.DeactivateField();
            buildingInspector?.ClearSelection();
            AnchorPresence(screenPos);
        }

        // ── Presence anchor ───────────────────────────────────────────────────

        /// <summary>
        /// Converts a screen-space tap to a world position on the Y=0 ground plane
        /// and sends it to PresenceSystem as the new anchor.
        /// </summary>
        private void AnchorPresence(Vector2 screenPos)
        {
            if (Camera.main == null) { GameLogger.Warning("[InputRouter] AnchorPresence — Camera.main is null"); return; }
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) { GameLogger.Warning("[InputRouter] AnchorPresence — degenerate ray"); return; }
            float   t        = -ray.origin.y / ray.direction.y;
            Vector3 worldPos = ray.origin + ray.direction * t;
            if (tapAnchor != null) tapAnchor.position = worldPos;
            var ps = PresenceSystem.Instance;
            if (ps != null) ps.SetAnchor(worldPos);
        }

        // ── Field hover highlight ─────────────────────────────────────────────

        private void UpdateFieldHover()
        {
            if (gridRenderer == null) return;

            bool busy = (placementController   != null && placementController.IsPlacing)           ||
                        (conveyorController    != null && conveyorController.IsPlacing)            ||
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

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Maps a screen position to a grid cell on the Y=0 ground plane.</summary>
        private bool ScreenToGridCell(Vector2 screenPos, out int cx, out int cy)
        {
            cx = cy = -1;
            if (Camera.main == null || gridRenderer == null) return false;
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float   t     = -ray.origin.y / ray.direction.y;
            Vector3 world = ray.origin + ray.direction * t;
            float   cs    = gridRenderer.CellSize;
            cx = Mathf.FloorToInt(world.x / cs + 0.5f);
            cy = Mathf.FloorToInt(world.z / cs + 0.5f);
            return true;
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
    }
}
