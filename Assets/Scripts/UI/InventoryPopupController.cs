using System.Collections;
using TMPro;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Displays a brief floating "+1 Item" label above the player whenever an item
    /// enters the inventory.  Call <see cref="Notify"/> from any code that grants items.
    ///
    /// Uses world-space TextMeshPro objects that float upward and fade out.
    /// The text is oriented to match the camera so it reads correctly in isometric view.
    ///
    /// Scene setup: attach to the Character GameObject (or any persistent object).
    /// The <see cref="anchor"/> defaults to this transform — override if the popup
    /// origin should differ from the component's GameObject.
    /// </summary>
    public class InventoryPopupController : SingletonMonoBehaviour<InventoryPopupController>
    {
        [Header("Spawn anchor")]
        [Tooltip("World-space origin for popups. Defaults to this GameObject if left empty.")]
        [SerializeField] private Transform anchor;

        [Header("Animation")]
        [SerializeField] private float duration      = 1.4f;  // total seconds the popup lives
        [SerializeField] private float floatDistance = 1.4f;  // world units it rises over its lifetime
        [SerializeField] private float holdFraction  = 0.35f; // 0–1: fraction of duration at full opacity

        [Header("Stacking")]
        [Tooltip("Vertical world-unit offset applied per concurrent popup so they don't overlap.")]
        [SerializeField] private float stackOffset   = 0.4f;

        [Header("Appearance")]
        [SerializeField] private Color  textColor   = Color.white;
        [SerializeField] private float  fontSize    = 0.45f;  // world-space TMP font size
        [SerializeField] private float  spawnHeight = 2.0f;   // units above anchor Y

        private int _activeCount;

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            if (anchor == null) anchor = transform;
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Shows "+<paramref name="quantity"/> <paramref name="itemName"/>" above the player.
        /// Safe to call from any MonoBehaviour on the same thread as Update.
        /// </summary>
        public static void Notify(string itemName, int quantity)
        {
            if (Instance == null) return;
            Instance.StartCoroutine(Instance.ShowPopup(itemName, quantity));
        }

        // ----------------------------------------------------------------
        // Internal coroutine
        // ----------------------------------------------------------------

        private IEnumerator ShowPopup(string itemName, int quantity)
        {
            // Build the label
            var go  = new GameObject("InventoryPopup");
            var tmp = go.AddComponent<TextMeshPro>();

            tmp.text      = $"+{quantity} {itemName}";
            tmp.fontSize  = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = textColor;

            // Sort above most world geometry
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingOrder = 20;

            // Position above anchor, stacked so concurrent popups don't overlap
            float   yStart  = spawnHeight + _activeCount * stackOffset;
            Vector3 origin  = anchor.position + Vector3.up * yStart;
            Vector3 target  = origin + Vector3.up * floatDistance;

            go.transform.position = origin;
            go.transform.rotation = Camera.main != null
                ? Camera.main.transform.rotation
                : Quaternion.identity;

            _activeCount++;

            // Animate
            float elapsed  = 0f;
            float fadeFrom = holdFraction;   // normalised time when fade begins

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                go.transform.position = Vector3.Lerp(origin, target, t);

                // Keep facing the camera as it moves (handles camera offset smoothly)
                if (Camera.main != null)
                    go.transform.rotation = Camera.main.transform.rotation;

                float alpha = t < fadeFrom
                    ? 1f
                    : 1f - Mathf.InverseLerp(fadeFrom, 1f, t);

                var c = textColor;
                c.a       = alpha;
                tmp.color = c;

                yield return null;
            }

            _activeCount = Mathf.Max(0, _activeCount - 1);
            Destroy(go);
        }
    }
}
