using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.UI
{
    /// <summary>
    /// Pads the document root so UI stays inside <see cref="Screen.safeArea"/> (status bar,
    /// camera cutout, gesture bar).
    ///
    /// Insets are applied in panel pixels, not percent: UI Toolkit resolves percentage padding
    /// against the parent's WIDTH on every side, so a percent top inset came out wrong on a tall
    /// portrait screen (#188). The panel-units-per-pixel ratio is re-measured each frame so a
    /// scale change (UIScaler) or the first real layout re-applies the insets.
    ///
    /// When the document has a top bar, the top, left and right insets are painted in the top
    /// bar's colour ("safe-area-fill" strips): the status bar reads as part of the top bar (#188)
    /// and, in landscape, the camera-cutout side is framed off as unusable instead of showing a
    /// blank gap (#228). The bottom (gesture bar) inset is left see-through.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SafeAreaAdapter : MonoBehaviour
    {
        internal const string FillClass = "safe-area-fill";

        UIDocument    _doc;
        Rect          _lastSafeArea;
        float         _lastScale = -1f;
        VisualElement _fillTop, _fillLeft, _fillRight;

        void Awake()
        {
            _doc = GetComponent<UIDocument>();
        }

        void OnEnable() => ApplySafeArea();

        void Update()
        {
            float scale = PanelScale();
            if (Screen.safeArea != _lastSafeArea || !Mathf.Approximately(scale, _lastScale))
                ApplySafeArea();
        }

        void ApplySafeArea()
        {
            var root = _doc != null ? _doc.rootVisualElement : null;
            // No panel yet (first frame): leave the cache unset so Update retries.
            if (root == null || root.panel == null) return;

            _lastSafeArea = Screen.safeArea;
            _lastScale    = PanelScale();

            var inset = ComputeInsets(Screen.safeArea, Screen.width, Screen.height, _lastScale);
            root.style.paddingLeft   = inset.left;
            root.style.paddingRight  = inset.right;
            root.style.paddingTop    = inset.top;
            root.style.paddingBottom = inset.bottom;

            ApplyFills(root, inset);
        }

        void ApplyFills(VisualElement root, (float left, float right, float top, float bottom) inset)
        {
            if (root.Q("top-bar") == null) return; // only documents with a top bar get the frame

            if (_fillTop == null)
            {
                _fillTop   = CreateFill(root, "safe-area-fill-top");
                _fillLeft  = CreateFill(root, "safe-area-fill-left");
                _fillRight = CreateFill(root, "safe-area-fill-right");

                _fillTop.style.left   = 0; _fillTop.style.right   = 0; _fillTop.style.top   = 0;
                _fillLeft.style.left  = 0; _fillLeft.style.top    = 0; _fillLeft.style.bottom  = 0;
                _fillRight.style.right = 0; _fillRight.style.top  = 0; _fillRight.style.bottom = 0;
            }

            _fillTop.style.height  = inset.top;
            _fillLeft.style.width  = inset.left;
            _fillRight.style.width = inset.right;
            SetShown(_fillTop,   inset.top   > 0f);
            SetShown(_fillLeft,  inset.left  > 0f);
            SetShown(_fillRight, inset.right > 0f);
        }

        static VisualElement CreateFill(VisualElement root, string name)
        {
            var fill = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            fill.AddToClassList(FillClass);
            fill.style.position = Position.Absolute;
            root.Insert(0, fill); // behind everything else in the root
            return fill;
        }

        static void SetShown(VisualElement e, bool shown)
            => e.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// Panel units per screen pixel, measured from the laid-out panel so it is right for any
        /// PanelSettings scale mode (and follows UIScaler's runtime scale). Before the first
        /// layout it falls back to a constant-pixel-size reading of the PanelSettings scale.
        /// </summary>
        float PanelScale()
        {
            var panelRoot = _doc != null ? _doc.rootVisualElement?.panel?.visualTree : null;
            if (panelRoot != null && Screen.width > 0)
            {
                float w = panelRoot.layout.width;
                if (w > 0f && !float.IsNaN(w)) return w / Screen.width;
            }
            var settings = _doc != null ? _doc.panelSettings : null;
            float s = settings != null ? settings.scale : 1f;
            return s > 0f ? 1f / s : 1f;
        }

        /// <summary>
        /// Safe-area insets in panel units. <paramref name="panelPerPixel"/> converts screen
        /// pixels to panel units. Screen.safeArea has its origin at the bottom-left. Pure (unit-tested).
        /// </summary>
        internal static (float left, float right, float top, float bottom) ComputeInsets(
            Rect safe, int screenWidth, int screenHeight, float panelPerPixel)
        {
            float l = Mathf.Max(0f, safe.x)                    * panelPerPixel;
            float r = Mathf.Max(0f, screenWidth - safe.xMax)   * panelPerPixel;
            float t = Mathf.Max(0f, screenHeight - safe.yMax)  * panelPerPixel;
            float b = Mathf.Max(0f, safe.y)                    * panelPerPixel;
            return (l, r, t, b);
        }
    }
}
