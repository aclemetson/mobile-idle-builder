using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Tests for the session login tick (AchievementService.NotifyLogin), which had no caller at
    /// all until ECSLoadBridge started firing it. Its absence made `daily_login` impossible, and
    /// because the Daily category bonus requires EVERY non-bonus Daily achievement, it also made
    /// `daily_complete` unreachable — the whole Daily faucet was dead.
    ///
    /// Own database rather than reusing AchievementResetTests': adding a Login-trigger Daily
    /// achievement to that fixture would break its CategoryComplete test, which assumes crafting
    /// alone completes every Daily.
    /// </summary>
    public class AchievementLoginTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _achievementGO;

        AchievementDatabase _testDb;
        AchievementSO       _loginAch;
        AchievementSO       _craftAch;
        AchievementSO       _bonusAch;

        static readonly FieldInfo  s_dbField     = typeof(AchievementService).GetField("database",       BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo  s_dbItems     = typeof(AchievementDatabase).GetField("achievements",  BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo s_buildLookup = typeof(AchievementDatabase).GetMethod("BuildLookup",  BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);

            // Mirrors the real daily_login: one login is enough.
            _loginAch = ScriptableObject.CreateInstance<AchievementSO>();
            _loginAch.id              = "daily_login";
            _loginAch.displayName     = "Daily Devotion";
            _loginAch.category        = AchievementCategory.Daily;
            _loginAch.triggerType     = AchievementTrigger.Login;
            _loginAch.triggerTargetId = "";
            _loginAch.triggerQuantity = 1;
            _loginAch.paidCurrencyReward = 2;

            // A second non-bonus Daily, so the category bonus needs BOTH to complete.
            _craftAch = ScriptableObject.CreateInstance<AchievementSO>();
            _craftAch.id              = "daily_craft";
            _craftAch.displayName     = "Test Daily Craft";
            _craftAch.category        = AchievementCategory.Daily;
            _craftAch.triggerType     = AchievementTrigger.CraftItem;
            _craftAch.triggerTargetId = "";
            _craftAch.triggerQuantity = 1;

            // The id must be exactly "daily_complete" — CategoryBonusId() maps Daily to that id.
            _bonusAch = ScriptableObject.CreateInstance<AchievementSO>();
            _bonusAch.id              = "daily_complete";
            _bonusAch.displayName     = "Day Well Spent";
            _bonusAch.category        = AchievementCategory.Daily;
            _bonusAch.triggerType     = AchievementTrigger.Login;
            _bonusAch.triggerTargetId = "";
            _bonusAch.triggerQuantity = 99999;   // sentinel: only reachable via CheckCategoryComplete
            _bonusAch.paidCurrencyReward = 15;

            _testDb = ScriptableObject.CreateInstance<AchievementDatabase>();
            s_dbItems.SetValue(_testDb, new[] { _loginAch, _craftAch, _bonusAch });
            s_buildLookup.Invoke(_testDb, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_achievementGO != null) { Object.DestroyImmediate(_achievementGO); _achievementGO = null; }
            if (_saveManagerGO != null) { Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_loginAch != null) { Object.DestroyImmediate(_loginAch); _loginAch = null; }
            if (_craftAch != null) { Object.DestroyImmediate(_craftAch); _craftAch = null; }
            if (_bonusAch != null) { Object.DestroyImmediate(_bonusAch); _bonusAch = null; }
            if (_testDb   != null) { Object.DestroyImmediate(_testDb);   _testDb   = null; }
        }

        // ── The gap this closes ───────────────────────────────────────────────

        [Test]
        public void NotifyLogin_CompletesLoginAchievement()
        {
            SpawnServices();

            AchievementService.Instance.NotifyLogin();

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_login"),
                "A session login must complete a quantity-1 Login achievement");
        }

        [Test]
        public void NotifyLogin_ThenCraft_MakesDailyCategoryBonusReachable()
        {
            SpawnServices();

            AchievementService.Instance.NotifyLogin();
            AchievementService.Instance.NotifyCraft("any", 1);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft"),
                "sanity: the craft achievement should complete");
            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_complete"),
                "With every non-bonus Daily complete, the category bonus must fire. Without the " +
                "login hook daily_login can never complete, which is what made this unreachable.");
        }

        [Test]
        public void WithoutLogin_DailyCategoryBonusStaysLocked()
        {
            SpawnServices();

            // Craft only — daily_login is left incomplete, the pre-fix situation.
            AchievementService.Instance.NotifyCraft("any", 1);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("daily_craft"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("daily_complete"),
                "The bonus must not fire while a non-bonus Daily is still incomplete");
        }

        // ── Idempotence ───────────────────────────────────────────────────────

        [Test]
        public void NotifyLogin_Twice_DoesNotDoubleCount()
        {
            SpawnServices();

            AchievementService.Instance.NotifyLogin();
            int afterFirst = AchievementService.Instance.CompletedCount;
            AchievementService.Instance.NotifyLogin();

            Assert.AreEqual(afterFirst, AchievementService.Instance.CompletedCount,
                "Repeat logins must be idempotent — Evaluate skips already-completed ids");
            Assert.AreEqual(1, AchievementService.Instance.CompletedCount,
                "Only daily_login should have completed from a login alone");
        }

        // ── Still respects the pre-prestige gate ──────────────────────────────

        [Test]
        public void NotifyLogin_PreFirstPrestige_AccruesNothing()
        {
            SpawnServices(hasCompletedFirstRun: false);

            AchievementService.Instance.NotifyLogin();

            Assert.IsFalse(AchievementService.Instance.IsCompleted("daily_login"),
                "Achievements stay dormant until the first prestige");
            Assert.AreEqual(0, AchievementService.Instance.CompletedCount);
            Assert.IsEmpty(SaveManager.Instance.Current.achievements,
                "A pre-prestige login must not persist anything");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void SpawnServices(bool hasCompletedFirstRun = true)
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

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
