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
        public const string PveEnabledKey         = "pve.enabled";
        public const string IapEnabledKey         = "iap.enabled";
        public const string AdsEnabledKey         = "ads.enabled";
        public const string AdsTestModeKey        = "ads.test_mode";
        public const string CloudSaveEnabledKey   = "cloudsave.enabled";
        public const string DailyEventsEnabledKey = "dailyevents.enabled";
        public const string NotificationsEnabledKey = "notifications.enabled";
        public const string PushEnabledKey          = "push.enabled";
        public const string MaintenanceEnabledKey = "maintenance.enabled";
        public const string MaintenanceMessageKey = "maintenance.message";
        public const string MaintenanceUntilUtcKey = "maintenance.untilUtc";
        public const string MaintenanceStartsUtcKey = "maintenance.startsUtc";
        public const string MaintenancePollSecondsKey = "maintenance.pollSeconds";
        public const string AnalyticsEnabledKey    = "analytics.enabled";
        public const string AnalyticsPhaseKey      = "analytics.phase";
        public const string GameDataUpdatedUtcKey  = "gamedata.updatedUtc";
        public const string GameDataVersionKey     = "gamedata.version";
        public const string GameDataNoticeTitleKey = "gamedata.noticeTitle";
        public const string GameDataNoticeMessageKey = "gamedata.noticeMessage";
        public const string GameDataOverridesKey   = "gamedata.overrides";
        public const string WhatsNewVersionKey     = "whatsnew.version";   // written by CI with the notes (#502)
        public const string WhatsNewNotesKey       = "whatsnew.notes";
        public const string FeedbackEmailKey       = "links.feedbackEmail";
        public const string PrivacyPolicyUrlKey    = "links.privacyPolicyUrl";
        public const string DiscordUrlKey          = "links.discordUrl";
        public const string UpdateEnabledKey       = "update.enabled";
        public const string UpdateLatestAndroidKey = "update.latest.android";
        public const string UpdateLatestIosKey     = "update.latest.ios";
        public const string UpdateMinimumAndroidKey = "update.minimum.android";
        public const string UpdateMinimumIosKey    = "update.minimum.ios";
        public const string UpdateUrlAndroidKey    = "update.url.android";
        public const string UpdateUrlIosKey        = "update.url.ios";
        public const string UpdateMessageKey       = "update.message";

        // ── Defaults ──────────────────────────────────────────────────────────
        // Chosen so a total fetch failure leaves the game in its current shipped behavior.
        public const bool PveEnabledDefault         = true;  // weekly PvE tournaments (kill-switch; backend: docs/runbooks/pve-backend-setup.md)
        public const bool IapEnabledDefault         = true;
        public const bool AdsEnabledDefault         = true;  // rewarded-ad placements + Free Rewards panel
        // Test ads: no ad network, a "test ad" card instead, reward still granted. Defaults ON so a dev
        // build can never show a real ad unless the dashboard explicitly turns it off. Only read in dev
        // builds (see AdsTestMode), so production ignores the key entirely.
        public const bool AdsTestModeDefault        = true;
        public const bool CloudSaveEnabledDefault   = true;
        public const bool DailyEventsEnabledDefault = true;
        public const bool NotificationsEnabledDefault = true;  // local OS notifications (storage full, research done, ...)
        public const bool PushEnabledDefault          = true;  // remote push registration (UGS Push); also needs analytics.enabled
        public const bool MaintenanceEnabledDefault = false; // default off => fail-open (never locks players out)
        public const string MaintenanceMessageDefault = "We're performing scheduled maintenance. Please check back soon.";
        public const string MaintenanceUntilUtcDefault = ""; // ISO-8601 UTC; blank => no time line shown
        public const string MaintenanceStartsUtcDefault = ""; // ISO-8601 UTC; blank => maintenance.enabled applies at once (#224)
        public const int    MaintenancePollSecondsDefault = 300; // mid-session re-fetch interval, clamped by MaintenancePollSchedule
        public const bool   AnalyticsEnabledDefault  = false;     // opt-in: telemetry stays off until a collection phase is opened
        public const string AnalyticsPhaseDefault    = "default"; // label stamped on every event so phases are segmentable
        // Game-data update notice. updatedUtc drives the once-per-publish popup (blank => never show);
        // version is the human-facing label / low-cardinality telemetry dimension. Defaults keep a fresh
        // install (or any fetch failure) silent until the dashboard publishes a stamp.
        public const string GameDataUpdatedUtcDefault  = "";              // ISO-8601 UTC; blank => no notice
        public const int    GameDataVersionDefault     = 0;
        public const string GameDataNoticeTitleDefault = "Game Updated";
        public const string GameDataNoticeMessageDefault = "We've adjusted some game values to improve balance. Tap to continue.";
        // Curated scalar overrides applied onto the baked ScriptableObjects at boot (config/items/research).
        // JSON blob; default empty => no overrides, the build runs its baked baseline. See GameDataOverrides.
        public const string GameDataOverridesDefault = "{}";
        // What's-new notes (#502): CI writes both from docs/whats-new.md on each release. Blank => no notice.
        public const string WhatsNewDefault = "";
        // Settings > Support links. Remote so the inbox / policy host can move without a release.
        public const string FeedbackEmailDefault    = "clemtek1@gmail.com";
        public const string PrivacyPolicyUrlDefault = "https://aclemetson.github.io/mobile-idle-builder/privacy-policy.html";
        // Community invite (#533): the server's permanent invite. Remote Config can swap it; blank hides the button.
        public const string DiscordUrlDefault       = "https://discord.gg/aVt8Z9ntkC";
        // App update prompt on the splash (AppUpdateCheck). Off by default so a fetch failure never prompts;
        // blank versions never prompt. CI writes update.latest.* after each store upload
        // (scripts/set-remote-config.ps1); update.minimum.* is set by hand to force an update.
        public const bool   UpdateEnabledDefault = false;
        public const string UpdateVersionDefault = "";   // latest / minimum, both platforms
        public const string UpdateMessageDefault = "A new version of Quantum Foundry is available.";

        /// <summary>Every registered flag — drives the fetch loop. Keep in sync with the accessors.</summary>
        public static readonly FlagDef[] All =
        {
            new FlagDef(PveEnabledKey,         FlagType.Bool),
            new FlagDef(IapEnabledKey,         FlagType.Bool),
            new FlagDef(AdsEnabledKey,         FlagType.Bool),
            new FlagDef(AdsTestModeKey,        FlagType.Bool),
            new FlagDef(CloudSaveEnabledKey,   FlagType.Bool),
            new FlagDef(DailyEventsEnabledKey, FlagType.Bool),
            new FlagDef(NotificationsEnabledKey, FlagType.Bool),
            new FlagDef(PushEnabledKey,          FlagType.Bool),
            new FlagDef(MaintenanceEnabledKey, FlagType.Bool),
            new FlagDef(MaintenanceMessageKey, FlagType.String),
            new FlagDef(MaintenanceUntilUtcKey, FlagType.String),
            new FlagDef(MaintenanceStartsUtcKey, FlagType.String),
            new FlagDef(MaintenancePollSecondsKey, FlagType.Int),
            new FlagDef(AnalyticsEnabledKey,   FlagType.Bool),
            new FlagDef(AnalyticsPhaseKey,     FlagType.String),
            new FlagDef(GameDataUpdatedUtcKey,    FlagType.String),
            new FlagDef(GameDataVersionKey,       FlagType.Int),
            new FlagDef(GameDataNoticeTitleKey,   FlagType.String),
            new FlagDef(GameDataNoticeMessageKey, FlagType.String),
            new FlagDef(GameDataOverridesKey,     FlagType.String),
            new FlagDef(WhatsNewVersionKey,       FlagType.String),
            new FlagDef(WhatsNewNotesKey,         FlagType.String),
            new FlagDef(FeedbackEmailKey,         FlagType.String),
            new FlagDef(PrivacyPolicyUrlKey,      FlagType.String),
            new FlagDef(DiscordUrlKey,            FlagType.String),
            new FlagDef(UpdateEnabledKey,         FlagType.Bool),
            new FlagDef(UpdateLatestAndroidKey,   FlagType.String),
            new FlagDef(UpdateLatestIosKey,       FlagType.String),
            new FlagDef(UpdateMinimumAndroidKey,  FlagType.String),
            new FlagDef(UpdateMinimumIosKey,      FlagType.String),
            new FlagDef(UpdateUrlAndroidKey,      FlagType.String),
            new FlagDef(UpdateUrlIosKey,          FlagType.String),
            new FlagDef(UpdateMessageKey,         FlagType.String),
        };

        // ── Typed accessors ───────────────────────────────────────────────────
        public static bool PveEnabled         => GetBool(PveEnabledKey,         PveEnabledDefault);
        public static bool IapEnabled         => GetBool(IapEnabledKey,         IapEnabledDefault);
        public static bool AdsEnabled         => GetBool(AdsEnabledKey,         AdsEnabledDefault);

        /// <summary>
        /// True in the Editor and in builds compiled with DEV_ENVIRONMENT (the ones that talk to the UGS
        /// "development" environment, see UgsEnvironment). Production builds compile this to false.
        /// </summary>
