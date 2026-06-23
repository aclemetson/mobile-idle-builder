using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles the prestige loop:
    ///   1. Detects when the player requests a prestige.
    ///   2. Calculates prestige currency from net worth (via GameConfig rate).
    ///   3. Resets current-run state: inventory, buildings.
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

            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.unlockedResearch              = new System.Collections.Generic.List<string>();
                // Multi-grids: clear all sites' grids + snapshots and return to site 0 (unlocks
                // survive) so the intermediate SaveLocal below never persists stale inactive sites.
                PrestigeSaveWatcher.ResetSitesForPrestige(save);
                save.tutorial.hasCompletedFirstRun = true;
                save.tutorial.isActive             = false;
                GameLogger.Info("[PrestigeSystem] hasCompletedFirstRun set → true in SaveData");
            }

            // Flush ECS state → SaveData and write to disk.
            // ECS singletons are already updated above so FlushToSave captures correct post-prestige values.
            SaveManager.Instance?.SaveLocal();

            GameLogger.Info(
                $"[PrestigeSystem] Run {prestige.RunCount} complete. " +
                $"Earned {earned} prestige currency. Total: {prestige.PrestigeCurrency}.");
        }
    }
}
