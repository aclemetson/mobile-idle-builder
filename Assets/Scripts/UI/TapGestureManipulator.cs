using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Fires a tap action on pointer-up when the pointer moved less than a threshold since
    /// pointer-down. Unlike the built-in <see cref="Clickable"/> used by <c>Button.clicked</c>,
    /// it tolerates small movement, so a button inside a <see cref="ScrollView"/> still registers
    /// a tap when the touch-scroll layer would otherwise cancel the click on the slightest finger
    /// jitter.
    ///
    /// The critical detail for mobile: a <see cref="ScrollView"/> <b>captures the pointer</b> the
    /// moment the finger moves even a little (which a finger always does), to drive touch-scrolling.
    /// Once captured, pointer-move / pointer-up events are routed to the ScrollView, and the button
    /// itself never sees the <see cref="PointerUpEvent"/> — so a manipulator that listens on the
    /// button loses the tap entirely. To survive that, we track the gesture from the button's
    /// <see cref="PointerDownEvent"/> (which always fires first, before any capture), then listen for
    /// the move/up on the <b>panel root</b>. The root is an ancestor of the capturing ScrollView, so
    /// it still receives every pointer event, and registering in the trickle-down phase guarantees we
    /// run before the ScrollView can stop propagation. We never capture the pointer ourselves, so the
    /// ScrollView stays free to scroll.
    ///
    /// Use this INSTEAD of <c>button.clicked += ...</c> for buttons inside scrollable lists
    /// (recipe list, buildings list, building-inspector menus) — do not use both, or a clean
    /// tap would fire the action twice.
    ///
    /// Threshold matches <see cref="CameraController.TapThreshold"/> so tap-vs-drag feels
    /// consistent across the map and the UI.
    /// </summary>
    public class TapGestureManipulator : Manipulator
    {
        private readonly Action _onTap;
        private readonly float  _moveThreshold;

        private Vector2       _startPos;
        private bool          _tracking;
        private int           _pointerId;
        private VisualElement _root;

        public TapGestureManipulator(Action onTap, float moveThreshold = CameraController.TapThreshold)
        {
            _onTap         = onTap;
            _moveThreshold = moveThreshold;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            // PointerDown always reaches the button before any ScrollView capture kicks in.
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            // Safety: if the button is torn down mid-gesture (list rebuild), release root listeners.
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            StopTracking();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            StopTracking(); // clear any stale gesture

            _tracking  = true;
            _pointerId = evt.pointerId;
            _startPos  = evt.position;
            // Deliberately no pointer capture — the ScrollView must stay free to scroll.

            // Listen for the rest of the gesture on the panel root, in the trickle-down (capture)
            // phase, so we still see the pointer-up after the ScrollView captures the pointer.
            _root = target.panel?.visualTree;
            GameLogger.Develop($"[Tap] down on '{Describe()}' pointer={_pointerId} pos={_startPos} " +
                               $"enabled={target?.enabledInHierarchy} rootFound={_root != null}");
            if (_root != null)
            {
                _root.RegisterCallback<PointerMoveEvent>(OnRootMove,     TrickleDown.TrickleDown);
                _root.RegisterCallback<PointerUpEvent>(OnRootUp,         TrickleDown.TrickleDown);
                _root.RegisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
            }
        }

        private void OnRootMove(PointerMoveEvent evt)
        {
            if (!_tracking || evt.pointerId != _pointerId) return;
            // Moving past the threshold means this is a scroll/drag, not a tap.
            float dist = Distance(evt.position, _startPos);
            if (dist > _moveThreshold)
            {
                GameLogger.Develop($"[Tap] '{Describe()}' became scroll (moved {dist:0}px > {_moveThreshold:0})");
                StopTracking();
            }
        }

        private void OnRootUp(PointerUpEvent evt)
        {
            if (!_tracking || evt.pointerId != _pointerId) return;

            float dist    = Distance(evt.position, _startPos);
            bool  enabled = target?.enabledInHierarchy ?? false;
            bool  isTap   = dist <= _moveThreshold && enabled;
            GameLogger.Develop($"[Tap] '{Describe()}' up dist={dist:0} threshold={_moveThreshold:0} " +
                               $"enabled={enabled} -> isTap={isTap}");
            StopTracking();
            if (isTap) _onTap?.Invoke();
        }

        // Best-effort label for the tapped element, for debugging which button fired.
        private string Describe()
        {
            if (target == null) return "<null>";
            if (!string.IsNullOrEmpty(target.name)) return target.name;
            if (target is TextElement te && !string.IsNullOrEmpty(te.text)) return te.text;
            return target.GetType().Name;
        }

        private void OnRootCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId == _pointerId) StopTracking();
        }

        private void OnDetach(DetachFromPanelEvent evt) => StopTracking();

        private void StopTracking()
        {
            _tracking = false;
            if (_root != null)
            {
                _root.UnregisterCallback<PointerMoveEvent>(OnRootMove,     TrickleDown.TrickleDown);
                _root.UnregisterCallback<PointerUpEvent>(OnRootUp,         TrickleDown.TrickleDown);
                _root.UnregisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
                _root = null;
            }
        }

        private static float Distance(Vector3 a, Vector2 b)
            => Vector2.Distance(new Vector2(a.x, a.y), b);
    }
}
