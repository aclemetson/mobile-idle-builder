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
        }
        [Serializable] private class Reward
        {
            public int day; public int crystals; public long entropy; public int prestige_currency;
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

        [Test]
        public void ChallengePool_IsGone_DailyTasksAreAchievements()
        {
            // 2026-11-20: the rotating pool duplicated four Daily achievements; it was merged into them.
            StringAssert.DoesNotContain("\"daily_challenges\"", File.ReadAllText(Path.Combine(Application.dataPath, "Data/game_data.json")));
            Assert.IsNull(typeof(DailyContentSO).GetField("challengePool"));
        }
    }
}
