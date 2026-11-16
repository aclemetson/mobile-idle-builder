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
    }
}
