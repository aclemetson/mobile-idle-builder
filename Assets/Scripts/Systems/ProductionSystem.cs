using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Each frame:
    ///  1. Checks the global inventory to determine if a building's recipe inputs are available.
    ///  2. Advances production progress while inputs are satisfied.
    ///  3. On completion: consumes inputs, deposits output, resets progress.
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

            foreach (var (building, process, inputs, outputs) in
                SystemAPI.Query<
                    RefRO<BuildingData>,
                    RefRW<RecipeProcessData>,
                    DynamicBuffer<RecipeInputSlot>,
                    DynamicBuffer<RecipeOutputSlot>>())
            {
                if (!building.ValueRO.IsActive)  continue;
                if (process.ValueRO.RecipeID < 0) continue;

                // --- Input check ---
                bool satisfied = true;
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (CountInInventory(inventory, inputs[i].ItemID) < inputs[i].Quantity)
                    {
                        satisfied = false;
                        break;
                    }
                }
                process.ValueRW.InputsSatisfied = satisfied;

                if (!satisfied) continue;

                // --- Progress ---
                process.ValueRW.Progress += deltaTime * building.ValueRO.ProductionSpeed;

                if (process.ValueRO.Progress < process.ValueRO.CraftTime) continue;

                // --- Recipe completion ---
                for (int i = 0; i < inputs.Length; i++)
                    RemoveFromInventory(ref inventory, inputs[i].ItemID, inputs[i].Quantity);

                for (int i = 0; i < outputs.Length; i++)
                    AddToInventory(ref inventory, outputs[i].ItemID, outputs[i].Quantity);

                process.ValueRW.Progress        = 0f;
                process.ValueRW.InputsSatisfied = false;
            }
        }

        // ---- Inventory helpers ----

        private static int CountInInventory(DynamicBuffer<InventorySlot> inv, int itemID)
        {
            for (int i = 0; i < inv.Length; i++)
                if (inv[i].ItemID == itemID) return inv[i].Quantity;
            return 0;
        }

        private static void AddToInventory(ref DynamicBuffer<InventorySlot> inv, int itemID, int qty)
        {
            for (int i = 0; i < inv.Length; i++)
            {
                if (inv[i].ItemID != itemID) continue;
                inv[i] = new InventorySlot { ItemID = itemID, Quantity = inv[i].Quantity + qty };
                return;
            }
            inv.Add(new InventorySlot { ItemID = itemID, Quantity = qty });
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
