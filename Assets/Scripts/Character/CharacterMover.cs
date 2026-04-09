using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Moves the player character (capsule placeholder) toward a world-space tap target.
    /// Attach to the Character GameObject. A Capsule child named "CharacterVisual" serves
    /// as the placeholder mesh.
    ///
    /// Scene setup: assign gridRenderer so the character can spawn at the grid centre.
    /// PlayerInputRouter calls SetMoveTarget() when the player taps empty ground.
    /// </summary>
    public class CharacterMover : MonoBehaviour
    {
        [SerializeField] private float        moveSpeed        = 4f;
        [SerializeField] private float        stoppingDistance = 0.05f;
        [SerializeField] private GridRenderer gridRenderer;

        private Vector3 _targetPosition;

        void Start()
        {
            // Spawn at the centre of the grid, Y=1 so the capsule sits on top of the tiles
            float cx = (gridRenderer.Width  - 1) * gridRenderer.CellSize * 0.5f;
            float cz = (gridRenderer.Height - 1) * gridRenderer.CellSize * 0.5f;
            transform.position = new Vector3(cx, 1f, cz);
            _targetPosition    = transform.position;
        }

        /// <summary>Sets the world-space destination, clamped to the grid boundary.</summary>
        public void SetMoveTarget(Vector3 worldTarget)
        {
            Vector3 origin  = gridRenderer.transform.position;
            float   half    = gridRenderer.CellSize * 0.5f;
            float   minX    = origin.x - half;
            float   maxX    = origin.x + (gridRenderer.Width  - 1) * gridRenderer.CellSize + half;
            float   minZ    = origin.z - half;
            float   maxZ    = origin.z + (gridRenderer.Height - 1) * gridRenderer.CellSize + half;

            _targetPosition = new Vector3(
                Mathf.Clamp(worldTarget.x, minX, maxX),
                transform.position.y,
                Mathf.Clamp(worldTarget.z, minZ, maxZ));
        }

        void Update()
        {
            if (Vector3.Distance(transform.position, _targetPosition) <= stoppingDistance)
                return;

            transform.position = Vector3.MoveTowards(
                transform.position, _targetPosition, moveSpeed * Time.deltaTime);

            // Face the movement direction
            transform.LookAt(new Vector3(_targetPosition.x, transform.position.y, _targetPosition.z));
        }
    }
}
