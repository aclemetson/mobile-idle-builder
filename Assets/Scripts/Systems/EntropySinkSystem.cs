using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Each frame, drains every BuildingInputSlot on Maxwell's Demon buildings,
    /// converts items to entropy at their Demon price (ItemSO.baseSellValue, 1 when it has none, times
    /// tournament stars), and adds the total to PlayerProgressData.BaseCurrency.
    ///
    /// Prices come from <see cref="ItemValueTable"/> (built by ItemValueTableSystem), so this is Burst-compiled
    /// (#284). Without the table every item pays 1.
    /// Runs after ConveyorSystem so items deposited this frame are consumed in the same frame.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ConveyorSystem))]
    public partial struct EntropySinkSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EntropySinkTag>();
            state.RequireForUpdate<PlayerProgressData>();

            // What the Demon consumed, for the world delivery tasks (WorldTaskService drains it each frame).
            var log = state.EntityManager.CreateEntity(typeof(DemonDeliveryLog));
            state.EntityManager.AddBuffer<DemonDelivery>(log);
        }

        // Drops the log when nothing drains it (no WorldTaskService, e.g. tests), so it can't grow without bound.
        const int MaxLoggedDeliveries = 512;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SystemAPI.TryGetSingleton<ItemValueTable>(out var prices);   // default (not created) = 1 per item
            long totalEntropy = 0;
            // Cyclotron Kick boost (1 when none is running).
            float boost = SystemAPI.TryGetSingleton<EntropyIncomeBoost>(out var eb) && eb.Mult > 0f ? eb.Mult : 1f;
            // Speed burst: items arrive faster, so "unboosted" income (Entropy Surge pricing) takes the clock back out.
            float clockRate = FactoryClock.Sanitize(SystemAPI.TryGetSingleton<FactoryClock>(out var fc) ? fc.Rate : 1f);
            bool logging = SystemAPI.TryGetSingletonBuffer<DemonDelivery>(out var deliveries);
            if (logging && deliveries.Length > MaxLoggedDeliveries) deliveries.Clear();

            foreach (var (building, inputSlots, entity) in
                SystemAPI.Query<RefRO<BuildingData>, DynamicBuffer<BuildingInputSlot>>()
                    .WithAll<EntropySinkTag>().WithEntityAccess())
            {
                if (!building.ValueRO.IsActive) continue;
                if (inputSlots.Length == 0) continue;
                // Only Maxwell's Demon logs deliveries for the world tasks (the Chemistry gate is fed in Physics);
                // Chemistry's Calorimeter pays the same entropy but never counts toward them.
                bool demon = SystemAPI.HasComponent<DemonTag>(entity);

                for (int i = 0; i < inputSlots.Length; i++)
                {
                    int itemID   = inputSlots[i].ItemID;
                    int quantity = inputSlots[i].Quantity;
                    if (quantity <= 0) continue;

                    float sellValue = prices.Demon(itemID);   // base (1 when none) x tournament stars

                    totalEntropy += (long)(sellValue * quantity * boost);
                    if (logging && demon) deliveries.Add(new DemonDelivery { ItemID = itemID, Quantity = quantity });
                }

                inputSlots.Clear();
            }

            if (totalEntropy <= 0) return;

            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += totalEntropy;
            SystemAPI.SetSingleton(progress);

            if (SystemAPI.HasSingleton<AutomationIncome>())
            {
                var income = SystemAPI.GetSingleton<AutomationIncome>();
                income.Total     += totalEntropy;
                income.Unboosted += totalEntropy / ((double)boost * clockRate);
                SystemAPI.SetSingleton(income);
            }
        }
    }
}
