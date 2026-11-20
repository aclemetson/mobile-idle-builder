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

        /// <summary>
        /// Panel scale for the current screen, taken from its SHORT side so the HUD is the same size in
        /// either orientation. Scaling by Screen.width made a landscape launch (2340 px wide) render a
        /// 540x250 HUD, with the slide panels squeezed into a strip.
        /// </summary>
        public static float ScaleForScreen(int screenWidth, int screenHeight,
                                           float designWidth = DefaultDesignWidth,
                                           float scaleFactor = DefaultScaleFactor)
            => ComputeScale(Mathf.Min(screenWidth, screenHeight), designWidth, scaleFactor);

        private UIDocument _doc;
        private int _lastW, _lastH;

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc?.panelSettings == null)
            {
                GameLogger.Warning("[UIScaler] No UIDocument or PanelSettings found.");
                return;
            }
            Apply();
            GameLogger.Info($"[UIScaler] {gameObject.name}: Screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F0} designWidth={designWidth} scaleFactor={scaleFactor} → scale={_doc.panelSettings.scale:F3}");
        }

        // Re-applies after a rotation or window resize so the scale always follows the short side.
        private void Update()
        {
            if (Screen.width != _lastW || Screen.height != _lastH) Apply();
        }

        private void Apply()
        {
            if (_doc?.panelSettings == null) return;
            _lastW = Screen.width; _lastH = Screen.height;
            _doc.panelSettings.scale = ScaleForScreen(_lastW, _lastH, designWidth, scaleFactor);
        }
    }
}
