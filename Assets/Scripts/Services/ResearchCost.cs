using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure research-price rule, shared by the charge (ResearchService) and every price shown in the UI.
    ///
    /// Research discounts come from upgrades only: <b>Memory Resonance</b> (prestige shop,
    /// <see cref="UpgradeEffectType.GlobalResearchDiscount"/>), 5% per level off every research, every run.
    /// There is deliberately no "you finished this on an earlier run" discount (removed 2026-11-15), and
    /// Cyclotron Insight is an upgrade currency, not a research discount.
    /// </summary>
    public static class ResearchCost
    {
        /// <summary>The discount never takes more than this off; keeps late research from going free.</summary>
        public const float MaxDiscount = 0.9f;

        public static long Effective(long baseCost, float globalDiscount)
        {
            if (baseCost <= 0) return 0;
            double factor = 1d - Clamp(globalDiscount);
            long cost = (long)Math.Round(baseCost * factor, MidpointRounding.AwayFromZero);
            return Math.Max(1, cost);
        }

        static double Clamp(float d) => d <= 0f ? 0d : Math.Min(MaxDiscount, d);
    }
}
