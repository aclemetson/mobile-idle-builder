using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Diablo-style isometric camera that follows the character at a fixed angle and distance.
    /// Attach to the Main Camera. Overrides orthographic with perspective projection.
    ///
    /// Tutorial panning: call PanTo(worldPos) to smoothly move the camera to any world point
    /// while keeping the isometric angle. Call ResumeFollow() to return to the character.
    /// The lerp in LateUpdate handles both the pan and the return — no cuts, no extra setup.
    ///
    /// NOTE: do NOT add a CinemachineBrain to this camera. This script is the sole driver.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class IsometricCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3   offset      = new Vector3(0f, 8f, -6f);
        [SerializeField] private float     followSpeed = 8f;
        [SerializeField] private float     panSpeed    = 4f;   // slightly slower for a cinematic feel

        private Quaternion _fixedRotation;
        private Vector3?   _panTarget;   // null = follow character; non-null = pan to this point

        // ── Tutorial pan API ─────────────────────────────────────────────────

        /// <summary>Smoothly pan the camera to look at <paramref name="worldPos"/>.</summary>
        public void PanTo(Vector3 worldPos) => _panTarget = worldPos;

        /// <summary>Return the camera to following the player character.</summary>
        public void ResumeFollow() => _panTarget = null;

        // ── Unity lifecycle ──────────────────────────────────────────────────

        void Start()
        {
            var cam = GetComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView  = 60f;

            _fixedRotation     = Quaternion.LookRotation(-offset.normalized);
            transform.rotation = _fixedRotation;

            // Snap to the character immediately on the first frame
            if (target != null)
                transform.position = target.position + offset;
        }

        void LateUpdate()
        {
            if (target == null) return;

            // Use pan target if active; otherwise follow the character
            var  followPoint = _panTarget ?? target.position;
            float speed      = _panTarget.HasValue ? panSpeed : followSpeed;

            transform.position = Vector3.Lerp(
                transform.position,
                followPoint + offset,
                speed * Time.unscaledDeltaTime);

            transform.rotation = _fixedRotation;
        }
    }
}
