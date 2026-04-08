using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Placeholder power grid system. Clamps CurrentEV to [0, MaxEV].
    /// Full grid linking logic will be added in the Buildings & Automation chapter.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial struct PowerGridSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PowerNodeData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var node in SystemAPI.Query<RefRW<PowerNodeData>>())
            {
                if (node.ValueRO.CurrentEV > node.ValueRO.MaxEV)
                    node.ValueRW.CurrentEV = node.ValueRO.MaxEV;
                if (node.ValueRO.CurrentEV < 0f)
                    node.ValueRW.CurrentEV = 0f;
            }
        }
    }
}
