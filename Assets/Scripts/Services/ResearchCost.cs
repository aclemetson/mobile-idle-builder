using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure research-price rule, shared by the charge (ResearchService) and every price shown in the UI.
    ///
    /// Two discounts stack multiplicatively on the authored <c>costBaseCurrency</c>:
    ///  - <b>Memory Resonance</b> (prestige shop, <see cref="UpgradeEffectType.GlobalResearchDiscount"/>):
    ///    5% per level off every research, every run.
    ///  - <b>Prestige memory</b> (<see cref="ResearchSO.prestigeMemoryDiscount"/>): a node's own discount,
    ///    only when the player finished that node on an earlier run (<c>SaveData.researchCompletedEver</c>).
    /// Before these were wired every run paid full price, which is most of why play slowed after the
    /// first prestige (docs/balance/prestige-pacing.xlsx).
    /// </summary>
    public static class ResearchCost
    {
        /// <summary>No single discount may take more than this off; keeps late research from going free.</summary>
        public const float MaxDiscount = 0.9f;

        public static long Effective(long baseCost, float globalDiscount, float memoryDiscount, bool completedBefore)
        {
            if (baseCost <= 0) return 0;
            double factor = (1d - Clamp(globalDiscount)) * (1d - (completedBefore ? Clamp(memoryDiscount) : 0d));
            long cost = (long)Math.Round(baseCost * factor, MidpointRounding.AwayFromZero);
            return Math.Max(1, cost);
        }

        static double Clamp(float d) => d <= 0f ? 0d : Math.Min(MaxDiscount, d);
    }
}
