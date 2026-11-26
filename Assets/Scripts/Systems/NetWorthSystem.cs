using System;
using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Calculates PlayerProgressData.NetWorth each frame. Must run before PrestigeSystem so wall
    /// detection sees the current value.
    ///
    /// NetWorth = inventory value (baseSellValue x stars x quantity)
    ///          + stored item value (items waiting in building inputs, craft staging and outputs, at the
    ///            Demon price they would sell for; also written to PlayerProgressData.StoredItemValue for
    ///            the Statistics panel)
    ///          + entropy held (BaseCurrency)
    ///          + entropy spent (TotalEntropySpent: buildings, upgrades, research, unlocks, at the price paid)
    ///          + discounts (TotalEntropyDiscounted: list price minus price paid, so purchases count at full value)
    ///          + BaseNetWorth (dev-skip floor).
    /// Maxwell's Demon is left out of the stored value: it sells its input the frame it arrives.
    /// Prices come from <see cref="ItemValueTable"/>, so the system is Burst-compiled (#284).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PrestigeSystem))]
    public partial struct NetWorthSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerProgressData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var playerEntity = SystemAPI.GetSingletonEntity<PlayerInventoryTag>();
            if (!SystemAPI.HasBuffer<InventorySlot>(playerEntity)) return;
            SystemAPI.TryGetSingleton<ItemValueTable>(out var prices);   // default (not created) = unknown-item prices

            var slots = SystemAPI.GetBuffer<InventorySlot>(playerEntity);
            // double, not float: a float holds integers exactly only up to 2^24 (~16.7M) (#284).
            double inventory = 0d;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Quantity <= 0) continue;
                inventory += (double)prices.Starred(slot.ItemID) * slot.Quantity;
            }

            double stored = 0d;
            foreach (var buf in SystemAPI.Query<DynamicBuffer<BuildingInputSlot>>().WithAll<BuildingData>().WithNone<EntropySinkTag>())
                for (int i = 0; i < buf.Length; i++) stored += Stored(prices, buf[i].ItemID, buf[i].Quantity);
            foreach (var buf in SystemAPI.Query<DynamicBuffer<CraftStagingSlot>>().WithAll<BuildingData>().WithNone<EntropySinkTag>())
                for (int i = 0; i < buf.Length; i++) stored += Stored(prices, buf[i].ItemID, buf[i].Quantity);
            foreach (var buf in SystemAPI.Query<DynamicBuffer<BuildingOutputSlot>>().WithAll<BuildingData>().WithNone<EntropySinkTag>())
                for (int i = 0; i < buf.Length; i++) stored += Stored(prices, buf[i].ItemID, buf[i].Quantity);

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            double desired = Compute(inventory, stored, progress.BaseCurrency, progress.TotalEntropySpent,
                                     progress.TotalEntropyDiscounted, progress.BaseNetWorth);
            if (progress.NetWorth == desired && progress.StoredItemValue == stored) return;
            progress.NetWorth        = desired;
            progress.StoredItemValue = stored;
            SystemAPI.SetSingleton(progress);
        }

        static double Stored(in ItemValueTable prices, int itemId, int quantity) =>
            quantity <= 0 ? 0d : (double)Math.Max(0f, prices.Demon(itemId)) * quantity;

        /// <summary>What <paramref name="quantity"/> stored items are worth at <paramref name="priceOf"/> each; 0 for none.</summary>
        internal static double StoredValue(int itemId, int quantity, Func<int, float> priceOf) =>
            quantity <= 0 ? 0d : (double)Math.Max(0f, priceOf(itemId)) * quantity;

        /// <summary>
        /// Net worth from its parts: inventory value + stored item value + entropy held + entropy spent +
        /// discounts on what was bought + dev-skip floor.
        /// </summary>
        internal static double Compute(double inventoryValue, double storedItemValue, long baseCurrency,
                                       long totalEntropySpent, long totalEntropyDiscounted, double baseNetWorth)
            => inventoryValue + storedItemValue
             + (double)baseCurrency + (double)totalEntropySpent + (double)totalEntropyDiscounted
             + baseNetWorth;
    }
}
