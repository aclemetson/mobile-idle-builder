using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Quantum Domains retired (2026-11-24): the extra Physics build sites (Quark Sea, Lepton Storm, Actinide Vein)
    /// were made obsolete by worlds (separate maps that idle in parallel) and Field Synthesis (more fields on your
    /// own map). Their entries stay in the site list with <see cref="SiteSO.retired"/> so saved site indices
    /// (grids[], siteSnapshots[], activeSiteIndex) keep pointing at the right grids.
    ///
    /// A save is migrated once on load (<see cref="ECSLoadBridge"/>, before idle earnings and currency):
    /// each retired site it unlocked is removed from <see cref="SaveData.unlockedSites"/> and its unlock price
    /// refunded as entropy; its grid and idle snapshot are cleared so it stops paying idle; a player standing on
    /// one is moved back to the Origin map. Idempotent: the refund follows the unlocked id, which is removed.
    /// </summary>
    public static class SiteRetirement
    {
        /// <summary>Applies the retirement to <paramref name="save"/>; returns the entropy to refund.</summary>
        public static long Migrate(SaveData save, IReadOnlyList<SiteSO> sites)
        {
            if (save == null || sites == null) return 0;
            long refund = 0;
            var run = save.currentRun;
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (site == null || !site.retired) continue;

                if (save.unlockedSites != null && save.unlockedSites.Remove(site.id))
                    refund += System.Math.Max(0, site.unlockCost);

                if (run?.grids != null && i < run.grids.Count && i > 0) run.grids[i] = new GridSaveData();
                if (save.siteSnapshots != null && i < save.siteSnapshots.Count && i > 0)
                    save.siteSnapshots[i] = new IdleCollectionSnapshot();

                if (run != null && run.activeSiteIndex == i)
                {
                    run.activeSiteIndex  = 0;   // the Origin map, in Physics
                    run.activeWorldIndex = 0;
                }
            }
            return refund;
        }
    }
}
