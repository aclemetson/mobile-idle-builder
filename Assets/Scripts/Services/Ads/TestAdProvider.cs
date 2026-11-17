using System;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Ad provider for test mode (<c>ads.test_mode</c>, dev builds only). Never touches an ad network: a
    /// "watch" opens a test-ad card (<see cref="TestAdOverlayController"/>) where the tester picks Finish
    /// (reward earned) or Skip (no reward). AdService then grants the reward through its normal path, so
    /// daily caps, cooldowns and saving are all exercised exactly as with a real ad.
    ///
    /// If no card is registered (no HUD yet), the ad counts as watched, like <see cref="MockAdProvider"/>.
    /// </summary>
    public class TestAdProvider : IAdProvider
    {
        /// <summary>
        /// Shows the test-ad card. The UI sets this; the argument is called once with true (Finish) or false (Skip).
        /// </summary>
        public static Action<Action<bool>> Presenter { get; set; }

        public bool IsRewardedReady => true;

        public Task InitializeAsync()
        {
            GameLogger.Info("[Ads] Test mode: no real ads will be shown (ads.test_mode).");
            return Task.CompletedTask;
        }

        public void LoadRewarded() { }

        public void ShowRewarded(Action<bool> onClosed)
        {
            var presenter = Presenter;
            if (presenter == null)
            {
                GameLogger.Info("[Ads] Test ad: no card available, counting it as watched.");
                onClosed?.Invoke(true);
                return;
            }

            bool closed = false;
            presenter(earned =>
            {
                if (closed) return;   // the callback fires exactly once
                closed = true;
                GameLogger.Info($"[Ads] Test ad closed, earned={earned}.");
                onClosed?.Invoke(earned);
            });
        }

        public void LaunchTestSuite() =>
            GameLogger.Info("[Ads] Test Suite is not available in ad test mode. Turn ads.test_mode off to use it.");
    }
}
