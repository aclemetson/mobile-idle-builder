using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for AchievementService (progress tracking, threshold completion,
    /// save flush).
    ///
    /// AchievementService has a serialized AchievementDatabase field that can't be set via
    /// the Inspector in tests, so we inject a minimal in-memory database using reflection
    /// before Start() fires. This is the established pattern until the service gains a
    /// proper injection point.
    ///
    /// Depends on SaveManager being active (AchievementService.Start → LoadFromSave reads
    /// SaveManager.Current), so both GameObjects are created in SetUp order.
    /// </summary>
    public class AchievementServiceTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _achievementGO;

        AchievementDatabase _testDb;
        AchievementSO       _craftAchievement;

        static readonly FieldInfo  s_dbField      = typeof(AchievementService).GetField("database",     BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo  s_dbItems      = typeof(AchievementDatabase).GetField("achievements", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo s_buildLookup  = typeof(AchievementDatabase).GetMethod("BuildLookup", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);

            // Build a minimal in-memory achievement database
            _craftAchievement = ScriptableObject.CreateInstance<AchievementSO>();
            _craftAchievement.id              = "test_craft_5";
            _craftAchievement.displayName     = "Test Crafter";
            _craftAchievement.triggerType     = AchievementTrigger.CraftItem;
            _craftAchievement.triggerTargetId = "hydrogen";
            _craftAchievement.triggerQuantity = 5;

            _testDb = ScriptableObject.CreateInstance<AchievementDatabase>();
            s_dbItems.SetValue(_testDb, new[] { _craftAchievement });
            s_buildLookup.Invoke(_testDb, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_achievementGO != null) { Object.DestroyImmediate(_achievementGO); _achievementGO = null; }
            if (_saveManagerGO != null) { Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_craftAchievement != null) { Object.DestroyImmediate(_craftAchievement); _craftAchievement = null; }
            if (_testDb           != null) { Object.DestroyImmediate(_testDb);           _testDb           = null; }
        }

        // Spawns SaveManager then AchievementService with the injected test database.
        // Database is set before RunStart so it is available when Start() → LoadFromSave() runs.
        void SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            _achievementGO = new GameObject("AchievementService");
            var svc = _achievementGO.AddComponent<AchievementService>();
            RunAwake(svc);
            s_dbField.SetValue(svc, _testDb);
            RunStart(svc);
        }

        // ── Progress tracking ─────────────────────────────────────────────────

        [Test]
        public void NotifyCraft_BelowThreshold_TracksProgress()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 3);

            Assert.AreEqual(3, AchievementService.Instance.GetProgress("test_craft_5"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        [Test]
        public void NotifyCraft_AccumulatesAcrossMultipleCalls()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 2);
            AchievementService.Instance.NotifyCraft("hydrogen", 2);

            Assert.AreEqual(4, AchievementService.Instance.GetProgress("test_craft_5"));
        }

        [Test]
        public void NotifyCraft_WrongItemId_DoesNotCount()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("proton", 10);

            Assert.AreEqual(0,     AchievementService.Instance.GetProgress("test_craft_5"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        // ── Completion ────────────────────────────────────────────────────────

        [Test]
        public void NotifyCraft_AtThreshold_CompletesAchievement()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        [Test]
        public void NotifyCraft_AtThreshold_FiresOnAchievementUnlocked()
        {
            SpawnServices();

            AchievementSO received = null;
            AchievementService.Instance.OnAchievementUnlocked += a => received = a;
            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsNotNull(received, "OnAchievementUnlocked should have fired");
            Assert.AreEqual("test_craft_5", received.id);
        }

        [Test]
        public void CompletedAchievement_CountsAsOne()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.AreEqual(1, AchievementService.Instance.CompletedCount);
        }

        // ── Save flush ────────────────────────────────────────────────────────

        [Test]
        public void ProgressFlushed_ToSaveData_AfterNotify()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 3);

            var progress = SaveManager.Instance.Current.achievementProgress;
            var entry    = progress.Find(e => e.id == "test_craft_5");
            Assert.IsNotNull(entry, "Progress entry should exist in SaveData after Notify");
            Assert.AreEqual(3, entry.count);
        }

        [Test]
        public void CompletedAchievement_FlushedToSaveAchievements()
        {
            SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsTrue(
                SaveManager.Instance.Current.achievements.Contains("test_craft_5"),
                "Completed achievement id should appear in SaveData.achievements");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
