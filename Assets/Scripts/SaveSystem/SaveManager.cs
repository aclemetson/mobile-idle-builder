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
                 "autoSaveIntervalSeconds; focus loss, background, quit and explicit SaveToCloud() push too.")]
        [SerializeField] float cloudPushIntervalSeconds = 60f;   // was 300: a short session never reached the cloud

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
        public GameConfigSO GameConfig => gameConfig;

        /// <summary>
        /// Raised after the save is written when the app goes to the background or quits, so listeners
        /// (local notifications) work from the state that was just saved. Runs synchronously: the player
        /// loop is about to be suspended, so handlers must not wait on frames.
        /// </summary>
        public event Action Backgrounding;
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
                PushToCloudBestEffort(skipIfAlreadyPushed: true);   // focus loss usually pushed this already
                RaiseBackgrounding();
            }
            else
            {
                OnReturnFromBackground();
            }
        }

        /// <summary>
        /// Focus is lost before Android suspends the app (app switcher, home gesture, notification shade),
        /// while the player loop still runs, so a push started here usually finishes. A push started at
        /// OnApplicationPause(true) often does not: on 0.6.5 a backgrounded session never reached the cloud.
        /// Skipped when the content matches the last pushed or in-flight save, so the pause that usually
        /// follows (and focus flicker from dialogs or the keyboard) does not upload the same state again.
        /// </summary>
        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;
            if (_current == null || !CloudReconcileDone) return;   // still loading: nothing final to upload
            SaveLocal();
            GameLogger.Info($"[Save] Focus lost: saved locally{(IsAlreadyPushed(_local.LastWrittenContentHash) ? "; cloud already has this state" : "; pushing to cloud")}.");
            PushToCloudBestEffort(skipIfAlreadyPushed: true);
        }

        void OnApplicationQuit()
        {
            SaveLocal();
            PushToCloudBestEffort();
            RaiseBackgrounding();
        }

        void RaiseBackgrounding()
        {
            try { Backgrounding?.Invoke(); }
            catch (Exception e) { GameLogger.Error($"[Save] Backgrounding handler failed: {e}"); }
        }

        // Fire-and-forget cloud push for lifecycle events (background / quit). A frame-based
        // coroutine (SaveToCloud) can't be used here because the Unity player loop is suspended
        // once the app is backgrounded — its WaitUntil would never pump. PushAsync serializes
        // the SaveData synchronously before its first await, so the snapshot is captured now;
        // the network write then proceeds on whatever background grace window the OS grants.
        // Best-effort by design: local save (above) remains the source of truth, and the next
        // cold start's reconcile pulls cloud if it happens to be newer.
        void PushToCloudBestEffort(bool skipIfAlreadyPushed = false)
        {
            if (_cloud == null || !_cloud.IsAvailable) return;
            if (!FeatureFlags.CloudSaveEnabled) return;   // honor the cloud-save kill-switch
            // The game now runs this device's save while "which save?" is open; pushing it would overwrite
            // the cloud save the player may still pick.
            if (AwaitingSaveChoice) return;
            if (skipIfAlreadyPushed && IsAlreadyPushed(_local.LastWrittenContentHash)) return;
            _ = PushAndRecord();
        }

        // Hash of the push in flight, and the window in which it counts as "already being uploaded".
        ulong _pushingHash;
        const float InFlightPushWindowSeconds = 15f;

        /// <summary>True when <paramref name="hash"/> already reached the cloud, or is being uploaded right now.</summary>
        bool IsAlreadyPushed(ulong hash)
        {
            if (hash == 0) return false;   // unknown content: always push
            if (hash == _lastPushedHash) return true;
            return hash == _pushingHash && RealtimeNow() - _lastPushStartedAt < InFlightPushWindowSeconds;
        }

        // Starts a cloud push of _current and records its content hash once it succeeds. Everything up
        // to the await runs synchronously, and PushAsync serializes before its own first await, so the
        // snapshot is taken now even if the app is about to be suspended. The continuation resumes on
        // Unity's main-thread SynchronizationContext, so the 64-bit field write is never torn.
        // A failed push still counts toward the interval: offline devices retry on the next window
        // rather than every autosave.
        //
        // The sync marker (SaveSync) is written before the await: a push on backgrounding often finishes
        // after the OS suspends the app, and its continuation never runs. If the push does report a
        // failure, the previous marker is put back so the unpushed progress still counts as local change.
        async Task<bool> PushAndRecord()
        {
            ulong hash = _local.LastWrittenContentHash;
            _pushingHash = hash;
            _lastPushStartedAt = RealtimeNow();
            var previousMarker = SaveSync.LoadMarker();
            string playerId = _cloud.AccountPlayerId;
            SaveSync.SaveMarker(playerId, _current.lastSaved, hash);
            bool ok = await _cloud.PushAsync(_current);
            if (ok) _lastPushedHash = hash;
            else if (!string.IsNullOrEmpty(playerId)) SaveSync.RestoreMarker(previousMarker);
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
            // (cached/default values stay in effect). The splash gate usually fetched moments ago, so
            // that fetch is reused (one Remote Config fetch per launch, #209).
            if (FeatureFlagService.Instance != null)
            {
                var flagTask = FeatureFlagService.Instance.FetchIfStaleAsync();
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

            yield return SyncWithCloudSave(fetchTask.Result, IsNewGame, _cloud.AccountSwitched);
        }

        // ── Cross-device sync (docs/agents/save-system.md, "Cross-device saves") ──

        /// <summary>
        /// True while the "which save?" choice is open. ECSLoadBridge keeps waiting past its cloud
        /// timeout meanwhile, so the chosen save is the one applied to ECS.
        /// </summary>
        public bool AwaitingSaveChoice { get; private set; }

        /// <summary>The cloud service, for the Settings account row. Null in tests without one.</summary>
        public ICloudSaveService Cloud => _cloud;

        /// <summary>Test seam: answers the "which save?" choice instead of the HUD prompt.</summary>
        internal static Func<SaveData, SaveData, SaveSyncAction> SaveChoiceOverrideForTests;

        /// <summary>How long to wait for the HUD before the choice falls back to newest-wins.</summary>
        const float PromptReadyTimeoutSeconds = 30f;

        internal const string ReplacedLocalFile = "save.replaced-local.json";
        internal const string ReplacedCloudFile = "save.replaced-cloud.json";

        /// <summary>Decides between the local and the fetched cloud save (<see cref="SaveSync.Decide"/>) and applies it.</summary>
        IEnumerator SyncWithCloudSave(SaveData cloudSave, bool localIsNew, bool accountSwitched)
        {
            string playerId = _cloud.AccountPlayerId ?? _current.playerId;
            var marker = SaveSync.LoadMarker();
            ulong localHash = LocalSaveService.ContentHash(_current);
            var action = SaveSync.Decide(_current, localHash, localIsNew, cloudSave, marker, playerId, accountSwitched);
            GameLogger.Info($"[SaveManager] Cloud sync: {action} (cloud {(cloudSave == null ? "none" : cloudSave.lastSaved)}, " +
                            $"local {_current.lastSaved}, marker {(marker.IsSet ? marker.CloudStamp : "none")}, switched={accountSwitched}; " +
                            $"local grid {GridSummary(_current)}, cloud grid {GridSummary(cloudSave)})");

            bool asked = action == SaveSyncAction.AskPlayer;
            if (asked)
                yield return AskWhichSave(cloudSave, a => action = a);

            switch (action)
            {
                case SaveSyncAction.TakeCloud:
                    // Keep this device's save even when the take is silent: it is the only way back if the
                    // cloud copy turns out to be the wrong one. A fresh install has nothing worth keeping.
                    if (asked || !localIsNew) KeepCopy(_current, ReplacedLocalFile);
                    TakeCloudSave(cloudSave, playerId);
                    break;

                case SaveSyncAction.KeepLocal when asked:
                    // The player kept this device's save: it replaces the cloud copy right away.
                    KeepCopy(cloudSave, ReplacedCloudFile);
                    SaveLocal();
                    if (FeatureFlags.CloudSaveEnabled && _cloud.IsAvailable)
                    {
                        var push = PushAndRecord();
                        yield return new WaitUntil(() => push.IsCompleted);
                    }
                    break;

                case SaveSyncAction.KeepLocal:
                    // Nothing changed on either side: remember that both are in sync.
                    if (cloudSave != null && marker.IsSet && cloudSave.lastSaved == marker.CloudStamp)
                        SaveSync.SaveMarker(playerId, cloudSave.lastSaved, localHash);
                    break;
            }
        }

        void TakeCloudSave(SaveData cloudSave, string playerId)
        {
            bool live = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.SaveApplied;
            string cloudStamp = cloudSave.lastSaved;

            if (live)
            {
                // ECS already runs this device's save (the cloud answered after the load timeout, or the
                // player switched accounts from Settings). Swapping Current under it would be overwritten
                // by the next flush, so reload the scene from the cloud save instead.
                GameLogger.Info("[SaveManager] Cloud save chosen while the game is running — reloading the scene.");
                // The default ECS world survives the reload. Without clearing it, this device's buildings
                // and belts stayed live and the cloud grid loaded on top of them (mixed map), and the next
                // flush wrote that mix back into the save and the cloud. Same clearing prestige does.
                ClearLiveRunState();
                _current  = cloudSave;
                IsNewGame = false;
                _local.SaveWithBackup(_current);
                SaveSync.SaveMarker(playerId, cloudStamp, _local.LastWrittenContentHash);
                RecipeKnowledgeService.Instance?.AdoptProfileKnowledge();
                PrestigeRunReset.BeginReload();   // the same guarded reload prestige uses
                return;
            }

            _local.SaveWithBackup(_current);
            _current  = cloudSave;
            // A fresh install that pulls a cloud save is no longer a new game: without this, ECSLoadBridge
            // and GridSaveService skipped the currency, inventory and grid of the pulled save, and the next
            // flush wrote the baked defaults back over it (and pushed them to the cloud).
            IsNewGame = false;
            _local.Save(_current);
            SaveSync.SaveMarker(playerId, cloudStamp, _local.LastWrittenContentHash);
            RecipeKnowledgeService.Instance?.AdoptProfileKnowledge();
            GameLogger.Info("[SaveManager] Reconciled with cloud (took the cloud save).");
        }

        /// <summary>"sites 2, buildings 14, belts 37" for the active and all other sites of <paramref name="save"/>.</summary>
        internal static string GridSummary(SaveData save)
        {
            if (save?.currentRun == null) return "none";
            var grids = save.currentRun.grids;
            int sites = 0, buildings = 0, belts = 0;
            if (grids != null && grids.Count > 0)
            {
                foreach (var g in grids)
                {
                    if (g == null) continue;
                    sites++;
                    buildings += g.buildings?.Count ?? 0;
                    belts     += g.conveyors?.Count ?? 0;
                }
            }
            else if (save.currentRun.grid != null)   // legacy single grid
            {
                sites     = 1;
                buildings = save.currentRun.grid.buildings?.Count ?? 0;
                belts     = save.currentRun.grid.conveyors?.Count ?? 0;
            }
            return $"sites {sites}, buildings {buildings}, belts {belts}";
        }

        /// <summary>
        /// Empties the running game before the scene is reloaded from another save: grid occupancy and
        /// visuals (<see cref="GridSaveService.ClearGrid"/>), then the run entities in the default ECS world,
        /// which the reload does not rebuild (<see cref="RunStateClearer"/>).
        /// </summary>
        internal static void ClearLiveRunState()
        {
            try
            {
                GridSaveService.Instance?.ClearGrid();
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    int cleared = RunStateClearer.ClearLiveRunEntities(world.EntityManager);
                    GameLogger.Info($"[SaveManager] Cleared the running game before the swap ({cleared} buildings left in ECS).");
                }
            }
            catch (Exception ex)
            {
                GameLogger.Error($"[SaveManager] Clearing the running game before the swap failed: {ex}");
            }
        }

        IEnumerator AskWhichSave(SaveData cloudSave, Action<SaveSyncAction> answer)
        {
            AwaitingSaveChoice = true;
            try
            {
                if (SaveChoiceOverrideForTests != null)
                {
                    answer(SaveChoiceOverrideForTests(_current, cloudSave));
                    yield break;
                }

                float giveUpAt = RealtimeNow() + PromptReadyTimeoutSeconds;
                while (!SaveConflictPrompt.IsReady && RealtimeNow() < giveUpAt)
                    yield return null;

                if (!SaveConflictPrompt.IsReady)
                {
                    bool cloudNewer = IsNewerStamp(cloudSave.lastSaved, _current.lastSaved);
                    GameLogger.Warning($"[SaveManager] Save choice prompt never became ready; keeping the newer save ({(cloudNewer ? "cloud" : "local")}).");
                    answer(cloudNewer ? SaveSyncAction.TakeCloud : SaveSyncAction.KeepLocal);
                    yield break;
                }

                bool? keepLocal = null;
                var now = GameClock.UtcNow;
                SaveConflictPrompt.Show(SaveSync.Summary(_current, now), SaveSync.Summary(cloudSave, now),
                                        k => keepLocal = k);
                while (keepLocal == null) yield return null;
                answer(keepLocal.Value ? SaveSyncAction.KeepLocal : SaveSyncAction.TakeCloud);
            }
            finally
            {
                AwaitingSaveChoice = false;
            }
        }

        static void KeepCopy(SaveData data, string fileName)
        {
            if (data == null) return;
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(LocalSaveService.SaveDirectory, fileName),
                                            JsonUtility.ToJson(data));
                GameLogger.Info($"[SaveManager] Kept the replaced save as {fileName}.");
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[SaveManager] Could not keep a copy of the replaced save: {ex.Message}");
            }
        }

        /// <summary>
        /// Settings "Sign in with Google": shows the account picker, then links Google to this guest
        /// player, or switches to the player that already owns the Google account and settles the two
        /// saves (asking the player if both have progress). <paramref name="onDone"/> gets a message to show.
        /// </summary>
        public IEnumerator SignInWithGoogleFromSettings(Action<string> onDone)
        {
            if (_cloud == null) { onDone?.Invoke("Cloud save is not available."); yield break; }

            var task = _cloud.SignInWithGoogleAsync(GoogleAuthMode.Interactive);
            yield return new WaitUntil(() => task.IsCompleted);
            var outcome = task.IsFaulted ? GoogleSignInOutcome.Failed : task.Result;

            switch (outcome)
            {
                case GoogleSignInOutcome.Failed:
                    onDone?.Invoke($"Google sign-in failed: {_cloud.LastSignInError ?? "unknown error"}");
                    yield break;

                case GoogleSignInOutcome.AlreadyGoogle:
                    onDone?.Invoke("Already signed in with Google.");
                    yield break;

                case GoogleSignInOutcome.Linked:
                    // Same player, so the same cloud save: push now so other devices see it.
                    if (FeatureFlags.CloudSaveEnabled && _cloud.IsAvailable)
                    {
                        SaveLocal();
                        var push = PushAndRecord();
                        yield return new WaitUntil(() => push.IsCompleted);
                    }
                    onDone?.Invoke("Signed in with Google. Your progress now syncs across your devices.");
                    yield break;
            }

            // SwitchedAccount: this Google account already had a save (another device's).
            _current.playerId = _cloud.AccountPlayerId ?? _current.playerId;
            if (!FeatureFlags.CloudSaveEnabled || !_cloud.IsAvailable)
            {
                onDone?.Invoke("Signed in with Google.");
                yield break;
            }

            SaveLocal();
            var fetch = _cloud.FetchAsync(_current.playerId);
            yield return new WaitUntil(() => fetch.IsCompleted);
            var cloudSave = fetch.IsFaulted ? null : fetch.Result;

            if (cloudSave == null)
            {
                var push = PushAndRecord();
                yield return new WaitUntil(() => push.IsCompleted);
                onDone?.Invoke("Signed in with Google. Your progress now syncs across your devices.");
                yield break;
            }

            // localIsNew: false. IsNewGame only describes the save found at launch; this session has played since.
            yield return SyncWithCloudSave(cloudSave, localIsNew: false, accountSwitched: true);
            onDone?.Invoke("Signed in with Google.");
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

        internal static bool TryParseStamp(string s, out DateTime utc)
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
