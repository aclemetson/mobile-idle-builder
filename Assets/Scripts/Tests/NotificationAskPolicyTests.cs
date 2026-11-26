using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>When the notification opt-in card shows: first startup, every 30 days after an answer, never after "Don't ask again".</summary>
    [TestFixture]
    public class NotificationAskPolicyTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        static SettingsData Fresh() => new SettingsData();

        [Test]
        public void NeverAsked_Asks()
        {
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(Fresh(), NotificationPermission.NotDetermined, Now));
        }

        [Test]
        public void NullSettings_DoesNotAsk()
        {
            Assert.IsFalse(NotificationAskPolicy.ShouldAsk(null, NotificationPermission.NotDetermined, Now));
        }

        [Test]
        public void Answered_WaitsThirtyDays()
        {
            var s = Fresh();
            NotificationAskPolicy.RecordAsked(s, Now, neverAskAgain: false);

            Assert.IsTrue(s.notificationPromptShown);
            Assert.AreEqual(Now.AddDays(30), NotificationAskPolicy.NextAskUtc(s));
            Assert.IsFalse(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.Denied, Now.AddDays(29)));
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.Denied, Now.AddDays(30)));
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.NotDetermined, Now.AddDays(45)));
        }

        [Test]
        public void DontAskAgain_NeverAsks()
        {
            var s = Fresh();
            NotificationAskPolicy.RecordAsked(s, Now, neverAskAgain: true);
            Assert.IsTrue(s.notificationNeverAsk);
            Assert.IsFalse(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.Denied, Now.AddDays(365)));
        }

        [Test]
        public void LaterAnswer_DoesNotClearDontAskAgain()
        {
            var s = Fresh();
            NotificationAskPolicy.RecordAsked(s, Now, neverAskAgain: true);
            NotificationAskPolicy.RecordAsked(s, Now.AddDays(1), neverAskAgain: false);
            Assert.IsTrue(s.notificationNeverAsk);
        }

        [Test]
        public void AlreadyOn_DoesNotAsk()
        {
            var s = Fresh();   // toggle defaults on
            Assert.IsFalse(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.Granted, Now));
        }

        [Test]
        public void Granted_ButToggleOff_AsksWhenDue()
        {
            var s = Fresh();
            s.notificationsEnabled = false;
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.Granted, Now));
        }

        [Test]
        public void UnreadableDate_AsksAgain()
        {
            var s = Fresh();
            s.notificationPromptShown = true;
            s.notificationNextAskUtc = "not a date";
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.NotDetermined, Now));
        }

        [Test]
        public void Reset_ClearsEveryAnswer()
        {
            var s = Fresh();
            NotificationAskPolicy.RecordAsked(s, Now, neverAskAgain: true);
            NotificationAskPolicy.Reset(s);
            Assert.IsFalse(s.notificationPromptShown);
            Assert.IsFalse(s.notificationNeverAsk);
            Assert.AreEqual("", s.notificationNextAskUtc);
            Assert.IsTrue(NotificationAskPolicy.ShouldAsk(s, NotificationPermission.NotDetermined, Now));
        }

        // ── On-time reminders (Android exact alarms) ────────────────────────

        [Test]
        public void Exact_AskedOnceWhenNotificationsOnAndGrantMissing()
        {
            var s = Fresh();   // toggle defaults to on
            Assert.IsTrue(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.Granted, exactNeedsGrant: true));
            s.exactAlarmAsked = true;
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.Granted, exactNeedsGrant: true));
        }

        [Test]
        public void Exact_NotAskedWhenAlreadyExactOrNotificationsOff()
        {
            var s = Fresh();
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.Granted, exactNeedsGrant: false));
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.NotDetermined, exactNeedsGrant: true));
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.Denied, exactNeedsGrant: true));
            s.notificationsEnabled = false;
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(s, NotificationPermission.Granted, exactNeedsGrant: true));
            Assert.IsFalse(NotificationAskPolicy.ShouldAskExact(null, NotificationPermission.Granted, exactNeedsGrant: true));
        }

        [Test]
        public void Reset_ClearsExactAnswer()
        {
            var s = Fresh();
            s.exactAlarmAsked = true;
            NotificationAskPolicy.Reset(s);
            Assert.IsFalse(s.exactAlarmAsked);
        }
    }
}
