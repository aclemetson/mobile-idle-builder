using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// When <see cref="MaintenanceWatcher"/> re-fetches Remote Config to look for maintenance, and when a
    /// scheduled window starts (#224). Pure, so it is unit-tested. Poll times are realtime seconds.
    ///
    /// <list type="bullet">
    /// <item>Every <c>maintenance.pollSeconds</c> (default <see cref="PollSeconds"/>, clamped to
    /// <see cref="MinPollSeconds"/>..<see cref="MaxPollSeconds"/>) while in GameScene.</item>
    /// <item>Immediately when the app returns from the background, since a resumed app may have
    /// missed the switch-on while it was away. Without this it waited out the rest of the timer.</item>
    /// <item>Never twice within <see cref="MinGapSeconds"/>: pause/resume bounces (a rewarded ad closing,
    /// a permission dialog) must not fire a fetch each time.</item>
    /// <item>A scheduled window (<c>maintenance.enabled</c> + a future <c>maintenance.startsUtc</c>) starts
    /// on the device clock at that time, with no fetch. Every client that polled after the schedule was set
    /// leaves at the same moment, so a window set at least one poll interval ahead has no poll latency.</item>
    /// </list>
    /// </summary>
    public static class MaintenancePollSchedule
    {
        public const float PollSeconds    = 300f;   // 5 minutes
        public const float MinGapSeconds  = 10f;
        public const float MinPollSeconds = 60f;    // floor so a typo can't make every client hammer Remote Config
        public const float MaxPollSeconds = 1800f;

        /// <summary>The remote <c>maintenance.pollSeconds</c> value, clamped to a safe range.</summary>
        public static float ClampPollSeconds(int remoteSeconds)
        {
            if (remoteSeconds <= 0) return PollSeconds;
            return Math.Min(MaxPollSeconds, Math.Max(MinPollSeconds, remoteSeconds));
        }

        /// <param name="now">Current realtime.</param>
        /// <param name="lastCheck">Realtime of the last check (start of the session counts as one).</param>
        /// <param name="resumeRequested">The app came back from the background since the last check.</param>
        /// <param name="pollSeconds">Interval between checks (see <see cref="ClampPollSeconds"/>).</param>
        public static bool IsDue(float now, float lastCheck, bool resumeRequested, float pollSeconds = PollSeconds)
        {
            float since = now - lastCheck;
            if (since < MinGapSeconds) return false;
            return resumeRequested || since >= pollSeconds;
        }

        /// <summary>
        /// Whether maintenance is in force at <paramref name="nowUtc"/>. Off unless <paramref name="enabled"/>.
        /// A blank start means "now". An unparseable start also counts as now, because the operator meant
        /// maintenance to be on; the splash gate still fails open when it can't reach Remote Config.
        /// </summary>
        public static bool IsActive(bool enabled, string startsUtc, DateTime nowUtc)
        {
            if (!enabled) return false;
            if (!TryParseUtc(startsUtc, out var start)) return true;
            return nowUtc >= start;
        }

        /// <summary>
        /// True when maintenance is switched on but its start is still ahead: the watcher arms a local
        /// trip for that moment instead of waiting for its next fetch.
        /// </summary>
        public static bool IsScheduledAhead(bool enabled, string startsUtc, DateTime nowUtc)
            => enabled && TryParseUtc(startsUtc, out var start) && nowUtc < start;

        public static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(iso)) return false;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
        }
    }
}
