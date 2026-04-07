using Unity.Burst;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles the prestige loop:
    ///   1. Detects when the player requests a prestige.
    ///   2. Calculates prestige currency from net worth (via GameConfig rate).
    ///   3. Resets current-run state: inventory, buildings.
    ///   4. Preserves: PrestigeData, recipe knowledge (tracked externally in save file).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PrestigeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // Note: BuildingData is NOT required — prestige must work even with zero buildings placed.
            state.RequireForUpdate<PlayerProgressData>();
            state.RequireForUpdate<PrestigeData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            if (!progress.PrestigeRequested) return;

            var prestige = SystemAPI.GetSingleton<PrestigeData>();

            // --- Calculate prestige currency earned ---
            // In a full implementation, netWorthToPrestigeCurrencyRate comes from GameConfigSO
            // loaded via a managed system. For now we use a constant.
            const float rate = 1.0f;
            long earned = (long)(progress.NetWorth * rate);
            prestige.PrestigeCurrency += earned;
            prestige.RunCount += 1;

            // --- Reset current-run state ---
            progress.BaseCurrency      = 0;
            progress.NetWorth          = 0f;
            progress.CurrentTier       = 1;
            progress.PrestigeAvailable = false;
            progress.PrestigeRequested = false;

            // Write updated singletons back
            SystemAPI.SetSingleton(prestige);
            SystemAPI.SetSingleton(progress);

            // --- Destroy all building entities ---
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<BuildingData>>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
            }

            // --- Clear player inventory ---
            var inventory = SystemAPI.GetSingletonBuffer<InventorySlot>();
            inventory.Clear();

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            UnityEngine.Debug.Log(
                $"[PrestigeSystem] Run {prestige.RunCount} complete. " +
                $"Earned {earned} prestige currency. Total: {prestige.PrestigeCurrency}.");
        }
    }
}
