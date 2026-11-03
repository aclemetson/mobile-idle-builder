using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Abstraction over the analytics backend so call sites (and tests) never touch the
    /// concrete UGS SDK. <see cref="UnityAnalyticsSink"/> is the live implementation; tests
    /// inject a recording mock. Keeping this seam also means a second backend could be added
    /// later (e.g. if raw-event export is ever needed) without changing any gameplay taps.
    ///
    /// Implementations must not throw. They report failure by returning false instead, so the
    /// caller can count it and the dev console can tell the truth about what reached the backend
    /// — a swallowed exception that still looks like success is worse than no telemetry at all.
    /// </summary>
    public interface IAnalyticsSink
    {
        /// <summary>Begin data collection. Returns false if the backend refused or threw.</summary>
        bool StartCollection();

        /// <summary>
        /// Record one custom event. <paramref name="parameters"/> values must be of a type the
        /// backend accepts (string, int, long, float, double, bool, DateTime). Returns false if
        /// the backend refused or threw.
        /// </summary>
        bool RecordEvent(string eventName, IDictionary<string, object> parameters);

        /// <summary>
        /// Force-upload buffered events now (events are otherwise batched). Returns false if the
        /// backend refused or threw.
        /// </summary>
        bool Flush();

        /// <summary>
        /// One-line description of backend state (SDK compiled in? services initialized? player
        /// signed in? which environment?) for the dev console. Diagnostic only — never parsed.
        /// </summary>
        string Describe();
    }
}
