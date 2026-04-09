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

            foreach (var (building, process, inputs, outputs, localIn, localOut, invConfig) in
                SystemAPI.Query<
                    RefRO<BuildingData>,
                    RefRW<RecipeProcessData>,
                    DynamicBuffer<RecipeInputSlot>,
                    DynamicBuffer<RecipeOutputSlot>,
                    DynamicBuffer<BuildingInputSlot>,
                    DynamicBuffer<BuildingOutputSlot>,
                    RefRO<BuildingInventoryConfig>>())
            {
                if (!building.ValueRO.IsActive)  continue;
                if (process.ValueRO.RecipeID < 0) continue;

                // ---- Input availability check (drives InputsSatisfied flag for UI) ----
                bool satisfied = true;
                bool useLocalInput = localIn.Length > 0;

                for (int i = 0; i < inputs.Length; i++)
                {
                    int have = useLocalInput
                        ? CountInInputBuffer(localIn, inputs[i].ItemID)
                        : CountInInventory(inventory, inputs[i].ItemID);

                    if (have < inputs[i].Quantity) { satisfied = false; break; }
                }
                process.ValueRW.InputsSatisfied = satisfied;

                // ---- Manual trigger gate ----
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

                // ---- Progress ----
                process.ValueRW.Progress += deltaTime * building.ValueRO.ProductionSpeed;

                if (process.ValueRO.Progress < process.ValueRO.CraftTime) continue;

                // ---- Recipe completion: re-verify inputs ----
                bool canComplete = true;
                for (int i = 0; i < inputs.Length; i++)
                {
                    int have = useLocalInput
                        ? CountInInputBuffer(localIn, inputs[i].ItemID)
                        : CountInInventory(inventory, inputs[i].ItemID);

                    if (have < inputs[i].Quantity) { canComplete = false; break; }
                }

                if (canComplete)
                {
                    // Consume inputs
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        if (useLocalInput)
                            RemoveFromInputBuffer(localIn, inputs[i].ItemID, inputs[i].Quantity);
                        else
                            RemoveFromInventory(ref inventory, inputs[i].ItemID, inputs[i].Quantity);
                    }

                    // Deposit outputs to local output buffer
                    for (int i = 0; i < outputs.Length; i++)
                        AddToOutputBuffer(localOut, outputs[i].ItemID, outputs[i].Quantity);
                }

                process.ValueRW.Progress   = 0f;
                process.ValueRW.IsCrafting = false;
            }
        }

        // ================================================================
        // Local BuildingInputSlot helpers
        // ================================================================

        private static int CountInInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ItemID == itemID) return buf[i].Quantity;
            return 0;
        }

        private static void RemoveFromInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                int remaining = buf[i].Quantity - qty;
                if (remaining <= 0)
                    buf.RemoveAt(i);
                else
                    buf[i] = new BuildingInputSlot { ItemID = itemID, Quantity = remaining };
                return;
            }
        }

        // ================================================================
        // Local BuildingOutputSlot helpers
        // ================================================================

        private static void AddToOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                buf[i] = new BuildingOutputSlot { ItemID = itemID, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new BuildingOutputSlot { ItemID = itemID, Quantity = qty });
        }

        // ================================================================
        // Global InventorySlot helpers (used as fallback when no local input)
        // ================================================================

        private static int CountInInventory(DynamicBuffer<InventorySlot> inv, int itemID)
        {
            for (int i = 0; i < inv.Length; i++)
                if (inv[i].ItemID == itemID) return inv[i].Quantity;
            return 0;
        }

        private static void RemoveFromInventory(ref DynamicBuffer<InventorySlot> inv, int itemID, int qty)
        {
            for (int i = 0; i < inv.Length; i++)
            {
                if (inv[i].ItemID != itemID) continue;
                int remaining = inv[i].Quantity - qty;
                if (remaining <= 0)
                    inv.RemoveAt(i);
                else
                    inv[i] = new InventorySlot { ItemID = itemID, Quantity = remaining };
                return;
            }
        }
    }
}
