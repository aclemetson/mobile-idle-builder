using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for megastructure persistence: SaveData round-trip, legacy (pre-feature) load
    /// defaults, and survival across a prestige reset (the fields live in the persistent section, never
    /// in currentRun).
    /// </summary>
    public class MegastructureSaveTests
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
        public void RoundTrip_PreservesMegastructureFields()
        {
            var svc  = new LocalSaveService();
            var data = new SaveData
            {
                megastructureStage         = 3,
                megastructureContribKeys   = new List<string> { "49", "50" },
                megastructureContribValues = new List<int>    { 12, 4 }
            };

            svc.Save(data);
            var loaded = svc.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(3, loaded.megastructureStage);
            CollectionAssert.AreEqual(new[] { "49", "50" }, loaded.megastructureContribKeys);
            CollectionAssert.AreEqual(new[] { 12, 4 },       loaded.megastructureContribValues);
        }

        [Test]
        public void LegacyLoad_DefaultsToStageZeroAndEmptyContributions()
        {
            // A pre-feature save with none of the megastructure fields present.
            const string legacyJson = "{\"prestigeCount\":2,\"prestigeCurrency\":500}";
            File.WriteAllText(_savePath, legacyJson);

            var loaded = new LocalSaveService().Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.prestigeCount, "Sanity: legacy field still loads");
            Assert.AreEqual(0, loaded.megastructureStage, "Missing field defaults to 0");
            Assert.IsNotNull(loaded.megastructureContribKeys);
            Assert.AreEqual(0, loaded.megastructureContribKeys.Count);
            Assert.IsNotNull(loaded.megastructureContribValues);
            Assert.AreEqual(0, loaded.megastructureContribValues.Count);
        }

        [Test]
        public void PrestigeReset_PreservesMegastructureProgress()
        {
            var save = new SaveData
            {
                megastructureStage         = 2,
                megastructureContribKeys   = new List<string> { "49" },
                megastructureContribValues = new List<int>    { 7 }
            };
            // Give the run some state so the reset has something to wipe.
            save.currentRun.baseCurrency = 99999L;
            save.currentRun.inventoryKeys.Add("49");
            save.currentRun.inventoryValues.Add(50);

            // Mirror the prestige reset performed by PrestigeSaveWatcher.OnPrestige.
            save.currentRun = new CurrentRunData();
            PrestigeSaveWatcher.ResetSitesForPrestige(save);
            save.unlockedResearch = new List<string>();

            Assert.AreEqual(2, save.megastructureStage, "Completed stages survive prestige");
            CollectionAssert.AreEqual(new[] { "49" }, save.megastructureContribKeys,
                "Partial contributions survive prestige");
            CollectionAssert.AreEqual(new[] { 7 }, save.megastructureContribValues);
            Assert.AreEqual(0L, save.currentRun.baseCurrency, "Sanity: run state was actually reset");
        }
    }
}
