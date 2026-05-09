using System;
using System.Collections;
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

            _cloud = gameConfig != null
                ? new CloudSaveService(gameConfig.apiBaseUrl)
                : null;
        }

        IEnumerator Start()
        {
            // Cloud reconciliation happens asynchronously after local is already ready
            yield return ReconcileWithCloud();
            StartCoroutine(AutoSaveLoop());
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveLocal();
        }

        void OnApplicationQuit() => SaveLocal();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Reconciles the already-loaded local save with cloud if available.</summary>
        private IEnumerator ReconcileWithCloud()
        {
            if (_cloud == null || !_cloud.IsAvailable) yield break;

            var task = _cloud.FetchAsync(_current.playerId);
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Result != null && IsCloudNewer(task.Result))
            {
                _local.SaveWithBackup(_current);
                _current = task.Result;
                _local.Save(_current);
                GameLogger.Info("[SaveManager] Reconciled with cloud (cloud was newer).");
            }
        }

        public void SaveLocal()
        {
            ECSLoadBridge.Instance?.FlushToSave();
            GridSaveService.Instance?.FlushToSave();
            GameLogger.Debug($"[Save] Writing to disk — tutorial step: '{_current.tutorial.currentStepId}'  " +
                      $"active={_current.tutorial.isActive}  inventory items: {_current.currentRun.inventory?.Count ?? 0}");
            _local.SaveWithBackup(_current);
        }

        public IEnumerator SaveToCloud()
        {
            if (_cloud == null || !_cloud.IsAvailable) { SaveLocal(); yield break; }

            SaveLocal();
            var task = _cloud.PushAsync(_current);
            yield return new WaitUntil(() => task.IsCompleted);
        }

        // ── Internal ──────────────────────────────────────────────────────────

        IEnumerator AutoSaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(autoSaveIntervalSeconds);
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
