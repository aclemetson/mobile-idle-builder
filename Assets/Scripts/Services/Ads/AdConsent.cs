using System;
using System.Threading.Tasks;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MobileIdleBuilder
{
    /// <summary>
    /// Central place for ad-related privacy consent. On iOS 14+ the App Tracking Transparency (ATT) prompt is
    /// shown before LevelPlay initialises, so the ad SDK sees the player's choice from its first request.
    /// Android needs no runtime prompt (the AD_ID permission comes in with play-services-ads-identifier).
    ///
    /// Native side: Assets/Plugins/iOS/TrackingAuthorization.mm. Info.plist usage string and framework link:
    /// Assets/Scripts/Editor/IOSTrackingPostProcess.cs. Editor and Android return immediately.
    /// </summary>
    public static class AdConsent
    {
        /// <summary>ATTrackingManagerAuthorizationStatus values.</summary>
        public enum TrackingStatus { NotDetermined = 0, Restricted = 1, Denied = 2, Authorized = 3 }

        // The prompt only appears while the app is active, and the player may leave it open. Ads must
        // not wait forever on it: after this, init goes ahead and the SDK treats tracking as not allowed.
        const double MaxWaitSeconds = 60;
        const int PollMilliseconds = 250;

        /// <summary>Requests tracking consent if the platform requires it, and waits for the answer.</summary>
        public static async Task RequestIfNeededAsync()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if ((TrackingStatus)MIB_TrackingAuthorizationStatus() != TrackingStatus.NotDetermined) return;

            MIB_RequestTrackingAuthorization();
            DateTime deadline = DateTime.UtcNow.AddSeconds(MaxWaitSeconds);
            TrackingStatus status;
            while ((status = (TrackingStatus)MIB_TrackingAuthorizationStatus()) == TrackingStatus.NotDetermined
                   && DateTime.UtcNow < deadline)
                await Task.Delay(PollMilliseconds);
            GameLogger.Info($"[Ads] App Tracking Transparency: {status}");
#else
            await Task.CompletedTask;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int MIB_TrackingAuthorizationStatus();
        [DllImport("__Internal")] static extern void MIB_RequestTrackingAuthorization();
#endif
    }
}
