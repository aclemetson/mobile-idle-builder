using System;
using System.Collections;
using Unity.Services.Authentication;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// MonoBehaviour coordinator for local + cloud save.
    /// Strategy: local is source of truth while offline. On reconnect compare server
    /// timestamp — take newest, keep the other as a 24-hour backup.
    /// </summary>
    public class SaveManager : SingletonMonoBehaviour<SaveManager>
    {
        protected override bool PersistAcrossScenes => true;

        [Header("Config")]
        [SerializeField] GameConfigSO gameConfig;
        [SerializeField] float autoSaveIntervalSeconds = 60f;

#if UNITY_EDITOR
        [Header("Debug (Editor only)")]
        [Tooltip("When checked, wipes unlocked research, recipes, and the current run on each Play so the tutorial restarts from step 1.")]
        [SerializeField] bool _resetTutorialOnPlay;
#endif

        LocalSaveService _local;
        ICloudSaveService _cloud;
        SaveData _current;

        public SaveData Current   => _current;
        public bool     IsNewGame { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _local = new LocalSaveService();
            SaveData loaded = _local.Load();
            IsNewGame = loaded == null;
            _current  = loaded ?? new SaveData { playerId = GeneratePlayerId() };

            if (IsNewGame)
                GameLogger.Info("[Save] No save file found — starting fresh.");
            else
                GameLogger.Info($"[Save] Loaded save — tutorial step: '{_current.tutorial.currentStepId}'  " +
                          $"active={_current.tutorial.isActive}  prestiged={_current.tutorial.hasCompletedFirstRun}");

#if UNITY_EDITOR
            if (_resetTutorialOnPlay)
            {
                _current.unlockedResearch = new();
                _current.unlockedRecipes  = new();
                _current.currentRun       = new();
                _current.tutorial         = new();
                IsNewGame = true; // treat as fresh install so baked starting items are preserved
                GameLogger.Debug("[Save] _resetTutorialOnPlay active — save/load test will NOT work while this is checked.");
            }
#endif

            _cloud = new UGSCloudSaveService();
        }

        IEnumerator Start()
        {
            // Cloud reconciliation happens asynchronously after local is already ready
            yield return ReconcileWithCloud();
            StartCoroutine(AutoSaveLoop());
        }

        void OnApplicationPause(bool paused)
        {
#if UNITY_EDITOR
            if (GameBootstrap.TestModeEnabled) return;
#endif
            if (paused) SaveLocal();
        }

        void OnApplicationQuit()
        {
#if UNITY_EDITOR
            if (GameBootstrap.TestModeEnabled) return;
#endif
            SaveLocal();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Reconciles the already-loaded local save with cloud if available.</summary>
        private IEnumerator ReconcileWithCloud()
        {
            if (_cloud == null) yield break;

            // Init UGS (anonymous sign-in). IsAvailable is false until this completes.
            var initTask = _cloud.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);

            if (!_cloud.IsAvailable) yield break;

            // Sync local playerId to UGS identity for cross-device consistency.
            _current.playerId = AuthenticationService.Instance.PlayerId;

            var fetchTask = _cloud.FetchAsync(_current.playerId);
            yield return new WaitUntil(() => fetchTask.IsCompleted);

            if (fetchTask.Result != null && IsCloudNewer(fetchTask.Result))
            {
                _local.SaveWithBackup(_current);
                _current = fetchTask.Result;
                _local.Save(_current);
                GameLogger.Info("[SaveManager] Reconciled with cloud (cloud was newer).");
            }
        }

        public void SaveLocal(bool skipGridFlush = false, bool skipECSFlush = false)
        {
            if (!skipECSFlush)
                ECSLoadBridge.Instance?.FlushToSave();
            if (!skipGridFlush)
                GridSaveService.Instance?.FlushToSave();
            GameLogger.Debug($"[Save] Writing to disk — tutorial step: '{_current.tutorial.currentStepId}'  " +
                      $"active={_current.tutorial.isActive}  inventory items: {_current.currentRun.inventoryKeys?.Count ?? 0}");
            _local.SaveWithBackup(_current);
        }

        public IEnumerator SaveToCloud()
        {
            if (_cloud == null || !_cloud.IsAvailable) { SaveLocal(); yield break; }

            SaveLocal();
            var task = _cloud.PushAsync(_current);
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>Deletes the cloud save key. onDone receives true on success, false if unavailable or faulted.</summary>
        public IEnumerator DeleteCloudSave(Action<bool> onDone = null)
        {
            if (_cloud == null || !_cloud.IsAvailable)
            {
                onDone?.Invoke(false);
                yield break;
            }
            var task = _cloud.DeleteAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            onDone?.Invoke(!task.IsFaulted);
        }

        /// <summary>Resets in-memory save state to a blank new game. Call before scene reload when wiping saves.</summary>
        public void ResetToFreshSave()
        {
            _current  = new SaveData { playerId = GeneratePlayerId() };
            IsNewGame = true;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        IEnumerator AutoSaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(autoSaveIntervalSeconds);
#if UNITY_EDITOR
                if (GameBootstrap.TestModeEnabled) continue;
#endif
                yield return SaveToCloud();
            }
        }

        bool IsCloudNewer(SaveData cloud)
        {
            if (string.IsNullOrEmpty(_current.lastSaved)) return true;
            if (string.IsNullOrEmpty(cloud.lastSaved))    return false;
            return DateTime.Parse(cloud.lastSaved) > DateTime.Parse(_current.lastSaved);
        }

        static string GeneratePlayerId() => $"uid_{Guid.NewGuid():N}";
    }
}
