using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class SafeAreaAdapter : MonoBehaviour
    {
        UIDocument _doc;
        Rect _lastSafeArea;

        void Awake()
        {
            _doc = GetComponent<UIDocument>();
        }

        void OnEnable() => ApplySafeArea();

        void Update()
        {
            if (Screen.safeArea != _lastSafeArea)
                ApplySafeArea();
        }

        void ApplySafeArea()
        {
            _lastSafeArea = Screen.safeArea;
            var root = _doc.rootVisualElement;
            if (root == null) return;

            var safe = Screen.safeArea;
            var sw = (float)Screen.width;
            var sh = (float)Screen.height;

            root.style.paddingLeft   = new Length(safe.x / sw * 100f,          LengthUnit.Percent);
            root.style.paddingRight  = new Length((sw - safe.xMax) / sw * 100f, LengthUnit.Percent);
            root.style.paddingTop    = new Length((sh - safe.yMax) / sh * 100f, LengthUnit.Percent);
            root.style.paddingBottom = new Length(safe.y / sh * 100f,           LengthUnit.Percent);
        }
    }
}
