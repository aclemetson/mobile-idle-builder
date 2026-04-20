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
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return Touchscreen.current.primaryTouch.position.ReadValue();
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
