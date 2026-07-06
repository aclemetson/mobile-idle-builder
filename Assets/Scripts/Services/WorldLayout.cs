using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The single place that maps between the flat global site list and the World layer.
    /// Worlds group sites (<see cref="WorldSO.siteIds"/>); the save stores grids/siteSnapshots flat and
    /// indexed by global site index, plus <c>currentRun.activeWorldIndex</c>. These helpers keep
    /// world &lt;-&gt; site consistent so no caller re-derives the grouping. Pure and testable: the databases
    /// are passed explicitly (mirrors the static <c>SiteService.IsUnlocked</c> helper pattern).
    /// </summary>
    public static class WorldLayout
    {
        /// <summary>World index into <c>worlds.allWorlds</c> that owns <paramref name="siteId"/>, or 0 (Physics) if unknown.</summary>
        public static int WorldIndexForSite(WorldDatabaseSO worlds, string siteId)
        {
            if (worlds?.allWorlds == null || string.IsNullOrEmpty(siteId)) return 0;
            for (int w = 0; w < worlds.allWorlds.Length; w++)
            {
                var world = worlds.allWorlds[w];
                if (world?.siteIds != null && world.siteIds.Contains(siteId)) return w;
            }
            return 0;
        }

        /// <summary>The world that owns <paramref name="siteId"/>, or null if unknown.</summary>
        public static WorldSO WorldForSite(WorldDatabaseSO worlds, string siteId)
        {
            if (worlds?.allWorlds == null || string.IsNullOrEmpty(siteId)) return null;
            foreach (var world in worlds.allWorlds)
                if (world?.siteIds != null && world.siteIds.Contains(siteId)) return world;
            return null;
        }

        /// <summary>
        /// World index that owns the active site, resolving the global site index through the
        /// <see cref="SiteDatabaseSO"/>. Returns 0 (Physics) when a database is missing or the index is
        /// out of range. This is the canonical way to keep <c>activeWorldIndex</c> in sync with
        /// <c>activeSiteIndex</c>.
        /// </summary>
        public static int WorldIndexForSiteIndex(WorldDatabaseSO worlds, SiteDatabaseSO sites, int siteIndex)
        {
            if (sites?.allSites == null || siteIndex < 0 || siteIndex >= sites.allSites.Length) return 0;
            var site = sites.allSites[siteIndex];
            return WorldIndexForSite(worlds, site != null ? site.id : null);
        }
    }
}
