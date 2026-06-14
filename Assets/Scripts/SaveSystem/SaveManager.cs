using System;
using System.Collections;
using Unity.Services.Authentication;
using Unity.Services.Core;
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

        // PlayerPrefs key written on every background event so that a foreground
        // resume can calculate idle earnings even when the save file's lastSaved
        // field hasn't been flushed yet (e.g. early-termination by the OS).
        public const string BackgroundTimestampKey = "idle_backgrounded_utc";

        // Set by the editor tool (MobileIdleBuilder → Clear Save) to wipe the cloud
        // save on next play-mode boot, after UGS has initialized.
        public const string k_WipePending = "dev_wipe_cloud_on_boot";

        [Header("Config")]
        [SerializeField] GameConfigSO gameConfig;
        [SerializeField] float autoSaveIntervalSeconds = 60f;

        LocalSaveService _local;
        ICloudSaveService _cloud;
        SaveData _current;

        public SaveData Current   => _current;
        public bool     IsNewGame { get; private set; }

        // True once ReconcileWithCloud() has finished (whether it pulled cloud data, found
        // none, or cloud was unavailable). ECSLoadBridge waits on this before applying the
        // save to ECS so a slower cloud fetch can't be clobbered by a stale local-into-ECS load.
        public bool CloudReconcileDone { get; private set; }

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

            AuthSessionPolicy.RecordAppOpen();
            _cloud = new UGSCloudSaveService();
        }

        IEnumerator Start()
        {
            // Cloud reconciliation happens asynchronously after local is already ready
            yield return InitialCloudReconcile();
            StartCoroutine(AutoSaveLoop());
        }

        // Reconcile with cloud, then mark done. CloudReconcileDone is set here (after the
        // inner coroutine returns) so every early yield-break path inside ReconcileWithCloud()
        // (no cloud, unavailable, offline, or a successful pull) is covered. ECSLoadBridge
        // gates ApplyLoadedSave on this flag. Internal so EditMode tests can pump it directly
        // without the play-mode lifecycle (the test assembly runs in edit mode).
        internal IEnumerator InitialCloudReconcile()
        {
            yield return ReconcileWithCloud();
            CloudReconcileDone = true;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // Write departure time to PlayerPrefs immediately — this is the most
                // reliable record because PlayerPrefs.Save() is OS-managed and
                // survives even if the process is killed before the JSON file flushes.
                PlayerPrefs.SetString(BackgroundTimestampKey, DateTime.UtcNow.ToString("o"));
                PlayerPrefs.Save();
                SaveLocal();
            }
            else
            {
                OnReturnFromBackground();
            }
        }

        void OnApplicationQuit()
        {
            SaveLocal();
        }

        // Called when the app returns from background (OnApplicationPause(false)).
        // Recalculates idle earnings using the PlayerPrefs departure timestamp so
        // that background→foreground cycles show the modal, not just cold boots.
        // Works identically on Android and iOS — no platform directives needed.
        void OnReturnFromBackground()
        {
            if (ECSLoadBridge.Instance == null || !ECSLoadBridge.Instance.IsLoaded)
            {
                GameLogger.Debug($"[Idle] OnReturnFromBackground — ECS not ready (Instance={ECSLoadBridge.Instance != null} IsLoaded={ECSLoadBridge.Instance?.IsLoaded})");
                return;
            }

            string backgroundedAt = PlayerPrefs.GetString(BackgroundTimestampKey, string.Empty);
            GameLogger.Debug($"[Idle] OnReturnFromBackground — backgroundedAt={backgroundedAt} lastSaved={_current?.lastSaved}");
            if (!string.IsNullOrEmpty(backgroundedAt))
                PlayerPrefs.DeleteKey(BackgroundTimestampKey);

            var result = OfflineCollectionService.CalculateAndApply(
                _current,
                gameConfig,
                PersistentUpgradeService.Instance,
                string.IsNullOrEmpty(backgroundedAt) ? null : backgroundedAt);

            if (result == null) return;

            // Persist earned resources and the updated idleCollectionApplied stamp.
            // Skip ECS/grid flushes — only currentRun (currency, inventory) changed.
            SaveLocal(skipECSFlush: true, skipGridFlush: true);
            FindAnyObjectByType<HUDController>()?.ShowIdleReturn(result);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Reconciles the already-loaded local save with cloud if available.</summary>
        private IEnumerator ReconcileWithCloud()
        {
            if (_cloud == null) yield break;

            // Init UGS (anonymous sign-in). IsAvailable is false until this completes.
            var initTask = _cloud.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Honor a pending wipe request from the editor tool (MobileIdleBuilder → Clear Save).
            // Skip the cloud fetch and delete the cloud key instead; _current is already a
            // fresh SaveData because the local file was deleted before entering play mode.
            if (PlayerPrefs.GetInt(k_WipePending, 0) == 1)
            {
                PlayerPrefs.DeleteKey(k_WipePending);
                PlayerPrefs.Save();
                if (_cloud.IsAvailable)
                {
                    var wipeTask = _cloud.DeleteAsync();
                    yield return new WaitUntil(() => wipeTask.IsCompleted);
                    GameLogger.Info("[SaveManager] Cloud save wiped by dev tool.");
                }
                yield break;
            }
#endif

            if (!_cloud.IsAvailable) yield break;

            // Sync local playerId to UGS identity for cross-device consistency.
            // Accessing AuthenticationService.Instance throws if UGS never initialized
            // (e.g. an injected/test cloud service), so guard and keep the local id on failure.
            try
            {
                _current.playerId = AuthenticationService.Instance.PlayerId;
            }
            catch (ServicesInitializationException)
            {
                GameLogger.Warning("[SaveManager] Auth not initialized — keeping local playerId.");
            }

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

#if UNITY_EDITOR
        /// <summary>Test seam: replace the cloud service after Awake but before Start runs.</summary>
        internal void SetCloudServiceForTesting(ICloudSaveService cloud) => _cloud = cloud;
#endif

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
