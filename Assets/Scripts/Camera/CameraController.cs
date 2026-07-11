using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Replaces IsometricCameraFollow. The camera orbits a ground pivot: it pans by
    /// single-finger swipe, zooms by pinch or scroll wheel, and rotates by two-finger
    /// swipe (mobile) or middle-mouse drag (desktop). There is no character to follow —
    /// ARCH's presence is purely environmental, driven by PresenceSystem.
    ///
    /// Orbit model: the camera position is always derived from a focal point on the
    /// Y=0 plane (_pivot) plus a spherical offset built from _yaw (rotation around Y),
    /// _pitch (angle above the ground, clamped between minPitch and 90), and _distance.
    /// The serialized <see cref="offset"/> only seeds the initial yaw/pitch/distance so
    /// the launch view matches the old fixed isometric angle.
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
        [SerializeField] private float        minPitch         = 10f;     // near-horizon (degrees above ground)
        [SerializeField] private float        maxPitch         = 90f;     // straight down
        [SerializeField] private float        rotateSpeedTouch = 0.2f;    // degrees per pixel of two-finger vertical (tilt) move
        [SerializeField] private float        rotateSpeedTwist = 1f;      // yaw degrees per degree of two-finger twist
        [SerializeField] private float        rotateSpeedMouse = 0.2f;    // degrees per pixel of middle-mouse drag
        [SerializeField] private float        panInertiaDamping = 6f;     // higher = pan glide stops sooner after release
        [SerializeField] private GridRenderer gridRenderer;

        // ── Player camera preferences (read live from SettingsService) ────────
        private static float PanSensitivity => SettingsService.Instance?.Current?.panSensitivity ?? 1f;
        private static bool  InvertTilt     => SettingsService.Instance?.Current?.invertTilt     ?? false;
        // Default tilt is flipped from the raw drag direction; the invert setting restores it.
        private static float TiltSign       => InvertTilt ? 1f : -1f;

        // ── Shared threshold (PlayerInputRouter reads this) ──────────────────
        public const float TapThreshold = 20f;  // pixels

        // ── Tutorial pan API (matches old IsometricCameraFollow) ─────────────
        public void PanTo(Vector3 worldPos) => _panTarget = worldPos;
        public void ResumeFollow()          => _panTarget = null;

        /// <summary>True while the active pointer gesture qualifies as a drag, not a tap.</summary>
        public bool IsPanning => _isPanActive && _dragAccum > TapThreshold;

        // ── Orbit rig (authoritative camera state) ───────────────────────────
        private Vector3 _pivot;     // focal point on the Y=0 ground plane
        private float   _yaw;       // degrees around Y
        private float   _pitch;     // degrees above ground (clamped minPitch..maxPitch)
        private float   _distance;  // camera distance from pivot

        // ── Private state ────────────────────────────────────────────────────
        private Camera   _cam;
        private Vector2  _panPrev;
        private bool     _isPanActive;
        private bool     _panLocked;
        private bool     _pressOverUI;
        private float    _dragAccum;
        private Vector3? _panTarget;
        private Vector2  _mouseOrbitPrev;
        private bool     _isMouseOrbiting;
        private Vector3  _panVelocity;   // world units/sec, drives inertial glide after a pan release

        public void SetPanLocked(bool locked) => _panLocked = locked;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Start()
        {
            _cam              = GetComponent<Camera>();
            _cam.orthographic = false;
            _cam.fieldOfView  = 60f;

            // Seed the orbit rig from the configured offset so the launch view is
            // identical to the old fixed isometric angle.
            _distance      = offset.magnitude;
            float horizLen = new Vector2(offset.x, offset.z).magnitude;
            _pitch         = Mathf.Clamp(Mathf.Atan2(offset.y, horizLen) * Mathf.Rad2Deg, minPitch, maxPitch);
            _yaw           = Mathf.Atan2(offset.x, -offset.z) * Mathf.Rad2Deg;

            if (gridRenderer != null)
            {
                float cx = (gridRenderer.Width  - 1) * gridRenderer.CellSize * 0.5f;
                float cz = (gridRenderer.Height - 1) * gridRenderer.CellSize * 0.5f;
                _pivot   = new Vector3(cx, 0f, cz);
            }

            ApplyRig();
        }

        void LateUpdate()
        {
            HandleSwipePan();
            HandleZoom();
            HandleMouseOrbit();

            // Inertial glide: after a pan release, keep drifting and decelerate so the map
            // feels natural rather than snapping to a dead stop. Suppressed while actively
            // panning, locked, or following a tutorial pan.
            if (!_isPanActive && !_panLocked && !_panTarget.HasValue && _panVelocity.sqrMagnitude > 1e-4f)
            {
                _pivot       = ClampPivot(_pivot + _panVelocity * Time.unscaledDeltaTime);
                _panVelocity = DecayVelocity(_panVelocity, panInertiaDamping, Time.unscaledDeltaTime);
                if (_panVelocity.sqrMagnitude < 1e-4f) _panVelocity = Vector3.zero;
            }

            // Smooth tutorial pan toward the requested focal point.
            if (_panTarget.HasValue)
                _pivot = Vector3.Lerp(_pivot, _panTarget.Value, tutorialPanSpeed * Time.unscaledDeltaTime);

            ApplyRig();
        }

        // ── Swipe pan ────────────────────────────────────────────────────────

        private void HandleSwipePan()
        {
            if (_panLocked) { _isPanActive = false; return; }

            // Two-finger gestures are owned by pinch-zoom / rotate; skip single-finger pan
            // and cancel any inertial glide so the map doesn't drift while zooming/rotating.
            if (ActiveTouchCount() >= 2)
            {
                _isPanActive = false;
                _panVelocity = Vector3.zero;
                return;
            }

            if (InputUtils.WasPointerPressed())
            {
                _panPrev     = InputUtils.GetPointerPosition();
                // A press that starts on a menu/panel/button must never pan the map.
                _pressOverUI = UIInputBlocker.IsPointerOverUI(_panPrev);
                _isPanActive = !_pressOverUI;
                _dragAccum   = 0f;
                _panVelocity = Vector3.zero; // touching down halts any inertial glide
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
                    // negative so the grid follows the finger; scaled by the sensitivity preference
                    Vector3 worldDelta = (prevGround - curGround) * PanSensitivity;

                    _pivot = ClampPivot(_pivot + worldDelta);

                    // Track a smoothed velocity so releasing the drag flings the map.
                    if (Time.unscaledDeltaTime > 0f)
                    {
                        Vector3 instantV = worldDelta / Time.unscaledDeltaTime;
                        _panVelocity = Vector3.Lerp(_panVelocity, instantV, 0.5f);
                    }
                }

                _panPrev = cur;
            }

            if (InputUtils.WasPointerReleased())
                _isPanActive = false; // _panVelocity persists -> inertial glide in LateUpdate
        }

        // ── Zoom + two-finger rotate ──────────────────────────────────────────

        private void HandleZoom()
        {
            // Two-finger touch: pinch distance drives zoom, centroid movement drives rotate.
            if (ActiveTouchCount() >= 2)
            {
                var t0   = Touchscreen.current.touches[0];
                var t1   = Touchscreen.current.touches[1];
                var pos0 = t0.position.ReadValue();
                var pos1 = t1.position.ReadValue();
                var prv0 = pos0 - t0.delta.ReadValue();
                var prv1 = pos1 - t1.delta.ReadValue();

                // Zoom from the change in finger SPACING (radial component of the gesture).
                float prevDist = Vector2.Distance(prv0, prv1);
                float currDist = Vector2.Distance(pos0, pos1);

                if (prevDist > 0f)
                    _cam.fieldOfView = Mathf.Clamp(
                        _cam.fieldOfView - (currDist - prevDist) * zoomSpeed,
                        minFOV, maxFOV);

                // Rotate/tilt, decoupled from zoom so the two gestures don't fight:
                //  - yaw   from TWIST (change in the finger-to-finger angle) — orthogonal to pinch spacing
                //  - pitch from the vertical movement of the fingers' centroid (tilt)
                // Frozen while the camera is locked (e.g. Maxwell's Demon minigame).
                if (!_panLocked)
                {
                    _yaw += SignedAngleDelta(prv0, prv1, pos0, pos1) * rotateSpeedTwist;

                    float centroidDeltaY = ((pos0.y + pos1.y) - (prv0.y + prv1.y)) * 0.5f;
                    _pitch = Mathf.Clamp(
                        _pitch + centroidDeltaY * rotateSpeedTouch * TiltSign,
                        minPitch, maxPitch);

                    _panTarget = null; // manual rotate cancels any tutorial pan
                }
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

        // ── Middle-mouse orbit (desktop) ──────────────────────────────────────

        private void HandleMouseOrbit()
        {
            if (_panLocked) { _isMouseOrbiting = false; return; }
            if (Mouse.current == null) return;

            if (Mouse.current.middleButton.wasPressedThisFrame)
            {
                _mouseOrbitPrev  = Mouse.current.position.ReadValue();
                _isMouseOrbiting = true;
            }

            if (_isMouseOrbiting && Mouse.current.middleButton.isPressed)
            {
                Vector2 cur    = Mouse.current.position.ReadValue();
                Vector2 cDelta = cur - _mouseOrbitPrev;
                ApplyRotate(cDelta, rotateSpeedMouse);
                _mouseOrbitPrev = cur;
                _panTarget      = null; // manual rotate cancels any tutorial pan
            }

            if (Mouse.current.middleButton.wasReleasedThisFrame)
                _isMouseOrbiting = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Applies a screen-space drag delta to yaw (horizontal) and pitch (vertical).
        /// Used by the desktop middle-mouse orbit. Pitch honours the invert-tilt preference
        /// and is clamped to [minPitch, maxPitch].
        /// </summary>
        private void ApplyRotate(Vector2 screenDelta, float speed)
        {
            _yaw  += screenDelta.x * speed;
            _pitch = Mathf.Clamp(_pitch + screenDelta.y * speed * TiltSign, minPitch, maxPitch);
        }

        /// <summary>
        /// Signed change in the angle of the vector between two fingers, in degrees
        /// (the "twist" of a two-finger gesture). Positive = counter-clockwise. Pure
        /// function so it stays orthogonal to pinch spacing and is unit-testable.
        /// </summary>
        public static float SignedAngleDelta(Vector2 prevA, Vector2 prevB, Vector2 curA, Vector2 curB)
        {
            Vector2 prev = prevB - prevA;
            Vector2 cur  = curB  - curA;
            if (prev.sqrMagnitude < 1e-6f || cur.sqrMagnitude < 1e-6f) return 0f;
            return Vector2.SignedAngle(prev, cur);
        }

        /// <summary>
        /// One step of exponential velocity decay toward zero. Pure function (unit-tested).
        /// </summary>
        public static Vector3 DecayVelocity(Vector3 velocity, float damping, float dt)
            => Vector3.Lerp(velocity, Vector3.zero, Mathf.Clamp01(damping * dt));

        /// <summary>
        /// Recomputes the camera transform from the orbit rig so it always looks at the pivot.
        /// </summary>
        private void ApplyRig()
        {
            Vector3 off        = OrbitOffset(_yaw, _pitch, _distance);
            transform.position = _pivot + off;
            transform.rotation = Quaternion.LookRotation(-off);
        }

        /// <summary>
        /// Spherical camera offset from a ground pivot. Pure function (unit-tested):
        /// yaw rotates around Y, pitch is the angle above the ground plane, distance is
        /// the radius. At yaw 0 the camera sits behind the pivot (-Z), matching the
        /// original fixed offset convention; at pitch 90 it sits straight overhead.
        /// </summary>
        public static Vector3 OrbitOffset(float yawDeg, float pitchDeg, float distance)
        {
            float p     = pitchDeg * Mathf.Deg2Rad;
            float y     = yawDeg   * Mathf.Deg2Rad;
            float horiz = distance * Mathf.Cos(p);
            return new Vector3(
                horiz * Mathf.Sin(y),
                distance * Mathf.Sin(p),
                -horiz * Mathf.Cos(y));
        }

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
        /// Returns the current pivot if the ray is degenerate (near-horizontal view).
        /// </summary>
        private Vector3 ScreenToGround(Vector2 screenPos)
        {
            var ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return _pivot;
            float t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }

        /// <summary>
        /// Clamps the ground pivot so it stays within the grid bounds plus a small margin.
        /// </summary>
        private Vector3 ClampPivot(Vector3 pivot)
        {
            if (gridRenderer == null) return pivot;

            float cs     = gridRenderer.CellSize;
            float maxX   = (gridRenderer.Width  - 1) * cs;
            float maxZ   = (gridRenderer.Height - 1) * cs;
            float margin = cs * 2f;

            pivot.x = Mathf.Clamp(pivot.x, -margin, maxX + margin);
            pivot.y = 0f;
            pivot.z = Mathf.Clamp(pivot.z, -margin, maxZ + margin);
            return pivot;
        }
    }
}
