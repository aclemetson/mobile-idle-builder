using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Single source of truth for the color of a transported item (conveyor spheres and
    /// building input-impact FX). Most items get a deterministic golden-ratio hue from their
    /// ItemID; a few have explicit overrides so they read as their in-world identity rather
    /// than a clashing procedural hue.
    /// </summary>
    internal static class ItemColors
    {
        // ItemIDs come from the `itemId` field in Resources/Items/*.asset.
        const int ElectronItemId = 3;

        static readonly Dictionary<int, Color> s_Overrides = new()
        {
            // Electron: its golden-ratio hue (~307 deg) looked like Unity's missing-shader
            // magenta. Pin it to electron_field.fieldColor so the belt item reads as the
            // same particle the field emits.
            { ElectronItemId, new Color(0.30588236f, 0.8039216f, 0.76862746f) },
        };

        /// <summary>Returns the display color for an item by its ItemID.</summary>
        public static Color For(int itemID)
        {
            if (s_Overrides.TryGetValue(itemID, out var c)) return c;
            float hue = (itemID * 0.618034f) % 1f; // golden-ratio spread
            return Color.HSVToRGB(hue, 0.9f, 1f);
        }
    }
}
