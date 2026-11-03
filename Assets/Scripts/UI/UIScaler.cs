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
        public const float DefaultDesignWidth = 1080f;
        public const float DefaultScaleFactor = 2f;

        [SerializeField] private float designWidth = DefaultDesignWidth;
        [SerializeField] private float scaleFactor = DefaultScaleFactor;

        /// <summary>
        /// Panel scale for a screen width. Scenes without a UIScaler component (SplashScene) call
        /// this directly so every scene renders the shared gamePanelSettings at the same scale.
        /// </summary>
        public static float ComputeScale(int screenWidth,
                                         float designWidth = DefaultDesignWidth,
                                         float scaleFactor = DefaultScaleFactor)
            => (screenWidth / designWidth) * scaleFactor;

        private void Awake()
        {
            var doc = GetComponent<UIDocument>();
            if (doc?.panelSettings == null)
            {
                GameLogger.Warning("[UIScaler] No UIDocument or PanelSettings found.");
                return;
            }

            float scale = ComputeScale(Screen.width, designWidth, scaleFactor);
            doc.panelSettings.scale = scale;

            GameLogger.Info($"[UIScaler] {gameObject.name}: Screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F0} designWidth={designWidth} scaleFactor={scaleFactor} → scale={scale:F3}");
        }
    }
}
