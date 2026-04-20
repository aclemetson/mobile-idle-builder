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
    public class SaveManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] GameConfigSO gameConfig;
        [SerializeField] float autoSaveIntervalSeconds = 60f;

        LocalSaveService _local;
        ICloudSaveService _cloud;
        SaveData _current;

        public SaveData Current => _current;
        public static SaveManager Instance { get; private set; }

        void Awake()
        {
            if (Instance != null) { Debug.LogError($"[SaveManager] DUPLICATE detected — destroying component on '{gameObject.name}', keeping '{Instance.gameObject.name}'"); Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _local   = new LocalSaveService();
            _current = _local.Load() ?? new SaveData { playerId = GeneratePlayerId() };

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
                Debug.Log("[SaveManager] Reconciled with cloud (cloud was newer).");
            }
        }

        public void SaveLocal() => _local.SaveWithBackup(_current);

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
