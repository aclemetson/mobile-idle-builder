using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles the PVP competition run start:
    ///   1. Detects when the player requests a competition run (PVPRunRequested = true).
    ///   2. Resets current-run state: inventory, buildings, currency.
    ///   3. Does NOT award prestige currency — this is a fresh competitive build, not a prestige.
    ///   4. Clears the PVPRunRequested flag.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PVPSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerProgressData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var progress = SystemAPI.GetSingleton<PlayerProgressData>();
            if (!progress.PVPRunRequested) return;

            // --- Reset current-run state (same as PrestigeSystem, but no prestige currency) ---
            progress.BaseCurrency      = 0;
            progress.NetWorth          = 0f;
            progress.CurrentTier       = 1;
            progress.PrestigeAvailable = false;
            progress.PVPRunRequested   = false;

            SystemAPI.SetSingleton(progress);

            // --- Destroy all placed buildings ---
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<BuildingData>>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
            }

            // --- Clear inventory ---
            SystemAPI.GetSingletonBuffer<InventorySlot>().Clear();

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            GameLogger.Info("[PVPSystem] Competition run started — grid and inventory reset.");
        }
    }
}
