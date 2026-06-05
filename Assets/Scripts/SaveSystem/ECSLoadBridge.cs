using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

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

        EntityManager _em;
        EntityQuery   _progressQuery;
        EntityQuery   _prestigeQuery;
        EntityQuery   _inventoryQuery;
        EntityQuery   _tutorialQuery;

        protected override void Awake()
        {
            base.Awake();
        }

        IEnumerator Start()
        {
            if (Instance != this) yield break;
            GameLogger.Info("[ECSLoadBridge] Start — polling for ECS world");

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
                if (!_progressQuery.IsEmpty && !_prestigeQuery.IsEmpty && !_inventoryQuery.IsEmpty)
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

            GameLogger.Info($"[ECSLoadBridge] All entities found after {elapsed:F1}s — applying save");
            ApplyLoadedSave();
            GridSaveService.Instance?.LoadGrid();
            IsLoaded = true;
            GameLogger.Info("[ECSLoadBridge] Save applied to ECS — IsLoaded=true");
        }

        // ── Load path ─────────────────────────────────────────────────────

        void ApplyLoadedSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            // Prestige multipliers — always safe to apply (defaults match SaveData defaults)
            var prestige = _prestigeQuery.GetSingleton<PrestigeData>();
            prestige.RunCount            = save.prestigeCount;
            prestige.PrestigeCurrency    = save.prestigeCurrency;
            prestige.SpeedMultiplier     = save.prestigeSpeedMultiplier;
            prestige.OutputMultiplier    = save.prestigeOutputMultiplier;
            prestige.CostReduction       = save.prestigeCostReduction;
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
            }

            // Prestige
            if (!_prestigeQuery.IsEmpty)
            {
                var p = _prestigeQuery.GetSingleton<PrestigeData>();
                save.prestigeCount              = p.RunCount;
                save.prestigeCurrency           = p.PrestigeCurrency;
                save.prestigeSpeedMultiplier    = p.SpeedMultiplier;
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
                save.tutorial.hasCompletedFirstRun = ts.FirstRunComplete;
                save.tutorial.currentStepId = (flow?.steps != null && ts.CurrentStepIndex < flow.steps.Length)
                    ? flow.steps[ts.CurrentStepIndex].id
                    : save.tutorial.currentStepId;
                GameLogger.Debug($"[Save] Flush tutorial → step {ts.CurrentStepIndex} ('{save.tutorial.currentStepId}')  active={ts.IsActive}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static int ResolveStepIndex(string stepId)
        {
            var flow = TutorialFlowSO.Current;
            if (flow?.steps == null || string.IsNullOrEmpty(stepId)) return 0;
            for (int i = 0; i < flow.steps.Length; i++)
                if (flow.steps[i].id == stepId) return i;
            return 0;
        }
    }
}
