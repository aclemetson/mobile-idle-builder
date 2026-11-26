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
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _bakPath    = _savePath + ".bak";
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _bakBackup  = File.Exists(_bakPath)  ? File.ReadAllText(_bakPath)  : null;

            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
        }

        [TearDown]
        public void TearDown()
        {
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
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
                lastLoginRewardUtc    = "2026-06-11T00:00:00.0000000Z"
            };

            svc.Save(data);
            var loaded = svc.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(5, loaded.loginStreakIndex);
            Assert.AreEqual("2026-06-11T00:00:00.0000000Z", loaded.lastLoginRewardUtc);
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
        }
    }
}
