using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Entropy Surge side icon + offer card (EntropySurgeOfferController): when the icon shows, and what
    /// "No thanks" does to the cooldown and the daily count.
    /// </summary>
    [TestFixture]
    public class EntropySurgeOfferTests
    {
        static readonly DateTime Noon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        static bool Show(bool ads = true, bool prestiged = true, bool loaded = true, bool ready = true,
                         int remaining = 8, double cooldownMin = 0, bool cardOpen = false)
            => EntropySurgeOfferController.ShouldShowIcon(ads, prestiged, loaded, ready, remaining,
                                                          TimeSpan.FromMinutes(cooldownMin), cardOpen);

        [Test]
        public void Icon_ShowsWhenEverythingIsReady() => Assert.IsTrue(Show());

        [Test]
        public void Icon_HiddenWhileCoolingDown() => Assert.IsFalse(Show(cooldownMin: 3));

        [Test]
        public void Icon_HiddenWhileTheCardIsOpen() => Assert.IsFalse(Show(cardOpen: true));

        [Test]
        public void Icon_HiddenAtTheDailyCap() => Assert.IsFalse(Show(remaining: 0));

        [Test]
        public void Icon_HiddenBeforeTheFirstPrestige() => Assert.IsFalse(Show(prestiged: false));

        [Test]
        public void Icon_HiddenWhenAdsAreOffOrNotLoaded()
        {
            Assert.IsFalse(Show(ads: false));
            Assert.IsFalse(Show(ready: false));
            Assert.IsFalse(Show(loaded: false));
        }

        [Test]
        public void Decline_RestartsTheCooldown_WithoutUsingAWatch()
        {
            var save = new SaveData();
            AdRewardCalculator.ApplyDailyReset(save, Noon);

            AdRewardCalculator.RestartCooldown(save, AdPlacement.EntropySurge, Noon);

            Assert.AreEqual(TimeSpan.FromMinutes(15),
                AdRewardCalculator.CooldownRemaining(save, AdPlacement.EntropySurge, Noon));
            Assert.AreEqual(0, AdRewardCalculator.GetWatchCount(save, AdPlacement.EntropySurge),
                "Declining must not count toward the daily cap");
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropySurge, Noon.AddMinutes(15)),
                "The offer comes back one cooldown after declining");
        }

        [Test]
        public void Message_ShowsTheAmountAndWatchesLeft()
        {
            Assert.AreEqual("Watch a short ad for +60,000 ◈ entropy?\n(7 left today)",
                EntropySurgeOfferController.MessageText(60_000, 7));
        }
    }
}
