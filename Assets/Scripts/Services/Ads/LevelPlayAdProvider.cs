// Unity LevelPlay rewarded-ad integration.
//
// This file only compiles when the LEVELPLAY_ADS define is set AND com.unity.services.levelplay is
// present in Packages/manifest.json. Until then the game uses MockAdProvider, so the Editor, tests, and
// CI all build and run without the SDK. See docs/levelplay-ads-setup.md for the go-live runbook
// (dashboard app + per-platform ad-unit IDs, the LEVELPLAY_ADS define, ATT/privacy, and test mode).
//
// NOTE: this is a scaffold. The exact LevelPlay API surface (class/method names) varies by SDK version;
// verify the calls below against the installed package before shipping. The structure — Init, load,
// readiness, show-with-result-callback — matches what IAdProvider/AdService expect and should not change.
#if LEVELPLAY_ADS
using System;
using System.Threading.Tasks;
using Unity.Services.LevelPlay;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Wraps a single LevelPlay rewarded ad unit behind <see cref="IAdProvider"/>. App key and ad-unit
    /// IDs come from the LevelPlay dashboard and are injected per platform (see runbook). Consent/ATT is
    /// requested before Init on iOS.
    /// </summary>
    public class LevelPlayAdProvider : IAdProvider
    {
        // LevelPlay App Key + Rewarded Ad Unit ID per platform, from the LevelPlay dashboard. These are
        // public CLIENT identifiers (shipped in the app binary, like an AdMob app/ad-unit id) — not
        // secrets, so they live in source. NOTE: AppKey is the LevelPlay App Key, NOT the numeric Unity
        // Ads Game ID; the Game IDs / Org Core ID configure the Unity Ads network in the dashboard. The
        // server-side reporting API secret (LevelPlay > API Management) is the only thing that must stay
        // out of source. See docs/levelplay-ads-setup.md.
#if UNITY_IOS
        const string AppKey         = "26db953ad";
        const string RewardedAdUnit = "ziuxnckkd9sinb69";
#else
        const string AppKey         = "26db91a25";
        const string RewardedAdUnit = "dw58oie29b1ti30m";
#endif

        LevelPlayRewardedAd _rewarded;
        Action<bool>        _pending;
        bool                _earned;

        public bool IsRewardedReady => _rewarded != null && _rewarded.IsAdReady();

        public async Task InitializeAsync()
        {
            // iOS App Tracking Transparency must be requested before SDK init on iOS 14+.
            await AdConsent.RequestIfNeededAsync();

            // Development builds opt into the LevelPlay Test Suite (must be set before Init). Never enabled
            // in a release build, so production players can't reach it.
            if (UnityEngine.Debug.isDebugBuild)
                LevelPlay.SetMetaData("is_test_suite", "enable");

            var tcs = new TaskCompletionSource<bool>();
            LevelPlay.OnInitSuccess += _ => tcs.TrySetResult(true);
            LevelPlay.OnInitFailed  += err => { GameLogger.Warning($"[Ads] LevelPlay init failed: {err}"); tcs.TrySetResult(false); };
            LevelPlay.Init(AppKey);
            await tcs.Task;

            _rewarded = new LevelPlayRewardedAd(RewardedAdUnit);
            _rewarded.OnAdRewarded += (_, __) => _earned = true;
            _rewarded.OnAdClosed   += _ => { var cb = _pending; _pending = null; cb?.Invoke(_earned); LoadRewarded(); };
            _rewarded.OnAdDisplayFailed += (_, __) => { var cb = _pending; _pending = null; cb?.Invoke(false); LoadRewarded(); };
            LoadRewarded();
        }

        public void LoadRewarded() => _rewarded?.LoadAd();

        public void LaunchTestSuite() => LevelPlay.LaunchTestSuite();

        public void ShowRewarded(Action<bool> onClosed)
        {
            if (!IsRewardedReady)
            {
                onClosed?.Invoke(false);
                return;
            }
            _earned  = false;
            _pending = onClosed;
            _rewarded.ShowAd();
        }
    }
}
#endif
