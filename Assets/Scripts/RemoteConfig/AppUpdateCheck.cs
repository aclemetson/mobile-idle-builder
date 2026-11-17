using System;

namespace MobileIdleBuilder
{
    /// <summary>What the splash does about a newer build.</summary>
    public enum AppUpdateKind
    {
        None,       // up to date, prompt disabled, or nothing published: load the game
        Optional,   // a newer build exists: "Update" / "Later"
        Required,   // this build is below the minimum: "Update" only
    }

    /// <summary>
    /// Pure decision for the boot-time update prompt (shown by SplashScreenController). No Unity/UGS
    /// dependencies, so it is unit-tested like <see cref="GameUpdateNotice"/>.
    ///
    /// The versions come from Remote Config: <c>update.latest.*</c> is written by CI after each store upload
    /// (scripts/set-remote-config.ps1); <c>update.minimum.*</c> is set by hand to force an update. The
    /// current version is <c>Application.version</c> (bundleVersion, semver).
    /// </summary>
    public static class AppUpdateCheck
    {
        /// <summary>Play listing. Opted-in internal testers get the test build from it, so dev and prod share it.</summary>
        public const string AndroidStoreUrl = "https://play.google.com/store/apps/details?id=com.clemtek.mobileidlebuilder";

        /// <summary>Opens the TestFlight app, where testers install the newest build.</summary>
        public const string TestFlightUrl = "itms-beta://";

        /// <summary>
        /// Required when <paramref name="current"/> is below <paramref name="minimum"/>, Optional when below
        /// <paramref name="latest"/>, otherwise None. Disabled, or an unparseable current version, is None.
        /// A blank or unparseable latest/minimum is ignored. Never throws.
        /// </summary>
        public static AppUpdateKind Decide(bool enabled, string current, string latest, string minimum)
        {
            if (!enabled || !TryParse(current, out var cur)) return AppUpdateKind.None;
            if (TryParse(minimum, out var min) && cur < min) return AppUpdateKind.Required;
            if (TryParse(latest,  out var lat) && cur < lat) return AppUpdateKind.Optional;
            return AppUpdateKind.None;
        }

        /// <summary>
        /// The link the Update button opens: the Remote Config value when set, else the built-in default
        /// (Play listing on Android; TestFlight on iOS dev builds; nothing on iOS production until the
        /// App Store link is set in the production environment). Empty means "no button".
        /// </summary>
        public static string ResolveUrl(bool ios, bool devBuild, string remoteUrl)
        {
            if (!string.IsNullOrWhiteSpace(remoteUrl)) return remoteUrl.Trim();
            if (!ios) return AndroidStoreUrl;
            return devBuild ? TestFlightUrl : "";
        }

        /// <summary>
        /// Parses "major.minor[.build[.revision]]", ignoring a pre-release/build suffix ("0.5.52-dev").
        /// Missing parts count as 0, so "0.6" equals "0.6.0".
        /// </summary>
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string core = text.Trim();
            int cut = core.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) core = core.Substring(0, cut);
            if (!Version.TryParse(core, out var v)) return false;
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
            return true;
        }

        // ── Per-platform reads (the Editor follows the active build target) ──────────────────────

#if UNITY_IOS
        public const bool IsIos = true;
        static string LatestKey  => FeatureFlags.UpdateLatestIosKey;
        static string MinimumKey => FeatureFlags.UpdateMinimumIosKey;
        static string UrlKey     => FeatureFlags.UpdateUrlIosKey;
#else
        public const bool IsIos = false;
        static string LatestKey  => FeatureFlags.UpdateLatestAndroidKey;
        static string MinimumKey => FeatureFlags.UpdateMinimumAndroidKey;
        static string UrlKey     => FeatureFlags.UpdateUrlAndroidKey;
#endif

        /// <summary>This platform's latest published version (Remote Config).</summary>
        public static string LatestVersion  => FeatureFlags.GetStringFlag(LatestKey,  FeatureFlags.UpdateVersionDefault);

        /// <summary>This platform's minimum supported version (Remote Config).</summary>
        public static string MinimumVersion => FeatureFlags.GetStringFlag(MinimumKey, FeatureFlags.UpdateVersionDefault);

        /// <summary>This platform's update link, after defaults.</summary>
        public static string UpdateUrl =>
            ResolveUrl(IsIos, FeatureFlags.IsDevBuild, FeatureFlags.GetStringFlag(UrlKey, ""));

        /// <summary>The decision for this build, from the current flag values.</summary>
        public static AppUpdateKind Evaluate(string currentVersion) =>
            Decide(FeatureFlags.UpdateEnabled, currentVersion, LatestVersion, MinimumVersion);
    }
}
