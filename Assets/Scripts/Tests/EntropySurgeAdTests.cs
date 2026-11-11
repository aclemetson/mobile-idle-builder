using System;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Entropy Surge: a rewarded ad worth N minutes of current automation income, on a cooldown, for the
    /// post-prestige lull (docs/balance/prestige-pacing.xlsx, Ads + Tests sheets). Pure calculator rules;
    /// AdService only wires them to the provider and the live income tracker.
    /// </summary>
    [TestFixture]
    public class EntropySurgeAdTests
    {
        static readonly DateTime Noon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        static AdPlacementDef Def => AdRewardCalculator.Def(AdPlacement.EntropySurge);

        [Test]
        public void Placement_IsTenMinutesOfIncome_EveryFifteenMinutes_EightPerDay()
        {
            Assert.AreEqual(AdRewardKind.IncomeMinutes, Def.Kind);
            Assert.AreEqual(10f, Def.IncomeMinutes);
            Assert.AreEqual(15f, Def.CooldownMinutes);
            Assert.AreEqual(8, Def.DailyCap);
        }

        // ── Reward ────────────────────────────────────────────────────────────

        [Test]
        public void Reward_IsIncomeTimesMinutes()
        {
            // 100 e/s x 60 s x 10 min = 60,000 e (the Tests sheet's first case).
            Assert.AreEqual(60_000, AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, 100d));
        }

        [Test]
        public void Reward_NoIncome_PaysTheFloor()
        {
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, 0d));
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, double.NaN));
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, -5d));
        }

        [Test]
        public void Reward_DoesNotGrowWithNetWorth()
        {
            // Unlike Entropy Boost (a % of net worth, which counts everything earned this run), the same
            // income pays the same reward however long the run has been.
            long early = AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, 250d);
            long late  = AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, 250d);
            Assert.AreEqual(early, late);
            Assert.AreEqual(150_000, early);
        }

        // ── Cooldown ──────────────────────────────────────────────────────────

        [Test]
        public void NeverWatched_IsReady()
        {
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(new SaveData(), AdPlacement.EntropySurge, Noon));
        }

        [Test]
        public void InsideCooldown_NotReady_UntilFifteenMinutes()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon);

            Assert.IsTrue(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropySurge, Noon.AddMinutes(14)));
            Assert.AreEqual(TimeSpan.FromMinutes(1),
                AdRewardCalculator.CooldownRemaining(save, AdPlacement.EntropySurge, Noon.AddMinutes(14)));
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropySurge, Noon.AddMinutes(15)));
        }

        [Test]
        public void ReturningAfterBeingAway_IsReady()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon);
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropySurge, Noon.AddHours(3)));
        }

        [Test]
        public void ClockMovedBack_LocksForAtMostOneCooldown()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon.AddDays(2));
            Assert.AreEqual(TimeSpan.FromMinutes(15),
                AdRewardCalculator.CooldownRemaining(save, AdPlacement.EntropySurge, Noon));
        }

        [Test]
        public void PlacementsWithoutCooldown_AreNeverOnCooldown()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropyBoost, Noon);
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropyBoost, Noon));
        }

        [Test]
        public void RecordWatch_UpdatesTheExistingEntry()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon);
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon.AddMinutes(20));
            Assert.AreEqual(1, save.adLastWatch.Count);
            Assert.IsTrue(AdRewardCalculator.IsOnCooldown(save, AdPlacement.EntropySurge, Noon.AddMinutes(30)));
        }

        // ── Daily cap ─────────────────────────────────────────────────────────

        [Test]
        public void Cap_StopsWatchesUntilTheDailyReset()
        {
            var save = new SaveData();
            AdRewardCalculator.ApplyDailyReset(save, Noon);
            for (int i = 0; i < Def.DailyCap; i++) AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropySurge);
            Assert.IsTrue(AdRewardCalculator.IsAtCap(save, AdPlacement.EntropySurge));

            AdRewardCalculator.ApplyDailyReset(save, Noon.AddDays(1));
            Assert.IsFalse(AdRewardCalculator.IsAtCap(save, AdPlacement.EntropySurge));
        }

        // ── Persistence ───────────────────────────────────────────────────────

        [Test]
        public void Cooldown_SurvivesASaveRoundTrip()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.EntropySurge, Noon);

            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));

            Assert.AreEqual(TimeSpan.FromMinutes(5),
                AdRewardCalculator.CooldownRemaining(loaded, AdPlacement.EntropySurge, Noon.AddMinutes(10)));
        }

        [Test]
        public void LegacySave_WithoutTheField_IsReady()
        {
            var loaded = JsonUtility.FromJson<SaveData>("{\"playerId\":\"legacy\"}");
            Assert.IsNotNull(loaded.adLastWatch);
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(loaded, AdPlacement.EntropySurge, Noon));
        }

        [Test]
        public void FormatCooldown_IsMinutesAndSeconds()
        {
            Assert.AreEqual("12:05", AdService.FormatCooldown(TimeSpan.FromSeconds(725)));
            Assert.AreEqual("0:00", AdService.FormatCooldown(TimeSpan.FromSeconds(-3)));
        }
    }
}
