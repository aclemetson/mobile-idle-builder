using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Replaces IsometricCameraFollow. The camera pans by swipe and zooms by pinch
    /// or scroll wheel. There is no character to follow — ARCH's presence is purely
    /// environmental, driven by PresenceSystem.
    ///
    /// Tutorial API is API-compatible with the old IsometricCameraFollow:
    ///   PanTo(worldPos)  — smoothly pan the camera to look at a world point
    ///   ResumeFollow()   — clear the pan target (returns camera to free movement)
    ///
    /// Tap-vs-swipe threshold is exposed as TapThreshold so PlayerInputRouter
    /// can use the same value for consistent gesture discrimination.
    ///
    /// Attach to Main Camera. Assign gridRenderer in the Inspector.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private Vector3      offset           = new Vector3(0f, 8f, -6f);
        [SerializeField] private float        minFOV           = 30f;
        [SerializeField] private float        maxFOV           = 80f;
        [SerializeField] private float        zoomSpeed        = 0.05f;   // FOV units per pinch pixel
        [SerializeField] private float        scrollZoom       = 4f;      // FOV units per scroll tick
        [SerializeField] private float        tutorialPanSpeed = 4f;
        [SerializeField] private GridRenderer gridRenderer;

        // ── Shared threshold (PlayerInputRouter reads this) ──────────────────
        public const float TapThreshold = 20f;  // pixels

        // ── Tutorial pan API (matches old IsometricCameraFollow) ─────────────
        public void PanTo(Vector3 worldPos) => _panTarget = worldPos;
        public void ResumeFollow()          => _panTarget = null;

        /// <summary>True while the active pointer gesture qualifies as a drag, not a tap.</summary>
        public bool IsPanning => _isPanActive && _dragAccum > TapThreshold;

        // ── Private state ────────────────────────────────────────────────────
        private Camera   _cam;
        private Vector2  _panPrev;
        private bool     _isPanActive;
        private float    _dragAccum;
        private Vector3? _panTarget;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Start()
        {
            _cam              = GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView  = 60f;
            transform.rotation = Quaternion.LookRotation(-offset.normalized);

            if (gridRenderer != null)
            {
                float cx = (gridRenderer.Width  - 1) * gridRenderer.CellSize * 0.5f;
                float cz = (gridRenderer.Height - 1) * gridRenderer.CellSize * 0.5f;
                transform.position = new Vector3(cx, 0f, cz) + offset;
            }
        }

        void LateUpdate()
        {
            HandleSwipePan();
            HandleZoom();

            // Smooth tutorial pan
            if (_panTarget.HasValue)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    _panTarget.Value + offset,
                    tutorialPanSpeed * Time.unscaledDeltaTime);
            }
        }

        // ── Swipe pan ────────────────────────────────────────────────────────

        private void HandleSwipePan()
        {
            // Two-finger gestures are owned by pinch-zoom; skip single-finger pan
            if (ActiveTouchCount() >= 2)
            {
                _isPanActive = false;
                return;
            }

            if (InputUtils.WasPointerPressed())
            {
                _panPrev     = InputUtils.GetPointerPosition();
                _isPanActive = true;
                _dragAccum   = 0f;
            }

            if (InputUtils.IsPointerHeld() && _isPanActive)
            {
                Vector2 cur   = InputUtils.GetPointerPosition();
                float   delta = Vector2.Distance(cur, _panPrev);
                _dragAccum += delta;

                if (_dragAccum > TapThreshold)
                {
                    // Player manually panning cancels any active tutorial pan
                    _panTarget = null;

                    Vector3 prevGround = ScreenToGround(_panPrev);
                    Vector3 curGround  = ScreenToGround(cur);
                    Vector3 worldDelta = prevGround - curGround; // negative so the grid follows the finger

                    transform.position = ClampToBounds(transform.position + worldDelta);
                }

                _panPrev = cur;
            }

            if (InputUtils.WasPointerReleased())
                _isPanActive = false;
        }

        // ── Zoom ─────────────────────────────────────────────────────────────

        private void HandleZoom()
        {
            // Pinch zoom (touch)
            if (ActiveTouchCount() >= 2)
            {
                var t0   = Touchscreen.current.touches[0];
                var t1   = Touchscreen.current.touches[1];
                var pos0 = t0.position.ReadValue();
                var pos1 = t1.position.ReadValue();
                var prv0 = pos0 - t0.delta.ReadValue();
                var prv1 = pos1 - t1.delta.ReadValue();

                float prevDist = Vector2.Distance(prv0, prv1);
                float currDist = Vector2.Distance(pos0, pos1);

                if (prevDist > 0f)
                    _cam.fieldOfView = Mathf.Clamp(
                        _cam.fieldOfView - (currDist - prevDist) * zoomSpeed,
                        minFOV, maxFOV);
            }

            // Scroll wheel (editor / desktop)
            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.y.ReadValue();
                if (Mathf.Abs(scroll) > 0.01f)
                    _cam.fieldOfView = Mathf.Clamp(
                        _cam.fieldOfView - scroll * scrollZoom,
                        minFOV, maxFOV);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the number of currently pressed touches.
        /// Iterates the fixed-size touch array rather than relying on EnhancedTouch,
        /// which requires an explicit Enable() call.
        /// </summary>
        private static int ActiveTouchCount()
        {
            if (Touchscreen.current == null) return 0;
            int count = 0;
            foreach (var touch in Touchscreen.current.touches)
                if (touch.press.isPressed) count++;
            return count;
        }

        /// <summary>
        /// Projects a screen-space point onto the Y=0 ground plane.
        /// Returns the current camera position if the ray is degenerate.
        /// </summary>
        private Vector3 ScreenToGround(Vector2 screenPos)
        {
            var ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return transform.position;
            float t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }

        /// <summary>
        /// Clamps the camera position so that its ground pivot stays within the
        /// grid bounds plus a small margin.
        /// </summary>
        private Vector3 ClampToBounds(Vector3 camPos)
        {
            if (gridRenderer == null) return camPos;

            float cs     = gridRenderer.CellSize;
            float maxX   = (gridRenderer.Width  - 1) * cs;
            float maxZ   = (gridRenderer.Height - 1) * cs;
            float margin = cs * 2f;

            Vector3 pivot = camPos - offset;
            pivot.x = Mathf.Clamp(pivot.x, -margin, maxX + margin);
            pivot.z = Mathf.Clamp(pivot.z, -margin, maxZ + margin);
            return pivot + offset;
        }
    }
}
