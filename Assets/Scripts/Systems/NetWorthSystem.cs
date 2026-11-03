using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Calculates PlayerProgressData.NetWorth each frame from inventory item values.
    /// Must run before PrestigeSystem so wall detection sees the current value.
    ///
    /// NetWorth = sum of (baseSellValue × quantity) for all items in the player inventory.
    /// Building placement costs are not yet tracked in BuildingData, so buildings are
    /// excluded for now — inventory value alone is sufficient for wall detection.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PrestigeSystem))]
    public partial class NetWorthSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerProgressData>();
            RequireForUpdate<PlayerInventoryTag>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingletonEntity<PlayerInventoryTag>(out var playerEntity)) return;
            if (!EntityManager.HasBuffer<InventorySlot>(playerEntity)) return;

            var slots = EntityManager.GetBuffer<InventorySlot>(playerEntity, isReadOnly: true);
            float total = 0f;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Quantity <= 0) continue;
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                if (itemSO == null) continue;
                total += itemSO.baseSellValue * slot.Quantity;
            }

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            float desired = total + (float)(progress.BaseCurrency + progress.TotalEntropySpent) + progress.BaseNetWorth;
            if (progress.NetWorth == desired) return;
            progress.NetWorth = desired;
            SystemAPI.SetSingleton(progress);
        }
    }
}
