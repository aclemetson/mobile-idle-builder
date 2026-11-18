using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Timing for the press-and-hold that puts a placed building into edit mode.
    ///
    /// A tap fires on release, and a normal tap is well under <see cref="RingDelay"/>, so it never shows the
    /// ring. Holding past <see cref="RingDelay"/> shows a ring that fills over the rest of the hold (how much
    /// longer to keep holding). At <see cref="HoldSeconds"/> the building enters edit mode, and that press's
    /// release is swallowed so it is not also read as a tap. Moving the finger past the tap threshold is a pan
    /// and cancels the hold.
    /// </summary>
    public static class LongPress
    {
        /// <summary>Hold time before the progress ring appears (s).</summary>
        public const float RingDelay = 0.2f;

        /// <summary>Total hold time that enters edit mode (s).</summary>
        public const float HoldSeconds = 0.7f;

        /// <summary>True once the hold has lasted long enough to show the ring.</summary>
        public static bool RingVisible(float heldSeconds) => heldSeconds >= RingDelay;

        /// <summary>Ring fill, 0 when it appears to 1 when the hold completes.</summary>
        public static float RingProgress(float heldSeconds) =>
            Mathf.Clamp01((heldSeconds - RingDelay) / (HoldSeconds - RingDelay));

        /// <summary>True once the hold has lasted long enough to enter edit mode.</summary>
        public static bool IsComplete(float heldSeconds) => heldSeconds >= HoldSeconds;
    }
}
