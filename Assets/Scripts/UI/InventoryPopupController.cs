using System.Collections;
using TMPro;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Displays a brief floating "+1 Item" label above the player whenever an item
    /// enters the inventory.  Call <see cref="Notify"/> from any code that grants items, or
    /// <see cref="NotifyAt"/> to spawn the label at a world position (e.g. the tapped field).
    ///
    /// Labels are bold with a dark outline (one shared material) so they read over any tile,
    /// pop in slightly and rise on an ease-out curve before fading (#198).
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
        [SerializeField] private Color  textColor    = Color.white;
        [SerializeField] private Color  warningColor = new Color(1f, 0.65f, 0.1f);
        [SerializeField] private float  fontSize     = 0.45f;  // world-space TMP font size
        [SerializeField] private float  spawnHeight  = 2.0f;   // units above anchor Y
        [SerializeField] private float  atSpawnHeight = 0.8f;  // units above a NotifyAt position (field tiles sit at Y=0)
        [SerializeField] private float  outlineWidth = 0.25f;  // TMP outline width (0..1)
        [SerializeField] private Color  outlineColor = new Color(0.04f, 0.05f, 0.1f, 1f);
        [SerializeField] private float  popScale     = 1.3f;   // starting scale of the pop-in
        [SerializeField] private float  popFraction  = 0.12f;  // 0-1: fraction of duration spent popping in

        private int           _activeCount;
        private Material      _outlineMaterial; // shared by every popup; created once
        private HUDController _hud;             // warning banner; found on first warning

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
            Instance.StartCoroutine(Instance.ShowPopup($"+{quantity} {itemName}", Instance.textColor,
                Instance.InView(Instance.anchor.position + Vector3.up * Instance.spawnHeight)));
        }

        /// <summary>
        /// Shows "+<paramref name="quantity"/> <paramref name="itemName"/>" just above
        /// <paramref name="worldPos"/>, tinted towards <paramref name="tint"/> (lightened so it stays readable).
        /// </summary>
        public static void NotifyAt(string itemName, int quantity, Vector3 worldPos, Color tint)
        {
            if (Instance == null) return;
            Instance.StartCoroutine(Instance.ShowPopup($"+{quantity} {itemName}", ReadableTint(tint),
                worldPos + Vector3.up * Instance.atSpawnHeight));
        }

        /// <summary>Lightens a colour towards white so text in that hue reads on dark tiles. Pure (unit-tested).</summary>
        internal static Color ReadableTint(Color c)
        {
            var t = Color.Lerp(c, Color.white, 0.45f);
            t.a = 1f;
            return t;
        }

        /// <summary>Cubic ease-out on [0,1]: fast start, gentle settle. Pure (unit-tested).</summary>
        internal static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>
        /// Signals a blocked or invalid action. Shown in the HUD's warning banner, which stays up long
        /// enough to read: as world text over the player anchor it often spawned at the screen edge or
        /// off-screen and was gone before it could be read (#238). Falls back to the amber world popup
        /// when there is no HUD.
        /// </summary>
        public static void NotifyWarning(string message)
        {
            if (Instance == null) return;
            if (Instance._hud == null) Instance._hud = FindAnyObjectByType<HUDController>();
            if (Instance._hud != null)
            {
                Instance._hud.ShowNotification("!", message, "warning");
                return;
            }
            Instance.StartCoroutine(Instance.ShowPopup(message, Instance.warningColor,
                Instance.InView(Instance.anchor.position + Vector3.up * Instance.spawnHeight)));
        }

        /// <summary>
        /// Moves <paramref name="world"/> into the readable part of the main camera's view, at the same
        /// depth, so a popup at the player anchor never spawns off-screen or half off an edge (#238).
        /// </summary>
        private Vector3 InView(Vector3 world)
        {
            var cam = Camera.main;
            if (cam == null) return world;
            var v = cam.WorldToViewportPoint(world);
            if (v.z <= cam.nearClipPlane) v.z = OffscreenDepth; // behind the camera: use a fixed depth
            return cam.ViewportToWorldPoint(ClampViewport(v));
        }

        private const float OffscreenDepth = 10f;

        /// <summary>
        /// Clamps a viewport point to the readable band: clear of the side edges, above the bottom bar
        /// and low enough to rise <see cref="floatDistance"/> without leaving the top. Pure (unit-tested).
        /// </summary>
        internal static Vector3 ClampViewport(Vector3 v) =>
            new(Mathf.Clamp(v.x, 0.15f, 0.85f), Mathf.Clamp(v.y, 0.2f, 0.7f), v.z);

        // ----------------------------------------------------------------
        // Internal coroutine
        // ----------------------------------------------------------------

        private IEnumerator ShowPopup(string text, Color color, Vector3 basePosition)
        {
            // Build the label
            var go  = new GameObject("InventoryPopup");
            var tmp = go.AddComponent<TextMeshPro>();

            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = color;

            var mat = OutlineMaterial(tmp);
            if (mat != null) tmp.fontSharedMaterial = mat;

            // Sort above most world geometry
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingOrder = 20;

            // Position above anchor, stacked so concurrent popups don't overlap
            Vector3 origin = basePosition + Vector3.up * (_activeCount * stackOffset);
            Vector3 target = origin + Vector3.up * floatDistance;

            go.transform.position = origin;
            go.transform.rotation = Camera.main != null
                ? Camera.main.transform.rotation
                : Quaternion.identity;

            _activeCount++;

            // Animate
            float elapsed  = 0f;
            float fadeFrom = holdFraction;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                go.transform.position   = Vector3.LerpUnclamped(origin, target, EaseOutCubic(t));
                float pop               = popFraction > 0f ? EaseOutCubic(t / popFraction) : 1f;
                go.transform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, pop);

                if (Camera.main != null)
                    go.transform.rotation = Camera.main.transform.rotation;

                float alpha = t < fadeFrom
                    ? 1f
                    : 1f - Mathf.InverseLerp(fadeFrom, 1f, t);

                var c = color;
                c.a       = alpha;
                tmp.color = c;

                yield return null;
            }

            _activeCount = Mathf.Max(0, _activeCount - 1);
            Destroy(go);
        }

        /// <summary>
        /// One outlined copy of the label's default font material, shared by every popup (setting
        /// tmp.outlineWidth directly would leak a material instance per popup).
        /// </summary>
        private Material OutlineMaterial(TextMeshPro tmp)
        {
            if (_outlineMaterial != null) return _outlineMaterial;
            var source = tmp.fontSharedMaterial;
            if (source == null) return null;

            _outlineMaterial = new Material(source) { name = "InventoryPopup Outline" };
            _outlineMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            _outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            return _outlineMaterial;
        }

        protected override void OnDestroy()
        {
            if (_outlineMaterial != null) Destroy(_outlineMaterial);
            base.OnDestroy();
        }
    }
}
