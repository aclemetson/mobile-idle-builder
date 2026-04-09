using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Diablo-style isometric camera that follows the character at a fixed angle and distance.
    /// Attach to the Main Camera. Overrides orthographic with perspective projection.
    ///
    /// Scene setup: assign Target to the Character GameObject's transform.
    /// GridRenderer.CentreCamera() will be skipped automatically when this component is present.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class IsometricCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3   offset      = new Vector3(0f, 8f, -6f);
        [SerializeField] private float     followSpeed = 8f;

        private Quaternion _fixedRotation;

        void Start()
        {
            var cam = GetComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView  = 60f;

            // Derive rotation from the offset direction and lock it permanently
            _fixedRotation     = Quaternion.LookRotation(-offset.normalized);
            transform.rotation = _fixedRotation;
            transform.position = target.position + offset;
        }

        void LateUpdate()
        {
            transform.position = Vector3.Lerp(
                transform.position,
                target.position + offset,
                followSpeed * Time.deltaTime);

            // Rotation is fixed — never updated so the camera never tilts
            transform.rotation = _fixedRotation;
        }
    }
}
