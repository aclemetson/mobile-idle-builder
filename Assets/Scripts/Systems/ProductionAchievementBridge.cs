using System.Collections.Generic;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Drains the <see cref="CraftedOutputEvent"/> buffer that ProductionSystem fills on each recipe
    /// completion, and reports every craft to AchievementService and TierProgress.
    ///
    /// This used to infer crafts by diffing BuildingOutputSlot totals frame to frame, which was wrong twice
    /// over. It lost crafts: ConveyorSystem and this system are both only UpdateAfter(ProductionSystem) with
    /// no ordering between them, so any craft a belt drained before the sample simply never counted — and a
    /// hungry belt is the steady state of a working factory. And it could not say WHICH item was made (a
    /// total is just a number), so it reported an empty item id, which meant automated production credited
    /// only the wildcard craft achievements and never an item-specific one.
    ///
    /// Reading the recorded events instead is exact — real item, real quantity, no ordering assumptions.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial class ProductionAchievementBridge : SystemBase
    {
        // Reused across frames: the common case (nothing crafted) then allocates nothing.
        readonly List<CraftedOutputEvent> _drained = new();

        protected override void OnUpdate()
        {
            _drained.Clear();
            var drained = _drained;

            Entities
                .WithAll<RecipeProcessData>()
                .WithoutBurst()
                .ForEach((ref DynamicBuffer<CraftedOutputEvent> crafted) =>
                {
                    if (crafted.Length == 0) return;

                    for (int i = 0; i < crafted.Length; i++)
                        if (crafted[i].Quantity > 0)
                            drained.Add(crafted[i]);

                    crafted.Clear();
                }).Run();

            // Notified only after the ForEach completes: TierProgress writes the PlayerProgressData
            // singleton, and a structural/singleton write from inside an in-flight ForEach trips ECS
            // safety checks.
            for (int i = 0; i < _drained.Count; i++)
            {
                var evt = _drained[i];

                // Achievements match on the item's string id. An unknown item still counts toward the
                // wildcard ("any craft") achievements, so fall back to "" rather than dropping the craft.
                string itemId = ItemDatabase.GetStatic(evt.ItemID)?.id ?? "";
                AchievementService.Instance?.NotifyCraft(itemId, evt.Quantity);
                TierProgress.NotifyItemProduced(evt.ItemID);
            }
        }
    }
}
