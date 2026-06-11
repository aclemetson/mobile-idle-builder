using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for DailyEventService. Core logic takes an injected <c>now</c> so reset,
    /// streak, and deterministic-pick behavior can be driven without the real wall clock.
    /// Backs up / restores the developer's save file like the other save-touching test fixtures.
    /// </summary>
    public class DailyEventServiceTests
    {
        static readonly FieldInfo s_contentField =
            typeof(DailyEventService).GetField("content", BindingFlags.NonPublic | BindingFlags.Instance);

        string _savePath;
        string _saveBackup;
        GameObject _saveManagerGO;
        GameObject _serviceGO;
        DailyContentSO _content;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGO    != null) { UnityEngine.Object.DestroyImmediate(_serviceGO);    _serviceGO    = null; }
            if (_saveManagerGO != null) { UnityEngine.Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }
            if (_content      != null) { UnityEngine.Object.DestroyImmediate(_content);      _content      = null; }

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
        }

        // ── Test content builders ─────────────────────────────────────────────

        static DailyChallengeEntry Ch(string id, string trigger, int target, int crystals = 15) =>
            new() { id = id, description = id, trigger = trigger, target = target, crystals = crystals };

        static DailyRewardEntry Rw(int day, int crystals, long entropy = 0, int pc = 0) =>
            new() { day = day, crystals = crystals, entropy = entropy, prestigeCurrency = pc };

        /// <summary>3-entry pool — all three are always drawn regardless of the day offset.</summary>
        DailyContentSO ThreeChallengePool()
        {
            _content = ScriptableObject.CreateInstance<DailyContentSO>();
            _content.challengePool = new[]
            {
                Ch("craft5", "CraftItem",     5),
                Ch("place2", "PlaceBuilding", 2),
                Ch("login1", "Login",         1),
            };
            _content.loginRewards = new[] { Rw(1, 10), Rw(2, 20), Rw(3, 150, 0, 1) };
            return _content;
        }

        DailyEventService SpawnService(DailyContentSO content)
        {
            _saveManagerGO = new GameObject("SaveManager");
            RunAwake(_saveManagerGO.AddComponent<SaveManager>());

            _serviceGO = new GameObject("DailyEventService");
            var svc = _serviceGO.AddComponent<DailyEventService>();
            RunAwake(svc);
            s_contentField.SetValue(svc, content);
            svc.ReloadFromSave();
            return svc;
        }

        // ── Challenge reset / deterministic pick ──────────────────────────────

        [Test]
        public void CheckDailyReset_FirstInit_RollsThreeChallenges()
        {
            var svc = SpawnService(ThreeChallengePool());
            svc.CheckDailyReset(new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc));

            Assert.AreEqual(3, svc.TodaysChallengeIds.Count);
            CollectionAssert.AreEquivalent(
                new[] { "craft5", "place2", "login1" }, svc.TodaysChallengeIds);
        }

        [Test]
        public void CheckDailyReset_DeterministicPick_SameDaySameIds()
        {
            // 6-entry pool so the offset actually selects a subset.
            _content = ScriptableObject.CreateInstance<DailyContentSO>();
            _content.challengePool = new[]
            {
                Ch("c0", "CraftItem", 1), Ch("c1", "CraftItem", 1), Ch("c2", "CraftItem", 1),
                Ch("c3", "CraftItem", 1), Ch("c4", "CraftItem", 1), Ch("c5", "CraftItem", 1),
            };
            var svc = SpawnService(_content);

            var day = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc); // DayOfYear = 3, offset 3%6 = 3
            svc.CheckDailyReset(day);
            var first = svc.TodaysChallengeIds.ToArray();

            CollectionAssert.AreEqual(new[] { "c3", "c4", "c5" }, first);

            // Re-rolling for the same UTC day yields the same set.
            SaveManager.Instance.Current.dailyChallengeResetUtc =
                day.AddHours(-1).ToString("o"); // force a re-roll trigger
            svc.CheckDailyReset(day);
            CollectionAssert.AreEqual(first, svc.TodaysChallengeIds.ToArray());
        }

        [Test]
        public void CheckDailyReset_Rollover_ClearsProgressAndClaimed()
        {
            var svc = SpawnService(ThreeChallengePool());
            var today = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);
            svc.CheckDailyReset(today);

            svc.NotifyCraft("any", 5);                    // complete craft5
            Assert.IsTrue(svc.IsChallengeComplete("craft5"));
            Assert.IsTrue(svc.ClaimChallenge("craft5"));
            Assert.IsTrue(svc.IsChallengeClaimed("craft5"));

            // Next UTC day → reset must wipe progress + claimed and draw a fresh set.
            var tomorrow = today.AddDays(1);
            svc.CheckDailyReset(tomorrow);

            Assert.AreEqual(0, svc.GetChallengeProgress("craft5"), "progress cleared on rollover");
            Assert.IsFalse(svc.IsChallengeClaimed("craft5"),       "claimed cleared on rollover");
        }

        // ── Challenge progress + claim ────────────────────────────────────────

        [Test]
        public void Challenge_AccumulatesAndCapsAtTarget()
        {
            var svc = SpawnService(ThreeChallengePool());
            svc.CheckDailyReset(new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc));

            svc.NotifyCraft("any", 3);
            Assert.AreEqual(3, svc.GetChallengeProgress("craft5"));
            Assert.IsFalse(svc.IsChallengeComplete("craft5"));

            svc.NotifyCraft("any", 10);                   // overshoot
            Assert.AreEqual(5, svc.GetChallengeProgress("craft5"), "progress caps at target");
            Assert.IsTrue(svc.IsChallengeComplete("craft5"));
        }

        [Test]
        public void Challenge_EntropySpent_AccumulatesAmountNotCallCount()
        {
            _content = ScriptableObject.CreateInstance<DailyContentSO>();
            _content.challengePool = new[] { Ch("spend100", "SpendEntropy", 100) };
            _content.loginRewards  = new[] { Rw(1, 10) };
            var svc = SpawnService(_content);
            svc.CheckDailyReset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            svc.NotifyEntropySpent(40);
            svc.NotifyEntropySpent(40);
            Assert.AreEqual(80, svc.GetChallengeProgress("spend100"),
                "entropy challenge accumulates spent amount, not number of calls");
        }

        [Test]
        public void ClaimChallenge_GrantsCrystals_AndRejectsSecondClaim()
        {
            var svc = SpawnService(ThreeChallengePool());
            svc.CheckDailyReset(new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc));

            svc.NotifyCraft("any", 5);
            Assert.IsTrue(svc.ClaimChallenge("craft5"));
            Assert.AreEqual(15, SaveManager.Instance.Current.paidCurrency);

            Assert.IsFalse(svc.ClaimChallenge("craft5"), "second claim rejected");
            Assert.AreEqual(15, SaveManager.Instance.Current.paidCurrency, "no double grant");
        }

        [Test]
        public void ClaimChallenge_IncompleteRejected()
        {
            var svc = SpawnService(ThreeChallengePool());
            svc.CheckDailyReset(new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc));

            svc.NotifyCraft("any", 2); // below target of 5
            Assert.IsFalse(svc.ClaimChallenge("craft5"));
            Assert.AreEqual(0, SaveManager.Instance.Current.paidCurrency);
        }

        // ── Login streak ──────────────────────────────────────────────────────

        [Test]
        public void ClaimLoginReward_AdvancesStreakAndGrantsCrystals()
        {
            var svc = SpawnService(ThreeChallengePool());
            var day1 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

            Assert.IsTrue(svc.CanClaimLoginReward(day1));
            Assert.IsTrue(svc.ClaimLoginReward(day1));

            Assert.AreEqual(1, SaveManager.Instance.Current.loginStreakIndex);
            Assert.AreEqual(10, SaveManager.Instance.Current.paidCurrency, "day-1 reward = 10 crystals");
        }

        [Test]
        public void ClaimLoginReward_SameDaySecondClaimRejected()
        {
            var svc = SpawnService(ThreeChallengePool());
            var day1 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

            Assert.IsTrue(svc.ClaimLoginReward(day1));
            Assert.IsFalse(svc.CanClaimLoginReward(day1.AddHours(6)), "still same UTC day");
            Assert.IsFalse(svc.ClaimLoginReward(day1.AddHours(6)));
            Assert.AreEqual(1, SaveManager.Instance.Current.loginStreakIndex, "index unchanged");
            Assert.AreEqual(10, SaveManager.Instance.Current.paidCurrency, "no double grant");
        }

        [Test]
        public void ClaimLoginReward_MissedDay_StreakDoesNotReset()
        {
            var svc = SpawnService(ThreeChallengePool());
            Assert.IsTrue(svc.ClaimLoginReward(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(1, SaveManager.Instance.Current.loginStreakIndex);

            // Skip Jan 2 entirely; claim again on Jan 3. Index must advance from 1, not reset to 0.
            Assert.IsTrue(svc.CanClaimLoginReward(new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc)));
            Assert.IsTrue(svc.ClaimLoginReward(new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(2, SaveManager.Instance.Current.loginStreakIndex,
                "missed day must not reset the streak (kindness rule)");
        }

        [Test]
        public void ClaimLoginReward_LoopsAfterLastCalendarDay()
        {
            var svc = SpawnService(ThreeChallengePool()); // 3-day calendar
            SaveManager.Instance.Current.loginStreakIndex = 2; // last day

            Assert.IsTrue(svc.ClaimLoginReward(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(0, SaveManager.Instance.Current.loginStreakIndex,
                "calendar loops back to day 1 after the final day");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void RunAwake(MonoBehaviour mb)
        {
            var t = mb.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(mb, null); return; }
                t = t.BaseType;
            }
        }
    }
}
