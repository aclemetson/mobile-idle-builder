using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The UTC "now" for gameplay timers: research, field cooldowns, speed boosts, offline
    /// earnings, daily/weekly resets. Use it instead of DateTime.UtcNow for anything a player
    /// waits on. Release builds always read the wall clock; editor and dev builds can shift it
    /// so AI playtests can freeze timers while the agent thinks and fast-forward a player's
    /// absence (Playtests/README.md). Auth and telemetry timestamps stay on the wall clock.
    /// </summary>
    public static class GameClock
    {
        private static TimeSpan s_Offset = TimeSpan.Zero;

        public static DateTime UtcNow => DateTime.UtcNow + s_Offset;

        /// <summary>How far gameplay time is ahead of (positive) or behind the wall clock.</summary>
        public static TimeSpan Offset => s_Offset;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Moves gameplay time by <paramref name="delta"/> (negative holds timers back).</summary>
        public static void DevShift(TimeSpan delta) => s_Offset += delta;

        public static void DevReset() => s_Offset = TimeSpan.Zero;
#endif
    }
}
