namespace MobileIdleBuilder
{
    /// <summary>The value type a feature flag carries. Used by the fetch path to read the
    /// type-appropriate value from Remote Config and store its invariant string form.</summary>
    public enum FlagType { Bool, Float, Int, String }

    /// <summary>One registered flag: its remote key and value type.</summary>
    public readonly struct FlagDef
    {
        public readonly string   Key;
        public readonly FlagType Type;
        public FlagDef(string key, FlagType type) { Key = key; Type = type; }
    }

    /// <summary>
    /// Single source of truth for every feature flag: keys, default values, and typed accessors.
    /// Accessors are null-safe — if <see cref="FeatureFlagService"/> is not yet in the scene or has
    /// not resolved values, they return the compile-time default (the always-safe shipped state).
    ///
    /// Adding a flag: add a key constant + accessor here, add it to <see cref="All"/>, create the key
    /// in the Unity Remote Config dashboard for each environment, then wire the gate. Keep
    /// docs/agents/feature-flags.md in sync.
    /// </summary>
    public static class FeatureFlags
    {
        // ── Keys ──────────────────────────────────────────────────────────────
        public const string PvpEnabledKey         = "pvp.enabled";
        public const string IapEnabledKey         = "iap.enabled";
        public const string CloudSaveEnabledKey   = "cloudsave.enabled";
        public const string DailyEventsEnabledKey = "dailyevents.enabled";

        // ── Defaults ──────────────────────────────────────────────────────────
        // Chosen so a total fetch failure leaves the game in its current shipped behavior.
        public const bool PvpEnabledDefault         = false; // server backend not live yet
        public const bool IapEnabledDefault         = true;
        public const bool CloudSaveEnabledDefault   = true;
        public const bool DailyEventsEnabledDefault = true;

        /// <summary>Every registered flag — drives the fetch loop. Keep in sync with the accessors.</summary>
        public static readonly FlagDef[] All =
        {
            new FlagDef(PvpEnabledKey,         FlagType.Bool),
            new FlagDef(IapEnabledKey,         FlagType.Bool),
            new FlagDef(CloudSaveEnabledKey,   FlagType.Bool),
            new FlagDef(DailyEventsEnabledKey, FlagType.Bool),
        };

        // ── Typed accessors ───────────────────────────────────────────────────
        public static bool PvpEnabled         => GetBool(PvpEnabledKey,         PvpEnabledDefault);
        public static bool IapEnabled         => GetBool(IapEnabledKey,         IapEnabledDefault);
        public static bool CloudSaveEnabled   => GetBool(CloudSaveEnabledKey,   CloudSaveEnabledDefault);
        public static bool DailyEventsEnabled => GetBool(DailyEventsEnabledKey, DailyEventsEnabledDefault);

        static bool GetBool(string key, bool fallback)
        {
            var svc = FeatureFlagService.Instance;
            return svc != null ? svc.GetBool(key, fallback) : fallback;
        }
    }
}
