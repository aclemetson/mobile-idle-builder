using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The swipe that dismisses the notification banner: sideways either way, or up. Down does nothing, since
    /// the banner has nowhere to go that way. Works in panel pixels with y growing downward.
    /// </summary>
    public static class BannerSwipe
    {
        public const float DismissDistance = 40f;
        /// <summary>A press that moves less than this is a tap (opens the banner's target), not a swipe.</summary>
        public const float TapSlop = 12f;
        const float ExitUp = 200f;

        public static bool IsTap(Vector2 drag) => drag.magnitude <= TapSlop;

        /// <summary>Where the banner sits for a drag: locked to the drag's main axis, and never below its home.</summary>
        public static Vector2 Follow(Vector2 drag) =>
            Mathf.Abs(drag.x) >= Mathf.Abs(drag.y)
                ? new Vector2(drag.x, 0f)
                : new Vector2(0f, Mathf.Min(drag.y, 0f));

        public static bool Dismisses(Vector2 drag)
        {
            var moved = Follow(drag);
            return Mathf.Abs(moved.x) >= DismissDistance || moved.y <= -DismissDistance;
        }

        /// <summary>Where a dismissed banner slides to, continuing the swipe's direction.</summary>
        public static Vector2 ExitOffset(Vector2 drag, float bannerWidth)
        {
            var moved = Follow(drag);
            return moved.x != 0f
                ? new Vector2(Mathf.Sign(moved.x) * (bannerWidth + DismissDistance), 0f)
                : new Vector2(0f, -ExitUp);
        }
    }
}
