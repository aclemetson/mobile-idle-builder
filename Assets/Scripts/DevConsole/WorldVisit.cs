#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Builds the save for "a first arrival in world X", for the dev console's <c>visit world</c> and
    /// <c>tutorial restart</c>. Works on a copy of the save, which the console then swaps in and reloads
    /// the scene with (one reload, like <c>snapshot load</c>; no live world switch first).
    /// <list type="bullet">
    /// <item>Physics (index 0): a fresh run (as after a prestige, prestige progress kept) with the main
    /// tutorial from its first step.</item>
    /// <item>Another world: the world and its sites unlocked, its entry site empty (buildings, channels,
    /// fields and field purchases gone; its starting buildings come back), the research gated to that
    /// world forgotten, and its own tutorial from the first step. The rest of the game is untouched.</item>
    /// </list>
    /// Pure (no ECS, no scene): tested by WorldVisitTests.
    /// </summary>
    public static class WorldVisit
    {
        public static void PrepareFreshArrival(SaveData save, WorldSO world, int worldIndex,
                                               IReadOnlyList<string> siteIdsByIndex,
                                               Func<string, string> researchWorld,
                                               Func<string, string> fieldWorld,
                                               long physicsStartingEntropy)
        {
            if (save == null || world == null) return;

            if (worldIndex == 0)
            {
                PrestigeRunReset.ResetRun(save, physicsStartingEntropy);
                save.tutorial = new TutorialSaveData();   // step 0, active, first run not complete
                return;
            }

            save.unlockedWorlds ??= new List<string>();
            if (!save.unlockedWorlds.Contains(world.id)) save.unlockedWorlds.Add(world.id);
            save.unlockedSites ??= new List<string>();

            int entry = -1;
            foreach (var sid in world.siteIds ?? new List<string>())
            {
                int idx = IndexOf(siteIdsByIndex, sid);
                if (idx < 0) continue;
                if (!save.unlockedSites.Contains(sid)) save.unlockedSites.Add(sid);
                if (entry < 0) entry = idx;
            }
            if (entry < 0) return;

            var run = save.currentRun;
            GridSaveService.EnsureSiteGrid(save, entry);
            run.grids[entry]     = new GridSaveData();     // empty; fields regenerate, starters come back
            run.activeSiteIndex  = entry;
            run.activeWorldIndex = worldIndex;

            save.siteSnapshots ??= new List<IdleCollectionSnapshot>();
            while (save.siteSnapshots.Count <= entry) save.siteSnapshots.Add(new IdleCollectionSnapshot());
            save.siteSnapshots[entry] = new IdleCollectionSnapshot();
            save.idleSnapshot         = new IdleCollectionSnapshot();

            // Field purchases of this world's fields start over (prices and the tutorial's "place a field").
            if (run.fieldPurchaseIds != null)
                for (int i = run.fieldPurchaseIds.Count - 1; i >= 0; i--)
                    if (fieldWorld?.Invoke(run.fieldPurchaseIds[i]) == world.id)
                    {
                        run.fieldPurchaseIds.RemoveAt(i);
                        if (run.fieldPurchaseCounts != null && i < run.fieldPurchaseCounts.Count)
                            run.fieldPurchaseCounts.RemoveAt(i);
                    }

            // Research that only this world offers is forgotten, so its tutorial teaches it again.
            save.unlockedResearch?.RemoveAll(r => researchWorld?.Invoke(r) == world.id);
            if (!string.IsNullOrEmpty(save.activeResearchId) && researchWorld?.Invoke(save.activeResearchId) == world.id)
            {
                save.activeResearchId          = null;
                save.activeResearchCompleteUtc = null;
            }

            TutorialFlows.ResetWorld(save, world.id);
        }

        static int IndexOf(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
            return -1;
        }

        /// <summary>A deep copy through JSON, the same way snapshots are stored.</summary>
        public static SaveData Clone(SaveData save) =>
            save == null ? null : JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
    }
}
#endif
