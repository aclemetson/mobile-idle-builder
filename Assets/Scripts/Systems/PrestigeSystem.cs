using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles the prestige loop:
    ///   1. Detects when the player requests a prestige.
    ///   2. Calculates prestige currency from net worth (via GameConfig rate).
    ///   3. Resets current-run state: inventory, buildings, belts and the items on them.
    ///   4. Resets: research unlocks (per-run). Preserves: PrestigeData, recipe knowledge.
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

            // Wall detection — fires before prestige request handling so the same tick can do both
            if (!progress.PrestigeAvailable
                && progress.PrestigeWallValue > 0f
                && progress.NetWorth >= progress.PrestigeWallValue)
            {
                progress.PrestigeAvailable = true;
                SystemAPI.SetSingleton(progress);
            }

            if (!progress.PrestigeRequested) return;

            var prestige = SystemAPI.GetSingleton<PrestigeData>();

            // Capture pre-reset values for telemetry before the reset below zeroes them.
            float netWorthBefore = progress.NetWorth;
            int   tierBefore     = progress.CurrentTier;

            // --- Calculate prestige currency earned ---
            // Formula: floor(max(0, log10(netWorth / prestigeBase) × prestigeScale))
            var cfg    = GameBootstrap.Instance?.gameConfig;
            float pbase  = cfg != null ? cfg.prestigeBaseValue       : 5000f;
            float pscale = cfg != null ? cfg.prestigeCurrencyScale   : 50f;
            long earned = pbase > 0f
                ? (long)System.Math.Max(0, System.Math.Floor(System.Math.Log10(progress.NetWorth / pbase) * pscale))
                : 0L;

            // Apply PrestigeGainMultiplier from permanent upgrades
            float gainBonus = PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.PrestigeGainMultiplier) ?? 0f;
            if (gainBonus > 0f)
                earned = (long)(earned * (1f + gainBonus));

            // Apply the megastructure's prestige-gain reward (e.g. Stellar Engine = ×2). Survives prestige,
            // so it keeps applying every run once unlocked. Managed call is safe: OnUpdate is not Burst-compiled.
            float megaGainBonus = MegastructureService.Instance?.GetPrestigeGainBonus() ?? 0f;
            if (megaGainBonus > 0f)
                earned = (long)(earned * (1f + megaGainBonus));

            prestige.PrestigeCurrency += earned;
            prestige.RunCount += 1;

            // --- Reset current-run state ---
            long cfgEntropy   = cfg != null ? cfg.startingEntropy : 0;
            long bonusEntropy = (long)(PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.StartingEntropyBonus) ?? 0f);
            progress.BaseCurrency      = cfgEntropy + bonusEntropy;
            progress.TotalEntropySpent = 0;
            progress.NetWorth          = 0f;
            progress.BaseNetWorth      = 0f;
            progress.CurrentTier       = 1;
            progress.PrestigeAvailable = false;
            progress.PrestigeRequested = false;

            SystemAPI.SetSingleton(prestige);
            SystemAPI.SetSingleton(progress);

            // --- Destroy all building entities (but keep permanent fixtures like Maxwell's Demon) ---
            int buildingCount = 0;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<BuildingData>>().WithNone<EntropySinkTag>().WithEntityAccess())
            {
                buildingCount++;
                ecb.DestroyEntity(entity);
            }

            // --- Destroy every belt segment, and the item riding on it ---
            // The default world outlives the scene reload below, so anything left here would carry
            // into the new run: items drawn on belts that are no longer there, and belts that the
            // next save writes back into the grid.
            foreach (var (_, entity) in SystemAPI.Query<RefRO<ConveyorSegmentData>>().WithEntityAccess())
                ecb.DestroyEntity(entity);

            // --- Empty what the kept fixtures (Maxwell's Demon) were holding ---
            foreach (var input in SystemAPI.Query<DynamicBuffer<BuildingInputSlot>>().WithAll<EntropySinkTag>())
                input.Clear();
            foreach (var output in SystemAPI.Query<DynamicBuffer<BuildingOutputSlot>>().WithAll<EntropySinkTag>())
                output.Clear();

            // --- Clear player inventory ---
            var inventory = SystemAPI.GetSingletonBuffer<InventorySlot>();
            inventory.Clear();

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            // --- Notify achievements (system is not Burst-compiled, so managed calls are safe) ---
            AchievementService.Instance?.NotifyPrestigeCurrencyEarned(earned);
            AchievementService.Instance?.NotifyPrestige();

            // --- Telemetry: rich prestige event with pre-reset scale data ---
            TelemetryService.Instance?.RecordPrestige(prestige.RunCount, netWorthBefore, earned,
                                                      buildingCount, tierBefore);

            // --- Reset managed services ---
            ResearchService.Instance?.ResetAll();

            // Update ECS TutorialStateData so FlushToSave() reads the correct value.
            // Without this, SaveLocal() would overwrite hasCompletedFirstRun back to false.
            if (SystemAPI.HasSingleton<TutorialStateData>())
            {
                var ts = SystemAPI.GetSingleton<TutorialStateData>();
                ts.FirstRunComplete = true;
                ts.IsActive         = false;
                SystemAPI.SetSingleton(ts);
            }

            ManagerService.Instance?.ResetAssignments();

            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                // Flush ECS first so the prestige totals (RunCount, PrestigeCurrency, multipliers)
                // reach the save, then wipe the whole run: grids, bought fields, research, craft queue.
                ECSLoadBridge.Instance?.FlushToSave();
                PrestigeRunReset.ResetRun(save, cfgEntropy + bonusEntropy);
                // The old scene's belts, fields and buildings are still live until the reload, so
                // write the reset save as it is instead of flushing them back into it.
                SaveManager.Instance.SaveLocal(skipGridFlush: true, skipECSFlush: true);
            }

            GameLogger.Info(
                $"[PrestigeSystem] Run {prestige.RunCount} complete. " +
                $"Earned {earned} prestige currency. Total: {prestige.PrestigeCurrency}.");

            // Start the new run from a clean scene (loading screen, fresh field layout, no tutorial).
            // Only with a real save in Play Mode: EditMode tests drive this system without one.
            if (save != null && UnityEngine.Application.isPlaying)
                PrestigeRunReset.BeginReload();
        }
    }
}
