using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Attached to every building placeholder cube by BuildingVisualizer.
    ///
    /// Owns all colour state for the cube so that BuildingVisualizer's hover tints
    /// and PresenceSystem's brightness boosts can coexist without overwriting each other.
    ///
    /// Usage:
    ///   BuildingVisualizer calls SetBaseColor() instead of writing _BaseColor directly.
    ///   PresenceSystem calls ApplyPresence(boost) every frame.
    ///   The rendered colour = baseColor + boost (clamped per channel).
    ///
    /// Registration with PresenceSystem is automatic via OnEnable / OnDisable.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class PresenceReceiver : MonoBehaviour
    {
        private MeshRenderer         _mr;
        private MaterialPropertyBlock _mpb;       // cached — avoids per-frame GC allocs
        private Color                _baseColor = Color.white;
        private float                _lastBoost;

        void Awake()
        {
            _mr  = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void OnEnable()  => PresenceSystem.Instance?.Register(this);
        void OnDisable() => PresenceSystem.Instance?.Unregister(this);

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Sets the building's logical base colour and immediately commits it to the
        /// renderer (with the last known presence boost applied on top).
        /// Call this instead of writing _BaseColor via MaterialPropertyBlock directly.
        /// </summary>
        public void SetBaseColor(Color c)
        {
            _baseColor = c;
            CommitColor(_lastBoost);
        }

        /// <summary>
        /// Called every frame by PresenceSystem with the current boost value in [0..1].
        /// A boost of 0 restores the cube to its exact base colour.
        /// </summary>
        public void ApplyPresence(float boost)
        {
            _lastBoost = boost;
            CommitColor(boost);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void CommitColor(float boost)
        {
            if (_mr == null) return;
            _mpb.SetColor("_BaseColor", new Color(
                Mathf.Clamp01(_baseColor.r + boost),
                Mathf.Clamp01(_baseColor.g + boost),
                Mathf.Clamp01(_baseColor.b + boost),
                _baseColor.a));
            _mr.SetPropertyBlock(_mpb);
        }
    }
}
