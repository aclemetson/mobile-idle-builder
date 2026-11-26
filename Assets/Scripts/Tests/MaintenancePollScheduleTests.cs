using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>When the mid-session maintenance watcher re-fetches Remote Config (#224).</summary>
    [TestFixture]
    public class MaintenancePollScheduleTests
    {
        const float Poll = MaintenancePollSchedule.PollSeconds;
        const float Gap  = MaintenancePollSchedule.MinGapSeconds;

        [Test]
        public void BeforeThePollInterval_NotDue()
            => Assert.IsFalse(MaintenancePollSchedule.IsDue(100f + Poll - 1f, 100f, false));

        [Test]
        public void AtThePollInterval_Due()
            => Assert.IsTrue(MaintenancePollSchedule.IsDue(100f + Poll, 100f, false));

        [Test]
        public void Resume_ChecksImmediately_WithoutWaitingOutTheTimer()
            => Assert.IsTrue(MaintenancePollSchedule.IsDue(100f + 60f, 100f, true),
                             "A resumed app must not wait for the rest of the 5-minute timer");

        [Test]
        public void Resume_RightAfterACheck_Waits_SoPauseBouncesDontSpamFetches()
        {
            Assert.IsFalse(MaintenancePollSchedule.IsDue(100f + Gap - 0.5f, 100f, true));
            Assert.IsTrue(MaintenancePollSchedule.IsDue(100f + Gap, 100f, true));
        }

        // -- Remote poll interval (maintenance.pollSeconds) --

        [Test]
        public void PollSeconds_UnsetOrInvalid_UsesTheDefault()
        {
            Assert.AreEqual(Poll, MaintenancePollSchedule.ClampPollSeconds(0));
            Assert.AreEqual(Poll, MaintenancePollSchedule.ClampPollSeconds(-5));
        }

        [Test]
        public void PollSeconds_IsClampedToTheSafeRange()
        {
            Assert.AreEqual(MaintenancePollSchedule.MinPollSeconds, MaintenancePollSchedule.ClampPollSeconds(5));
            Assert.AreEqual(MaintenancePollSchedule.MaxPollSeconds, MaintenancePollSchedule.ClampPollSeconds(99999));
            Assert.AreEqual(120f, MaintenancePollSchedule.ClampPollSeconds(120));
        }

        [Test]
        public void ShorterPollInterval_MakesTheCheckDueSooner()
        {
            Assert.IsTrue(MaintenancePollSchedule.IsDue(100f + 120f, 100f, false, 120f));
            Assert.IsFalse(MaintenancePollSchedule.IsDue(100f + 119f, 100f, false, 120f));
        }

        // -- Scheduled start (maintenance.startsUtc) --

        static readonly System.DateTime Now = new System.DateTime(2026, 11, 26, 12, 0, 0, System.DateTimeKind.Utc);

        [Test]
        public void Disabled_IsNeverActive_WhateverTheSchedule()
        {
            Assert.IsFalse(MaintenancePollSchedule.IsActive(false, "", Now));
            Assert.IsFalse(MaintenancePollSchedule.IsActive(false, "2026-11-26T11:00:00Z", Now));
            Assert.IsFalse(MaintenancePollSchedule.IsScheduledAhead(false, "2026-11-26T13:00:00Z", Now));
        }

        [Test]
        public void Enabled_WithoutAStart_IsActiveAtOnce()
        {
            Assert.IsTrue(MaintenancePollSchedule.IsActive(true, "", Now));
            Assert.IsTrue(MaintenancePollSchedule.IsActive(true, "not a date", Now),
                          "An unreadable start must not silently cancel maintenance");
            Assert.IsFalse(MaintenancePollSchedule.IsScheduledAhead(true, "", Now));
        }

        [Test]
        public void Enabled_WithAFutureStart_IsScheduledNotActive()
        {
            Assert.IsFalse(MaintenancePollSchedule.IsActive(true, "2026-11-26T12:05:00Z", Now));
            Assert.IsTrue(MaintenancePollSchedule.IsScheduledAhead(true, "2026-11-26T12:05:00Z", Now));
        }

        [Test]
        public void Enabled_AtOrAfterTheStart_IsActive()
        {
            Assert.IsTrue(MaintenancePollSchedule.IsActive(true, "2026-11-26T12:00:00Z", Now));
            Assert.IsTrue(MaintenancePollSchedule.IsActive(true, "2026-11-26T11:59:00Z", Now));
            Assert.IsFalse(MaintenancePollSchedule.IsScheduledAhead(true, "2026-11-26T12:00:00Z", Now));
        }

        [Test]
        public void Start_WithAnOffset_IsComparedInUtc()
            => Assert.IsTrue(MaintenancePollSchedule.IsScheduledAhead(true, "2026-11-26T07:30:00-05:00", Now),
                             "07:30-05:00 is 12:30 UTC, still ahead of 12:00 UTC");
    }
}
