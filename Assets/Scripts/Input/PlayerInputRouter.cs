using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Routes confirmed tap input to building selection, field collection, and
    /// ARCH's presence anchor. Swipe-to-pan is handled entirely by CameraController.
    ///
    /// Tap vs. swipe discrimination is <see cref="TapGesture"/> (shared with the placement modes): a press
    /// that moved past the slop, had a second finger, stopped a gliding map, rested over a second, or began
    /// on UI is not a tap.
    ///
    /// Taps fire on RELEASE (not press) so swipes never accidentally trigger gameplay.
    ///
    /// Press-and-hold on a placed building (still, for <see cref="LongPress.HoldSeconds"/>) puts it into
    /// edit mode (<see cref="BuildingPlacementController.BeginEdit"/>), with a ring showing the hold's
    /// progress. A press released before that is an ordinary tap; once edit mode starts, the release is
    /// swallowed so it never also opens the inspector.
    ///
    /// Scene setup: attach to any GameObject, wire placementController, buildingInspector and
    /// gridRenderer. CharacterMover is no longer needed.
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
        [SerializeField] private Transform                    tapAnchor;

        private readonly TapGesture _tap = new();
        private Vector2 _pressStart;
        private bool    _pressWasOnUI;
        private bool    _warnedMissingCollectorWiring;

        // ---- Press-and-hold on a building → edit mode (LongPress) ----
        private Entity           _holdEntity = Entity.Null;
        private float            _holdStart;
        private bool             _holdConsumed; // the hold entered edit mode: this press's release is not a tap
        private HoldProgressRing _holdRing;

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();
            if (buildingInspector == null)
                buildingInspector = FindAnyObjectByType<BuildingInspectorController>();
            if (placementController == null)
                placementController = FindAnyObjectByType<BuildingPlacementController>();
            if (fieldCollector == null)
                fieldCollector = FindAnyObjectByType<ManualFieldCollector>()
                                 ?? gameObject.AddComponent<ManualFieldCollector>();
        }

        void OnDisable() => CancelHold();

        void Update()
        {
            UpdateFieldHover();

            // TapGesture rules out pans, pinches, glide stops, long rests and presses that began on UI
            // (that last one also handles click-through when a panel closes on the release frame).
            bool pressed = InputUtils.WasPointerPressed();
            bool tapped  = InputUtils.PumpTap(_tap, IsPointerOverUI, out Vector2 screenPos);

            if (pressed)
            {
                _pressStart   = _tap.PressPosition;
                _pressWasOnUI = IsPointerOverUI(_pressStart);
                BeginHold();
            }

            if (InputUtils.IsPointerHeld()) UpdateHold();

            if (!InputUtils.WasPointerReleased()) return;
            bool holdConsumed = _holdConsumed;
            CancelHold();
            _holdConsumed = false;
            if (holdConsumed) return; // the press became an edit, not a tap
            if (!tapped) return;

            // Exclusive modes consume all taps
            if (AnyModeActive()) return;

            // UI may still cover the release point (a panel opened during the press).
            if (IsPointerOverUI(screenPos)) return;

            // Building inspector — tapping a placed building opens it
            bool inspectorHit = buildingInspector != null && buildingInspector.TrySelectBuildingAt(screenPos);
            if (inspectorHit)
            {
                AnchorPresence(screenPos);
                return;
            }

            // Field collection — tapping a field tile or its particle collider
            bool collectedByCell = false;
            if (fieldCollector != null && gridRenderer != null)
            {
                if (ScreenToGridCell(screenPos, out int cx, out int cy))
                    collectedByCell = fieldCollector.TryCollectAtGridCell(cx, cy);
            }
            else if (!_warnedMissingCollectorWiring)
            {
                // One-shot: this is a wiring fault, so it is the same message every tap forever.
                // Left un-gated it was a release-tier Warning firing on every single tap.
                _warnedMissingCollectorWiring = true;
                GameLogger.Warning($"[InputRouter] Field collection skipped — fieldCollector={fieldCollector}, gridRenderer={gridRenderer}. Further occurrences suppressed.");
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
            buildingInspector?.ClearSelection();
            AnchorPresence(screenPos);
        }

        // ── Press-and-hold → building edit mode ──────────────────────────────

        private bool AnyModeActive() =>
            (placementController   != null && placementController.IsPlacing)           ||
            (conveyorController    != null && conveyorController.IsPlacing)            ||
            (deconstructController != null && deconstructController.IsDeconstructing);

        /// <summary>A press on an editable building (not over UI, no mode active) starts the hold timer.</summary>
        private void BeginHold()
        {
            CancelHold();
            _holdConsumed = false;
            if (_pressWasOnUI || AnyModeActive() || buildingInspector == null || placementController == null) return;
            if (!buildingInspector.TryGetEditableBuildingAt(_pressStart, out var entity)) return;

            _holdEntity = entity;
            _holdStart  = Time.unscaledTime;
        }

        /// <summary>
        /// While the press holds still on the building: past <see cref="LongPress.RingDelay"/> show the ring
        /// filling, and at <see cref="LongPress.HoldSeconds"/> enter edit mode. Moving past the tap threshold
        /// is a pan, so it cancels the hold.
        /// </summary>
        private void UpdateHold()
        {
            if (_holdEntity == Entity.Null) return;
            // A pan, a second finger (pinch/rotate) or a glide stop can't become an edit either.
            if (_tap.IsDisqualified || AnyModeActive()) { CancelHold(); return; }

            float held = Time.unscaledTime - _holdStart;
            if (LongPress.IsComplete(held))
            {
                var entity = _holdEntity;
                CancelHold();
                _holdConsumed = true;
                buildingInspector?.ClearSelection();
                placementController.BeginEdit(entity);
                return;
            }

            if (LongPress.RingVisible(held))
                EnsureHoldRing()?.Show(InputUtils.GetPointerPosition(), LongPress.RingProgress(held));
        }

        private void CancelHold()
        {
            _holdEntity = Entity.Null;
            _holdRing?.Hide();
        }

        /// <summary>The ring lives on the HUD's UIDocument, created on first use (no scene wiring).</summary>
        private HoldProgressRing EnsureHoldRing()
        {
            if (_holdRing != null) return _holdRing;
            var hud  = FindAnyObjectByType<HUDController>();
            var root = hud != null ? hud.GetComponent<UIDocument>()?.rootVisualElement : null;
            if (root == null) return null;
            _holdRing = new HoldProgressRing();
            root.Add(_holdRing);
            return _holdRing;
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
            var     cell  = GridRenderer.WorldToCell(world, gridRenderer.CellSize);
            cx = cell.x;
            cy = cell.y;
            return true;
        }

        // UI hit-testing lives in the shared UIInputBlocker so the camera, this router, and the
        // placement/conveyor/deconstruct controllers all agree on what counts as "over UI".
        private static bool IsPointerOverUI(Vector2 screenPos) => UIInputBlocker.IsPointerOverUI(screenPos);
    }
}
