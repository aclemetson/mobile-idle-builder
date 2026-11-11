using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Ad test mode (<c>ads.test_mode</c>): dev builds can swap the real ad network for a test-ad card
    /// that still grants the reward. Covers the flag rule, provider choice, and the card's callback.
    /// </summary>
    [TestFixture]
    public class AdTestModeTests
    {
        [TearDown]
        public void TearDown() => TestAdProvider.Presenter = null;

        // ── Flag rule ──────────────────────────────────────────────────────────

        [Test]
        public void TestMode_OnlyInDevBuilds()
        {
            Assert.IsTrue (FeatureFlags.ResolveAdsTestMode(devBuild: true,  flagValue: true));
            Assert.IsFalse(FeatureFlags.ResolveAdsTestMode(devBuild: true,  flagValue: false));
            Assert.IsFalse(FeatureFlags.ResolveAdsTestMode(devBuild: false, flagValue: true),
                "A production build must ignore the flag, so a dashboard mistake can't hand out free rewards");
            Assert.IsFalse(FeatureFlags.ResolveAdsTestMode(devBuild: false, flagValue: false));
        }

        [Test]
        public void TestMode_DefaultsOn_SoDevBuildsNeverShowRealAdsByAccident()
        {
            Assert.IsTrue(FeatureFlags.AdsTestModeDefault);
            CollectionAssert.Contains(
                System.Array.ConvertAll(FeatureFlags.All, f => f.Key), FeatureFlags.AdsTestModeKey,
                "ads.test_mode must be fetched from Remote Config");
        }

        // ── Provider choice ────────────────────────────────────────────────────

        [Test]
        public void CreateProvider_TestMode_UsesTestProvider() =>
            Assert.IsInstanceOf<TestAdProvider>(AdService.CreateProvider(testMode: true));

        [Test]
        public void CreateProvider_NotTestMode_InEditor_UsesMock() =>
            Assert.IsInstanceOf<MockAdProvider>(AdService.CreateProvider(testMode: false));

        // ── Test-ad card callback ──────────────────────────────────────────────

        [Test]
        public void Finish_EarnsTheReward()
        {
            System.Action<bool> close = null;
            TestAdProvider.Presenter = cb => close = cb;
            bool? earned = null;

            new TestAdProvider().ShowRewarded(e => earned = e);
            Assert.IsNull(earned, "Nothing is decided until the tester picks a button");

            close(true);
            Assert.AreEqual(true, earned);
        }

        [Test]
        public void Skip_EarnsNothing()
        {
            TestAdProvider.Presenter = cb => cb(false);
            bool? earned = null;
            new TestAdProvider().ShowRewarded(e => earned = e);
            Assert.AreEqual(false, earned);
        }

        [Test]
        public void Callback_FiresOnlyOnce()
        {
            TestAdProvider.Presenter = cb => { cb(true); cb(false); cb(true); };
            int calls = 0;
            new TestAdProvider().ShowRewarded(_ => calls++);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void NoCard_CountsAsWatched()
        {
            TestAdProvider.Presenter = null;
            bool? earned = null;
            new TestAdProvider().ShowRewarded(e => earned = e);
            Assert.AreEqual(true, earned);
        }

        [Test]
        public void TestProvider_IsAlwaysReady()
        {
            var p = new TestAdProvider();
            Assert.IsTrue(p.IsRewardedReady);
            p.LoadRewarded();
            Assert.IsTrue(p.IsRewardedReady);
        }

        // ── Android back ───────────────────────────────────────────────────────

        [Test]
        public void Back_SkipsTheTestAd_BeforeAnyOtherModal()
        {
            var s = new BackNavigation.State
            {
                TestAdOpen = true, ConfirmOpen = true, SurgeOfferOpen = true, PanelOpen = true,
            };
            Assert.AreEqual(BackNavigation.Layer.TestAd, BackNavigation.TopLayer(s));
        }
    }
}
