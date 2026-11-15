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
            // double, not float: a float holds integers exactly only up to 2^24 (~16.7M), so above
            // that net worth drifted and the prestige-wall check and the equality early-out below
            // stopped being reliable (#284).
            double total = 0d;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Quantity <= 0) continue;
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                if (itemSO == null) continue;
                total += (double)itemSO.baseSellValue * slot.Quantity;
            }

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            double desired = Compute(total, progress.BaseCurrency, progress.TotalEntropySpent, progress.BaseNetWorth);
            if (progress.NetWorth == desired) return;
            progress.NetWorth = desired;
            SystemAPI.SetSingleton(progress);
        }

        /// <summary>Net worth from its parts: inventory value + entropy held + entropy spent + dev-skip floor.</summary>
        internal static double Compute(double inventoryValue, long baseCurrency, long totalEntropySpent, double baseNetWorth)
            => inventoryValue + (double)(baseCurrency + totalEntropySpent) + baseNetWorth;
    }
}
