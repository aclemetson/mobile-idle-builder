using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages ARCH's distributed environmental "presence" — the ghost-in-the-machine
    /// effect that signals where the player last interacted with the grid.
    ///
    /// When SetAnchor() is called (by PlayerInputRouter on every confirmed tap):
    ///   1. A luminance ripple ring expands outward from the anchor point, briefly
    ///      brightening each building cube it passes over.
    ///   2. An ambient glow lingers near the anchor and fades over ambientFadeDuration.
    ///
    /// Building cubes register via PresenceReceiver.OnEnable / OnDisable.
    /// PresenceSystem pushes a per-frame boost value [0..1] to each receiver;
    /// the receiver blends that boost on top of its own base colour.
    ///
    /// Place on any persistent scene GameObject (e.g. GameManager).
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class PresenceSystem : MonoBehaviour
    {
        public static PresenceSystem Instance { get; private set; }

        [Header("Ripple")]
        [Tooltip("How fast the ripple ring expands (world units / second).")]
        [SerializeField] private float rippleSpeed     = 7f;
        [Tooltip("Ripple stops expanding beyond this radius (world units).")]
        [SerializeField] private float rippleMaxRadius = 15f;
        [Tooltip("Gaussian spread of the ring (world units). Larger = softer edge.")]
        [SerializeField] private float rippleWidth     = 1.8f;
        [Tooltip("Peak brightness boost at the ring centre [0..1].")]
        [SerializeField] private float ripplePeak      = 0.45f;

        [Header("Ambient glow")]
        [Tooltip("Radius within which buildings receive a sustained glow (world units).")]
        [SerializeField] private float ambientRadius       = 5f;
        [Tooltip("Peak brightness boost at the anchor centre [0..1].")]
        [SerializeField] private float ambientPeak         = 0.22f;
        [Tooltip("Seconds for the ambient glow to fade to zero after the ripple ends.")]
        [SerializeField] private float ambientFadeDuration = 4.5f;

        // ── State ────────────────────────────────────────────────────────────
        private Vector3 _anchorPos;
        private bool    _hasAnchor;
        private float   _rippleRadius;
        private float   _anchorAge;

        private readonly List<PresenceReceiver> _receivers = new();

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[PresenceSystem] DUPLICATE — destroying component only on '{gameObject.name}', keeping '{Instance.gameObject.name}'");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_hasAnchor || _receivers.Count == 0) return;

            _rippleRadius += rippleSpeed * Time.deltaTime;
            _anchorAge    += Time.deltaTime;

            bool rippleActive = _rippleRadius < rippleMaxRadius;

            // Iterate in reverse so null-receiver cleanup doesn't skip indices
            for (int i = _receivers.Count - 1; i >= 0; i--)
            {
                var r = _receivers[i];
                if (r == null) { _receivers.RemoveAt(i); continue; }

                // Use XZ distance — building cubes sit at Y=0.5, anchor is on the ground
                float dist = Vector2.Distance(
                    new Vector2(r.transform.position.x, r.transform.position.z),
                    new Vector2(_anchorPos.x,            _anchorPos.z));

                // Ripple ring contribution: Gaussian peak centred on the expanding ring
                float ripple = 0f;
                if (rippleActive)
                {
                    float ring = Mathf.Exp(-Mathf.Pow((dist - _rippleRadius) / rippleWidth, 2f));
                    ripple = ripplePeak * ring;
                }

                // Ambient glow contribution: max at anchor, falls off linearly, fades over time
                float ambientFade    = Mathf.Clamp01(1f - _anchorAge / ambientFadeDuration);
                float ambientFalloff = Mathf.Clamp01(1f - dist / ambientRadius);
                float ambient        = ambientPeak * ambientFalloff * ambientFade;

                r.ApplyPresence(ripple + ambient);
            }

            // Once both ripple and ambient have fully expired, go dormant and reset receivers
            if (!rippleActive && _anchorAge >= ambientFadeDuration)
            {
                foreach (var r in _receivers)
                    if (r != null) r.ApplyPresence(0f);
                _hasAnchor = false;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Sets a new anchor point, restarting the ripple from zero.
        /// Called by PlayerInputRouter on every confirmed tap.
        /// </summary>
        public void SetAnchor(Vector3 worldPos)
        {
            _anchorPos    = worldPos;
            _hasAnchor    = true;
            _rippleRadius = 0f;
            _anchorAge    = 0f;
        }

        /// <summary>Called automatically by PresenceReceiver.OnEnable.</summary>
        public void Register(PresenceReceiver r)
        {
            if (!_receivers.Contains(r))
                _receivers.Add(r);
        }

        /// <summary>Called automatically by PresenceReceiver.OnDisable.</summary>
        public void Unregister(PresenceReceiver r)
        {
            _receivers.Remove(r);
        }
    }
}
