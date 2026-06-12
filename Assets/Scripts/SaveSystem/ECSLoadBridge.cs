using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Waits for ECS singletons to be available after SubScene load, then applies
    /// SaveManager.Current into ECS. Also provides FlushToSave() which is called by
    /// SaveManager.SaveLocal() to snapshot ECS state back into SaveData before writing disk.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ECSLoadBridge : SingletonMonoBehaviour<ECSLoadBridge>
    {
        protected override bool PersistAcrossScenes => false;

        public bool IsLoaded { get; private set; }

        IdleCollectionResult _pendingIdleResult;

        EntityManager _em;
        EntityQuery   _progressQuery;
        EntityQuery   _prestigeQuery;
        EntityQuery   _inventoryQuery;
        EntityQuery   _tutorialQuery;

        protected override void Awake()
        {
            base.Awake();
        }

        void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        // When another singleton's DontDestroyOnLoad drags this component into a new scene,
        // re-run initialization so ApplyLoadedSave() fires against the fresh ECS SubScene.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != this) return;
            if (scene.name == SceneLoader.LoadingSceneName) return;
            if (!IsLoaded) return; // still initializing from Start()

            IsLoaded = false;
            StopAllCoroutines();
            StartCoroutine(InitializeAsync());
        }

        IEnumerator Start()
        {
            yield return StartCoroutine(InitializeAsync());
        }

        IEnumerator InitializeAsync()
        {
            if (Instance != this) yield break;
            GameLogger.Info("[ECSLoadBridge] Initializing — polling for ECS world");

            // Poll for the ECS world — it may not be ready on the first frame on Android.
            const float kWorldTimeout = 5f;
            float worldElapsed = 0f;
            while (World.DefaultGameObjectInjectionWorld == null && worldElapsed < kWorldTimeout)
            {
                worldElapsed += Time.deltaTime;
                yield return null;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                GameLogger.Error($"[ECSLoadBridge] No ECS world after {worldElapsed:F1}s — save load skipped.");
                IsLoaded = true;
                yield break;
            }
            GameLogger.Info($"[ECSLoadBridge] World found after {worldElapsed:F1}s — creating queries");

            _em = world.EntityManager;
            _progressQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>());
            _tutorialQuery = _em.CreateEntityQuery(
                ComponentType.ReadWrite<TutorialStateData>());

            GameLogger.Info("[ECSLoadBridge] Queries created — polling for entities");

            // Poll until SubScene entities are loaded
            const float kTimeout = 10f;
            float elapsed = 0f;
            float logTimer = 0f;
            while (elapsed < kTimeout)
            {
                // Tutorial is included here: if its singleton lags even one frame behind the
                // others, ApplyLoadedSave would skip the tutorial block and the baked step-0
                // default would survive, replaying the intro on every load.
                if (!_progressQuery.IsEmpty && !_prestigeQuery.IsEmpty && !_inventoryQuery.IsEmpty
                    && !_tutorialQuery.IsEmpty)
                    break;
                elapsed  += Time.deltaTime;
                logTimer += Time.deltaTime;
                if (logTimer >= 2f)
                {
                    logTimer = 0f;
                    GameLogger.Info($"[ECSLoadBridge] Waiting for entities ({elapsed:F1}s) — " +
                        $"progress={!_progressQuery.IsEmpty}  prestige={!_prestigeQuery.IsEmpty}  " +
                        $"inventory={!_inventoryQuery.IsEmpty}  tutorial={!_tutorialQuery.IsEmpty}");
                }
                yield return null;
            }

            if (_progressQuery.IsEmpty || _prestigeQuery.IsEmpty || _inventoryQuery.IsEmpty)
            {
                GameLogger.Error($"[ECSLoadBridge] Timeout after {elapsed:F1}s — " +
                    $"progress={!_progressQuery.IsEmpty}  prestige={!_prestigeQuery.IsEmpty}  " +
                    $"inventory={!_inventoryQuery.IsEmpty}  tutorial={!_tutorialQuery.IsEmpty}");
                IsLoaded = true;
                yield break;
            }

            GameLogger.Info($"[ECSLoadBridge] All entities found after {elapsed:F1}s — waiting for cloud reconcile");

            // Wait for SaveManager to finish cloud reconciliation so Current is final
            // (local or cloud-replaced) before we copy it into ECS. Without this, ECS could be
            // seeded from the local save while a slower cloud fetch replaces Current afterward,
            // and the next FlushToSave would write the stale ECS state back over the cloud data.
            // Bounded so a slow or offline network never blocks the load indefinitely.
            const float kCloudTimeout = 5f;
            float cloudElapsed = 0f;
            var sm = SaveManager.Instance;
            while (sm != null && !sm.CloudReconcileDone && cloudElapsed < kCloudTimeout)
            {
                cloudElapsed += Time.deltaTime;
                yield return null;
            }
            if (sm != null && !sm.CloudReconcileDone)
                GameLogger.Warning($"[ECSLoadBridge] Cloud reconcile not done after {cloudElapsed:F1}s — applying current save anyway.");

            GameLogger.Info("[ECSLoadBridge] Applying save to ECS");
            ApplyLoadedSave();
            GridSaveService.Instance?.LoadGrid();
            IsLoaded = true;
            GameLogger.Info("[ECSLoadBridge] Save applied to ECS — IsLoaded=true");

            if (_pendingIdleResult != null)
            {
                FindAnyObjectByType<HUDController>()?.ShowIdleReturn(_pendingIdleResult);
                _pendingIdleResult = null;
            }
        }

        // ── Load path ─────────────────────────────────────────────────────

        void ApplyLoadedSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            // Restore purchased permanent upgrades first — effects depend on this
            PersistentUpgradeService.Instance?.LoadFromSave(save.permanentUpgrades);

            // Prestige multipliers — always safe to apply (defaults match SaveData defaults)
            var prestige = _prestigeQuery.GetSingleton<PrestigeData>();
            prestige.RunCount              = save.prestigeCount;
            prestige.PrestigeCurrency      = save.prestigeCurrency;
            prestige.PrestigeCurrencySpent = save.prestigeCurrencySpent;
            // Apply speed boost on top of the prestige base (2× when active, 1× otherwise)
            float speedBoost = PremiumShopService.Instance?.GetSpeedBoostMultiplier() ?? 1f;
            prestige.SpeedMultiplier       = save.prestigeSpeedMultiplier * speedBoost;
            prestige.OutputMultiplier      = save.prestigeOutputMultiplier;
            prestige.CostReduction         = save.prestigeCostReduction;
            _prestigeQuery.SetSingleton(prestige);

            // Tutorial — always restore so baked defaults never clobber saved progress
            if (!_tutorialQuery.IsEmpty)
            {
                var ts = _tutorialQuery.GetSingleton<TutorialStateData>();
                if (save.tutorial.hasCompletedFirstRun)
                {
                    ts.IsActive         = false;
                    ts.FirstRunComplete = true;
                    GameLogger.Debug("[Save] Tutorial → disabled (hasCompletedFirstRun=true)");
                }
                else
                {
                    ts.IsActive            = save.tutorial.isActive;
                    ts.FirstRunComplete    = false;
                    ts.CurrentStepIndex    = ResolveStepIndex(save.tutorial.currentStepId);
                    GameLogger.Debug($"[Save] Tutorial → step {ts.CurrentStepIndex} ('{save.tutorial.currentStepId}')  " +
                              $"active={ts.IsActive}  isNewGame={SaveManager.Instance.IsNewGame}");
                }
                _tutorialQuery.SetSingleton(ts);
            }
            else if (!string.IsNullOrEmpty(save.tutorial.currentStepId))
            {
                // The readiness gate in InitializeAsync should prevent this; if it still
                // happens (e.g. entity-load timeout) the baked step-0 default will stand and
                // the intro replays. Surface it loudly rather than silently regressing.
                GameLogger.Warning($"[Save] Tutorial entity not present at load — saved step " +
                          $"'{save.tutorial.currentStepId}' was NOT applied; baked default stands.");
            }

            // Calculate and merge idle earnings before restoring ECS buffers
            var idleResult = OfflineCollectionService.CalculateAndApply(
                save,
                GameBootstrap.Instance?.gameConfig,
                PersistentUpgradeService.Instance);
            if (idleResult != null)
                _pendingIdleResult = idleResult;

            // On fresh install, skip currency + inventory so SubScene baked defaults stand
            if (SaveManager.Instance.IsNewGame) return;

            // Currency
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency      = save.currentRun.baseCurrency;
            progress.TotalEntropySpent = save.currentRun.totalEntropySpent;
            progress.BaseNetWorth      = save.currentRun.baseNetWorth;
            _progressQuery.SetSingleton(progress);

            // Inventory
            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: false);
            buffer.Clear();
            var iKeys  = save.currentRun.inventoryKeys;
            var iVals  = save.currentRun.inventoryValues;
            int iCount = System.Math.Min(iKeys?.Count ?? 0, iVals?.Count ?? 0);
            for (int i = 0; i < iCount; i++)
            {
                if (!int.TryParse(iKeys[i], out int itemId)) continue;
                if (iVals[i] > 0)
                    buffer.Add(new InventorySlot { ItemID = itemId, Quantity = iVals[i] });
            }
        }

        // ── Save path ─────────────────────────────────────────────────────

        /// <summary>Called by SaveManager.SaveLocal() before writing to disk.</summary>
        public void FlushToSave()
        {
            if (!IsLoaded) return;

            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            // Currency
            if (!_progressQuery.IsEmpty)
            {
                var pp = _progressQuery.GetSingleton<PlayerProgressData>();
                save.currentRun.baseCurrency      = pp.BaseCurrency;
                save.currentRun.totalEntropySpent = pp.TotalEntropySpent;
                save.currentRun.baseNetWorth      = pp.BaseNetWorth;
                save.lastKnownNetWorth            = pp.NetWorth;
            }

            // Prestige
            if (!_prestigeQuery.IsEmpty)
            {
                var p = _prestigeQuery.GetSingleton<PrestigeData>();
                save.prestigeCount              = p.RunCount;
                save.prestigeCurrency           = p.PrestigeCurrency;
                save.prestigeCurrencySpent      = p.PrestigeCurrencySpent;
                // Strip the shop boost before saving so base × boost isn't accumulated on each save
                float activeBoost = PremiumShopService.Instance?.GetSpeedBoostMultiplier() ?? 1f;
                save.prestigeSpeedMultiplier    = activeBoost > 1f ? p.SpeedMultiplier / activeBoost : p.SpeedMultiplier;
                save.prestigeOutputMultiplier   = p.OutputMultiplier;
                save.prestigeCostReduction      = p.CostReduction;
            }

            // Inventory
            if (!_inventoryQuery.IsEmpty)
            {
                save.currentRun.inventoryKeys   ??= new List<string>();
                save.currentRun.inventoryValues ??= new List<int>();
                save.currentRun.inventoryKeys.Clear();
                save.currentRun.inventoryValues.Clear();
                var invEntity = _inventoryQuery.GetSingletonEntity();
                var buf = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: true);
                foreach (var slot in buf)
                {
                    if (slot.Quantity > 0)
                    {
                        save.currentRun.inventoryKeys.Add(slot.ItemID.ToString());
                        save.currentRun.inventoryValues.Add(slot.Quantity);
                    }
                }
            }

            // Tutorial
            if (!_tutorialQuery.IsEmpty)
            {
                var ts   = _tutorialQuery.GetSingleton<TutorialStateData>();
                var flow = TutorialFlowSO.Current;
                save.tutorial.isActive            = ts.IsActive;
                // Never downgrade: PrestigeSystem sets hasCompletedFirstRun on SaveData before
                // ECS TutorialStateData is updated, so preserve any true already on the save.
                save.tutorial.hasCompletedFirstRun = ts.FirstRunComplete || save.tutorial.hasCompletedFirstRun;
                save.tutorial.currentStepId = (flow?.steps != null && ts.CurrentStepIndex < flow.steps.Length)
                    ? flow.steps[ts.CurrentStepIndex].id
                    : save.tutorial.currentStepId;
                GameLogger.Debug($"[Save] Flush tutorial → step {ts.CurrentStepIndex} ('{save.tutorial.currentStepId}')  active={ts.IsActive}");
            }
        }

        // ── Shop ECS write-through ────────────────────────────────────────────

        /// <summary>Adds entropy directly to the ECS singleton (called from PremiumShopService when game scene is live).</summary>
        public void AddEntropy(long amount)
        {
            if (!IsLoaded || _progressQuery.IsEmpty) return;
            var pp = _progressQuery.GetSingleton<PlayerProgressData>();
            pp.BaseCurrency += amount;
            _progressQuery.SetSingleton(pp);
        }

        /// <summary>Adds prestige currency directly to the ECS singleton (called from PremiumShopService when game scene is live).</summary>
        public void AddPrestigeCurrency(long amount)
        {
            if (!IsLoaded || _prestigeQuery.IsEmpty) return;
            var p = _prestigeQuery.GetSingleton<PrestigeData>();
            p.PrestigeCurrency += amount;
            _prestigeQuery.SetSingleton(p);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static int ResolveStepIndex(string stepId)
        {
            // Empty id is a legitimate fresh-game value — resolve to step 0 silently.
            if (string.IsNullOrEmpty(stepId)) return 0;

            var flow = TutorialFlowSO.Current;
            if (flow?.steps == null)
            {
                GameLogger.Warning($"[Save] Cannot resolve tutorial step '{stepId}' — " +
                          $"TutorialFlowSO.Current is null; defaulting to step 0.");
                return 0;
            }
            for (int i = 0; i < flow.steps.Length; i++)
                if (flow.steps[i].id == stepId) return i;

            GameLogger.Warning($"[Save] Saved tutorial step '{stepId}' not found in flow " +
                      $"({flow.steps.Length} steps) — defaulting to step 0 (id drift?).");
            return 0;
        }
    }
}
