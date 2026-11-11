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

            // Global multipliers (1 when a singleton is absent): megastructure and prestige shop. Speed now
            // applies to harvesters too, as it always has in the idle snapshot (IdleGraphAnalyzer scales
            // collector rates by the global speed); without it a speed bonus could never lift a chain that
            // starts at a harvester.
            float globalOutputMult = 1f;
            float globalSpeedMult  = 1f;
            if (SystemAPI.HasSingleton<GlobalProductionBonus>())
            {
                var gb = SystemAPI.GetSingleton<GlobalProductionBonus>();
                globalOutputMult = gb.OutputMult;
                globalSpeedMult  = gb.SpeedMult;
            }
            if (SystemAPI.HasSingleton<PrestigeData>())
            {
                var pd = SystemAPI.GetSingleton<PrestigeData>();
                if (pd.SpeedMultiplier  > 0f) globalSpeedMult  *= pd.SpeedMultiplier;
                if (pd.OutputMultiplier > 0f) globalOutputMult *= pd.OutputMultiplier;
            }
            if (globalSpeedMult <= 0f) globalSpeedMult = 1f;

            foreach (var (collector, building, recipeOutputSlots, outputSlots, invConfig, entity) in
                SystemAPI.Query<
                    RefRW<CollectorData>,
                    RefRO<BuildingData>,
                    DynamicBuffer<RecipeOutputSlot>,
                    DynamicBuffer<BuildingOutputSlot>,
                    RefRO<BuildingInventoryConfig>>().WithEntityAccess())
            {
                if (!building.ValueRO.IsActive) continue;
                if (recipeOutputSlots.Length == 0) continue;

                collector.ValueRW.Timer += deltaTime * globalSpeedMult;

                float rate     = collector.ValueRO.OutputRate > 0f ? collector.ValueRO.OutputRate : 1f;
                float interval = 1f / rate;

                if (collector.ValueRO.Timer < interval) continue;

                int total = 0;
                for (int i = 0; i < outputSlots.Length; i++)
                    total += outputSlots[i].Quantity;

                // An OutputQuantity manager (if assigned) multiplies the per-interval deposit;
                // AppliedOutputMult is 1 for every other case. Mirrors ProductionSystem.
                float outMult = SystemAPI.HasComponent<ManagerAssignmentData>(entity)
                    ? SystemAPI.GetComponent<ManagerAssignmentData>(entity).AppliedOutputMult
                    : 1f;
                outMult *= globalOutputMult; // global output bonuses compose on top of managers

                if (total < invConfig.ValueRO.OutputCapacity)
                {
                    float carry = collector.ValueRO.OutputCarry;
                    int amount  = OutputCarry.Take(outMult, ref carry);
                    collector.ValueRW.OutputCarry = carry;
                    if (amount > 0)
                        SlotBufferUtils.AddToOutputBuffer(outputSlots, recipeOutputSlots[0].ItemID, amount);
                }

                // Subtract interval rather than resetting to preserve sub-interval remainder
                collector.ValueRW.Timer -= interval;
            }
        }
    }
}
