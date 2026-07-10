using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for SaveManager (MonoBehaviour lifecycle) and LocalSaveService (file I/O).
    ///
    /// Setup/teardown backs up and restores the real save file so running tests never
    /// corrupts a developer's active save.
    /// </summary>
    public class SaveSystemTests
    {
        string _savePath;
        string _bakPath;
        string _saveBackup;
        string _bakBackup;

        GameObject _saveManagerGO;

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
            if (_saveManagerGO != null)
            {
                Object.DestroyImmediate(_saveManagerGO);
                _saveManagerGO = null;
            }

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_bakPath))  File.Delete(_bakPath);

            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            if (_bakBackup  != null) File.WriteAllText(_bakPath,  _bakBackup);
        }

        // ── SaveManager ──────────────────────────────────────────────────────

        [Test]
        public void IsNewGame_TrueWhenNoSaveFileExists()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.IsTrue(SaveManager.Instance.IsNewGame);
        }

        [Test]
        public void IsNewGame_FalseWhenSaveFileExists()
        {
            // First session: create and save
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }
            SaveManager.Instance.SaveLocal();
            Object.DestroyImmediate(_saveManagerGO);
            _saveManagerGO = null;

            // Second session: should detect the existing file
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.IsFalse(SaveManager.Instance.IsNewGame);
        }

        [Test]
        public void SaveLocal_PersistsPrestigeCount()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            SaveManager.Instance.Current.prestigeCount = 7;
            SaveManager.Instance.SaveLocal();
            Object.DestroyImmediate(_saveManagerGO);
            _saveManagerGO = null;

            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.AreEqual(7, SaveManager.Instance.Current.prestigeCount);
        }

        [Test]
        public void SaveLocal_PersistsPrestigeCurrency()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            SaveManager.Instance.Current.prestigeCurrency = 9999L;
            SaveManager.Instance.SaveLocal();
            Object.DestroyImmediate(_saveManagerGO);
            _saveManagerGO = null;

            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.AreEqual(9999L, SaveManager.Instance.Current.prestigeCurrency);
        }

        [Test]
        public void SaveLocal_PersistsTutorialState()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            SaveManager.Instance.Current.tutorial.currentStepId       = "step_build_first";
            SaveManager.Instance.Current.tutorial.hasCompletedFirstRun = true;
            SaveManager.Instance.SaveLocal();
            Object.DestroyImmediate(_saveManagerGO);
            _saveManagerGO = null;

            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.AreEqual("step_build_first", SaveManager.Instance.Current.tutorial.currentStepId);
            Assert.IsTrue(SaveManager.Instance.Current.tutorial.hasCompletedFirstRun);
        }

        // ── LocalSaveService ─────────────────────────────────────────────────

        [Test]
        public void LocalSaveService_Exists_FalseWhenNoFile()
        {
            var svc = new LocalSaveService();
            Assert.IsFalse(svc.Exists());
        }

        [Test]
        public void LocalSaveService_Exists_TrueAfterSave()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData());
            Assert.IsTrue(svc.Exists());
        }

        [Test]
        public void LocalSaveService_Load_ReturnsNullWhenNoFile()
        {
            var svc = new LocalSaveService();
            Assert.IsNull(svc.Load());
        }

        [Test]
        public void LocalSaveService_RoundTrip_PreservesFields()
        {
            var svc  = new LocalSaveService();
            var data = new SaveData
            {
                prestigeCount    = 3,
                prestigeCurrency = 1500L,
                playerId         = "test_player_abc"
            };

            svc.Save(data);
            var loaded = svc.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(3,                 loaded.prestigeCount);
            Assert.AreEqual(1500L,             loaded.prestigeCurrency);
            Assert.AreEqual("test_player_abc", loaded.playerId);
        }

        [Test]
        public void LocalSaveService_SaveWithBackup_CreatesBackupFile()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData { prestigeCount = 1 });
            svc.SaveWithBackup(new SaveData { prestigeCount = 2 });

            Assert.IsTrue(File.Exists(_bakPath), ".bak file should be created by SaveWithBackup");

            var main    = svc.Load();
            var bakData = JsonUtility.FromJson<SaveData>(File.ReadAllText(_bakPath));
            Assert.AreEqual(2, main.prestigeCount,    "Main file should have updated data");
            Assert.AreEqual(1, bakData.prestigeCount, "Backup file should have previous data");
        }

        [Test]
        public void LocalSaveService_Delete_RemovesFile()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData());
            svc.Delete();
            Assert.IsFalse(svc.Exists());
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