#if UNITY_EDITOR || DEV_ENVIRONMENT
        public const bool IsDevBuild = true;
#else
        public const bool IsDevBuild = false;
#endif

        /// <summary>
        /// Test ads (<c>ads.test_mode</c>): AdService uses <see cref="TestAdProvider"/> instead of the real
        /// ad network. Dev builds only; always false in a production build, whatever the dashboard says.
        /// </summary>
        public static bool AdsTestMode => ResolveAdsTestMode(IsDevBuild, GetBool(AdsTestModeKey, AdsTestModeDefault));

        /// <summary>Pure rule behind <see cref="AdsTestMode"/>, unit-tested.</summary>
        public static bool ResolveAdsTestMode(bool devBuild, bool flagValue) => devBuild && flagValue;
        public static bool CloudSaveEnabled   => GetBool(CloudSaveEnabledKey,   CloudSaveEnabledDefault);
        public static bool DailyEventsEnabled => GetBool(DailyEventsEnabledKey, DailyEventsEnabledDefault);
        public static bool NotificationsEnabled => GetBool(NotificationsEnabledKey, NotificationsEnabledDefault);
        public static bool PushEnabled          => GetBool(PushEnabledKey,          PushEnabledDefault);
        public static bool   MaintenanceEnabled  => GetBool(MaintenanceEnabledKey,  MaintenanceEnabledDefault);
        public static string MaintenanceMessage  => GetString(MaintenanceMessageKey,  MaintenanceMessageDefault);
        public static string MaintenanceUntilUtc => GetString(MaintenanceUntilUtcKey, MaintenanceUntilUtcDefault);
        public static string MaintenanceStartsUtc => GetString(MaintenanceStartsUtcKey, MaintenanceStartsUtcDefault);
        public static int    MaintenancePollSeconds => GetInt(MaintenancePollSecondsKey, MaintenancePollSecondsDefault);
        public static bool   AnalyticsEnabled => GetBool(AnalyticsEnabledKey, AnalyticsEnabledDefault);
        public static string AnalyticsPhase   => GetString(AnalyticsPhaseKey,  AnalyticsPhaseDefault);
        public static string GameDataUpdatedUtc   => GetString(GameDataUpdatedUtcKey,   GameDataUpdatedUtcDefault);
        public static int    GameDataVersion      => GetInt(GameDataVersionKey,         GameDataVersionDefault);
        public static string GameDataNoticeTitle  => GetString(GameDataNoticeTitleKey,  GameDataNoticeTitleDefault);
        public static string GameDataNoticeMessage => GetString(GameDataNoticeMessageKey, GameDataNoticeMessageDefault);
        public static string GameDataOverridesJson => GetString(GameDataOverridesKey,    GameDataOverridesDefault);
        public static string WhatsNewVersion      => GetString(WhatsNewVersionKey,      WhatsNewDefault);
        public static string WhatsNewNotes        => GetString(WhatsNewNotesKey,        WhatsNewDefault);
        public static string FeedbackEmail        => GetString(FeedbackEmailKey,        FeedbackEmailDefault);
        public static string PrivacyPolicyUrl     => GetString(PrivacyPolicyUrlKey,     PrivacyPolicyUrlDefault);
        public static string DiscordUrl           => GetString(DiscordUrlKey,           DiscordUrlDefault);
        public static bool   UpdateEnabled        => GetBool(UpdateEnabledKey,          UpdateEnabledDefault);
        public static string UpdateMessage        => GetString(UpdateMessageKey,        UpdateMessageDefault);

        /// <summary>Raw string flag read (used by AppUpdateCheck for its per-platform keys).</summary>
        public static string GetStringFlag(string key, string fallback) => GetString(key, fallback);

        static bool GetBool(string key, bool fallback)
        {
            var svc = FeatureFlagService.Instance;
            return svc != null ? svc.GetBool(key, fallback) : fallback;
        }

        static int GetInt(string key, int fallback)
        {
            var svc = FeatureFlagService.Instance;
            return svc != null ? svc.GetInt(key, fallback) : fallback;
        }

        static string GetString(string key, string fallback)
        {
            var svc = FeatureFlagService.Instance;
            return svc != null ? svc.GetString(key, fallback) : fallback;
        }
    }
}
