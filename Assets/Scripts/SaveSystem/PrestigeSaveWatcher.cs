using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects when PrestigeSystem increments RunCount, then clears the current-run
    /// save data and triggers a save. This bridges the Burst-compiled PrestigeSystem
    /// to managed save logic.
    /// </summary>
    public class PrestigeSaveWatcher : MonoBehaviour
    {
        EntityQuery _prestigeQuery;
        int         _lastRunCount = -1;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _prestigeQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PrestigeData>());
        }

        void Update()
        {
            if (_prestigeQuery == null || _prestigeQuery.IsEmpty) return;

            int current = _prestigeQuery.GetSingleton<PrestigeData>().RunCount;

            // Track every frame; skip init frame to avoid spurious trigger on load
            if (_lastRunCount == -1)
            {
                _lastRunCount = current;
                return;
            }

            if (current == _lastRunCount) return;
            _lastRunCount = current;

            // Prestige just completed — wipe run state and permanently retire the tutorial.
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                // Flush first so prestige totals (RunCount, PrestigeCurrency, multipliers)
                // are written to save before we zero the current-run fields.
                ECSLoadBridge.Instance?.FlushToSave();

                // Explicitly zero the current run regardless of ECS timing — this prevents
                // any stale pre-prestige values from being written if ECS flushed early.
                save.currentRun = new CurrentRunData();

                // Multi-grids: clear every site's grid + idle snapshots, return to site 0.
                // Site unlocks (save.unlockedSites) intentionally survive.
                ResetSitesForPrestige(save);

                // Research fully resets on prestige (player re-unlocks each run).
                save.unlockedResearch = new();

                save.tutorial.hasCompletedFirstRun = true;
                save.tutorial.isActive             = false;
            }

            // Reset the in-memory research set so IsUnlocked() returns false immediately.
            ResearchService.Instance?.ResetAll();

            // Buildings were destroyed by PrestigeSystem, so clear every manager's building link
            // (the hired roster survives prestige — managers stay owned, just unassigned).
            ManagerService.Instance?.ResetAssignments();

            // skipECSFlush=true: we just explicitly zeroed currentRun above;
            // a second FlushToSave would overwrite with potentially stale ECS values.
            // Grid flush is still allowed — buildings are already destroyed by PrestigeSystem.
            SaveManager.Instance?.SaveLocal(skipECSFlush: true);

            GameLogger.Info($"[PrestigeSaveWatcher] Run {current} saved after prestige.");
        }

        /// <summary>
        /// Resets the multi-grids run state on prestige: clears all sites' grids, returns to
        /// site 0, and wipes the active + per-site idle snapshots. Site UNLOCKS
        /// (<c>save.unlockedSites</c>) are intentionally preserved — they are expensive entropy
        /// purchases that survive prestige, like recipe knowledge. Shared by
        /// <see cref="PrestigeSystem"/>.OnUpdate and this watcher so both save paths agree.
        /// </summary>
        internal static void ResetSitesForPrestige(SaveData save)
        {
            if (save?.currentRun == null) return;
            save.currentRun.grids           = new List<GridSaveData>();
            save.currentRun.grid            = new GridSaveData();
            save.currentRun.activeSiteIndex = 0;
            save.idleSnapshot               = new IdleCollectionSnapshot();
            save.siteSnapshots              = new List<IdleCollectionSnapshot>();
            // save.unlockedSites: NOT touched — site unlocks survive prestige.
        }
    }
}
