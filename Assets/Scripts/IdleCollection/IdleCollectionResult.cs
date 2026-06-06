using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Result of a single offline idle collection calculation.
    /// Passed from OfflineCollectionService to IdleReturnSubController for display.
    /// </summary>
    public class IdleCollectionResult
    {
        public float ElapsedSeconds;
        public float CappedSeconds;
        public float MaxSeconds;
        public Dictionary<int, int> ItemsEarned = new();
        public long  EntropyEarned;

        public bool HasAnyOutput =>
            EntropyEarned > 0 || (ItemsEarned != null && ItemsEarned.Count > 0);

        public string FormatElapsed() => FormatDuration(ElapsedSeconds);
        public string FormatCapped()  => FormatDuration(CappedSeconds);
        public string FormatMax()     => FormatDuration(MaxSeconds);

        private static string FormatDuration(float seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}
