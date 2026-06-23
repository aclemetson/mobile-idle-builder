using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Abstraction over the analytics backend so call sites (and tests) never touch the
    /// concrete UGS SDK. <see cref="UnityAnalyticsSink"/> is the live implementation; tests
    /// inject a recording mock. Keeping this seam also means a second backend could be added
    /// later (e.g. if raw-event export is ever needed) without changing any gameplay taps.
    /// </summary>
    public interface IAnalyticsSink
    {
        /// <summary>Begin data collection. Safe to call once; implementations must not throw.</summary>
        void StartCollection();

        /// <summary>
        /// Record one custom event. <paramref name="parameters"/> values must be of a type the
        /// backend accepts (string, int, long, float, double, bool, DateTime). Must not throw.
        /// </summary>
        void RecordEvent(string eventName, IDictionary<string, object> parameters);
    }
}
