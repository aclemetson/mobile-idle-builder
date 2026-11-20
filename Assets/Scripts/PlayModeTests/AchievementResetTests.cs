using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Tests for the enhanced AchievementService:
    ///   - Daily/Weekly/Monthly period reset (re-locks completed achievements and wipes progress)
    ///   - Claim reward flow (grants paidCurrency, removes from unclaimed)
    ///   - Category-complete bonus auto-fires
    ///   - ResetInMemory clears in-memory state
    /// </summary>
    public class AchievementResetTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _achievementGO;

        AchievementDatabase _testDb;
        AchievementSO       _dailyAch;
        AchievementSO       _dailyBonusAch;
        AchievementSO       _progAch;

        static readonly FieldInfo  s_dbField     = typeof(AchievementService).GetField("database",      BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo  s_dbItems     = typeof(AchievementDatabase).GetField("achievements",  BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo s_buildLookup = typeof(AchievementDatabase).GetMethod("BuildLookup", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            // Period resets need server time; tests use the (shiftable) game clock instead of the network.
            ServerClock.TestNowOverride = () => GameClock.UtcNow;
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)

            // Single daily craft achievement (quantity=2)
            _dailyAch = ScriptableObject.CreateInstance<AchievementSO>();
            _dailyAch.id              = "daily_craft_2";
            _dailyAch.displayName     = "Test Daily Craft";
            _dailyAch.category        = AchievementCategory.Daily;
            _dailyAch.triggerType     = AchievementTrigger.CraftItem;
            _dailyAch.triggerTargetId = "";
            _dailyAch.triggerQuantity = 2;
            _dailyAch.paidCurrencyReward = 5;

            // Bonus achievement that auto-completes when all dailies done
            _dailyBonusAch = ScriptableObject.CreateInstance<AchievementSO>();
            _dailyBonusAch.id              = "daily_complete";
            _dailyBonusAch.displayName     = "Day Well Spent";
            _dailyBonusAch.category        = AchievementCategory.Daily;
            _dailyBonusAch.triggerType     = AchievementTrigger.Login;
            _dailyBonusAch.triggerTargetId = "";
            _dailyBonusAch.triggerQuantity = 99999;
            _dailyBonusAch.paidCurrencyReward = 25;

            // Progression achievement (never resets)
            _progAch = ScriptableObject.CreateInstance<AchievementSO>();
            _progAch.id              = "prog_first_craft";
            _progAch.displayName     = "First Fusion";
            _progAch.category        = AchievementCategory.Progression;
            _progAch.triggerType     = AchievementTrigger.CraftItem;
            _progAch.triggerTargetId = "";
            _progAch.triggerQuantity = 1;
            _progAch.paidCurrencyReward = 5;

            _testDb = ScriptableObject.CreateInstance<AchievementDatabase>();
            s_dbItems.SetValue(_testDb, new[] { _dailyAch, _dailyBonusAch, _progAch });
            s_buildLookup.Invoke(_testDb, null);
        }

        [TearDown]
        public void TearDown()
        {
            ServerClock.TestNowOverride = null;
            if (_achievementGO != null) { UnityEngine.Object.DestroyImmediate(_achievementGO); _achievementGO = null; }
            if (_saveManagerGO != null) { UnityEngine.Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }

            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_dailyAch      != null) { UnityEngine.Object.DestroyImmediate(_dailyAch);      _dailyAch      = null; }
            if (_dailyBonusAch != null) { UnityEngine.Object.DestroyImmediate(_dailyBonusAch); _dailyBonusAch = null; }
            if (_progAch       != null) { UnityEngine.Object.DestroyImmediate(_progAch);       _progAch       = null; }
            if (_testDb        != null) { UnityEngine.Object.DestroyImmediate(_testDb);        _testDb        = null; }
        }

        // ── Pre-first-prestige gating ─────────────────────────────────────────
        // 2026-11-20: Daily achievements (which replaced the Daily tab's challenges) count from the start;
        // Weekly, Monthly and Progression stay dormant until the first prestige.

        [Test]
        public void PreFirstPrestige_DailyCounts_ProgressionDoesNot()
        {
            SpawnServices(hasCompletedFirstRun: false);

            AchievementService.Instance.NotifyCraft("any", 5);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"),
                "Daily achievements count before the first prestige");
            Assert.IsTrue(AchievementService.Instance.IsClaimable("daily_craft_2"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("prog_first_craft"),
                "Progression waits for the first prestige");
        }

        [Test]
        public void PreFirstPrestige_OnlyDailyProgressReachesTheSave()
        {
            SpawnServices(hasCompletedFirstRun: false);

            AchievementService.Instance.NotifyCraft("any", 5);

            CollectionAssert.DoesNotContain(SaveManager.Instance.Current.achievements, "prog_first_craft");
            CollectionAssert.Contains(SaveManager.Instance.Current.achievements, "daily_craft_2");
        }

        [Test]
        public void FirstPrestigeMidSession_InitPostPrestige_StartsAccruing()
        {
            SpawnServices(hasCompletedFirstRun: false);

            // Pre-prestige crafts don't count toward Progression.
            AchievementService.Instance.NotifyCraft("any", 5);
            Assert.IsFalse(AchievementService.Instance.IsCompleted("prog_first_craft"));

            // First prestige lands mid-session: HUDController flips the flag then initializes.
            SaveManager.Instance.Current.tutorial.hasCompletedFirstRun = true;
            AchievementService.Instance.InitPostPrestige();

            AchievementService.Instance.NotifyCraft("any", 1);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("prog_first_craft"),
                "Crafting after the unlock must complete a quantity-1 achievement");
        }

        // ── Claim flow ────────────────────────────────────────────────────────

        [Test]
        public void ClaimReward_GrantsPaidCurrency()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);

            Assert.IsTrue(AchievementService.Instance.IsClaimable("daily_craft_2"),
                "Achievement should be claimable after completion");

            AchievementService.Instance.ClaimReward("daily_craft_2");

            Assert.AreEqual(5, SaveManager.Instance.Current.paidCurrency,
                "5 Crystals should be granted on claim");
            Assert.IsFalse(AchievementService.Instance.IsClaimable("daily_craft_2"),
                "Achievement should no longer be claimable after claim");
        }

        [Test]
        public void ClaimReward_Idempotent_SecondClaimDoesNothing()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            AchievementService.Instance.ClaimReward("daily_craft_2");
            AchievementService.Instance.ClaimReward("daily_craft_2"); // second call

            Assert.AreEqual(5, SaveManager.Instance.Current.paidCurrency,
                "Second claim should not double-grant currency");
        }

        [Test]
        public void ClaimReward_FiresOnRewardClaimed()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);

            AchievementSO received = null;
            AchievementService.Instance.OnRewardClaimed += a => received = a;
            AchievementService.Instance.ClaimReward("daily_craft_2");

            Assert.IsNotNull(received, "OnRewardClaimed should fire");
            Assert.AreEqual("daily_craft_2", received.id);
        }

        // ── Category-complete bonus ───────────────────────────────────────────

        [Test]
        public void CategoryComplete_AllDailyDone_BonusAutoCompletes()
        {
            SpawnServices();

            // The only non-bonus daily in our test DB is daily_craft_2. Complete it.
            AchievementService.Instance.NotifyCraft("any", 2);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_complete"),
                "Bonus achievement should auto-complete when all dailies are done");
            Assert.IsTrue(AchievementService.Instance.IsClaimable("daily_complete"),
                "Bonus should be claimable");
        }

        // ── Daily period reset ────────────────────────────────────────────────

        [Test]
        public void DailyReset_ExpiredDate_RelocksCompletedDailyAchievement()
        {
            // Complete the daily achievement and mark its reset date in the past.
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"));

            // Manually set dailyResetUtc to yesterday (already expired).
            var save = SaveManager.Instance.Current;
            save.dailyResetUtc = DateTime.UtcNow.AddDays(-1).ToString("o");

            // Recreate the service (simulates game restart); CheckPeriodResets fires on Start().
            UnityEngine.Object.DestroyImmediate(_achievementGO);
            _achievementGO = new GameObject("AchievementService2");
            var svc2 = _achievementGO.AddComponent<AchievementService>();
            RunAwake(svc2);
            s_dbField.SetValue(svc2, _testDb);
            RunStart(svc2);

            Assert.IsFalse(AchievementService.Instance.IsCompleted("daily_craft_2"),
                "Daily achievement should be re-locked after period expiry");
            Assert.AreEqual(0, AchievementService.Instance.GetProgress("daily_craft_2"),
                "Progress should be cleared on reset");
        }

        [Test]
        public void DailyReset_ProgressionAchievementNotAffected()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 1);
            Assert.IsTrue(AchievementService.Instance.IsCompleted("prog_first_craft"));

            var save = SaveManager.Instance.Current;
            save.dailyResetUtc = DateTime.UtcNow.AddDays(-1).ToString("o");

            UnityEngine.Object.DestroyImmediate(_achievementGO);
            _achievementGO = new GameObject("AchievementService3");
            var svc3 = _achievementGO.AddComponent<AchievementService>();
            RunAwake(svc3);
            s_dbField.SetValue(svc3, _testDb);
            RunStart(svc3);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("prog_first_craft"),
                "Progression achievements must survive daily reset");
        }

        // ── Mid-session checks and server time ────────────────────────────────

        [Test]
        public void PeriodCheck_MidSession_ResetsExpiredDailyAndFiresEvent()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"));

            // The day ends while the game is still running (no restart).
            SaveManager.Instance.Current.dailyResetUtc = DateTime.UtcNow.AddMinutes(-1).ToString("o");
            bool fired = false;
            AchievementService.Instance.OnPeriodReset += () => fired = true;

            AchievementService.Instance.RequestPeriodCheck("test");

            Assert.IsFalse(AchievementService.Instance.IsCompleted("daily_craft_2"), "an expired daily must re-lock mid-session");
            Assert.IsTrue(fired, "OnPeriodReset lets an open menu rebuild");
            Assert.IsTrue(DateTime.Parse(SaveManager.Instance.Current.dailyResetUtc, null,
                              System.Globalization.DateTimeStyles.RoundtripKind) > DateTime.UtcNow,
                          "the next deadline must be in the future");
        }

        [Test]
        public void PeriodCheck_DeadlineNotReached_KeepsCompletedDaily()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);

            AchievementService.Instance.RequestPeriodCheck("test");

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"));
            Assert.IsTrue(AchievementService.Instance.IsClaimable("daily_craft_2"));
        }

        [Test]
        public void PeriodCheck_NoServerTime_DoesNotReset()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            SaveManager.Instance.Current.dailyResetUtc = DateTime.UtcNow.AddDays(-1).ToString("o");

            ServerClock.TestNowOverride = () => null;   // offline: the sync finds no server time
            AchievementService.Instance.RequestPeriodCheck("test");

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"),
                "without server time the device clock is not trusted, so nothing resets");
        }

        [Test]
        public void PeriodCheck_DeadlineFromFastClock_IsClampedWithoutReset()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            // Written while the phone's date was set a week ahead.
            SaveManager.Instance.Current.dailyResetUtc = DateTime.UtcNow.Date.AddDays(8).ToString("o");

            AchievementService.Instance.RequestPeriodCheck("test");

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"), "clamping must not reset");
            var stored = DateTime.Parse(SaveManager.Instance.Current.dailyResetUtc, null,
                                        System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.AreEqual(DateTime.UtcNow.Date.AddDays(1), stored.ToUniversalTime(),
                "deadline is pulled back to the end of the current day");
        }

        [Test]
        public void ServerClock_ParsesHttpDateHeaderAsUtc()
        {
            Assert.IsTrue(ServerClock.TryParseHttpDate("Wed, 18 Nov 2026 07:28:05 GMT", out var utc));
            Assert.AreEqual(new DateTime(2026, 11, 18, 7, 28, 5, DateTimeKind.Utc), utc);
            Assert.AreEqual(DateTimeKind.Utc, utc.Kind);
            Assert.IsFalse(ServerClock.TryParseHttpDate("", out _));
            Assert.IsFalse(ServerClock.TryParseHttpDate("not a date", out _));
        }

        // ── ResetInMemory ─────────────────────────────────────────────────────

        [Test]
        public void ResetInMemory_ClearsCompletedAndProgress()
        {
            SpawnServices();
            AchievementService.Instance.NotifyCraft("any", 2);
            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft_2"));

            AchievementService.Instance.ResetInMemory();

            Assert.IsFalse(AchievementService.Instance.IsCompleted("daily_craft_2"),
                "Completed set should be empty after ResetInMemory");
            Assert.AreEqual(0, AchievementService.Instance.GetProgress("daily_craft_2"),
                "Progress should be 0 after ResetInMemory");
            Assert.IsFalse(AchievementService.Instance.IsClaimable("daily_craft_2"),
                "Unclaimed set should be empty after ResetInMemory");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void SpawnServices(bool hasCompletedFirstRun = true)
        {
            _saveManagerGO = new GameObject("SaveManager");
            var sm = _saveManagerGO.AddComponent<SaveManager>();
            RunAwake(sm);
            // Non-daily achievements wait for the first prestige; most tests want the unlocked state.
            SaveManager.Instance.Current.tutorial.hasCompletedFirstRun = hasCompletedFirstRun;

            _achievementGO = new GameObject("AchievementService");
            var svc = _achievementGO.AddComponent<AchievementService>();
            RunAwake(svc);
            s_dbField.SetValue(svc, _testDb);
            RunStart(svc);
        }

        static void RunStart(MonoBehaviour mb) =>
            mb.GetType()
              .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
              ?.Invoke(mb, null);

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
