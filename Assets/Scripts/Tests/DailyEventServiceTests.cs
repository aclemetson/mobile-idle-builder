using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for DailyEventService (the 28-day login calendar). Core logic takes an injected
    /// <c>now</c> so the streak can be driven without the real wall clock. The rotating challenges it used to
    /// hold became Daily achievements on 2026-11-20 (see DailyMergeTests).
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
            // EnsureToday / the no-arg claim API use server time; tests use the game clock instead of the network.
            ServerClock.TestNowOverride = () => GameClock.UtcNow;
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
        }

        [TearDown]
        public void TearDown()
        {
            ServerClock.TestNowOverride = null;
            if (_serviceGO    != null) { UnityEngine.Object.DestroyImmediate(_serviceGO);    _serviceGO    = null; }
            if (_saveManagerGO != null) { UnityEngine.Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }
            if (_content      != null) { UnityEngine.Object.DestroyImmediate(_content);      _content      = null; }

            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
        }

        // ── Test content builders ─────────────────────────────────────────────

        static DailyRewardEntry Rw(int day, int crystals, long entropy = 0, int pc = 0) =>
            new() { day = day, crystals = crystals, entropy = entropy, prestigeCurrency = pc };

        /// <summary>A 3-day login calendar.</summary>
        DailyContentSO Calendar()
        {
            _content = ScriptableObject.CreateInstance<DailyContentSO>();
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
            return svc;
        }

        // ── Login streak ──────────────────────────────────────────────────────

        [Test]
        public void ClaimLoginReward_AdvancesStreakAndGrantsCrystals()
        {
            var svc = SpawnService(Calendar());
            var day1 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

            Assert.IsTrue(svc.CanClaimLoginReward(day1));
            Assert.IsTrue(svc.ClaimLoginReward(day1));

            Assert.AreEqual(1, SaveManager.Instance.Current.loginStreakIndex);
            Assert.AreEqual(10, SaveManager.Instance.Current.paidCurrency, "day-1 reward = 10 crystals");
        }

        [Test]
        public void ClaimLoginReward_SameDaySecondClaimRejected()
        {
            var svc = SpawnService(Calendar());
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
            var svc = SpawnService(Calendar());
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
            var svc = SpawnService(Calendar()); // 3-day calendar
            SaveManager.Instance.Current.loginStreakIndex = 2; // last day

            Assert.IsTrue(svc.ClaimLoginReward(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(0, SaveManager.Instance.Current.loginStreakIndex,
                "calendar loops back to day 1 after the final day");
        }

        // ── Server time ───────────────────────────────────────────────────────

        [Test]
        public void NoServerTime_LoginRewardCannotBeClaimed()
        {
            var svc = SpawnService(Calendar());
            ServerClock.TestNowOverride = () => null;   // offline

            Assert.IsTrue(svc.WaitingForServerTime);
            Assert.IsFalse(svc.CanClaimLoginReward(), "the device clock is not trusted for the login reward");
            Assert.IsFalse(svc.ClaimLoginReward());
            Assert.AreEqual(0, SaveManager.Instance.Current.paidCurrency);
        }

        [Test]
        public void WithServerTime_LoginRewardClaimsOncePerDay()
        {
            var svc = SpawnService(Calendar());

            Assert.IsTrue(svc.ClaimLoginReward());
            Assert.IsFalse(svc.ClaimLoginReward(), "second claim on the same server day is refused");
            Assert.AreEqual(10, SaveManager.Instance.Current.paidCurrency);
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
