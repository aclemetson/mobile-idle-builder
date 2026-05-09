using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Autonomously produces items from field-collector buildings at a fixed rate.
    ///
    /// Unlike ProductionSystem (which requires IsCrafting to be manually triggered),
    /// this system runs continuously while the building is active, depositing one item
    /// per interval into BuildingOutputSlot. ConveyorSystem then drains that buffer
    /// onto connected belt segments.
    ///
    /// Runs before ConveyorSystem so items are available to be pulled in the same frame
    /// they are produced.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ConveyorSystem))]
    public partial struct CollectorSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CollectorData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (collector, building, recipeOutputSlots, outputSlots, invConfig) in
                SystemAPI.Query<
                    RefRW<CollectorData>,
                    RefRO<BuildingData>,
                    DynamicBuffer<RecipeOutputSlot>,
                    DynamicBuffer<BuildingOutputSlot>,
                    RefRO<BuildingInventoryConfig>>())
            {
                if (!building.ValueRO.IsActive) continue;
                if (recipeOutputSlots.Length == 0) continue;

                collector.ValueRW.Timer += deltaTime;

                float rate     = collector.ValueRO.OutputRate > 0f ? collector.ValueRO.OutputRate : 1f;
                float interval = 1f / rate;

                if (collector.ValueRO.Timer < interval) continue;

                int total = 0;
                for (int i = 0; i < outputSlots.Length; i++)
                    total += outputSlots[i].Quantity;

                if (total < invConfig.ValueRO.OutputCapacity)
                    SlotBufferUtils.AddToOutputBuffer(outputSlots, recipeOutputSlots[0].ItemID, 1);

                // Subtract interval rather than resetting to preserve sub-interval remainder
                collector.ValueRW.Timer -= interval;
            }
        }
    }
}
