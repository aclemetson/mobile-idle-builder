using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Round-trip and legacy-load coverage for the manager SaveData fields
    /// (hiredManagers + managerAssignments). Mirrors DailyEventSaveTests setup/teardown:
    /// backs up and restores the developer's real save file so tests never corrupt it.
    /// </summary>
    public class ManagerSaveTests
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
        public void RoundTrip_PreservesManagerFields()
        {
            var svc  = new LocalSaveService();
            var data = new SaveData();
            data.hiredManagers.Add("mgr_tinker");
            data.hiredManagers.Add("mgr_packrat");
            data.managerAssignments.Add(new ManagerAssignmentEntry
            {
                managerId = "mgr_tinker",
                siteIndex = 1,
                buildingPosKey = 3 * 10000 + 7,
            });

            svc.Save(data);
            var loaded = svc.Load();

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(new[] { "mgr_tinker", "mgr_packrat" }, loaded.hiredManagers);
            Assert.AreEqual(1, loaded.managerAssignments.Count);
            Assert.AreEqual("mgr_tinker", loaded.managerAssignments[0].managerId);
            Assert.AreEqual(1, loaded.managerAssignments[0].siteIndex);
            Assert.AreEqual(3 * 10000 + 7, loaded.managerAssignments[0].buildingPosKey);
        }

        [Test]
        public void LegacyLoad_MissingManagerFields_DefaultToEmpty()
        {
            // A pre-feature save with neither manager key present.
            const string legacyJson =
                "{\"playerId\":\"legacy\",\"prestigeCount\":2,\"paidCurrency\":100}";

            var loaded = JsonUtility.FromJson<SaveData>(legacyJson);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("legacy", loaded.playerId);
            Assert.IsNotNull(loaded.hiredManagers);
            Assert.IsEmpty(loaded.hiredManagers);
            Assert.IsNotNull(loaded.managerAssignments);
            Assert.IsEmpty(loaded.managerAssignments);
        }
    }
}
