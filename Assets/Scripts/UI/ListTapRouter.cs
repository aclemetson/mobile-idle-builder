using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Routes taps for buttons inside HUD lists, replacing <c>TapGestureManipulator</c> (which could
    /// never work - see below). Register a button with the container it lives in; the container
    /// watches pointer down/up and invokes the registered action for whichever registered element
    /// sits under the release point, as long as the pointer did not travel far enough to be a scroll.
    ///
    /// Why a per-button manipulator cannot work, verified on device and in the Editor:
    ///
    ///   1. A <see cref="Button"/> always owns a built-in <c>Clickable</c>, registered in the BUBBLE
    ///      phase by Button's constructor - so before any manipulator added later. On pointer-down it
    ///      captures the pointer and stops immediate propagation, so a second pointer-down handler on
    ///      the same element never runs. The button still shows its :active press state, which is why
    ///      this looked like "the button highlights but nothing happens".
    ///   2. The captured pointer-up is delivered to the capture holder (the button), where Clickable
    ///      runs first and calls StopPropagation, so the up never reaches an ancestor either.
    ///   3. Separately, on TOUCH only, ScrollView.OnPointerDown (registered TrickleDown) captures its
    ///      contentContainer and calls StopPropagation, so a descendant button receives no pointer-down
    ///      at all. No per-button approach of any kind survives that.
    ///
    /// The fixes that follow from those three facts, and which this class applies:
    ///
    ///   - Clear the button's <c>clickable</c> on registration. Without that, Clickable consumes the
    ///     gesture before this router can see it (points 1 and 2). This is why registering a button
    ///     here and ALSO assigning <c>button.clicked</c> will not work - the router owns the gesture.
    ///   - Listen on the CONTAINER, not the button, and register TrickleDown so the handler runs
    ///     before anything nested can consume the event (point 3). ScrollView uses plain
    ///     StopPropagation, which does not block other callbacks on the same element, so a handler
    ///     attached to the ScrollView still runs even when it captures for touch-scrolling.
    ///   - Also listen for bubbling pointer-up, because on touch the capture holder is the
    ///     ScrollView's contentContainer and the up bubbles from there rather than trickling down.
    ///
    /// This mirrors the approach <c>MaxwellsDemonController</c> already uses for its inventory grid,
    /// which is the one list in the project whose taps have always worked on device.
    ///
    /// Scrolling is unaffected: nothing here captures the pointer or stops propagation, and a drag
    /// beyond <see cref="CameraController.TapThreshold"/> is ignored so the ScrollView keeps it.
    /// </summary>
    public static class ListTapRouter
    {
        // Registered element -> action. Static because HUD lists are rebuilt constantly and the
        // entries are keyed by element identity, so a rebuild simply replaces them.
        static readonly Dictionary<VisualElement, Action> s_actions = new();

        // Containers already wired, so repeated Register calls do not stack duplicate handlers.
        static readonly HashSet<VisualElement> s_containers = new();

        // In-flight gesture. One at a time is enough: these are discrete taps on HUD lists, and a
        // second pointer starting mid-gesture replaces the first rather than queueing.
        static int     s_pointerId = -1;
        static Vector2 s_downPos;
        static bool    s_tracking;

        /// <summary>
        /// Registers <paramref name="button"/> so a tap inside <paramref name="container"/> invokes
        /// <paramref name="onTap"/>. Clears the button's built-in Clickable - do NOT also assign
        /// <c>button.clicked</c>, or the two mechanisms fight and neither is reliable.
        /// </summary>
        public static void Register(VisualElement container, Button button, Action onTap)
        {
            if (button == null || onTap == null) return;

            // Point 1 above: Clickable would otherwise consume the pointer-down before this router.
            button.clickable = null;

            s_actions[button] = onTap;

            // Lists are rebuilt wholesale, so drop the entry when the element leaves the panel
            // rather than letting the dictionary grow with dead elements and captured closures.
            button.RegisterCallback<DetachFromPanelEvent>(_ => s_actions.Remove(button));

            if (container != null && s_containers.Add(container))
            {
                // TrickleDown so this runs before a nested ScrollView can consume the down.
                container.RegisterCallback<PointerDownEvent>(OnContainerPointerDown, TrickleDown.TrickleDown);
                container.RegisterCallback<PointerUpEvent>(OnContainerPointerUp, TrickleDown.TrickleDown);
                // Bubble as well: for a captured touch gesture the up arrives from the capture holder
                // below this container and never trickles down through it.
                container.RegisterCallback<PointerUpEvent>(OnContainerPointerUp);
                container.RegisterCallback<DetachFromPanelEvent>(_ => s_containers.Remove(container));
            }
        }

        /// <summary>Drops every registration. Call on scene teardown so nothing is held across loads.</summary>
        public static void Clear()
        {
            s_actions.Clear();
            s_containers.Clear();
            s_tracking  = false;
            s_pointerId = -1;
        }

        static void OnContainerPointerDown(PointerDownEvent evt)
        {
            s_tracking  = true;
            s_pointerId = evt.pointerId;
            s_downPos   = evt.position;
        }

        static void OnContainerPointerUp(PointerUpEvent evt)
        {
            if (!s_tracking || evt.pointerId != s_pointerId) return;

            // A drag past the threshold was a scroll, not a tap - leave it to the ScrollView.
            if (Vector2.Distance(evt.position, s_downPos) > CameraController.TapThreshold)
            {
                s_tracking = false;
                return;
            }

            // Consume the gesture before invoking: the up is registered in both phases, and the
            // action often rebuilds the list, which must not re-enter through the second handler.
            s_tracking = false;

            var action = Resolve(evt.target as VisualElement);
            action?.Invoke();
        }

        /// <summary>
        /// Walks up from the released element to the nearest registered ancestor, so a tap landing on
        /// a button's child (its icon or text label, which are separate elements) still counts.
        /// Returns null when the element is disabled, so a disabled row swallows the tap rather than
        /// letting it fall through to whatever sits behind it.
        /// </summary>
        internal static Action Resolve(VisualElement released)
        {
            for (var e = released; e != null; e = e.parent)
            {
                if (!s_actions.TryGetValue(e, out var action)) continue;
                return e.enabledInHierarchy ? action : null;
            }
            return null;
        }
    }
}
