using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Shared input helpers for touch and mouse (editor) input.
    /// Uses the new Unity Input System.
    /// </summary>
    public static class InputUtils
    {
        public static Vector2 GetPointerPosition()
        {
            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                // Include wasReleasedThisFrame so callers on the release frame still get the
                // correct position instead of falling through to Mouse (which may be null on
                // Android or use a different Y convention).
                if (touch.press.isPressed || touch.press.wasReleasedThisFrame)
                    return touch.position.ReadValue();
            }
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();
            return Vector2.zero;
        }

        public static bool WasPointerPressed()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                return true;
            return false;
        }

        /// <summary>Returns true while the primary pointer is held down this frame.</summary>
        public static bool IsPointerHeld()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                return true;
            return false;
        }

        /// <summary>Returns true on the frame the primary pointer is released.</summary>
        public static bool WasPointerReleased()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
                return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
                return true;
            return false;
        }

        /// <summary>
        /// Number of fingers on the screen now. Iterates the fixed-size touch array rather than relying on
        /// EnhancedTouch, which requires an explicit Enable() call.
        /// </summary>
        public static int ActiveTouchCount()
        {
            if (Touchscreen.current == null) return 0;
            int count = 0;
            foreach (var touch in Touchscreen.current.touches)
                if (touch.press.isPressed) count++;
            return count;
        }

        /// <summary>
        /// Feeds <paramref name="tap"/> from this frame's input: begins it on press (asking
        /// <paramref name="isOverUI"/> about the press point, and whether the camera is still gliding), tracks
        /// it while held, and ends it on release. Returns true on the frame a deliberate tap completes, with
        /// its release position. See <see cref="TapGesture"/> for what disqualifies a press.
        /// </summary>
        public static bool PumpTap(TapGesture tap, System.Func<Vector2, bool> isOverUI, out Vector2 tapPosition)
        {
            tapPosition = default;
            float now = Time.unscaledTime;

            if (WasPointerPressed())
            {
                Vector2 p = GetPointerPosition();
                tap.Begin(p, now, isOverUI?.Invoke(p) ?? false, CameraController.IsGliding);
            }

            if (tap.IsActive && IsPointerHeld())
                tap.Track(GetPointerPosition(), ActiveTouchCount());

            if (tap.IsActive && WasPointerReleased())
            {
                tapPosition = GetPointerPosition();
                return tap.End(tapPosition, now);
            }
            return false;
        }

        /// <summary>
        /// Returns true if the Android back button (or back gesture) was pressed this frame. The Input
        /// System reports it as the Escape key, so this is also Escape on desktop. Unlike
        /// <see cref="WasCancelPressed"/> it ignores the right mouse button: back closes UI layers.
        /// </summary>
        public static bool WasBackPressed() =>
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        /// <summary>
        /// Returns true if the player pressed a cancel input this frame —
        /// Escape key or right mouse button.
        /// </summary>
        public static bool WasCancelPressed()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return true;
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                return true;
            return false;
        }
    }
}
