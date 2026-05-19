using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// PlayMode tests for AchievementService (progress tracking, threshold completion,
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

        // [UnityTearDown] (not [TearDown]) is required for IEnumerator return type.
        // The yield lets OnDestroy fire so SingletonMonoBehaviour.Instance is null before the next SetUp.
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_achievementGO != null) { Object.Destroy(_achievementGO); _achievementGO = null; }
            if (_saveManagerGO != null) { Object.Destroy(_saveManagerGO); _saveManagerGO = null; }
            yield return null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_craftAchievement != null) { Object.Destroy(_craftAchievement); _craftAchievement = null; }
            if (_testDb           != null) { Object.Destroy(_testDb);           _testDb           = null; }
        }

        // Spawns SaveManager then AchievementService with the injected test database.
        // Database must be set before yield so it is available when Start() → LoadFromSave() runs.
        IEnumerator SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            _saveManagerGO.AddComponent<SaveManager>();
            yield return null;

            _achievementGO = new GameObject("AchievementService");
            var svc = _achievementGO.AddComponent<AchievementService>();
            s_dbField.SetValue(svc, _testDb);
            yield return null;
        }

        // ── Progress tracking ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator NotifyCraft_BelowThreshold_TracksProgress()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 3);

            Assert.AreEqual(3, AchievementService.Instance.GetProgress("test_craft_5"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        [UnityTest]
        public IEnumerator NotifyCraft_AccumulatesAcrossMultipleCalls()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 2);
            AchievementService.Instance.NotifyCraft("hydrogen", 2);

            Assert.AreEqual(4, AchievementService.Instance.GetProgress("test_craft_5"));
        }

        [UnityTest]
        public IEnumerator NotifyCraft_WrongItemId_DoesNotCount()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("proton", 10);

            Assert.AreEqual(0,     AchievementService.Instance.GetProgress("test_craft_5"));
            Assert.IsFalse(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        // ── Completion ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator NotifyCraft_AtThreshold_CompletesAchievement()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsTrue(AchievementService.Instance.IsCompleted("test_craft_5"));
        }

        [UnityTest]
        public IEnumerator NotifyCraft_AtThreshold_FiresOnAchievementUnlocked()
        {
            yield return SpawnServices();

            AchievementSO received = null;
            AchievementService.Instance.OnAchievementUnlocked += a => received = a;
            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsNotNull(received, "OnAchievementUnlocked should have fired");
            Assert.AreEqual("test_craft_5", received.id);
        }

        [UnityTest]
        public IEnumerator CompletedAchievement_CountsAsOne()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.AreEqual(1, AchievementService.Instance.CompletedCount);
        }

        // ── Save flush ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ProgressFlushed_ToSaveData_AfterNotify()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 3);

            var progress = SaveManager.Instance.Current.achievementProgress;
            var entry    = progress.Find(e => e.id == "test_craft_5");
            Assert.IsNotNull(entry, "Progress entry should exist in SaveData after Notify");
            Assert.AreEqual(3, entry.count);
        }

        [UnityTest]
        public IEnumerator CompletedAchievement_FlushedToSaveAchievements()
        {
            yield return SpawnServices();

            AchievementService.Instance.NotifyCraft("hydrogen", 5);

            Assert.IsTrue(
                SaveManager.Instance.Current.achievements.Contains("test_craft_5"),
                "Completed achievement id should appear in SaveData.achievements");
        }
    }
}
