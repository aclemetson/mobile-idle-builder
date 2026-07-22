using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Fires a tap action for a button that lives inside a <see cref="ScrollView"/>, where the
    /// built-in <see cref="Clickable"/> (<c>Button.clicked</c>) is unreliable on touch.
    ///
    /// The core problem on mobile: a <see cref="ScrollView"/> captures the pointer and STOPS
    /// <see cref="PointerDownEvent"/> propagation during the trickle-down (capture) phase — before
    /// the event ever descends to the button — so it can drive touch-scrolling. A callback
    /// registered on the button itself therefore never runs (verified on device: the pointer-down's
    /// target is the button, yet the button's own PointerDown callback does not fire). Listening on
    /// the button for pointer-up has the same problem: once the ScrollView captures, up/move events
    /// route to the ScrollView, not the button.
    ///
    /// The fix is to run the ENTIRE gesture from the panel root in the trickle-down phase. The root
    /// is the topmost ancestor, so its trickle-down callbacks run first — before any ScrollView can
    /// capture or stop propagation — and it keeps receiving move/up even after the ScrollView
    /// captures the pointer. We identify our button by the pointer-down's target and never capture
    /// the pointer ourselves, so the ScrollView stays free to scroll.
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
            // The button may not be parented into a panel yet (AddManipulator is often called before
            // the row is added to the list), so hook the root on attach — and now, if already attached.
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            if (target.panel != null) HookRoot();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            UnhookRoot();
        }

        private void OnAttach(AttachToPanelEvent evt) => HookRoot();
        private void OnDetach(DetachFromPanelEvent evt) => UnhookRoot();

        private void HookRoot()
        {
            var root = target.panel?.visualTree;
            if (root == null || ReferenceEquals(root, _root)) return;
            UnhookRoot();
            _root = root;
            // Everything in trickle-down so we run before any ScrollView ancestor intercepts.
            _root.RegisterCallback<PointerDownEvent>(OnRootDown,     TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(OnRootMove,     TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnRootUp,         TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
        }

        private void UnhookRoot()
        {
            _tracking = false;
            if (_root == null) return;
            _root.UnregisterCallback<PointerDownEvent>(OnRootDown,     TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerMoveEvent>(OnRootMove,     TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnRootUp,         TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
            _root = null;
        }

        private void OnRootDown(PointerDownEvent evt)
        {
            // Did this pointer-down land on our button (or one of its children)? evt.target is the
            // picked element, resolved before dispatch, so it is correct even in trickle-down.
            if (!IsOnTarget(evt.target as VisualElement) || !(target?.enabledInHierarchy ?? false))
            {
                _tracking = false;
                return;
            }
            _tracking  = true;
            _pointerId = evt.pointerId;
            _startPos  = evt.position;
            GameLogger.Develop($"[Tap] down on '{Describe()}' pointer={_pointerId} pos={_startPos}");
            // Deliberately no pointer capture — the ScrollView must stay free to scroll.
        }

        private void OnRootMove(PointerMoveEvent evt)
        {
            if (!_tracking || evt.pointerId != _pointerId) return;
            // Moving past the threshold means this is a scroll/drag, not a tap.
            float dist = Distance(evt.position, _startPos);
            GameLogger.Develop($"[TapEvt] move p={evt.pointerId} pos=({evt.position.x:0},{evt.position.y:0}) dist={dist:0}");
            if (dist > _moveThreshold)
            {
                GameLogger.Develop($"[Tap] '{Describe()}' became scroll (moved {dist:0}px > {_moveThreshold:0})");
                _tracking = false;
            }
        }

        private void OnRootUp(PointerUpEvent evt)
        {
            GameLogger.Develop($"[TapEvt] UP p={evt.pointerId} myPointer={_pointerId} tracking={_tracking} " +
                               $"pos=({evt.position.x:0},{evt.position.y:0})");
            if (!_tracking || evt.pointerId != _pointerId) return;

            float dist    = Distance(evt.position, _startPos);
            bool  enabled = target?.enabledInHierarchy ?? false;
            bool  isTap   = dist <= _moveThreshold && enabled;
            GameLogger.Develop($"[Tap] '{Describe()}' up dist={dist:0} threshold={_moveThreshold:0} " +
                               $"enabled={enabled} -> isTap={isTap}");
            _tracking = false;
            if (isTap) _onTap?.Invoke();
        }

        private void OnRootCancel(PointerCancelEvent evt)
        {
            GameLogger.Develop($"[TapEvt] CANCEL p={evt.pointerId} myPointer={_pointerId} tracking={_tracking}");
            if (evt.pointerId == _pointerId) _tracking = false;
        }

        // True if `hit` is our target button or a descendant of it.
        private bool IsOnTarget(VisualElement hit)
        {
            for (var e = hit; e != null; e = e.parent)
                if (ReferenceEquals(e, target)) return true;
            return false;
        }

        // Best-effort label for the tapped element, for debugging which button fired.
        private string Describe()
        {
            if (target == null) return "<null>";
            if (!string.IsNullOrEmpty(target.name)) return target.name;
            if (target is TextElement te && !string.IsNullOrEmpty(te.text)) return te.text;
            return target.GetType().Name;
        }

        private static float Distance(Vector3 a, Vector2 b)
            => Vector2.Distance(new Vector2(a.x, a.y), b);
    }
}
