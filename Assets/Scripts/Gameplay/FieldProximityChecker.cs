using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects when the player enters or leaves a field's proximity radius
    /// and drives the persistent HUD field banner accordingly.
    ///
    /// Attach to the Character GameObject alongside CharacterMover.
    /// Wire up the HUDController reference in the Inspector.
    /// </summary>
    public class FieldProximityChecker : MonoBehaviour
    {
        [SerializeField] private HUDController hud;
        [SerializeField] [Min(0.1f)] private float proximityRadius = 2.5f;

        private FieldInstance[] _fields;
        private FieldInstance   _currentField;

        void Start()
        {
            // FieldGenerator runs at execution order -10, so all FieldInstances
            // are already created by the time this Start() is called.
            _fields = FindObjectsByType<FieldInstance>(FindObjectsSortMode.None);
        }

        void Update()
        {
            if (_fields == null || _fields.Length == 0) return;

            FieldInstance closest    = null;
            float         closestSqr = proximityRadius * proximityRadius;

            foreach (var field in _fields)
            {
                if (field == null) continue;
                float sqr = (field.transform.position - transform.position).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest    = field;
                }
            }

            if (closest == _currentField) return;

            _currentField = closest;

            if (_currentField != null)
                hud?.ShowFieldBanner(_currentField.Field);
            else
                hud?.HideFieldBanner();
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.8f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
        }
#endif
    }
}
