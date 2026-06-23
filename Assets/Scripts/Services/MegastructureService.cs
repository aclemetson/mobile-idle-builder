using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Drives the Dyson Sphere megastructure: a single multi-stage construction project the player feeds
    /// tier-5 components into. Each completed stage grants a permanent, stacking global reward.
    ///
    /// This is a meta-progression layer ABOVE prestige: completed stages and partial contributions both
    /// survive prestige, so all state lives in the persistent section of <see cref="SaveData"/>
    /// (<c>megastructureStage</c> / <c>megastructureContrib*</c>) and PrestigeSystem never clears it.
    ///
    /// Reward wiring:
    ///  - Output / speed bonuses are pushed into the <see cref="GlobalProductionBonus"/> ECS singleton,
    ///    read live (Burst) by ProductionSystem / CollectorSystem and folded into the idle snapshot.
    ///  - The prestige-gain bonus is read directly by PrestigeSystem via <see cref="GetPrestigeGainBonus"/>.
    /// </summary>
    public class MegastructureService : SingletonMonoBehaviour<MegastructureService>
    {
        private MegastructureSO _data;

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _bonusQuery;
        private bool          _ecsReady;

        // In-memory mirror of the persistent save state (authoritative copy lives in SaveData).
        private int _completedStages;
        private readonly Dictionary<int, int> _contrib = new(); // itemId -> contributed toward current stage

        /// <summary>Fired whenever contributions change or a stage completes (UI refresh).</summary>
        public event Action OnChanged;
        /// <summary>Fired with the just-completed stage when a stage finishes.</summary>
        public event Action<MegastructureStage> OnStageCompleted;

        public MegastructureSO Data => _data;
        public int  CompletedStages => _completedStages;
        public bool IsComplete      => _data != null && _data.stages != null && _completedStages >= _data.stages.Length;

        /// <summary>The stage currently being built, or null if the project is complete / not loaded.</summary>
        public MegastructureStage CurrentStage =>
            (_data?.stages != null && _completedStages >= 0 && _completedStages < _data.stages.Length)
                ? _data.stages[_completedStages]
                : null;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _data = Resources.Load<MegastructureSO>("Megastructure");
            if (_data == null)
                GameLogger.Warning("[MegastructureService] Megastructure asset not found in Resources — run Import Game Data.");
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _em = world.EntityManager;
                _inventoryQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerInventoryTag>(),
                    ComponentType.ReadWrite<InventorySlot>());
                _bonusQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<GlobalProductionBonus>());
                _ecsReady = true;
            }

            LoadFromSave(SaveManager.Instance?.Current);
            ApplyBonusToECS();
        }

        // ── Gating ───────────────────────────────────────────────────────────

        /// <summary>True once the gating research is unlocked (panel/button should be visible).</summary>
        public bool IsUnlocked()
        {
            if (_data == null || string.IsNullOrEmpty(_data.requiredResearch)) return false;
            return ResearchService.Instance != null && ResearchService.Instance.IsUnlocked(_data.requiredResearch);
        }

        // ── Stage queries ──────────────────────────────────────────────────────

        public int GetContributed(int itemId) => _contrib.TryGetValue(itemId, out var c) ? c : 0;

        /// <summary>Quantity of <paramref name="itemId"/> the given stage still needs.</summary>
        public int GetRemaining(MegastructureStage stage, int itemId)
        {
            if (stage?.costItems == null) return 0;
            for (int i = 0; i < stage.costItems.Length; i++)
            {
                if (stage.costItems[i] == null || stage.costItems[i].itemId != itemId) continue;
                int need = (i < stage.costQuantities.Length) ? stage.costQuantities[i] : 0;
                return Mathf.Max(0, need - GetContributed(itemId));
            }
            return 0;
        }

        /// <summary>Current count of <paramref name="itemId"/> in the global inventory.</summary>
        public int InventoryCount(int itemId)
        {
            if (!_ecsReady || _inventoryQuery.IsEmptyIgnoreFilter) return 0;
            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            return SlotBufferUtils.CountInInventory(buffer, itemId);
        }

        // ── Contribution ─────────────────────────────────────────────────────

        /// <summary>
        /// Contributes exactly <paramref name="requestedQty"/> of <paramref name="itemId"/> (clamped down to
        /// what the stage still needs — over-contribution is harmless). If the inventory cannot cover that
        /// clamped amount the contribution is rejected and nothing is deducted. Returns the quantity
        /// actually contributed. Use <see cref="ContributeAll"/> for "deposit whatever I have".
        /// </summary>
        public int Contribute(int itemId, int requestedQty)
        {
            if (requestedQty <= 0 || !CanContribute(itemId, out var stage, out int remaining, out int have))
                return 0;

            int want = Mathf.Min(requestedQty, remaining); // clamp over-contribution to what's needed
            if (have < want) return 0;                      // insufficient inventory -> reject, deduct nothing
            return Deduct(itemId, want, stage);
        }

        /// <summary>Contributes as much of <paramref name="itemId"/> as the inventory holds, up to remaining.</summary>
        public int ContributeAll(int itemId)
        {
            if (!CanContribute(itemId, out var stage, out int remaining, out int have)) return 0;
            int qty = Mathf.Min(remaining, have);
            return qty > 0 ? Deduct(itemId, qty, stage) : 0;
        }

        private bool CanContribute(int itemId, out MegastructureStage stage, out int remaining, out int have)
        {
            stage = null; remaining = 0; have = 0;
            // Block only while a live bridge is mid-load; tests run with no bridge.
            if (ECSLoadBridge.Instance != null && !ECSLoadBridge.Instance.IsLoaded) return false;
            if (!_ecsReady || _inventoryQuery.IsEmptyIgnoreFilter) return false;

            stage = CurrentStage;
            if (stage == null) return false;
            remaining = GetRemaining(stage, itemId);
            if (remaining <= 0) return false;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            have = SlotBufferUtils.CountInInventory(buffer, itemId);
            return true;
        }

        private int Deduct(int itemId, int qty, MegastructureStage stage)
        {
            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: false);
            SlotBufferUtils.RemoveFromInventory(ref buffer, itemId, qty);
            _contrib[itemId] = GetContributed(itemId) + qty;

            bool completed = TryCompleteStage(stage);

            FlushToSave(SaveManager.Instance?.Current);
            SaveManager.Instance?.SaveLocal();
            OnChanged?.Invoke();
            if (completed)
            {
                OnStageCompleted?.Invoke(stage);
                TelemetryService.Instance?.RecordMegastructureStage(SaveManager.Instance?.Current?.megastructureStage ?? 0);
            }
            return qty;
        }

        private bool TryCompleteStage(MegastructureStage stage)
        {
            if (stage?.costItems == null) return false;
            for (int i = 0; i < stage.costItems.Length; i++)
            {
                if (stage.costItems[i] == null) continue;
                int need = (i < stage.costQuantities.Length) ? stage.costQuantities[i] : 0;
                if (GetContributed(stage.costItems[i].itemId) < need) return false;
            }

            _completedStages++;
            _contrib.Clear();
            ApplyBonusToECS();
            GameLogger.Info($"[MegastructureService] Stage complete: {stage.displayName} ({_completedStages}/{_data.stages.Length})");
            return true;
        }

        // ── Reward queries ─────────────────────────────────────────────────────

        public float GetOutputBonus()       => SumReward(MegastructureRewardType.OutputMultiplier);
        public float GetSpeedBonus()         => SumReward(MegastructureRewardType.SpeedMultiplier);
        public float GetPrestigeGainBonus()  => SumReward(MegastructureRewardType.PrestigeGainMultiplier);

        private float SumReward(MegastructureRewardType type)
        {
            if (_data?.stages == null) return 0f;
            float sum = 0f;
            int n = Mathf.Min(_completedStages, _data.stages.Length);
            for (int i = 0; i < n; i++)
                if (_data.stages[i] != null && _data.stages[i].rewardType == type)
                    sum += _data.stages[i].rewardValue;
            return sum;
        }

        /// <summary>Pushes the current output/speed bonuses into the GlobalProductionBonus ECS singleton.</summary>
        public void ApplyBonusToECS()
        {
            if (!_ecsReady || _bonusQuery.IsEmptyIgnoreFilter) return;
            _bonusQuery.SetSingleton(new GlobalProductionBonus
            {
                OutputMult = 1f + GetOutputBonus(),
                SpeedMult  = 1f + GetSpeedBonus()
            });
        }

        // ── Save sync ──────────────────────────────────────────────────────────

        public void LoadFromSave(SaveData save)
        {
            _contrib.Clear();
            _completedStages = 0;
            if (save == null) return;

            _completedStages = Mathf.Max(0, save.megastructureStage);

            var keys = save.megastructureContribKeys;
            var vals = save.megastructureContribValues;
            int count = Math.Min(keys?.Count ?? 0, vals?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (int.TryParse(keys[i], out int itemId) && vals[i] > 0)
                    _contrib[itemId] = vals[i];
            }
        }

        public void FlushToSave(SaveData save)
        {
            if (save == null) return;
            save.megastructureStage = _completedStages;
            save.megastructureContribKeys   ??= new List<string>();
            save.megastructureContribValues ??= new List<int>();
            save.megastructureContribKeys.Clear();
            save.megastructureContribValues.Clear();
            foreach (var kv in _contrib)
            {
                if (kv.Value <= 0) continue;
                save.megastructureContribKeys.Add(kv.Key.ToString());
                save.megastructureContribValues.Add(kv.Value);
            }
        }
    }
}
