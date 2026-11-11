using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Backoff for retrying a failed ad-network call (SDK init or a rewarded-ad load). Without it a single
    /// "no fill" or offline moment left the game with no ad for the rest of the session, which hid the
    /// Entropy Surge icon and greyed out every Watch button.
    /// Delays double from <see cref="FirstDelaySeconds"/> up to <see cref="MaxDelaySeconds"/>, and reset
    /// once a call succeeds. Pure, so the schedule is unit-tested; the provider owns the actual waiting.
    /// </summary>
    public sealed class AdLoadRetry
    {
        public const float FirstDelaySeconds = 5f;
        public const float MaxDelaySeconds   = 120f;

        /// <summary>Consecutive failures since the last success.</summary>
        public int Failures { get; private set; }

        /// <summary>Delay before the next attempt after <paramref name="failures"/> consecutive failures (1-based).</summary>
        public static float DelayFor(int failures)
        {
            if (failures <= 0) return 0f;
            // Cap the exponent so the double never overflows on a very long outage.
            double delay = FirstDelaySeconds * Math.Pow(2, Math.Min(failures - 1, 16));
            return (float)Math.Min(MaxDelaySeconds, delay);
        }

        /// <summary>Records a failure and returns how long to wait before trying again.</summary>
        public float OnFailed() => DelayFor(++Failures);

        /// <summary>Records a success: the next failure starts the schedule again from the first delay.</summary>
        public void OnSucceeded() => Failures = 0;
    }
}
