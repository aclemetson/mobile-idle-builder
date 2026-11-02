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
            if (_saveManagerGO != null)
            {
                Object.DestroyImmediate(_saveManagerGO);
                _saveManagerGO = null;
            }

            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
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

        // ── LocalSaveService crash safety (#280) ─────────────────────────────

        string TmpPath => _savePath + ".tmp";

        void WriteBackup(int prestigeCount) =>
            File.WriteAllText(_bakPath, JsonUtility.ToJson(new SaveData { prestigeCount = prestigeCount }));

        [Test]
        public void LocalSaveService_Save_LeavesNoTempFile()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData { prestigeCount = 1 });
            svc.SaveWithBackup(new SaveData { prestigeCount = 2 });

            Assert.IsFalse(File.Exists(TmpPath), "The .tmp file must be swapped into place, not left behind");
            Assert.AreEqual(2, svc.Load().prestigeCount);
        }

        [Test]
        public void LocalSaveService_Save_StampsLastSavedAndItRoundTrips()
        {
            var svc  = new LocalSaveService();
            var data = new SaveData { playerId = "p" };
            svc.Save(data);

            Assert.IsFalse(string.IsNullOrEmpty(data.lastSaved), "Save should stamp lastSaved on the object");
            Assert.AreEqual(data.lastSaved, svc.Load().lastSaved, "The spliced stamp must deserialize intact");
        }

        [Test]
        public void LocalSaveService_Serialize_MatchesPlainJsonUtilityOutput()
        {
            var data = new SaveData { playerId = "p", prestigeCount = 7, paidCurrency = 42 };
            string stamp   = "2026-10-31T12:34:56.7890000Z";
            string spliced = LocalSaveService.Serialize(data, stamp, out _);

            Assert.AreEqual(stamp, data.lastSaved);
            Assert.AreEqual(JsonUtility.ToJson(data, prettyPrint: false), spliced,
                "Splicing the stamp into the blank-stamp JSON must equal a direct serialization");
        }

        [Test]
        public void LocalSaveService_ContentHash_IgnoresTimestamp_ButTracksContent()
        {
            var a = new SaveData { playerId = "p", prestigeCount = 1 };
            LocalSaveService.Serialize(a, "2026-01-01T00:00:00.0000000Z", out ulong h1);
            LocalSaveService.Serialize(a, "2026-06-01T00:00:00.0000000Z", out ulong h2);
            a.prestigeCount = 2;
            LocalSaveService.Serialize(a, "2026-06-01T00:00:00.0000000Z", out ulong h3);

            Assert.AreEqual(h1, h2, "Only lastSaved changed, so the content hash must not");
            Assert.AreNotEqual(h1, h3, "Changed game state must change the content hash");
        }

        [Test]
        public void LocalSaveService_Load_CorruptMain_FallsBackToBackup()
        {
            File.WriteAllText(_savePath, "{\"prestigeCount\":9,\"playerId\":\"trunc");   // truncated mid-write
            WriteBackup(prestigeCount: 4);

            var svc    = new LocalSaveService();
            var loaded = svc.Load();

            Assert.IsNotNull(loaded, "A good .bak must be used when save.json is corrupt");
            Assert.AreEqual(4, loaded.prestigeCount);
            Assert.AreEqual(LocalSaveService.LoadSource.Backup, svc.LastLoadSource);
        }

        [Test]
        public void LocalSaveService_Load_EmptyMain_FallsBackToBackup()
        {
            File.WriteAllText(_savePath, string.Empty);
            WriteBackup(prestigeCount: 5);

            var svc = new LocalSaveService();
            Assert.AreEqual(5, svc.Load().prestigeCount);
            Assert.AreEqual(LocalSaveService.LoadSource.Backup, svc.LastLoadSource);
        }

        [Test]
        public void LocalSaveService_Load_MainMissing_RecoversCompleteTempFile()
        {
            // A write that died between deleting save.json and moving the .tmp into place.
            File.WriteAllText(TmpPath, JsonUtility.ToJson(new SaveData { prestigeCount = 8 }));
            WriteBackup(prestigeCount: 3);

            var svc = new LocalSaveService();
            Assert.AreEqual(8, svc.Load().prestigeCount, "The complete .tmp is newer than the .bak");
            Assert.AreEqual(LocalSaveService.LoadSource.Temp, svc.LastLoadSource);
        }

        [Test]
        public void LocalSaveService_Load_ValidMainWinsOverStaleTemp()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData { prestigeCount = 2 });
            File.WriteAllText(TmpPath, JsonUtility.ToJson(new SaveData { prestigeCount = 99 }));

            Assert.AreEqual(2, svc.Load().prestigeCount);
            Assert.AreEqual(LocalSaveService.LoadSource.Main, svc.LastLoadSource);
        }

        [Test]
        public void LocalSaveService_Load_PartialTemp_SkippedForBackup()
        {
            File.WriteAllText(_savePath, "garbage");
            File.WriteAllText(TmpPath, "{\"prestigeCount\":");
            WriteBackup(prestigeCount: 6);

            var svc = new LocalSaveService();
            Assert.AreEqual(6, svc.Load().prestigeCount);
        }

        [Test]
        public void LocalSaveService_Load_EverythingCorrupt_ReturnsNull()
        {
            File.WriteAllText(_savePath, "garbage");
            File.WriteAllText(_bakPath,  "also garbage");

            bool prev = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // the "all unreadable" error log
            try
            {
                var svc = new LocalSaveService();
                Assert.IsNull(svc.Load(), "With no readable copy the game starts fresh");
                Assert.AreEqual(LocalSaveService.LoadSource.None, svc.LastLoadSource);
            }
            finally { UnityEngine.TestTools.LogAssert.ignoreFailingMessages = prev; }
        }

        [Test]
        public void LocalSaveService_SaveWithBackup_AfterCorruptLoad_KeepsGoodBackup()
        {
            File.WriteAllText(_savePath, "garbage");
            WriteBackup(prestigeCount: 4);

            var svc = new LocalSaveService();
            var loaded = svc.Load();                 // recovered from .bak
            loaded.prestigeCount = 5;
            svc.SaveWithBackup(loaded);              // must NOT copy the corrupt save.json over .bak

            var bak = JsonUtility.FromJson<SaveData>(File.ReadAllText(_bakPath));
            Assert.AreEqual(4, bak.prestigeCount, "The good backup must survive the first save after recovery");
            Assert.AreEqual(5, svc.Load().prestigeCount);

            svc.SaveWithBackup(new SaveData { prestigeCount = 6 });   // save.json is good again: rotate as normal
            bak = JsonUtility.FromJson<SaveData>(File.ReadAllText(_bakPath));
            Assert.AreEqual(5, bak.prestigeCount, "Once save.json is good again, backups rotate normally");
        }

        [Test]
        public void LocalSaveService_Delete_AlsoRemovesBackupAndTemp()
        {
            var svc = new LocalSaveService();
            svc.Save(new SaveData());
            svc.SaveWithBackup(new SaveData());
            File.WriteAllText(TmpPath, "{}");

            svc.Delete();

            Assert.IsFalse(File.Exists(_savePath));
            Assert.IsFalse(File.Exists(_bakPath));
            Assert.IsFalse(File.Exists(TmpPath));
        }

        [Test]
        public void SaveManager_Awake_CorruptSave_LoadsBackup_NotNewGame()
        {
            File.WriteAllText(_savePath, "{\"playerId\":\"x\",\"prest");
            File.WriteAllText(_bakPath, JsonUtility.ToJson(new SaveData { playerId = "bak_player", prestigeCount = 3 }));

            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            Assert.IsFalse(SaveManager.Instance.IsNewGame, "A recovered backup is a returning player, not a new game");
            Assert.AreEqual("bak_player", SaveManager.Instance.Current.playerId);
            Assert.AreEqual(3, SaveManager.Instance.Current.prestigeCount);
        }

        // ── SaveManager pure helpers (#280) ──────────────────────────────────

        [TestCase("2026-10-31T12:00:01.0000000Z", "2026-10-31T12:00:00.0000000Z", true)]
        [TestCase("2026-10-31T12:00:00.0000000Z", "2026-10-31T12:00:01.0000000Z", false)]
        [TestCase("2026-10-31T12:00:00.0000000Z", "2026-10-31T12:00:00.0000000Z", false)]
        [TestCase("2026-10-31T12:00:00.0000000Z", "",                             true)]   // empty local always loses
        [TestCase("",                             "2026-10-31T12:00:00.0000000Z", false)]
        [TestCase("not a date",                   "2026-10-31T12:00:00.0000000Z", false)]  // no throw
        [TestCase("2026-10-31T14:00:00.0000000+02:00", "2026-10-31T11:59:59.0000000Z", true)] // offsets normalised
        public void SaveManager_IsNewerStamp(string candidate, string current, bool expected) =>
            Assert.AreEqual(expected, SaveManager.IsNewerStamp(candidate, current));

        [Test]
        public void SaveManager_IsNewerStamp_IgnoresDeviceCulture()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
                Assert.IsTrue(SaveManager.IsNewerStamp("2026-10-31T12:00:01.0000000Z", "2026-10-31T12:00:00.0000000Z"));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        [TestCase(1UL, 1UL, 1000f, 0f, 300f, false, TestName = "Unchanged content never pushes")]
        [TestCase(2UL, 1UL, 100f,  0f, 300f, false, TestName = "Changed but inside the interval")]
        [TestCase(2UL, 1UL, 300f,  0f, 300f, true,  TestName = "Changed and interval elapsed")]
        [TestCase(0UL, 0UL, 300f,  0f, 300f, true,  TestName = "Unknown hash counts as changed")]
        public void SaveManager_ShouldAutoPush(ulong hash, ulong lastPushed, float now, float lastStart,
                                               float interval, bool expected) =>
            Assert.AreEqual(expected, SaveManager.ShouldAutoPush(hash, lastPushed, now, lastStart, interval));

        [Test]
        public void SaveManager_ShouldAutoPush_FirstPushIsNeverThrottled() =>
            Assert.IsTrue(SaveManager.ShouldAutoPush(5UL, 0UL, 0f, float.NegativeInfinity, 300f));

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
