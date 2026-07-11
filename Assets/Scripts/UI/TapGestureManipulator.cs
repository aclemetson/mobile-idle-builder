using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Fires a tap action on pointer-up when the pointer moved less than a threshold since
    /// pointer-down. Unlike the built-in <see cref="Clickable"/> used by <c>Button.clicked</c>,
    /// it does NOT capture the pointer and tolerates small movement, so a button inside a
    /// <see cref="ScrollView"/> still registers a tap when the touch-scroll layer would otherwise
    /// cancel the click on the slightest finger jitter. The gesture never stops event
    /// propagation, so vertical scrolling on the list still works normally.
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

        private Vector2 _startPos;
        private bool    _tracking;
        private int     _pointerId;

        public TapGestureManipulator(Action onTap, float moveThreshold = CameraController.TapThreshold)
        {
            _onTap         = onTap;
            _moveThreshold = moveThreshold;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            _tracking  = true;
            _pointerId = evt.pointerId;
            _startPos  = evt.position;
            // Deliberately no pointer capture — the ScrollView must stay free to scroll.
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            // Moving past the threshold means this is a scroll/drag, not a tap.
            if (_tracking && Distance(evt.position, _startPos) > _moveThreshold)
                _tracking = false;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            bool isTap = _tracking
                         && evt.pointerId == _pointerId
                         && Distance(evt.position, _startPos) <= _moveThreshold;
            _tracking = false;
            if (isTap && (target?.enabledInHierarchy ?? false))
                _onTap?.Invoke();
        }

        private static float Distance(Vector3 a, Vector2 b)
            => Vector2.Distance(new Vector2(a.x, a.y), b);
    }
}
