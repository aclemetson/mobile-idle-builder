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
        string _savePath, _bakPath, _saveBackup, _bakBackup;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _bakPath    = _savePath + ".bak";
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _bakBackup  = File.Exists(_bakPath)  ? File.ReadAllText(_bakPath)  : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
            PlayerPrefs.DeleteKey(SaveManager.k_WipePending);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) { UnityEngine.Object.DestroyImmediate(_go); _go = null; }
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_bakPath))  File.Delete(_bakPath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            if (_bakBackup  != null) File.WriteAllText(_bakPath,  _bakBackup);
        }

        class FakeCloud : ICloudSaveService
        {
            public SaveData ToReturn;
            public bool IsAvailable => true;
            public Task InitializeAsync()                     => Task.CompletedTask;
            public Task<SaveData> FetchAsync(string playerId) => Task.FromResult(ToReturn);
            public Task PushAsync(SaveData data)              => Task.CompletedTask;
            public Task DeleteAsync()                         => Task.CompletedTask;
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

        // ── Helpers ───────────────────────────────────────────────────────────

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
