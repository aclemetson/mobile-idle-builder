using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Values a save's purchases at list price on load, so net worth counts things bought before
    /// <c>TotalEntropyDiscounted</c> existed (0.6.14). That counter only grows when a purchase is made,
    /// so saves from earlier versions started it at 0 and never counted their earlier discounts.
    ///
    /// The list value is every researched node's <c>costBaseCurrency</c> plus every placed building's
    /// <c>entropyCost</c> and the upgrade levels it holds, across all sites. The discount total is raised
    /// to cover any gap between that and what was paid, and is never lowered: a purchase made before a
    /// price cut stays at the price paid. Running it on every load is safe; once the ledger covers the
    /// list value it changes nothing.
    /// </summary>
    public static class NetWorthBackfill
    {
        /// <summary>List-price value of the research unlocked and the buildings placed in <paramref name="run"/>.</summary>
        public static long ListValue(IEnumerable<string> unlockedResearch, CurrentRunData run,
                                     Func<string, ResearchSO> researchById, Func<int, BuildingSO> buildingById)
        {
            long total = 0;
            if (unlockedResearch != null && researchById != null)
                foreach (var id in new HashSet<string>(unlockedResearch))
                {
                    var r = string.IsNullOrEmpty(id) ? null : researchById(id);
                    if (r != null) total += Math.Max(0L, r.costBaseCurrency);
                }

            if (run != null && buildingById != null)
                foreach (var grid in Grids(run))
                {
                    if (grid?.buildings == null) continue;
                    foreach (var b in grid.buildings)
                    {
                        if (b == null) continue;
                        var so = buildingById(b.buildingId);
                        if (so == null || so.isEntropySink) continue;
                        total += DemolishRefund.EntropySpent(so, b.level, b.storageLevel, b.inputLevel, 0f);
                    }
                }
            return total;
        }

        /// <summary>
        /// The discount total after the backfill: raised so spent + discounted reaches
        /// <paramref name="listValue"/>, never lowered.
        /// </summary>
        public static long Discounted(long listValue, long totalSpent, long totalDiscounted) =>
            Math.Max(Math.Max(0L, totalDiscounted), listValue - Math.Max(0L, totalSpent));

        // Multi-site saves keep one grid per site in 'grids'; older saves only have the single 'grid'.
        private static IEnumerable<GridSaveData> Grids(CurrentRunData run) =>
            run.grids != null && run.grids.Count > 0 ? run.grids : new List<GridSaveData> { run.grid };
    }
}
