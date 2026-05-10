using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Applies a DPI-aware scale to this UIDocument's PanelSettings at runtime,
    /// overriding whatever scale mode the asset was saved with.
    ///
    /// Attach one instance per UIDocument that needs device-adaptive scaling.
    /// Set designWidth to the pixel width you designed for (default 1080).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-80)]
    public sealed class UIScaler : MonoBehaviour
    {
        [SerializeField] private float designWidth = 1080f;
        [SerializeField] private float scaleFactor = 2f;

        private void Awake()
        {
            var doc = GetComponent<UIDocument>();
            if (doc?.panelSettings == null)
            {
                GameLogger.Warning("[UIScaler] No UIDocument or PanelSettings found.");
                return;
            }

            float scale = (Screen.width / designWidth) * scaleFactor;
            doc.panelSettings.scale = scale;

            GameLogger.Info($"[UIScaler] {gameObject.name}: Screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F0} designWidth={designWidth} scaleFactor={scaleFactor} → scale={scale:F3}");
        }
    }
}
