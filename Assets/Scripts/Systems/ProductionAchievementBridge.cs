using System.Collections.Generic;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Non-Burst managed system that detects when automated production completes a cycle
    /// and notifies AchievementService. Runs after ProductionSystem so output buffer
    /// changes are already committed when we sample them.
    ///
    /// Strategy: each frame, compare current BuildingOutputSlot totals to the previous
    /// frame's snapshot. An increase means at least one recipe cycle deposited output.
    /// Collector buildings (no RecipeProcessData) are excluded from the query.
    ///
    /// Totals are tracked twice, deliberately: in aggregate per building (what the craft
    /// achievements consume, unchanged) and per item (what tier progression needs — it has to know
    /// WHICH item was produced to read its tier).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial class ProductionAchievementBridge : SystemBase
    {
        readonly Dictionary<Entity, int>          _prevOutputTotal = new();
        readonly Dictionary<(Entity, int), int>   _prevByItem      = new();

        // Reused across frames so the per-frame path allocates nothing when nothing is produced.
        readonly List<int> _producedItemIds = new();

        protected override void OnUpdate()
        {
            var toRemove = new List<Entity>();
            foreach (var key in _prevOutputTotal.Keys)
            {
                if (!EntityManager.Exists(key))
                    toRemove.Add(key);
            }
            foreach (var e in toRemove)
            {
                _prevOutputTotal.Remove(e);
                PurgeItemEntries(e);
            }

            _producedItemIds.Clear();
            var produced = _producedItemIds;
            var prevByItem = _prevByItem;

            Entities
                .WithAll<RecipeProcessData>()
                .WithoutBurst()
                .ForEach((Entity entity, in DynamicBuffer<BuildingOutputSlot> outputSlots) =>
                {
                    int total = 0;
                    for (int i = 0; i < outputSlots.Length; i++)
                    {
                        var slot = outputSlots[i];
                        total += slot.Quantity;

                        var key = (entity, slot.ItemID);
                        if (prevByItem.TryGetValue(key, out int prevQty) && slot.Quantity > prevQty)
                            produced.Add(slot.ItemID);
                        prevByItem[key] = slot.Quantity;
                    }

                    if (_prevOutputTotal.TryGetValue(entity, out int prev) && total > prev)
                        AchievementService.Instance?.NotifyCraft("", total - prev);

                    _prevOutputTotal[entity] = total;
                }).Run();

            // Deferred until after the ForEach: TierProgress touches the PlayerProgressData singleton,
            // and writing a singleton from inside an in-flight Entities.ForEach trips ECS safety checks.
            for (int i = 0; i < _producedItemIds.Count; i++)
                TierProgress.NotifyItemProduced(_producedItemIds[i]);
        }

        void PurgeItemEntries(Entity dead)
        {
            var stale = new List<(Entity, int)>();
            foreach (var key in _prevByItem.Keys)
                if (key.Item1 == dead) stale.Add(key);
            foreach (var key in stale)
                _prevByItem.Remove(key);
        }
    }
}
