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
    }
}
