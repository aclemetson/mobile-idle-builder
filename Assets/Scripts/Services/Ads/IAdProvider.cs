using System;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    /// <summary>
    /// SDK-agnostic rewarded-ad provider. AdService talks only to this interface so the concrete
    /// ad network (Unity LevelPlay) can be swapped for the MockAdProvider in the Editor, tests, and CI.
    ///
    /// Lifecycle: InitializeAsync() once at startup, then LoadRewarded() to pre-cache. IsRewardedReady
    /// reflects whether a rewarded ad can be shown right now. ShowRewarded invokes its callback with
    /// true if the user earned the reward (watched to completion) or false if they dismissed/failed it.
    /// </summary>
    public interface IAdProvider
    {
        /// <summary>Initialises the underlying SDK. Idempotent and safe to await more than once.</summary>
        Task InitializeAsync();

        /// <summary>True when a rewarded ad is cached and can be shown immediately.</summary>
        bool IsRewardedReady { get; }

        /// <summary>Requests/pre-caches the next rewarded ad. No-op if one is already loading/ready.</summary>
        void LoadRewarded();

        /// <summary>
        /// Shows a rewarded ad. <paramref name="onClosed"/> is called once with <c>true</c> when the
        /// reward is earned, or <c>false</c> if the ad was dismissed early, failed, or none was ready.
        /// </summary>
        void ShowRewarded(Action<bool> onClosed);
    }
}
