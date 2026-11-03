using System.Collections.Generic;
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

        private FieldInstance _currentField;

        /// <summary>The field instance the player is currently standing near, or null.</summary>
        public FieldInstance CurrentField => _currentField;

        /// <summary>Returns true if <paramref name="field"/> is within the proximity radius.</summary>
        public bool IsInRange(FieldInstance field)
        {
            if (field == null) return false;
            float sqr = (field.transform.position - transform.position).sqrMagnitude;
            return sqr <= proximityRadius * proximityRadius;
        }

        void Update()
        {
            // Read FieldGenerator's live registry every frame instead of caching a scene scan. A cached
            // array went stale on site switch / prestige: ClearAllFields destroyed every entry, the
            // array stayed non-empty, and it was never re-scanned, so the banner died until a reload
            // (#283). The registry is also empty until FieldGenerator's Start coroutine spawns fields,
            // which covers the old "re-scan until fields appear" case.
            var closest = FindClosest(FieldGenerator.AllInstances, transform.position, proximityRadius);

            if (closest == _currentField) return;

            _currentField = closest;

            if (_currentField != null)
                hud?.ShowFieldBanner(_currentField.Field);
            else
                hud?.HideFieldBanner();
        }

        /// <summary>
        /// The field nearest <paramref name="position"/> strictly within <paramref name="radius"/>, or
        /// null. Skips null / destroyed entries.
        /// </summary>
        internal static FieldInstance FindClosest(Dictionary<Vector2Int, FieldInstance>.ValueCollection fields,
                                                  Vector3 position, float radius)
        {
            FieldInstance closest    = null;
            float         closestSqr = radius * radius;

            foreach (var field in fields)
            {
                if (field == null) continue;
                float sqr = (field.transform.position - position).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest    = field;
                }
            }
            return closest;
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
