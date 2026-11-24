using System;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Entropy Surge (#489): one rewarded ad every 15 minutes, no daily cap, that rolls 10% of net worth in
    /// entropy (80%) or 10 crystals (20%). Pure calculator rules; AdService wires them to the provider.
    /// </summary>
    [TestFixture]
    public class EntropySurgeAdTests
    {
        static readonly DateTime Noon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        static AdPlacementDef Def => AdRewardCalculator.Def(AdPlacement.EntropySurge);

        [Test]
        public void Placement_IsARoll_EveryFifteenMinutes_Uncapped()
        {
            Assert.AreEqual(AdRewardKind.SurgeRoll, Def.Kind);
            Assert.AreEqual(0.10f, Def.NetWorthFraction);
            Assert.AreEqual(10, Def.CrystalAmount);
            Assert.AreEqual(0.20f, Def.CrystalChance, 1e-6);
            Assert.AreEqual(15f, Def.CooldownMinutes);
            Assert.AreEqual(AdRewardCalculator.Uncapped, Def.DailyCap);
        }

        // ── Reward ────────────────────────────────────────────────────────────

        [Test]
        public void Entropy_IsTenPercentOfNetWorth()
        {
            Assert.AreEqual(10_000, AdRewardCalculator.CalcEntropyReward(100_000d));
            Assert.AreEqual(1_000_000_000_000L, AdRewardCalculator.CalcEntropyReward(1e13));
        }

        [Test]
        public void Entropy_NoNetWorth_PaysTheFloor()
        {
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcEntropyReward(0d));
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcEntropyReward(double.NaN));
            Assert.AreEqual(Def.MinimumFloor, AdRewardCalculator.CalcEntropyReward(-5d));
        }

        [Test]
        public void Entropy_HugeNetWorth_DoesNotOverflow() =>
            Assert.AreEqual(long.MaxValue, AdRewardCalculator.CalcEntropyReward(1e300));

        [TestCase(0.0, true)]
        [TestCase(0.1999, true)]
        [TestCase(0.2, false)]
        [TestCase(0.9999, false)]
        public void Roll_BelowTwentyPercent_PaysCrystals(double roll, bool crystals) =>
            Assert.AreEqual(crystals, AdRewardCalculator.SurgeRollsCrystals(roll));

        [Test]
        public void Roll_OverManyWatches_IsAboutOneInFiveCrystals()
        {
            var rng = new System.Random(489);
            int crystals = 0;
            const int n = 20_000;
            for (int k = 0; k < n; k++) if (AdRewardCalculator.SurgeRollsCrystals(rng.NextDouble())) crystals++;
            Assert.AreEqual(0.20, crystals / (double)n, 0.01);
        }

        [Test]
        public void NoDailyCap_ManyWatchesNeverReachIt()
        {
            var save = new SaveData();
            AdRewardCalculator.ApplyDailyReset(save, Noon);
            for (int k = 0; k < 200; k++) AdRewardCalculator.IncrementWatch(save, AdPlacement.EntropySurge);
            Assert.IsFalse(AdRewardCalculator.IsAtCap(save, AdPlacement.EntropySurge));
            Assert.Greater(AdRewardCalculator.Remaining(save, AdPlacement.EntropySurge), 0);
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
        public void EveryOtherPlacement_HasTheStandardFiveMinuteCooldown()
        {
            foreach (var def in AdRewardCalculator.Placements)
            {
                if (def.Placement == AdPlacement.EntropySurge) continue;
                Assert.AreEqual(AdRewardCalculator.StandardCooldownMinutes, def.CooldownMinutes, def.Name);
            }
            Assert.AreEqual(5f, AdRewardCalculator.StandardCooldownMinutes);
        }

        [Test]
        public void StandardPlacement_IsReadyAgainFiveMinutesAfterAWatch()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.SpeedBoost, Noon);
            Assert.IsTrue(AdRewardCalculator.IsOnCooldown(save, AdPlacement.SpeedBoost, Noon.AddMinutes(4)));
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.SpeedBoost, Noon.AddMinutes(5)));
        }

        [Test]
        public void Cooldowns_ArePerPlacement()
        {
            var save = new SaveData();
            AdRewardCalculator.RecordWatch(save, AdPlacement.SpeedBoost, Noon);
            Assert.IsFalse(AdRewardCalculator.IsOnCooldown(save, AdPlacement.TimeWarp, Noon));
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
