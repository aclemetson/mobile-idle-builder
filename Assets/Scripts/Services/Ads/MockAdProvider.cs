using System;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    /// <summary>
    /// No-network rewarded-ad provider used in the Editor, EditMode tests, and any build without the
    /// LEVELPLAY_ADS define. ShowRewarded completes synchronously, granting the reward by default so the
    /// full reward/limit/UI flow is exercisable offline. Set <see cref="GrantReward"/> to false to drive
    /// the "ad dismissed / no reward" path in tests.
    /// </summary>
    public class MockAdProvider : IAdProvider
    {
        /// <summary>When false, ShowRewarded reports the user did not earn the reward (declined path).</summary>
        public bool GrantReward { get; set; } = true;

        public bool IsRewardedReady { get; private set; } = true;

        public Task InitializeAsync()
        {
            IsRewardedReady = true;
            GameLogger.Debug("[Ads] MockAdProvider initialised (no real ads will be shown).");
            return Task.CompletedTask;
        }

        public void LoadRewarded() => IsRewardedReady = true;

        public void ShowRewarded(Action<bool> onClosed)
        {
            GameLogger.Debug($"[Ads] MockAdProvider.ShowRewarded -> earned={GrantReward}");
            onClosed?.Invoke(GrantReward);
        }
    }
}
