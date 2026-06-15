using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Each frame:
    ///  1. Checks the building's local input inventory (BuildingInputSlot) for recipe inputs,
    ///     falling back to the global InventorySlot singleton when the local buffer is empty.
    ///  2. Advances production progress while inputs are satisfied.
    ///  3. On completion: consumes inputs, deposits output into BuildingOutputSlot (local output
    ///     buffer), pausing production if the output buffer is at capacity.
    ///
    /// This replaces the old behaviour where all outputs went directly to the global inventory.
    /// Conveyors pull from BuildingOutputSlot and push to BuildingInputSlot.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ProductionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerInventoryTag>();
            state.RequireForUpdate<BuildingData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var inventory   = SystemAPI.GetSingletonBuffer<InventorySlot>();

            foreach (var (building, process, inputs, outputs, localIn, localOut, invConfig, entity) in
                SystemAPI.Query<
                    RefRO<BuildingData>,
                    RefRW<RecipeProcessData>,
                    DynamicBuffer<RecipeInputSlot>,
                    DynamicBuffer<RecipeOutputSlot>,
                    DynamicBuffer<BuildingInputSlot>,
                    DynamicBuffer<BuildingOutputSlot>,
                    RefRO<BuildingInventoryConfig>>().WithEntityAccess())
            {
                if (!building.ValueRO.IsActive)  continue;
                if (process.ValueRO.RecipeID < 0) continue;

                // ---- Power availability (proximity grid) ----
                // Power consumers carry PowerStatus, written by PowerGridSystem earlier this frame.
                // ThrottleRatio is 0 when disconnected (no generator in range), <1 during a brownout,
                // and 1 when supply meets demand. Buildings without PowerStatus run unthrottled.
                float powerRatio = SystemAPI.HasComponent<PowerStatus>(entity)
                    ? SystemAPI.GetComponent<PowerStatus>(entity).ThrottleRatio
                    : 1f;

                // ---- Input availability check (drives InputsSatisfied flag for UI) ----
                bool satisfied = true;
                bool useLocalInput = localIn.Length > 0;

                for (int i = 0; i < inputs.Length; i++)
                {
                    int have = useLocalInput
                        ? SlotBufferUtils.CountInInputBuffer(localIn, inputs[i].ItemID)
                        : SlotBufferUtils.CountInInventory(inventory, inputs[i].ItemID);

                    if (have < inputs[i].Quantity) { satisfied = false; break; }
                }
                process.ValueRW.InputsSatisfied = satisfied;

                // Auto-start: begin a new craft cycle whenever inputs are ready.
                // Guard: collectors have no RecipeInputSlots and are driven by CollectorSystem;
                // only trigger ProductionSystem auto-start for buildings that consume inputs.
                if (inputs.Length > 0 && satisfied && powerRatio > 0f && !process.ValueRO.IsCrafting)
                    process.ValueRW.IsCrafting = true;

                if (!process.ValueRO.IsCrafting) continue;

                // ---- Output capacity check — pause if output buffer full ----
                int totalOutput = 0;
                for (int i = 0; i < localOut.Length; i++)
                    totalOutput += localOut[i].Quantity;

                if (totalOutput >= invConfig.ValueRO.OutputCapacity)
                {
                    process.ValueRW.IsCrafting = false;
                    continue;
                }

                // ---- Progress (scaled by available power; 0 stalls, <1 is a brownout) ----
                process.ValueRW.Progress += deltaTime * building.ValueRO.ProductionSpeed * powerRatio;

                if (process.ValueRO.Progress < process.ValueRO.CraftTime) continue;

                // ---- Recipe completion: re-verify inputs ----
                bool canComplete = true;
                for (int i = 0; i < inputs.Length; i++)
                {
                    int have = useLocalInput
                        ? SlotBufferUtils.CountInInputBuffer(localIn, inputs[i].ItemID)
                        : SlotBufferUtils.CountInInventory(inventory, inputs[i].ItemID);

                    if (have < inputs[i].Quantity) { canComplete = false; break; }
                }

                if (canComplete)
                {
                    // Consume inputs
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        if (useLocalInput)
                            SlotBufferUtils.RemoveFromInputBuffer(localIn, inputs[i].ItemID, inputs[i].Quantity);
                        else
                            SlotBufferUtils.RemoveFromInventory(ref inventory, inputs[i].ItemID, inputs[i].Quantity);
                    }

                    // Deposit outputs to local output buffer. An OutputQuantity manager (if assigned)
                    // multiplies the deposited amount; AppliedOutputMult is 1 for every other case.
                    float outMult = SystemAPI.HasComponent<ManagerAssignmentData>(entity)
                        ? SystemAPI.GetComponent<ManagerAssignmentData>(entity).AppliedOutputMult
                        : 1f;
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        int qty = (int)(outputs[i].Quantity * outMult); // floor; outMult >= 1
                        SlotBufferUtils.AddToOutputBuffer(localOut, outputs[i].ItemID, qty);
                    }
                }

                process.ValueRW.Progress   = 0f;
                process.ValueRW.IsCrafting = false;
            }
        }

    }
}
