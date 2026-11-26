using System;
using Unity.Collections;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Calculates PlayerProgressData.NetWorth each frame. Must run before PrestigeSystem so wall
    /// detection sees the current value.
    ///
    /// NetWorth = inventory value (baseSellValue x quantity)
    ///          + stored item value (items waiting in building inputs, craft staging and outputs, at the
    ///            Demon price they would sell for; also written to PlayerProgressData.StoredItemValue for
    ///            the Statistics panel)
    ///          + entropy held (BaseCurrency)
    ///          + entropy spent (TotalEntropySpent: buildings, upgrades, research, unlocks, at the price paid)
    ///          + discounts (TotalEntropyDiscounted: list price minus price paid, so purchases count at full value)
    ///          + BaseNetWorth (dev-skip floor).
    /// Maxwell's Demon is left out of the stored value: it sells its input the frame it arrives.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PrestigeSystem))]
    public partial class NetWorthSystem : SystemBase
    {
        private EntityQuery _inputQuery, _stagingQuery, _outputQuery;
        private static readonly Func<int, float> PriceOf = SurplusSale.DemonPrice;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerProgressData>();
            RequireForUpdate<PlayerInventoryTag>();

            _inputQuery   = BuildingBufferQuery<BuildingInputSlot>();
            _stagingQuery = BuildingBufferQuery<CraftStagingSlot>();
            _outputQuery  = BuildingBufferQuery<BuildingOutputSlot>();
        }

        private EntityQuery BuildingBufferQuery<T>() where T : unmanaged, IBufferElementData =>
            GetEntityQuery(new EntityQueryDesc
            {
                All  = new[] { ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<T>() },
                None = new[] { ComponentType.ReadOnly<EntropySinkTag>() },
            });

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingletonEntity<PlayerInventoryTag>(out var playerEntity)) return;
            if (!EntityManager.HasBuffer<InventorySlot>(playerEntity)) return;

            var slots = EntityManager.GetBuffer<InventorySlot>(playerEntity, isReadOnly: true);
            // double, not float: a float holds integers exactly only up to 2^24 (~16.7M), so above
            // that net worth drifted and the prestige-wall check and the equality early-out below
            // stopped being reliable (#284).
            double inventory = 0d;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Quantity <= 0) continue;
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                if (itemSO == null) continue;
                inventory += (double)ItemStars.StarredBase(itemSO) * slot.Quantity;
            }

            double stored = SumBuffers<BuildingInputSlot>(_inputQuery, s => (s.ItemID, s.Quantity))
                          + SumBuffers<CraftStagingSlot>(_stagingQuery, s => (s.ItemID, s.Quantity))
                          + SumBuffers<BuildingOutputSlot>(_outputQuery, s => (s.ItemID, s.Quantity));

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            double desired = Compute(inventory, stored, progress.BaseCurrency, progress.TotalEntropySpent,
                                     progress.TotalEntropyDiscounted, progress.BaseNetWorth);
            if (progress.NetWorth == desired && progress.StoredItemValue == stored) return;
            progress.NetWorth        = desired;
            progress.StoredItemValue = stored;
            SystemAPI.SetSingleton(progress);
        }

        private double SumBuffers<T>(EntityQuery query, Func<T, (int itemId, int quantity)> read)
            where T : unmanaged, IBufferElementData
        {
            if (query.IsEmpty) return 0d;
            double total = 0d;
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int e = 0; e < entities.Length; e++)
            {
                var buffer = EntityManager.GetBuffer<T>(entities[e], isReadOnly: true);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var (itemId, quantity) = read(buffer[i]);
                    total += StoredValue(itemId, quantity, PriceOf);
                }
            }
            return total;
        }

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
