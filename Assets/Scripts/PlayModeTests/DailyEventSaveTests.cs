using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Round-trip and legacy-load coverage for the daily-event SaveData fields
    /// (login streak + rotating challenges). Mirrors SaveSystemTests setup/teardown:
    /// backs up and restores the developer's real save file so tests never corrupt it.
    /// </summary>
    public class DailyEventSaveTests
    {
        string _savePath;
        string _bakPath;
        string _saveBackup;
        string _bakBackup;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _bakPath    = _savePath + ".bak";
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _bakBackup  = File.Exists(_bakPath)  ? File.ReadAllText(_bakPath)  : null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_bakPath))  File.Delete(_bakPath);

            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            if (_bakBackup  != null) File.WriteAllText(_bakPath,  _bakBackup);
        }

        [Test]
        public void RoundTrip_PreservesDailyEventFields()
        {
            var svc  = new LocalSaveService();
            var data = new SaveData
            {
                loginStreakIndex      = 5,
                lastLoginRewardUtc    = "2026-06-11T00:00:00.0000000Z",
                dailyChallengeResetUtc = "2026-06-12T00:00:00.0000000Z"
            };
            data.dailyChallengeIds.Add("dc_craft_50");
            data.dailyChallengeIds.Add("dc_place_3");
            data.dailyChallengeIds.Add("dc_login");
            data.dailyChallengeProgress.Add(new AchievementProgressEntry { id = "dc_craft_50", count = 30 });
            data.dailyChallengesClaimed.Add("dc_login");

            svc.Save(data);
            var loaded = svc.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(5, loaded.loginStreakIndex);
            Assert.AreEqual("2026-06-11T00:00:00.0000000Z", loaded.lastLoginRewardUtc);
            Assert.AreEqual("2026-06-12T00:00:00.0000000Z", loaded.dailyChallengeResetUtc);
            CollectionAssert.AreEqual(
                new[] { "dc_craft_50", "dc_place_3", "dc_login" }, loaded.dailyChallengeIds);
            Assert.AreEqual(1, loaded.dailyChallengeProgress.Count);
            Assert.AreEqual("dc_craft_50", loaded.dailyChallengeProgress[0].id);
            Assert.AreEqual(30,            loaded.dailyChallengeProgress[0].count);
            CollectionAssert.AreEqual(new[] { "dc_login" }, loaded.dailyChallengesClaimed);
        }

        [Test]
        public void LegacyLoad_MissingDailyEventFields_DefaultToEmpty()
        {
            // A pre-feature save with none of the daily-event keys present.
            const string legacyJson =
                "{\"playerId\":\"legacy\",\"prestigeCount\":2,\"paidCurrency\":100}";

            var loaded = JsonUtility.FromJson<SaveData>(legacyJson);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("legacy", loaded.playerId);
            Assert.AreEqual(0, loaded.loginStreakIndex, "streak index must default to 0 (calendar never used)");
            Assert.IsTrue(string.IsNullOrEmpty(loaded.lastLoginRewardUtc));
            Assert.IsTrue(string.IsNullOrEmpty(loaded.dailyChallengeResetUtc));
            Assert.IsNotNull(loaded.dailyChallengeIds);
            Assert.IsEmpty(loaded.dailyChallengeIds);
            Assert.IsNotNull(loaded.dailyChallengeProgress);
            Assert.IsEmpty(loaded.dailyChallengeProgress);
            Assert.IsNotNull(loaded.dailyChallengesClaimed);
            Assert.IsEmpty(loaded.dailyChallengesClaimed);
        }
    }
}
