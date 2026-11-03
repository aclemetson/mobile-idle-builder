using System.Collections.Generic;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure value-resolution helpers for feature flags, split out from <see cref="FeatureFlagService"/>
    /// so the remote &gt; cache &gt; default precedence is unit-testable without Unity or the network.
    ///
    /// Values are stored as invariant strings in a single dictionary. The service builds that
    /// dictionary by loading the on-disk cache first and then overlaying freshly-fetched remote
    /// values on top (see <see cref="MergeInto"/>), so a present key always reflects the most
    /// authoritative source and an absent key falls through to the caller's default.
    /// </summary>
    public static class FeatureFlagResolver
    {
        public static bool ResolveBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
            => values != null && values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v) ? v : fallback;

        public static float ResolveFloat(IReadOnlyDictionary<string, string> values, string key, float fallback)
            => values != null && values.TryGetValue(key, out var raw) &&
               float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static int ResolveInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
            => values != null && values.TryGetValue(key, out var raw) &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static string ResolveString(IReadOnlyDictionary<string, string> values, string key, string fallback)
            => values != null && values.TryGetValue(key, out var raw) ? raw : fallback;

        /// <summary>Overlays <paramref name="overrides"/> onto <paramref name="target"/> (overrides win).</summary>
        public static void MergeInto(Dictionary<string, string> target, IReadOnlyDictionary<string, string> overrides)
        {
            if (target == null || overrides == null) return;
            foreach (var kvp in overrides)
                target[kvp.Key] = kvp.Value;
        }
    }
}
