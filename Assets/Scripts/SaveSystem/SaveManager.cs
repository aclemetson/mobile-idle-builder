using System;
using System.Collections;
using System.Globalization;
using System.Threading.Tasks;
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
        [Tooltip("Minimum seconds between autosave cloud pushes. Autosave still writes locally every " +
                 "autoSaveIntervalSeconds; background/quit and explicit SaveToCloud() always push.")]
        [SerializeField] float cloudPushIntervalSeconds = 300f;

        LocalSaveService _local;
        ICloudSaveService _cloud;
        SaveData _current;

        // Content hash (LocalSaveService.LastWrittenContentHash) of the last save that reached the
        // cloud, and when that push was started. The autosave skips pushes that would upload
        // identical state or come sooner than cloudPushIntervalSeconds after the previous one.
        ulong _lastPushedHash;
        float _lastPushStartedAt = float.NegativeInfinity;

        /// <summary>Test seam: realtime clock (seconds) used for the cloud-push throttle.</summary>
        internal Func<float> RealtimeNow = () => Time.realtimeSinceStartup;

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
            else if (_local.LastLoadSource != LocalSaveService.LoadSource.Main)
                GameLogger.Warning($"[Save] Loaded save from fallback ({_local.LastLoadSource}) — save.json was " +
                                   $"missing or corrupt. Tutorial step: '{_current.tutorial.currentStepId}'");
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

            // Telemetry starts after reconcile so UGS auth + Remote Config flags are ready and
            // _current.playerId has been synced to the UGS id. Placed here (not inside
            // ReconcileWithCloud) so it runs on every exit path, including when the cloud-save
            // kill-switch is off — analytics is independent of cloud save. No-op unless the
            // analytics.enabled flag is true.
            TelemetryService.Instance?.StartIfEnabled();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // Write departure time to PlayerPrefs immediately — this is the most
                // reliable record because PlayerPrefs.Save() is OS-managed and
                // survives even if the process is killed before the JSON file flushes.
                PlayerPrefs.SetString(BackgroundTimestampKey, GameClock.UtcNow.ToString("o"));
                PlayerPrefs.Save();
                SaveLocal();
                PushToCloudBestEffort();
            }
            else
            {
                OnReturnFromBackground();
            }
        }

        void OnApplicationQuit()
        {
            SaveLocal();
            PushToCloudBestEffort();
        }

        // Fire-and-forget cloud push for lifecycle events (background / quit). A frame-based
        // coroutine (SaveToCloud) can't be used here because the Unity player loop is suspended
        // once the app is backgrounded — its WaitUntil would never pump. PushAsync serializes
        // the SaveData synchronously before its first await, so the snapshot is captured now;
        // the network write then proceeds on whatever background grace window the OS grants.
        // Best-effort by design: local save (above) remains the source of truth, and the next
        // cold start's reconcile pulls cloud if it happens to be newer.
        void PushToCloudBestEffort()
        {
            if (_cloud == null || !_cloud.IsAvailable) return;
            if (!FeatureFlags.CloudSaveEnabled) return;   // honor the cloud-save kill-switch
            _ = PushAndRecord();
        }

        // Starts a cloud push of _current and records its content hash once it succeeds. Everything up
        // to the await runs synchronously, and PushAsync serializes before its own first await, so the
        // snapshot is taken now even if the app is about to be suspended. The continuation resumes on
        // Unity's main-thread SynchronizationContext, so the 64-bit field write is never torn.
        // A failed push still counts toward the interval: offline devices retry on the next window
        // rather than every autosave.
        async Task<bool> PushAndRecord()
        {
            ulong hash = _local.LastWrittenContentHash;
            _lastPushStartedAt = RealtimeNow();
            bool ok = await _cloud.PushAsync(_current);
            if (ok) _lastPushedHash = hash;
            return ok;
        }

        /// <summary>
        /// Whether an autosave should push to the cloud: only when the saved content differs from
        /// the last successful push AND at least <paramref name="minIntervalSeconds"/> have passed
        /// since the previous push started. A content hash of 0 means "unknown" and always counts
        /// as changed.
        /// </summary>
        internal static bool ShouldAutoPush(ulong contentHash, ulong lastPushedHash,
                                            float now, float lastPushStartedAt, float minIntervalSeconds)
        {
            if (contentHash != 0 && contentHash == lastPushedHash) return false;
            return now - lastPushStartedAt >= minIntervalSeconds;
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

            // CalculateAndApply only credits SaveData. ECS is already live here (unlike the cold-boot
            // path, which copies save -> ECS afterwards), so the payout must be written through too,
            // or the next FlushToSave overwrites the save with the stale ECS total and it is lost.
            ECSLoadBridge.Instance.AddEntropy(result.EntropyEarned);
            ECSLoadBridge.Instance.AddInventoryItems(result.ItemsEarned);

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

            // Feature flags share the UGS session established above. Fetch now, before we decide
            // whether to honor the cloud-save kill-switch below. A failed/offline fetch is a no-op
            // (cached/default values stay in effect).
            if (FeatureFlagService.Instance != null)
            {
                var flagTask = FeatureFlagService.Instance.FetchAsync();
                yield return new WaitUntil(() => flagTask.IsCompleted);
            }

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
                    // Observe the fault so it isn't rethrown on the finalizer thread later.
                    if (wipeTask.IsFaulted) GameLogger.Warning($"[SaveManager] Cloud wipe failed: {wipeTask.Exception?.GetBaseException().Message}");
                    else GameLogger.Info("[SaveManager] Cloud save wiped by dev tool.");
                }
                yield break;
            }
