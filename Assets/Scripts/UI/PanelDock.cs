using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Where floating HUD pieces (the "Show hint" pill, the tutorial dialogue) go while a slide panel is open,
    /// so they sit on the map instead of over the panel's buttons. In portrait a slide panel is a bottom sheet,
    /// so they go in the strip of map above it; in landscape it is a side sheet on the right, so they go in the
    /// strip on its left. When neither strip is big enough they keep their normal place.
    ///
    /// Before this, the pill sat on the research card's Start button and the craft queue's Cancel button, and
    /// the dialogue covered the very recipe it asked the player to craft (2026-11-19 device screenshots).
    /// </summary>
    public readonly struct PanelDock
    {
        public enum Side { None, Above, Left }

        /// <summary>Gap between a docked piece and the panel or the frame edge.</summary>
        public const float Gap = 8f;

        public readonly Side Where;
        /// <summary>Free map area, in the frame's coordinates (top-left origin).</summary>
        public readonly Rect Free;

        PanelDock(Side where, Rect free) { Where = where; Free = free; }

        public static readonly PanelDock None = new(Side.None, Rect.zero);

        /// <summary>
        /// Picks the free strip next to an open panel. <paramref name="frame"/> is the size of the element the
        /// panel and the floating pieces share; <paramref name="panel"/> is the panel's layout rect inside it;
        /// <paramref name="topReserved"/> is the top bar height, which nothing docks over.
        /// </summary>
        public static PanelDock For(Vector2 frame, Rect panel, float topReserved, float minHeight, float minWidth)
        {
            if (frame.x <= 0f || frame.y <= 0f || panel.width <= 0f || panel.height <= 0f) return None;

            // Side sheet: map visible on the left.
            if (panel.xMin - 2 * Gap >= minWidth)
            {
                float top = Mathf.Max(topReserved, 0f) + Gap;
                var free = Rect.MinMaxRect(Gap, top, panel.xMin - Gap, frame.y - Gap);
                if (free.height >= minHeight) return new PanelDock(Side.Left, free);
            }

            // Bottom sheet: map visible above.
            {
                float top = Mathf.Max(topReserved, 0f) + Gap;
                var free = Rect.MinMaxRect(Gap, top, frame.x - Gap, panel.yMin - Gap);
                if (free.height >= minHeight && free.width >= minWidth) return new PanelDock(Side.Above, free);
            }
            return None;
        }

        /// <summary>Distance from the frame's right edge to the free area's right edge.</summary>
        public float RightInset(Vector2 frame) => frame.x - Free.xMax;
        /// <summary>Distance from the frame's bottom edge to the free area's bottom edge.</summary>
        public float BottomInset(Vector2 frame) => frame.y - Free.yMax;

        /// <summary>
        /// Puts <paramref name="e"/> in the bottom-right corner of the free area, or clears the inline
        /// position so its USS placement applies again when nothing is docked.
        /// </summary>
        public void PlaceBottomRight(VisualElement e, Vector2 frame)
        {
            if (e == null) return;
            if (Where == Side.None)
            {
                e.style.right  = StyleKeyword.Null;
                e.style.bottom = StyleKeyword.Null;
                return;
            }
            e.style.right  = RightInset(frame);
            e.style.bottom = BottomInset(frame);
        }

        /// <summary>
        /// Stretches <paramref name="e"/> over the free area (height from its edges), or clears the inline
        /// position so its USS placement applies again.
        /// </summary>
        public void Fill(VisualElement e, Vector2 frame)
        {
            if (e == null) return;
            if (Where == Side.None)
            {
                e.style.left = e.style.right = e.style.top = e.style.bottom = StyleKeyword.Null;
                e.style.height = StyleKeyword.Null;
                return;
            }
            e.style.left   = Free.xMin;
            e.style.right  = RightInset(frame);
            e.style.top    = Free.yMin;
            e.style.bottom = BottomInset(frame);
            e.style.height = StyleKeyword.Auto;
        }
    }
}
