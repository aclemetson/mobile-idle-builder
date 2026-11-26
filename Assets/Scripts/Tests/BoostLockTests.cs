using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// One boost at a time (BoostLock), the factory clock rate, and idle payouts that only boost the part
    /// of the time away a timed boost actually covered.
    /// </summary>
    [TestFixture]
    public class BoostLockTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

        static string In(double minutes) => Now.AddMinutes(minutes).ToString("O");

        // ── Which boost is running ──────────────────────────────────────────

        [Test]
        public void NoBoosts_NotLocked()
        {
            var save = new SaveData();
            Assert.AreEqual(TimedBoost.None, BoostLock.Active(save, Now));
            Assert.IsFalse(BoostLock.IsLocked(save, Now));
            Assert.IsNull(BoostLock.Message(save, Now));
            Assert.AreEqual(TimeSpan.Zero, BoostLock.Remaining(save, Now));
        }

        [Test]
        public void NullSave_NotLocked() => Assert.IsFalse(BoostLock.IsLocked(null, Now));

        [Test]
        public void ExpiredBoost_NotLocked()
        {
            var save = new SaveData { speedBoostExpiryUtc = In(-1) };
            Assert.IsFalse(BoostLock.IsLocked(save, Now));
        }

        [Test]
        public void GarbageExpiry_NotLocked()
        {
            var save = new SaveData { speedBoostExpiryUtc = "not a date" };
            Assert.IsFalse(BoostLock.IsLocked(save, Now));
        }

        [Test]
        public void SpeedBurst_Locks()
        {
            var save = new SaveData { speedBoostExpiryUtc = In(30) };
            Assert.AreEqual(TimedBoost.SpeedBurst, BoostLock.Active(save, Now));
            Assert.AreEqual(TimeSpan.FromMinutes(30), BoostLock.Remaining(save, Now));
            StringAssert.Contains("Speed burst", BoostLock.Message(save, Now));
            StringAssert.Contains("30:00", BoostLock.Message(save, Now));
        }

        [Test]
        public void IdleRateAdBoost_Locks()
        {
            var save = new SaveData { adsIdleBoostExpiryUtc = In(240) };
            Assert.AreEqual(TimedBoost.IdleRate, BoostLock.Active(save, Now));
        }

        [Test]
        public void CyclotronBoost_Locks_OnlyWithAMultiplier()
        {
            var save = new SaveData { cyclotronBoostExpiryUtc = In(5), cyclotronBoostMultiplier = 2f };
            Assert.AreEqual(TimedBoost.CyclotronKick, BoostLock.Active(save, Now));

            save.cyclotronBoostMultiplier = 1f; // a run that paid no boost
            Assert.IsFalse(BoostLock.IsLocked(save, Now));
        }

        [Test]
        public void TwoBoosts_LockLastsUntilTheLaterOneEnds()
        {
            // Older saves may hold more than one; the lock lifts only when all have ended.
            var save = new SaveData { speedBoostExpiryUtc = In(10), adsIdleBoostExpiryUtc = In(90) };
            Assert.AreEqual(TimedBoost.IdleRate, BoostLock.Active(save, Now));
            Assert.AreEqual(TimeSpan.FromMinutes(90), BoostLock.Remaining(save, Now));
        }

        // ── What the lock covers ────────────────────────────────────────────

        [Test]
        public void EveryAdExceptDoubleOffline_IsLocked()
        {
            foreach (AdPlacement p in Enum.GetValues(typeof(AdPlacement)))
                Assert.AreEqual(p != AdPlacement.DoubleOffline, BoostLock.LocksAd(p), p.ToString());
        }

        [Test]
        public void BlocksAd_OnlyWhileABoostRuns()
        {
            var save = new SaveData();
            Assert.IsFalse(BoostLock.BlocksAd(save, AdPlacement.EntropySurge, Now));
            save.speedBoostExpiryUtc = In(30);
            Assert.IsTrue(BoostLock.BlocksAd(save, AdPlacement.EntropySurge, Now));
            Assert.IsFalse(BoostLock.BlocksAd(save, AdPlacement.DoubleOffline, Now));
        }

        [TestCase(0,    "0:00")]
        [TestCase(75,   "1:15")]
        [TestCase(3725, "1:02:05")]
        public void FormatLeft(int seconds, string expected) =>
            Assert.AreEqual(expected, BoostLock.FormatLeft(TimeSpan.FromSeconds(seconds)));

        // ── Factory clock ───────────────────────────────────────────────────

        [Test]
        public void FactoryClock_SanitizesNonPositiveToOne()
        {
            Assert.AreEqual(1f, FactoryClock.Sanitize(0f));
            Assert.AreEqual(1f, FactoryClock.Sanitize(-3f));
            Assert.AreEqual(2f, FactoryClock.Sanitize(2f));
        }

        // ── Idle: boosts count only for the time they covered ───────────────

        [Test]
        public void Idle_NoBoosts_IsJustTheTimeAway() =>
            Assert.AreEqual(3600d, OfflineCollectionService.BoostWeightedSeconds(3600, 0, 2f, 0, 1.5f), 1e-9);

        [Test]
        public void Idle_ShortSpeedBurst_DoublesOnlyItsWindow()
        {
            // 30 min burst, 8 h away: 30 min counts twice, the rest once. Before the fix all 8 h counted twice.
            double eightHours = 8 * 3600d, burst = 30 * 60d;
            Assert.AreEqual(eightHours + burst,
                OfflineCollectionService.BoostWeightedSeconds(eightHours, burst, 2f, 0, 1.5f), 1e-6);
        }

        [Test]
        public void Idle_BurstLongerThanTheAbsence_DoublesAllOfIt() =>
            Assert.AreEqual(7200d, OfflineCollectionService.BoostWeightedSeconds(3600, 24 * 3600, 2f, 0, 1.5f), 1e-6);

        [Test]
        public void Idle_IdleRateBoost_CountsOnlyWhileItRan()
        {
            // +50% for the first hour of a 4 h absence (it used to be all-or-nothing on the return time).
            Assert.AreEqual(3600 * 1.5 + 3 * 3600,
                OfflineCollectionService.BoostWeightedSeconds(4 * 3600, 0, 2f, 3600, 1.5f), 1e-6);
        }

        [Test]
        public void Idle_OverlappingBoosts_Multiply()
        {
            // speed 2x for 1 h, idle 1.5x for 2 h, away 3 h: 1h*3 + 1h*1.5 + 1h*1.
            Assert.AreEqual(3600 * (3 + 1.5 + 1),
                OfflineCollectionService.BoostWeightedSeconds(3 * 3600, 3600, 2f, 2 * 3600, 1.5f), 1e-6);
        }

        [Test]
        public void Idle_SecondsLeftAt_FromTheDepartureTime()
        {
            Assert.AreEqual(1800d, OfflineCollectionService.SecondsLeftAt(In(30), Now), 1e-6);
            Assert.AreEqual(0d,    OfflineCollectionService.SecondsLeftAt(In(-5), Now));
            Assert.AreEqual(0d,    OfflineCollectionService.SecondsLeftAt(null, Now));
        }
    }
}
