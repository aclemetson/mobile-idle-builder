using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class AdRewardCalculatorTests
    {
        static SaveData NewSave() => new SaveData();

        // ── Placement table sanity ────────────────────────────────────────────

        [Test]
        public void AllPlacements_HavePositiveCapAndName()
        {
            foreach (var def in AdRewardCalculator.Placements)
            {
                Assert.Greater(def.DailyCap, 0, $"{def.Placement} cap");
                Assert.IsFalse(string.IsNullOrEmpty(def.Name), $"{def.Placement} name");
                Assert.IsFalse(string.IsNullOrEmpty(def.Description), $"{def.Placement} description");
                Assert.AreEqual(def.Placement.ToString(), def.Id, "Id must equal the enum name");
            }
        }

        [Test]
        public void EntropyBoost_IsCappedAtFivePerDay()
        {
            Assert.AreEqual(5, AdRewardCalculator.DailyCap(AdPlacement.EntropyBoost));
        }

        [Test]
        public void EveryPlacement_HasaDef()
        {
            foreach (AdPlacement p in Enum.GetValues(typeof(AdPlacement)))
                Assert.IsNotNull(AdRewardCalculator.Def(p), $"missing def for {p}");
        }

        // ── Entropy reward math ───────────────────────────────────────────────

        [Test]
        public void CalcEntropyReward_UsesNetWorthFraction_WhenAboveFloor()
        {
            // 10% of 100,000 = 10,000, well above the 500 floor.
            Assert.AreEqual(10_000, AdRewardCalculator.CalcEntropyReward(100_000f));
        }

        [Test]
        public void CalcEntropyReward_AppliesFloor_WhenNetWorthIsTiny()
        {
            // 10% of 100 = 10, below the 500 floor → floor wins.
            Assert.AreEqual(500, AdRewardCalculator.CalcEntropyReward(100f));
        }

        // ── Daily cap bookkeeping ─────────────────────────────────────────────

        [Test]
        public void IncrementWatch_RaisesCountAndLowersRemaining()
        {
            var save = NewSave();
            Assert.AreEqual(0, AdRewardCalculator.GetWatchCount(save, AdPlacement.SpeedBoost));
            Assert.AreEqual(2, AdRewardCalculator.Remaining(save, AdPlacement.SpeedBoost));

            AdRewardCalculator.IncrementWatch(save, AdPlacement.SpeedBoost);

            Assert.AreEqual(1, AdRewardCalculator.GetWatchCount(save, AdPlacement.SpeedBoost));
            Assert.AreEqual(1, AdRewardCalculator.Remaining(save, AdPlacement.SpeedBoost));
            Assert.IsFalse(AdRewardCalculator.IsAtCap(save, AdPlacement.SpeedBoost));
        }

        [Test]
        public void IsAtCap_TrueOnceLimitReached()
        {
            var save = NewSave();
            for (int i = 0; i < AdRewardCalculator.DailyCap(AdPlacement.Crystals); i++)
                AdRewardCalculator.IncrementWatch(save, AdPlacement.Crystals);

            Assert.IsTrue(AdRewardCalculator.IsAtCap(save, AdPlacement.Crystals));
            Assert.AreEqual(0, AdRewardCalculator.Remaining(save, AdPlacement.Crystals));
        }

        [Test]
        public void Counters_AreIndependentPerPlacement()
        {
            var save = NewSave();
            AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropyBoost);
            AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropyBoost);

            Assert.AreEqual(2, AdRewardCalculator.GetWatchCount(save, AdPlacement.EntropyBoost));
            Assert.AreEqual(0, AdRewardCalculator.GetWatchCount(save, AdPlacement.TimeWarp));
        }

        // ── Daily reset (UTC midnight, injected now) ──────────────────────────

        [Test]
        public void ApplyDailyReset_FirstCall_StampsNextMidnight()
        {
            var save = NewSave();
            var now  = new DateTime(2026, 6, 23, 14, 0, 0, DateTimeKind.Utc);

            AdRewardCalculator.ApplyDailyReset(save, now);

            Assert.IsFalse(string.IsNullOrEmpty(save.adWatchResetUtc));
            var reset = DateTime.Parse(save.adWatchResetUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.AreEqual(new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc), reset.ToUniversalTime());
        }

        [Test]
        public void ApplyDailyReset_WithinSameDay_KeepsCounts()
        {
            var save = NewSave();
            var morning = new DateTime(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc);
            AdRewardCalculator.ApplyDailyReset(save, morning);
            AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropyBoost);

            // Later the same UTC day — must NOT reset.
            var evening = new DateTime(2026, 6, 23, 22, 0, 0, DateTimeKind.Utc);
            AdRewardCalculator.ApplyDailyReset(save, evening);

            Assert.AreEqual(1, AdRewardCalculator.GetWatchCount(save, AdPlacement.EntropyBoost));
        }

        [Test]
        public void ApplyDailyReset_AfterMidnight_ZeroesCounts()
        {
            var save = NewSave();
            var day1 = new DateTime(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc);
            AdRewardCalculator.ApplyDailyReset(save, day1);
            AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropyBoost);
            AdRewardCalculator.IncrementWatch(save, AdPlacement.Crystals);

            // Next UTC day — counts clear.
            var day2 = new DateTime(2026, 6, 24, 0, 0, 1, DateTimeKind.Utc);
            AdRewardCalculator.ApplyDailyReset(save, day2);

            Assert.AreEqual(0, AdRewardCalculator.GetWatchCount(save, AdPlacement.EntropyBoost));
            Assert.AreEqual(0, AdRewardCalculator.GetWatchCount(save, AdPlacement.Crystals));
        }
    }
}
