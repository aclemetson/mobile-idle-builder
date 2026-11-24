using System;

namespace MobileIdleBuilder
{
    /// <summary>Free Research (#491): how many of this run's research still skip their timer. Pure for tests.</summary>
    public static class FreeResearchRule
    {
        /// <summary>The upgrade's effect (one per level) less the ones used this run, never below zero.</summary>
        public static int Remaining(float granted, int usedThisRun) =>
            Math.Max(0, (int)Math.Round(granted) - Math.Max(0, usedThisRun));
    }
}
