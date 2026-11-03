using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Central place for ad-related privacy consent. On iOS 14+ a rewarded-ad SDK should request App
    /// Tracking Transparency (ATT) before initialising; on Android no runtime prompt is required.
    ///
    /// This is intentionally a thin scaffold: it returns immediately so the Mock/CI path never blocks.
    /// The real ATT request is wired at go-live (see docs/levelplay-ads-setup.md) — either via the
    /// LevelPlay consent API or Unity's iOS ATT plugin — and the Info.plist must carry
    /// NSUserTrackingUsageDescription (injected in scripts/archive-upload-ios.sh).
    /// </summary>
    public static class AdConsent
    {
        /// <summary>Requests tracking consent if the platform/policy requires it. No-op scaffold today.</summary>
        public static Task RequestIfNeededAsync()
        {
            // TODO(go-live): on UNITY_IOS, request ATT and await the user's choice before returning.
            return Task.CompletedTask;
        }
    }
}
