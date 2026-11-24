using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Ad-free purchase (#503, decision 2026-11-24): a one-time non-consumable (~$10). Owning it
    ///  - grants every rewarded-ad reward without the ad (the Entropy Surge stays, just with no ad), and
    ///  - adds a permanent +20% to entropy income, live (the <see cref="EntropyIncomeBoost"/> singleton, on top of
    ///    the Cyclotron boost) and offline (<see cref="OfflineCollectionService"/>).
    /// Ownership lives in <see cref="SaveData.adFree"/> (survives prestige and syncs to the cloud save) and is
    /// restored from the store receipt on reinstall (IAPService.OnInitialized / RestorePurchases).
    /// </summary>
    public static class AdFree
    {
        /// <summary>Store product id; register it as a non-consumable in Play Console and App Store Connect.</summary>
        public const string ProductId = "ad_free";
        public const string FallbackPrice = "$9.99";
        public const float  IncomeBonus = 0.20f;

        public const string ShopName = "Ad-Free Forever";
        public const string ShopDescription =
            "Every ad reward without the ad (the Entropy Surge included), plus +20% entropy income forever.";

        /// <summary>Fires once when ownership is granted (purchase or restore).</summary>
        public static event Action Granted;

        public static bool Owned(SaveData save) => save != null && save.adFree;

        /// <summary>1.2 while owned, else 1.</summary>
        public static float IncomeMultiplier(SaveData save) => Owned(save) ? 1f + IncomeBonus : 1f;

        /// <summary>The live entropy-income multiplier: the Cyclotron boost (1 when none) times the ad-free bonus.</summary>
        public static float CombinedIncomeMultiplier(SaveData save, float cyclotronMult) =>
            (cyclotronMult > 0f ? cyclotronMult : 1f) * IncomeMultiplier(save);

        /// <summary>Entropy after the ad-free bonus (offline payouts).</summary>
        public static long ApplyIncome(SaveData save, long entropy) =>
            Owned(save) && entropy > 0 ? (long)Math.Round(entropy * (double)(1f + IncomeBonus)) : entropy;

        /// <summary>Marks the purchase owned. Returns false when it already was (a restore of an owned save).</summary>
        public static bool Grant(SaveData save)
        {
            if (save == null || save.adFree) return false;
            save.adFree = true;
            Granted?.Invoke();
            return true;
        }
    }
}