#endif

            // Cloud-save kill-switch. Note this gates save-data sync only — UGS itself stayed
            // initialized above so Remote Config (and the flags) could load.
            if (!FeatureFlags.CloudSaveEnabled)
            {
                GameLogger.Info("[SaveManager] Cloud save disabled by feature flag — running local-only this session.");
                yield break;
            }

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
            // Until the save has been applied to ECS, ECS holds baked defaults and an EMPTY grid. Flushing
            // that would overwrite the good save: currency with zeros, and every building, belt and field
            // with nothing. Skip both ECS and grid snapshots; _current still holds the save loaded at
            // startup, so writing it back below is harmless (and the disk file is preserved).
            //
            // This covers both the timed-out load (IsLoaded && !SaveApplied) AND the window before the load
            // finishes (!IsLoaded). The second one wiped grids on device: a research timer that finished
            // while the app was closed completes in ResearchService.Start, which calls SaveLocal seconds
            // before LoadGrid has re-placed anything. Backgrounding during the load does the same.
            if (ECSLoadBridge.Instance != null && !ECSLoadBridge.Instance.SaveApplied)
            {
                GameLogger.Warning("[Save] Save not applied to ECS yet (still loading, or load timed out) — " +
                    "skipping ECS/grid flush so the on-disk save is not overwritten with defaults.");
                skipECSFlush  = true;
                skipGridFlush = true;
            }

            // Prestige already wrote the reset run into _current; the old scene's grid and ECS state
            // (still live until the reload lands) must not be flushed back over it.
            if (PrestigeRunReset.ReloadPending)
            {
                skipECSFlush  = true;
                skipGridFlush = true;
            }

            if (!skipECSFlush)
                ECSLoadBridge.Instance?.FlushToSave();
            if (!skipGridFlush)
                GridSaveService.Instance?.FlushToSave();
            GameLogger.Debug($"[Save] Writing to disk — tutorial step: '{_current.tutorial.currentStepId}'  " +
                      $"active={_current.tutorial.isActive}  inventory items: {_current.currentRun.inventoryKeys?.Count ?? 0}");
            _local.SaveWithBackup(_current);
        }

        /// <summary>Saves locally, then always pushes to the cloud (if available). For explicit saves.</summary>
        public IEnumerator SaveToCloud()
        {
            if (_cloud == null || !_cloud.IsAvailable) { SaveLocal(); yield break; }

            SaveLocal();
            var task = PushAndRecord();
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>
        /// One autosave: always writes locally, but pushes to the cloud only when the state changed
        /// since the last successful push and the push interval has elapsed (<see cref="ShouldAutoPush"/>).
        /// Internal so EditMode tests can pump it directly.
        /// </summary>
        internal IEnumerator AutoSaveTick()
        {
            SaveLocal();

            if (_cloud == null || !_cloud.IsAvailable) yield break;
            if (!FeatureFlags.CloudSaveEnabled) yield break;
            if (!ShouldAutoPush(_local.LastWrittenContentHash, _lastPushedHash,
                                RealtimeNow(), _lastPushStartedAt, cloudPushIntervalSeconds))
                yield break;

            var task = PushAndRecord();
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
            _ = task.Exception; // observe the fault so it isn't rethrown on the finalizer thread
            onDone?.Invoke(!task.IsFaulted);
        }

        /// <summary>Resets in-memory save state to a blank new game. Call before scene reload when wiping saves.</summary>
        public void ResetToFreshSave()
        {
            _current  = new SaveData { playerId = GeneratePlayerId() };
            IsNewGame = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Dev-only: replaces the in-memory save with <paramref name="data"/> and writes it to disk.
        /// Used by the dev-console snapshot loader — after calling this, reload the scene so
        /// ECSLoadBridge re-applies the swapped state to ECS. Clears IsNewGame so the restored
        /// currency/inventory are actually applied on reload (ApplyLoadedSave skips them for new games).
        /// </summary>
        public void DevReplaceCurrent(SaveData data)
        {
            if (data == null) return;
            _current  = data;
            IsNewGame = false;
            _local.Save(_current);
        }

        /// <summary>
        /// Dev-only: the player closes the app for <paramref name="seconds"/> and comes back. Runs
        /// the real background and resume handlers with GameClock moved forward in between, so
        /// offline earnings (idle cap included), research timers, cooldowns and boosts all behave
        /// as after a real absence, and the welcome-back modal shows.
        /// </summary>
        public void DevSimulateAway(double seconds)
        {
            if (seconds <= 0) return;
            OnApplicationPause(true);
            GameClock.DevShift(TimeSpan.FromSeconds(seconds));
            OnApplicationPause(false);
        }
#endif

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
                yield return AutoSaveTick();
            }
        }

        bool IsCloudNewer(SaveData cloud) => IsNewerStamp(cloud.lastSaved, _current.lastSaved);

        /// <summary>
        /// True if ISO 8601 stamp <paramref name="candidate"/> is later than <paramref name="current"/>.
        /// Same rules as before #280: an empty (or now also unparseable) current stamp always loses;
        /// otherwise an empty or unparseable candidate never wins. Parsed culture-invariantly so the
        /// device locale cannot change the result, and without throwing on a malformed stamp.
        /// </summary>
        internal static bool IsNewerStamp(string candidate, string current)
        {
            if (!TryParseStamp(current, out var cur))    return true;
            if (!TryParseStamp(candidate, out var cand)) return false;
            return cand > cur;
        }

        static bool TryParseStamp(string s, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(s)) return false;
            // RoundtripKind keeps the "Z" of an "o" stamp as DateTimeKind.Utc. It must not be combined
            // with AdjustToUniversal (that throws), so normalise any offset stamp afterwards.
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return false;
            utc = parsed.Kind == DateTimeKind.Local ? parsed.ToUniversalTime() : parsed;
            return true;
        }

        static string GeneratePlayerId() => $"uid_{Guid.NewGuid():N}";
    }
}
