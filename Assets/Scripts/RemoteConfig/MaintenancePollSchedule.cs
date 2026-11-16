namespace MobileIdleBuilder
{
    /// <summary>
    /// When <see cref="MaintenanceWatcher"/> re-fetches Remote Config to look for maintenance (#224). Pure,
    /// so it is unit-tested. Times are realtime seconds.
    ///
    /// <list type="bullet">
    /// <item>Every <see cref="PollSeconds"/> while in GameScene.</item>
    /// <item>Immediately when the app returns from the background, since a resumed app may have
    /// missed the switch-on while it was away. Without this it waited out the rest of the 5-minute timer.</item>
    /// <item>Never twice within <see cref="MinGapSeconds"/>: pause/resume bounces (a rewarded ad closing,
    /// a permission dialog) must not fire a fetch each time.</item>
    /// </list>
    /// </summary>
    public static class MaintenancePollSchedule
    {
        public const float PollSeconds   = 300f;   // 5 minutes
        public const float MinGapSeconds = 10f;

        /// <param name="now">Current realtime.</param>
        /// <param name="lastCheck">Realtime of the last check (start of the session counts as one).</param>
        /// <param name="resumeRequested">The app came back from the background since the last check.</param>
        public static bool IsDue(float now, float lastCheck, bool resumeRequested)
        {
            float since = now - lastCheck;
            if (since < MinGapSeconds) return false;
            return resumeRequested || since >= PollSeconds;
        }
    }
}
