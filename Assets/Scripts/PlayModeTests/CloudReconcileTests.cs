using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Edit Mode test for the cloud-vs-ECS load ordering fix. When the cloud save is newer,
    /// SaveManager must replace Current with it AND flip CloudReconcileDone — the flag
    /// ECSLoadBridge waits on before copying Current into ECS. Without that wait, ECS would
    /// be seeded from stale local data and the next flush would clobber the newer cloud save.
    ///
    /// Drives the reconcile coroutine manually (this assembly runs in edit mode, so the
    /// MonoBehaviour Start lifecycle does not fire) and uses a fake ICloudSaveService so no
    /// live UGS auth / network is required.
    /// </summary>
    public class CloudReconcileTests
    {
        string _savePath, _bakPath, _saveBackup, _bakBackup, _markerBackup;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _bakPath    = _savePath + ".bak";
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _bakBackup  = File.Exists(_bakPath)  ? File.ReadAllText(_bakPath)  : null;
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
            PlayerPrefs.DeleteKey(SaveManager.k_WipePending);
            _markerBackup = PlayerPrefs.GetString(SaveSync.MarkerKey, null);
            PlayerPrefs.DeleteKey(SaveSync.MarkerKey);
            SaveManager.SaveChoiceOverrideForTests = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) { UnityEngine.Object.DestroyImmediate(_go); _go = null; }
            SaveManager.SaveChoiceOverrideForTests = null;
            if (_markerBackup != null) PlayerPrefs.SetString(SaveSync.MarkerKey, _markerBackup);
            else                       PlayerPrefs.DeleteKey(SaveSync.MarkerKey);
            foreach (var f in new[] { SaveManager.ReplacedLocalFile, SaveManager.ReplacedCloudFile })
            {
                string path = Path.Combine(LocalSaveService.SaveDirectory, f);
                if (File.Exists(path)) File.Delete(path);
            }
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            if (_bakBackup  != null) File.WriteAllText(_bakPath,  _bakBackup);
        }

        class FakeCloud : ICloudSaveService
        {
            public SaveData ToReturn;
            public bool Available = true;
            public bool FailPushes;
            public int PushCount;
            public SaveData LastPushed;
            public bool IsAvailable => Available;
            public Task InitializeAsync()                     => Task.CompletedTask;
            public Task<SaveData> FetchAsync(string playerId) => Task.FromResult(ToReturn);
            public Task<bool> PushAsync(SaveData data)        { PushCount++; LastPushed = data; return Task.FromResult(Available && !FailPushes); }
            public Task DeleteAsync()                         => Task.CompletedTask;

            public string PlayerId = "player-google";
            public bool Switched;
            public CloudAccountKind AccountKind => CloudAccountKind.Google;
            public string AccountEmail => null;
            public string AccountPlayerId => PlayerId;
            public string LastSignInError => null;
            public bool AccountSwitched => Switched;
            public Task<GoogleSignInOutcome> SignInWithGoogleAsync(GoogleAuthMode mode) =>
                Task.FromResult(GoogleSignInOutcome.AlreadyGoogle);
        }

        [Test]
        public void Reconcile_CloudNewer_ReplacesCurrentAndSetsFlag()
        {
            _go = new GameObject("SaveManager");
            var sm = _go.AddComponent<SaveManager>();
            RunAwake(sm);   // fresh new-game Current (no save file); creates the real cloud service

            // Cloud copy is "newer" (local lastSaved is empty for a new game) and carries a
            // different tutorial step. Inject the fake before reconciling.
            var cloudSave = new SaveData { lastSaved = DateTime.UtcNow.ToString("o") };
            cloudSave.tutorial.currentStepId = "cloud_step";
            sm.SetCloudServiceForTesting(new FakeCloud { ToReturn = cloudSave });

            Pump(sm.InitialCloudReconcile());

            Assert.IsTrue(sm.CloudReconcileDone, "InitialCloudReconcile should set the flag");
            Assert.AreEqual("cloud_step", SaveManager.Instance.Current.tutorial.currentStepId,
                "Newer cloud save should replace local Current before ECS reads it");
        }

        [Test]
        public void OnApplicationPause_Backgrounded_PushesToCloud()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);

            InvokePrivate(sm, "OnApplicationPause", true);

            Assert.AreEqual(1, cloud.PushCount, "Backgrounding should push the local save to cloud");
            Assert.AreSame(sm.Current, cloud.LastPushed, "Should push the current SaveData");
        }

        [Test]
        public void OnApplicationFocus_Lost_PushesToCloud()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            Pump(sm.InitialCloudReconcile());   // focus-loss pushes only once the save is final
            int before = cloud.PushCount;

            InvokePrivate(sm, "OnApplicationFocus", false);

            Assert.AreEqual(before + 1, cloud.PushCount, "Losing focus pushes while the app can still finish the upload");
        }

        [Test]
        public void OnApplicationFocus_Gained_DoesNotPush()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            Pump(sm.InitialCloudReconcile());
            int before = cloud.PushCount;

            InvokePrivate(sm, "OnApplicationFocus", true);

            Assert.AreEqual(before, cloud.PushCount);
        }

        [Test]
        public void FocusLossThenPause_SameState_PushesOnce()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            sm.RealtimeNow = () => 0f;
            Pump(sm.InitialCloudReconcile());
            sm.Current.prestigeCount = 3;       // state the cloud has not seen
            int before = cloud.PushCount;

            InvokePrivate(sm, "OnApplicationFocus", false);
            InvokePrivate(sm, "OnApplicationPause", true);   // Android follows focus loss with pause

            Assert.AreEqual(before + 1, cloud.PushCount, "the pause must not upload the state focus loss just pushed");
        }

        [Test]
        public void OnApplicationFocus_LostBeforeReconcile_DoesNotPush()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);

            InvokePrivate(sm, "OnApplicationFocus", false);

            Assert.AreEqual(0, cloud.PushCount, "nothing final to upload while still loading");
        }

        [Test]
        public void OnApplicationQuit_PushesToCloud()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);

            InvokePrivate(sm, "OnApplicationQuit");

            Assert.AreEqual(1, cloud.PushCount, "Quitting should push the local save to cloud");
        }

        [Test]
        public void OnApplicationQuit_CloudUnavailable_DoesNotPush()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud { Available = false };
            sm.SetCloudServiceForTesting(cloud);

            InvokePrivate(sm, "OnApplicationQuit");

            Assert.AreEqual(0, cloud.PushCount, "Must not push when cloud is unavailable (local stays source of truth)");
        }

        // ── Cross-device saves ────────────────────────────────────────────────

        static SaveData Stamped(int prestige, string stamp) =>
            new SaveData { prestigeCount = prestige, lastSaved = stamp };

        /// <summary>A SaveManager that loaded an existing (non-new) local save with this prestige count.</summary>
        SaveManager ReturningSaveManager(int localPrestige)
        {
            new LocalSaveService().Save(new SaveData { prestigeCount = localPrestige });
            var sm = NewSaveManager();
            Assert.IsFalse(sm.IsNewGame);
            return sm;
        }

        [Test]
        public void FreshInstall_TakesTheCloudSave_AndIsNoLongerANewGame()
        {
            var sm = NewSaveManager();
            Assert.IsTrue(sm.IsNewGame);
            sm.SetCloudServiceForTesting(new FakeCloud { ToReturn = Stamped(4, "2026-11-14T10:00:00.0000000Z") });

            Pump(sm.InitialCloudReconcile());

            Assert.AreEqual(4, sm.Current.prestigeCount);
            Assert.IsFalse(sm.IsNewGame,
                "a pulled cloud save must be applied in full (currency, grid), not skipped as a new game");
            var marker = SaveSync.LoadMarker();
            Assert.AreEqual("player-google", marker.PlayerId);
            Assert.AreEqual("2026-11-14T10:00:00.0000000Z", marker.CloudStamp);
        }

        [Test]
        public void OnlyTheCloudChanged_TakesItWithoutAsking()
        {
            var sm = ReturningSaveManager(2);
            SaveSync.SaveMarker("player-google", "2026-11-14T09:00:00.0000000Z", LocalSaveService.ContentHash(sm.Current));
            bool asked = false;
            SaveManager.SaveChoiceOverrideForTests = (_, __) => { asked = true; return SaveSyncAction.KeepLocal; };
            sm.SetCloudServiceForTesting(new FakeCloud { ToReturn = Stamped(3, "2026-11-14T10:00:00.0000000Z") });

            Pump(sm.InitialCloudReconcile());

            Assert.IsFalse(asked);
            Assert.AreEqual(3, sm.Current.prestigeCount);
        }

        [Test]
        public void BothChanged_AsksThePlayer_KeepLocalPushesIt_AndKeepsTheCloudCopy()
        {
            var sm = ReturningSaveManager(5);
            SaveSync.SaveMarker("player-google", "2026-11-14T09:00:00.0000000Z", 12345UL);   // local differs
            SaveManager.SaveChoiceOverrideForTests = (_, __) => SaveSyncAction.KeepLocal;
            var cloud = new FakeCloud { ToReturn = Stamped(3, "2026-11-14T10:00:00.0000000Z") };
            sm.SetCloudServiceForTesting(cloud);

            Pump(sm.InitialCloudReconcile());

            Assert.AreEqual(5, sm.Current.prestigeCount);
            Assert.AreEqual(1, cloud.PushCount, "the kept save replaces the cloud copy right away");
            Assert.IsTrue(File.Exists(Path.Combine(LocalSaveService.SaveDirectory, SaveManager.ReplacedCloudFile)));
            Assert.AreEqual(sm.Current.lastSaved, SaveSync.LoadMarker().CloudStamp);
        }

        [Test]
        public void AccountSwitched_AsksThePlayer_UseCloudReplacesLocal_AndKeepsTheLocalCopy()
        {
            var sm = ReturningSaveManager(1);
            SaveData shownLocal = null, shownCloud = null;
            SaveManager.SaveChoiceOverrideForTests = (l, c) => { shownLocal = l; shownCloud = c; return SaveSyncAction.TakeCloud; };
            sm.SetCloudServiceForTesting(new FakeCloud { ToReturn = Stamped(6, "2026-11-14T10:00:00.0000000Z"), Switched = true });

            Pump(sm.InitialCloudReconcile());

            Assert.AreEqual(1, shownLocal.prestigeCount);
            Assert.AreEqual(6, shownCloud.prestigeCount);
            Assert.AreEqual(6, sm.Current.prestigeCount);
            Assert.IsTrue(File.Exists(Path.Combine(LocalSaveService.SaveDirectory, SaveManager.ReplacedLocalFile)));
            Assert.IsFalse(sm.AwaitingSaveChoice);
        }

        [Test]
        public void Push_RecordsTheSyncMarker()
        {
            var sm = NewSaveManager();
            sm.SetCloudServiceForTesting(new FakeCloud());
            sm.RealtimeNow = () => 0f;

            Pump(sm.SaveToCloud());

            var marker = SaveSync.LoadMarker();
            Assert.AreEqual("player-google", marker.PlayerId);
            Assert.AreEqual(sm.Current.lastSaved, marker.CloudStamp);
            Assert.AreEqual(LocalSaveService.ContentHash(sm.Current), marker.LocalHash);
        }

        [Test]
        public void FailedPush_PutsThePreviousMarkerBack()
        {
            var sm = NewSaveManager();
            SaveSync.SaveMarker("player-google", "old-stamp", 7UL);
            sm.SetCloudServiceForTesting(new FakeCloud { FailPushes = true });
            sm.RealtimeNow = () => 0f;

            Pump(sm.SaveToCloud());

            Assert.AreEqual("old-stamp", SaveSync.LoadMarker().CloudStamp);
        }

        // ── Autosave cloud throttling (#280) ──────────────────────────────────

        [Test]
        public void AutoSave_PushesOnlyWhenStateChangedAndIntervalElapsed()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            float now = 1000f;
            sm.RealtimeNow = () => now;

            Pump(sm.AutoSaveTick());                       // t=1000
            Assert.AreEqual(1, cloud.PushCount, "First autosave pushes");

            sm.Current.prestigeCount++;
            now += 30f;                                    // t=1030: 30s after the last push started
            Pump(sm.AutoSaveTick());
            Assert.AreEqual(1, cloud.PushCount, "Changed, but within the push interval: local save only");

            now += 30f;                                    // t=1060: 60s (the interval) after the last push started
            Pump(sm.AutoSaveTick());
            Assert.AreEqual(2, cloud.PushCount, "Changed and interval elapsed: push");

            now += 1000f;                                  // long after, but nothing changed since
            Pump(sm.AutoSaveTick());
            Assert.AreEqual(2, cloud.PushCount, "Nothing changed: no push even after the interval");
        }

        [Test]
        public void AutoSave_AlwaysWritesLocally()
        {
            var sm = NewSaveManager();
            sm.SetCloudServiceForTesting(new FakeCloud());
            sm.RealtimeNow = () => 0f;

            Pump(sm.AutoSaveTick());
            sm.Current.prestigeCount = 7;
            Pump(sm.AutoSaveTick());   // throttled for the cloud...

            var onDisk = JsonUtility.FromJson<SaveData>(File.ReadAllText(_savePath));
            Assert.AreEqual(7, onDisk.prestigeCount, "...but the local file is still written every autosave");
        }

        [Test]
        public void AutoSave_FailedPush_IsRetriedEvenIfUnchanged()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            float now = 0f;
            sm.RealtimeNow = () => now;

            cloud.Available = true;
            cloud.FailPushes = true;
            Pump(sm.AutoSaveTick());
            Assert.AreEqual(1, cloud.PushCount);

            cloud.FailPushes = false;
            now += 300f;
            Pump(sm.AutoSaveTick());
            Assert.AreEqual(2, cloud.PushCount, "A failed push must not mark the content as uploaded");
        }

        [Test]
        public void SaveToCloud_ExplicitSave_AlwaysPushes()
        {
            var sm    = NewSaveManager();
            var cloud = new FakeCloud();
            sm.SetCloudServiceForTesting(cloud);
            sm.RealtimeNow = () => 0f;

            Pump(sm.SaveToCloud());
            Pump(sm.SaveToCloud());

            Assert.AreEqual(2, cloud.PushCount, "Explicit saves (e.g. maintenance) are never throttled");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        SaveManager NewSaveManager()
        {
            _go = new GameObject("SaveManager");
            var sm = _go.AddComponent<SaveManager>();
            RunAwake(sm);   // fresh new-game Current (no save file); creates the real cloud service
            return sm;
        }

        static void InvokePrivate(MonoBehaviour mb, string name, params object[] args)
        {
            var m = mb.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(m, $"{name} not found on {mb.GetType().Name}");
            m.Invoke(mb, args);
        }


        // Synchronously drives a coroutine, recursing into nested yields. WaitUntil and other
        // CustomYieldInstructions implement IEnumerator (MoveNext returns keepWaiting), so they
        // are driven the same way. Safe here because the fake cloud's tasks complete
        // immediately, so no yield ever blocks.
        static void Pump(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 100000)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
            Assert.Less(guard, 100000, "Coroutine pump did not terminate");
        }

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
