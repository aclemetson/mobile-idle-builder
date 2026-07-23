using System;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Fires a tap action for a button that lives inside a <see cref="ScrollView"/>.
    ///
    /// Why this exists: on touch, a UI Toolkit <see cref="ScrollView"/> captures the pointer for
    /// scroll recognition and drops/defers the <c>PointerUp</c> for buttons inside it. Verified on
    /// device — a tap on such a button produces a clean pointer-down but the up never arrives (or
    /// arrives seconds later), so neither <c>Button.clicked</c> nor any pointer-event manipulator
    /// fires. This cannot be fixed from inside UI Toolkit.
    ///
    /// Instead we register the button + action with <see cref="UIInputBlocker"/>. PlayerInputRouter
    /// detects the tap through the Input System (the same reliable path grid taps use), hit-tests the
    /// panel, and invokes the button under the finger — bypassing UI Toolkit's touch pipeline
    /// entirely. Tap-vs-scroll discrimination is handled there (release with drag &lt;=
    /// <see cref="CameraController.TapThreshold"/>), so a drag scrolls the list and only a real tap
    /// invokes the action.
    ///
    /// Use this INSTEAD of <c>button.clicked += ...</c> for buttons inside scrollable lists (recipe
    /// list, buildings list, building-inspector menus).
    /// </summary>
    public class TapGestureManipulator : Manipulator
    {
        private readonly Action _onTap;

        // moveThreshold is kept for call-site compatibility; tap-vs-drag is now decided by
        // PlayerInputRouter against CameraController.TapThreshold.
        public TapGestureManipulator(Action onTap, float moveThreshold = CameraController.TapThreshold)
        {
            _onTap = onTap;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            // The button may not be parented into a panel yet, but registration is panel-independent,
            // so register immediately and keep it in sync with attach/detach for list rebuilds.
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            UIInputBlocker.RegisterTap(target, _onTap);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            UIInputBlocker.UnregisterTap(target);
        }

        private void OnAttach(AttachToPanelEvent evt) => UIInputBlocker.RegisterTap(target, _onTap);
        private void OnDetach(DetachFromPanelEvent evt) => UIInputBlocker.UnregisterTap(target);
    }
}
