using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Each frame, drains every BuildingInputSlot on Maxwell's Demon buildings,
    /// converts items to entropy using ItemSO.baseSellValue, and adds the total
    /// to PlayerProgressData.BaseCurrency.
    ///
    /// Cannot be [BurstCompile] because it reads ItemDatabase (a managed MonoBehaviour singleton).
    /// Runs after ConveyorSystem so items deposited this frame are consumed in the same frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ConveyorSystem))]
    public partial struct EntropySinkSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EntropySinkTag>();
            state.RequireForUpdate<PlayerProgressData>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var db = ItemDatabase.Instance;
            long totalEntropy = 0;

            foreach (var (building, inputSlots) in
                SystemAPI.Query<RefRO<BuildingData>, DynamicBuffer<BuildingInputSlot>>()
                    .WithAll<EntropySinkTag>())
            {
                if (!building.ValueRO.IsActive) continue;
                if (inputSlots.Length == 0) continue;

                for (int i = 0; i < inputSlots.Length; i++)
                {
                    int itemID   = inputSlots[i].ItemID;
                    int quantity = inputSlots[i].Quantity;
                    if (quantity <= 0) continue;

                    float sellValue = 1f;
                    if (db != null)
                    {
                        var itemSO = db.Get(itemID);
                        if (itemSO != null && itemSO.baseSellValue > 0f) sellValue = itemSO.baseSellValue;
                    }

                    totalEntropy += (long)(sellValue * quantity);
                }

                inputSlots.Clear();
            }

            if (totalEntropy <= 0) return;

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += totalEntropy;
            SystemAPI.SetSingleton(progress);
        }
    }
}
