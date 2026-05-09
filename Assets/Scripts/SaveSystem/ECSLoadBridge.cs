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

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                GameLogger.Error("[ECSLoadBridge] No ECS world found.");
                yield break;
            }

            _em = world.EntityManager;
            _progressQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>());
            _tutorialQuery = _em.CreateEntityQuery(
                ComponentType.ReadWrite<TutorialStateData>());

            // Poll until SubScene entities are loaded
            const float kTimeout = 10f;
            float elapsed = 0f;
            while (elapsed < kTimeout)
            {
                if (!_progressQuery.IsEmpty && !_prestigeQuery.IsEmpty && !_inventoryQuery.IsEmpty)
                    break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (_progressQuery.IsEmpty || _prestigeQuery.IsEmpty || _inventoryQuery.IsEmpty)
            {
                GameLogger.Error("[ECSLoadBridge] ECS singletons not found within timeout — save load skipped.");
                yield break;
            }

            ApplyLoadedSave();
            GridSaveService.Instance?.LoadGrid();
            IsLoaded = true;
            GameLogger.Info("[ECSLoadBridge] Save applied to ECS.");
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
            progress.BaseCurrency = save.currentRun.baseCurrency;
            _progressQuery.SetSingleton(progress);

            // Inventory
            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: false);
            buffer.Clear();
            if (save.currentRun.inventory != null)
            {
                foreach (var kv in save.currentRun.inventory)
                {
                    if (!int.TryParse(kv.Key, out int itemId)) continue;
                    if (kv.Value > 0)
                        buffer.Add(new InventorySlot { ItemID = itemId, Quantity = kv.Value });
                }
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
                save.currentRun.baseCurrency = _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;

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
                save.currentRun.inventory ??= new System.Collections.Generic.Dictionary<string, int>();
                save.currentRun.inventory.Clear();
                var invEntity = _inventoryQuery.GetSingletonEntity();
                var buf = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: true);
                foreach (var slot in buf)
                    if (slot.Quantity > 0)
                        save.currentRun.inventory[slot.ItemID.ToString()] = slot.Quantity;
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
