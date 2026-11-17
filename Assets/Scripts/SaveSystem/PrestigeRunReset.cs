using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Prestige restarts the run from scratch: <see cref="ResetRun"/> wipes the run part of the save,
    /// then <see cref="BeginReload"/> sends the player through the loading screen into a freshly loaded
    /// GameScene. The reload is what clears the grid, belts, fields and every run-scoped view; the new
    /// scene loads the reset save like any returning player (the tutorial stays retired).
    /// Survives prestige: PrestigeData totals, permanent upgrades, recipe knowledge, site/world unlocks,
    /// hired managers (unassigned), megastructure, achievements, crystals, daily events.
    /// </summary>
    public static class PrestigeRunReset
    {
        /// <summary>
        /// True from the prestige until the loading screen hands over to the new scene. While set,
        /// <see cref="SaveManager.SaveLocal"/> writes the reset save as it is and does not flush the
        /// old scene's grid or ECS state back over it.
        /// </summary>
        public static bool ReloadPending { get; private set; }

        /// <summary>
        /// Clears every run-scoped field of <paramref name="save"/>: entropy (set to
        /// <paramref name="startingEntropy"/>), inventory, grids and bought fields, research, craft
        /// queue, idle snapshots and manager assignments. Marks the tutorial done for good.
        /// </summary>
        public static void ResetRun(SaveData save, long startingEntropy)
        {
            if (save == null) return;

            save.currentRun = new CurrentRunData { baseCurrency = startingEntropy };
            ResetSitesForPrestige(save);

            save.lastKnownNetWorth          = 0f;
            save.unlockedResearch           = new List<string>();
            save.activeResearchId           = null;
            save.activeResearchCompleteUtc  = null;
            // Buildings are gone, so no manager has a building to run (the roster itself survives).
            save.managerAssignments         = new List<ManagerAssignmentEntry>();

            save.tutorial ??= new TutorialSaveData();
            save.tutorial.hasCompletedFirstRun = true;
            save.tutorial.isActive             = false;
        }

        /// <summary>
        /// Resets the multi-grids run state on prestige: clears all sites' grids, returns to
        /// site 0, and wipes the active + per-site idle snapshots. Site UNLOCKS
        /// (<c>save.unlockedSites</c>) are intentionally preserved — they are expensive entropy
        /// purchases that survive prestige, like recipe knowledge.
        /// </summary>
        internal static void ResetSitesForPrestige(SaveData save)
        {
            if (save?.currentRun == null) return;
            save.currentRun.grids           = new List<GridSaveData>();
            save.currentRun.grid            = new GridSaveData();
            save.currentRun.activeSiteIndex = 0;
            save.currentRun.activeWorldIndex = 0;   // back to Physics (site 0) — keep world consistent with the site reset.
            save.idleSnapshot               = new IdleCollectionSnapshot();
            save.siteSnapshots              = new List<IdleCollectionSnapshot>();
            // save.unlockedSites / save.unlockedWorlds: NOT touched — site & world unlocks survive prestige.
        }

        /// <summary>Loads GameScene again through the loading screen so the new run starts clean.</summary>
        public static void BeginReload()
        {
            ReloadPending = true;
            SceneLoader.OnTransitionComplete += () => ReloadPending = false;
            SceneLoader.GoTo("GameScene");
        }
    }
}
