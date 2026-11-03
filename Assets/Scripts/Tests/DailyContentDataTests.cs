using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Validates the daily-event content authored in game_data.json (the single source of truth)
    /// and the DailyContentSO schema it imports into. Parses the raw JSON directly so the test does
    /// not depend on the Editor importer having generated the asset.
    /// </summary>
    [TestFixture]
    public class DailyContentDataTests
    {
        // ── Minimal mirror types for direct JsonUtility parse of game_data.json ──
        [Serializable] private class Root
        {
            public List<Reward>    daily_rewards    = new();
            public List<Challenge> daily_challenges = new();
        }
        [Serializable] private class Reward
        {
            public int day; public int crystals; public long entropy; public int prestige_currency;
        }
        [Serializable] private class Challenge
        {
            public string id; public string description; public string trigger; public int target; public int crystals;
        }

        static Root LoadData()
        {
            string path = Path.Combine(Application.dataPath, "Data/game_data.json");
            Assert.IsTrue(File.Exists(path), $"game_data.json not found at {path}");
            return JsonUtility.FromJson<Root>(File.ReadAllText(path));
        }

        // ── DailyContentSO schema ─────────────────────────────────────────────

        [Test]
        public void DailyContentSO_HasRequiredFields()
        {
            var type = typeof(DailyContentSO);
            Assert.IsNotNull(type.GetField("loginRewards"),  "DailyContentSO missing: loginRewards");
            Assert.IsNotNull(type.GetField("challengePool"), "DailyContentSO missing: challengePool");
        }

        [Test]
        public void DailyRewardEntry_HasRequiredFields()
        {
            var type = typeof(DailyRewardEntry);
            Assert.IsNotNull(type.GetField("day"),              "DailyRewardEntry missing: day");
            Assert.IsNotNull(type.GetField("crystals"),         "DailyRewardEntry missing: crystals");
            Assert.IsNotNull(type.GetField("entropy"),          "DailyRewardEntry missing: entropy");
            Assert.IsNotNull(type.GetField("prestigeCurrency"), "DailyRewardEntry missing: prestigeCurrency");
        }

        [Test]
        public void DailyChallengeEntry_HasRequiredFields()
        {
            var type = typeof(DailyChallengeEntry);
            Assert.IsNotNull(type.GetField("id"),       "DailyChallengeEntry missing: id");
            Assert.IsNotNull(type.GetField("trigger"),  "DailyChallengeEntry missing: trigger");
            Assert.IsNotNull(type.GetField("target"),   "DailyChallengeEntry missing: target");
            Assert.IsNotNull(type.GetField("crystals"), "DailyChallengeEntry missing: crystals");
        }

        // ── Login reward calendar ─────────────────────────────────────────────

        [Test]
        public void LoginCalendar_Has28ConsecutiveDays()
        {
            var data = LoadData();
            Assert.AreEqual(28, data.daily_rewards.Count, "Login calendar must have exactly 28 days");
            data.daily_rewards.Sort((a, b) => a.day.CompareTo(b.day));
            for (int i = 0; i < 28; i++)
                Assert.AreEqual(i + 1, data.daily_rewards[i].day, $"Calendar day at index {i} should be {i + 1}");
        }

        [Test]
        public void LoginCalendar_MilestoneRewardsMatchBalanceTable()
        {
            var byDay = new Dictionary<int, Reward>();
            foreach (var r in LoadData().daily_rewards) byDay[r.day] = r;

            AssertReward(byDay, 7,  40,  500,   0);
            AssertReward(byDay, 14, 60,  2000,  0);
            AssertReward(byDay, 21, 80,  10000, 0);
            AssertReward(byDay, 28, 150, 0,     1);   // the only prestige-currency day
        }

        static void AssertReward(Dictionary<int, Reward> byDay, int day, int crystals, long entropy, int pc)
        {
            Assert.IsTrue(byDay.TryGetValue(day, out var r), $"Day {day} missing from calendar");
            Assert.AreEqual(crystals, r.crystals,         $"Day {day} crystals");
            Assert.AreEqual(entropy,  r.entropy,          $"Day {day} entropy");
            Assert.AreEqual(pc,       r.prestige_currency,$"Day {day} prestige_currency");
        }

        [Test]
        public void LoginCalendar_OnlyDay28GrantsPrestigeCurrency()
        {
            foreach (var r in LoadData().daily_rewards)
                if (r.day != 28)
                    Assert.AreEqual(0, r.prestige_currency, $"Day {r.day} must not grant prestige currency");
        }

        // ── Challenge pool ────────────────────────────────────────────────────

        [Test]
        public void ChallengePool_HasSixValidChallenges()
        {
            var data = LoadData();
            Assert.AreEqual(6, data.daily_challenges.Count, "Challenge pool must have 6 entries");
            foreach (var c in data.daily_challenges)
            {
                Assert.IsFalse(string.IsNullOrEmpty(c.id), "Challenge id must not be empty");
                Assert.Greater(c.target, 0,   $"Challenge '{c.id}' target must be positive");
                Assert.Greater(c.crystals, 0, $"Challenge '{c.id}' crystal reward must be positive");
                Assert.IsTrue(Enum.IsDefined(typeof(AchievementTrigger), c.trigger),
                    $"Challenge '{c.id}' trigger '{c.trigger}' is not a valid AchievementTrigger value");
            }
        }
    }
}
