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
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial class ProductionAchievementBridge : SystemBase
    {
        readonly Dictionary<Entity, int> _prevOutputTotal = new();

        protected override void OnUpdate()
        {
            var toRemove = new List<Entity>();
            foreach (var key in _prevOutputTotal.Keys)
            {
                if (!EntityManager.Exists(key))
                    toRemove.Add(key);
            }
            foreach (var e in toRemove)
                _prevOutputTotal.Remove(e);

            Entities
                .WithAll<RecipeProcessData>()
                .WithoutBurst()
                .ForEach((Entity entity, in DynamicBuffer<BuildingOutputSlot> outputSlots) =>
                {
                    int total = 0;
                    for (int i = 0; i < outputSlots.Length; i++)
                        total += outputSlots[i].Quantity;

                    if (_prevOutputTotal.TryGetValue(entity, out int prev) && total > prev)
                        AchievementService.Instance?.NotifyCraft("", total - prev);

                    _prevOutputTotal[entity] = total;
                }).Run();
        }
    }
}
