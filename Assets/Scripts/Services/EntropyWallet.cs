using System;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The one place that reads and spends entropy (<see cref="PlayerProgressData.BaseCurrency"/>) and
    /// prices things with the prestige cost reduction (#279). Before this, six call sites each wrote
    /// their own "subtract BaseCurrency, add TotalEntropySpent" and disagreed on what happens when the
    /// player is short; the reduced-price formula was copied for display, charge and refund.
    ///
    /// Works on the caller's own <c>PlayerProgressData</c> query (ReadWrite). Callers keep their
    /// existing ECS-readiness guards (<c>ECSLoadBridge.IsLoaded</c> / <c>_ecsReady</c>); this class only
    /// checks that the singleton exists.
    /// </summary>
    public static class EntropyWallet
    {
        /// <summary>Current spendable entropy, or 0 if the singleton does not exist yet.</summary>
        public static long Balance(EntityQuery progressQuery)
        {
            if (progressQuery.IsEmpty) return 0;
            return progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        /// <summary>
        /// Spends exactly <paramref name="amount"/> if the player can afford it: lowers
        /// <c>BaseCurrency</c> and raises <c>TotalEntropySpent</c> together. Returns false and changes
        /// nothing if they can't, if the singleton is missing, or if <paramref name="amount"/> is
        /// negative. Spending 0 succeeds and changes nothing. Use this for every purchase that is
        /// checked before it happens.
        /// </summary>
        public static bool TrySpend(EntityQuery progressQuery, long amount)
        {
            if (amount < 0 || progressQuery.IsEmpty) return false;
            if (amount == 0) return true;

            var progress = progressQuery.GetSingleton<PlayerProgressData>();
            if (progress.BaseCurrency < amount) return false;

            progress.BaseCurrency      -= amount;
            progress.TotalEntropySpent += amount;
            progressQuery.SetSingleton(progress);
            return true;
        }

        /// <summary>
        /// Charges as much of <paramref name="amount"/> as the player has, never going below zero, and
        /// returns what was actually charged. Only for charges applied AFTER the thing was already
        /// granted (building placement: <c>HUDController.OnBuildingPlaced</c> runs once the building is
        /// down, so refusing would make it free). Everything else should use <see cref="TrySpend"/>.
        /// </summary>
        public static long SpendUpTo(EntityQuery progressQuery, long amount)
        {
            if (amount <= 0 || progressQuery.IsEmpty) return 0;

            var progress = progressQuery.GetSingleton<PlayerProgressData>();
            long charged = Math.Min(amount, Math.Max(0, progress.BaseCurrency));
            if (charged == 0) return 0;

            progress.BaseCurrency      -= charged;
            progress.TotalEntropySpent += charged;
            progressQuery.SetSingleton(progress);
            return charged;
        }

        /// <summary>
        /// A price after the prestige <see cref="PrestigeData.CostReduction"/>. Matches the original
        /// formula exactly (float multiply, truncate toward zero) so shown, charged and refunded
        /// amounts agree with each other and with older saves; never negative.
        /// </summary>
        public static long ReducedCost(long rawCost, float costReduction)
        {
            if (rawCost <= 0) return 0;
            return Math.Max(0L, (long)(rawCost * (1f - costReduction)));
        }
    }
}
