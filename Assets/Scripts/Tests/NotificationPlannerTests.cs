using System;
using System.Linq;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Local notification planning: which timers become notifications, quiet hours, merging and the daily cap.</summary>
    [TestFixture]
    public class NotificationPlannerTests
    {
        // Noon UTC, device on UTC: outside quiet hours unless a test moves it.
        static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        static NotificationInputs Inputs() => new NotificationInputs { NowUtc = Now, LocalOffset = TimeSpan.Zero };

        static SaveData TutorialDoneSave()
        {
            var save = new SaveData();
            save.tutorial.isActive = false;
            save.tutorial.hasCompletedFirstRun = true;
            return save;
        }

        static NotificationInputs FromSave(SaveData save, float idleCap = 0f, bool daily = false,
                                           Func<AchievementCategory, bool> claimable = null) =>
            NotificationInputs.FromSave(save, Now, Now, TimeSpan.Zero, idleCap, daily, claimable);

        // ── Planner rules ───────────────────────────────────────────────────

        [Test]
        public void NothingPending_NoNotifications()
        {
            Assert.IsEmpty(NotificationPlanner.Plan(Inputs()));
            Assert.IsEmpty(NotificationPlanner.Plan(null));
        }

        [Test]
        public void Research_ScheduledAtItsFinishTime()
        {
            var i = Inputs();
            i.ResearchDoneIn = TimeSpan.FromMinutes(45);
            var plan = NotificationPlanner.Plan(i);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(NotificationKind.ResearchDone, plan[0].Kind);
            Assert.AreEqual(Now.AddMinutes(45), plan[0].FireUtc);
            Assert.AreEqual(NotificationChannel.Production, plan[0].Channel);
            Assert.IsFalse(string.IsNullOrEmpty(plan[0].Title));
        }

        [Test]
        public void TooSoon_IsSkipped()
        {
            var i = Inputs();
            i.ResearchDoneIn = TimeSpan.FromSeconds(30);
            Assert.IsEmpty(NotificationPlanner.Plan(i));
        }

        [Test]
        public void QuietHours_EveningMovesToNextMorning()
        {
            // 23:30 local -> 08:00 local next day.
            var fire = NotificationPlanner.ApplyQuietHours(new DateTime(2026, 11, 20, 23, 30, 0, DateTimeKind.Utc), TimeSpan.Zero);
            Assert.AreEqual(new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc), fire);
        }

        [Test]
        public void QuietHours_EarlyMorningMovesToSameMorning()
        {
            var fire = NotificationPlanner.ApplyQuietHours(new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);
            Assert.AreEqual(new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc), fire);
        }

        [Test]
        public void QuietHours_UseTheDeviceOffset()
        {
            // 03:00 UTC is 22:00 in UTC-5: quiet, moves to 08:00 local = 13:00 UTC.
            var offset = TimeSpan.FromHours(-5);
            var fire = NotificationPlanner.ApplyQuietHours(new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc), offset);
            Assert.AreEqual(new DateTime(2026, 11, 21, 13, 0, 0, DateTimeKind.Utc), fire);
        }

        [Test]
        public void Daytime_IsUnchanged()
        {
            var t = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(t, NotificationPlanner.ApplyQuietHours(t, TimeSpan.Zero));
        }

        [Test]
        public void CloseProductionAlerts_MergeIntoOne()
        {
            var i = Inputs();
            i.ResearchDoneIn = TimeSpan.FromMinutes(30);
            i.CraftDoneIn    = TimeSpan.FromMinutes(40);
            var plan = NotificationPlanner.Plan(i);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(NotificationKind.ProductionCombined, plan[0].Kind);
            Assert.AreEqual(Now.AddMinutes(40), plan[0].FireUtc, "fires when the last of the group is done");
        }

        [Test]
        public void FarApartProductionAlerts_StaySeparate()
        {
            var i = Inputs();
            i.ResearchDoneIn = TimeSpan.FromMinutes(30);
            i.CraftDoneIn    = TimeSpan.FromMinutes(120);
            var plan = NotificationPlanner.Plan(i);
            CollectionAssert.AreEqual(new[] { NotificationKind.ResearchDone, NotificationKind.CraftDone },
                                      plan.Select(p => p.Kind).ToArray());
        }

        [Test]
        public void AtMostThreeInAnyDay_HighestPriorityKept()
        {
            var i = Inputs();
            i.StorageFullIn     = TimeSpan.FromHours(2);
            i.ResearchDoneIn    = TimeSpan.FromHours(1);
            i.CyclotronReadyIn  = TimeSpan.FromHours(3);
            i.BoostEndedIn      = TimeSpan.FromHours(4);
            i.DailyRewardIn     = TimeSpan.FromHours(5);
            i.RewardsExpiringIn = TimeSpan.FromHours(6);
            var plan = NotificationPlanner.Plan(i);
            Assert.AreEqual(3, plan.Count);
            CollectionAssert.AreEquivalent(
                new[] { NotificationKind.StorageFull, NotificationKind.ResearchDone, NotificationKind.CyclotronReady },
                plan.Select(p => p.Kind).ToArray());
        }

        [Test]
        public void Comeback_OnlyWhenIncluded_AndSorted()
        {
            var i = Inputs();
            Assert.IsEmpty(NotificationPlanner.Plan(i));
            i.IncludeComeback = true;
            var plan = NotificationPlanner.Plan(i);
            CollectionAssert.AreEqual(new[] { NotificationKind.Comeback24h, NotificationKind.Comeback72h },
                                      plan.Select(p => p.Kind).ToArray());
            Assert.AreEqual(NotificationChannel.Reminders, plan[0].Channel);
        }

        [Test]
        public void Ids_AreStablePerKind()
        {
            var a = new PlannedNotification(NotificationKind.CraftDone, NotificationChannel.Production, "", "", Now);
            var b = new PlannedNotification(NotificationKind.CraftDone, NotificationChannel.Production, "x", "y", Now.AddHours(1));
            Assert.AreEqual(a.Id, b.Id);
            Assert.AreNotEqual(a.Id, new PlannedNotification(NotificationKind.ResearchDone, NotificationChannel.Production, "", "", Now).Id);
        }

        [Test]
        public void MaxLateness_PreciseForEvents_RelaxedForComebacks()
        {
            foreach (NotificationKind k in Enum.GetValues(typeof(NotificationKind)))
            {
                var expected = k == NotificationKind.Comeback24h || k == NotificationKind.Comeback72h
                    ? NotificationPlanner.RelaxedWindow
                    : NotificationPlanner.PreciseWindow;
                Assert.AreEqual(expected, NotificationPlanner.MaxLatenessFor(k), k.ToString());
            }
            Assert.AreEqual(TimeSpan.FromMinutes(10), NotificationPlanner.PreciseWindow,
                "10 min is the tightest window Android 12+ grants without an exact-alarm permission");
        }

        [Test]
        public void PlannedNotification_CarriesItsKindsLateness()
        {
            var i = Inputs();
            i.ResearchDoneIn  = TimeSpan.FromMinutes(45);
            i.IncludeComeback = true;
            var plan = NotificationPlanner.Plan(i);
            Assert.AreEqual(NotificationPlanner.PreciseWindow,
                plan.First(p => p.Kind == NotificationKind.ResearchDone).MaxLateness);
            Assert.AreEqual(NotificationPlanner.RelaxedWindow,
                plan.First(p => p.Kind == NotificationKind.Comeback24h).MaxLateness);
        }

        // ── Inputs from the save ───────────────────────────────────────────

        [Test]
        public void FromSave_NullSave_IsEmpty()
        {
            var i = NotificationInputs.FromSave(null, Now, Now, TimeSpan.Zero, 1800, true, _ => true);
            Assert.IsEmpty(NotificationPlanner.Plan(i));
        }

        [Test]
        public void FromSave_DuringTutorial_NoStorageNoComebackNoDaily()
        {
            var save = new SaveData();   // tutorial active by default
            var i = FromSave(save, idleCap: 1800, daily: true);
            Assert.IsNull(i.StorageFullIn);
            Assert.IsNull(i.DailyRewardIn);
            Assert.IsFalse(i.IncludeComeback);
        }

        [Test]
        public void FromSave_StorageFull_AtTheIdleCap()
        {
            var i = FromSave(TutorialDoneSave(), idleCap: 1800);
            Assert.AreEqual(TimeSpan.FromSeconds(1800), i.StorageFullIn);
            Assert.IsTrue(i.IncludeComeback);
        }

        [Test]
        public void FromSave_Research_UsesTheSavedFinishTime()
        {
            var save = TutorialDoneSave();
            save.activeResearchCompleteUtc = Now.AddMinutes(20).ToString("O");
            Assert.AreEqual(TimeSpan.FromMinutes(20), FromSave(save).ResearchDoneIn);

            save.activeResearchCompleteUtc = Now.AddMinutes(-5).ToString("O");
            Assert.IsNull(FromSave(save).ResearchDoneIn, "already finished: nothing to wait for");
        }

        [Test]
        public void FromSave_Cyclotron_OnlyOnceUnlocked()
        {
            var save = TutorialDoneSave();
            save.cyclotronLastRunUtc = Now.AddMinutes(-10).ToString("O");
            save.prestigeCount = 0;
            Assert.IsNull(FromSave(save).CyclotronReadyIn);

            save.prestigeCount = 10;
            var left = FromSave(save).CyclotronReadyIn;
            Assert.IsNotNull(left);
            Assert.AreEqual(CyclotronProgress.CooldownLeft(save, Now), left.Value);
        }

        [Test]
        public void FromSave_BoostEnded_WhenABoostRuns()
        {
            var save = TutorialDoneSave();
            Assert.IsNull(FromSave(save).BoostEndedIn);
            save.speedBoostExpiryUtc = Now.AddMinutes(25).ToString("O");
            Assert.AreEqual(TimeSpan.FromMinutes(25), FromSave(save).BoostEndedIn);
        }

        [Test]
        public void FromSave_RewardsExpiring_OnlyWithClaimableRewards()
        {
            var save = TutorialDoneSave();
            save.dailyResetUtc = Now.AddHours(12).ToString("O");
            Assert.IsNull(FromSave(save, claimable: _ => false).RewardsExpiringIn);
            Assert.AreEqual(TimeSpan.FromHours(10),
                FromSave(save, claimable: c => c == AchievementCategory.Daily).RewardsExpiringIn,
                "warns two hours before the reset");
        }

        [Test]
        public void FromSave_RewardsExpiring_SkippedWhenResetIsTooClose()
        {
            var save = TutorialDoneSave();
            save.dailyResetUtc = Now.AddHours(1).ToString("O");
            Assert.IsNull(FromSave(save, claimable: _ => true).RewardsExpiringIn);
        }

        [Test]
        public void DailyReward_ClaimedToday_RemindsAtNextUtcMidnight()
        {
            var delay = NotificationInputs.DailyRewardDelay(Now.Date.ToString("O"), Now);
            Assert.AreEqual(TimeSpan.FromHours(12), delay);
        }

        [Test]
        public void DailyReward_Unclaimed_RemindsInAFewHours()
        {
            var delay = NotificationInputs.DailyRewardDelay(Now.Date.AddDays(-1).ToString("O"), Now);
            Assert.AreEqual(NotificationPlanner.UnclaimedDailyDelay, delay);
        }

        [Test]
        public void DailyReward_UnclaimedLateInDay_WaitsForTheReset()
        {
            var late = Now.Date.AddHours(22);
            var delay = NotificationInputs.DailyRewardDelay(null, late);
            Assert.AreEqual(TimeSpan.FromHours(2), delay);
        }

        // ── Entropy Surge ───────────────────────────────────────────────────

        static NotificationInputs SurgeInputs(SaveData save, bool adsOn = true) =>
            NotificationInputs.FromSave(save, Now, Now, TimeSpan.Zero, 0f, false, null, surgeOfferEnabled: adsOn);

        [Test]
        public void Surge_Ready_WaitsTheMinimumAway()
        {
            var i = SurgeInputs(TutorialDoneSave());
            Assert.AreEqual(NotificationPlanner.SurgeMinAway, i.SurgeReadyIn);
            var surge = NotificationPlanner.Plan(i).Single(p => p.Kind == NotificationKind.SurgeReady);
            Assert.AreEqual(NotificationChannel.Boosts, surge.Channel);
            Assert.AreEqual(Now + NotificationPlanner.SurgeMinAway, surge.FireUtc);
        }

        [Test]
        public void Surge_JustWatched_ShortCooldownStillUsesTheMinimum()
        {
            var save = TutorialDoneSave();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Now);   // 15 min cooldown
            Assert.AreEqual(NotificationPlanner.SurgeMinAway, SurgeInputs(save).SurgeReadyIn);
        }

        [Test]
        public void Surge_LongBoostRunning_WaitsForTheLock()
        {
            var save = TutorialDoneSave();
            save.speedBoostExpiryUtc = Now.AddHours(3).ToString("O");
            Assert.AreEqual(TimeSpan.FromHours(3), SurgeInputs(save).SurgeReadyIn);
        }

        [Test]
        public void Surge_BeforeFirstPrestigeOrAdsOff_NotPlanned()
        {
            var early = TutorialDoneSave();
            early.tutorial.hasCompletedFirstRun = false;
            Assert.IsNull(SurgeInputs(early).SurgeReadyIn);
            Assert.IsNull(SurgeInputs(TutorialDoneSave(), adsOn: false).SurgeReadyIn);
            Assert.IsNull(FromSave(TutorialDoneSave()).SurgeReadyIn);   // default: off
        }

        [Test]
        public void Surge_GivesWayFirstUnderTheDailyCap()
        {
            var i = Inputs();
            i.ResearchDoneIn  = TimeSpan.FromHours(1);
            i.CraftDoneIn     = TimeSpan.FromHours(3);
            i.BoostEndedIn    = TimeSpan.FromHours(5);
            i.SurgeReadyIn    = TimeSpan.FromHours(2);
            var kinds = NotificationPlanner.Plan(i).Select(p => p.Kind).ToList();
            Assert.AreEqual(3, kinds.Count);
            CollectionAssert.DoesNotContain(kinds, NotificationKind.SurgeReady);
        }

        [Test]
        public void SurgeReady_AppendedAfterExistingKinds()
        {
            // Ids are 1000 + enum value: earlier kinds must keep theirs.
            Assert.AreEqual(1010, new PlannedNotification(NotificationKind.DevTest, default, "", "", Now).Id);
            Assert.AreEqual(1011, new PlannedNotification(NotificationKind.SurgeReady, default, "", "", Now).Id);
        }
    }
}
