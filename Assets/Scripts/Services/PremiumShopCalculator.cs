using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure static helpers for premium shop logic.
    /// No MonoBehaviour dependency — fully unit-testable.
    /// </summary>
    public static class PremiumShopCalculator
    {
        // ── Tier Definitions ─────────────────────────────────────────────────

        public static readonly SpeedUpTier[] SpeedUpTiers =
        {
            new SpeedUpTier("Quick Burst",      "30 min at 2×",  50,    TimeSpan.FromMinutes(30)),
            new SpeedUpTier("Production Surge", "2 hr at 2×",    175,   TimeSpan.FromHours(2)),
            new SpeedUpTier("Overclocked",      "8 hr at 2×",    600,   TimeSpan.FromHours(8)),
            new SpeedUpTier("Hyperdrive",       "24 hr at 2×",   1500,  TimeSpan.FromHours(24)),
        };

        public static readonly EntropyTier[] EntropyTiers =
        {
            new EntropyTier("Trickle",   "5% of net worth",   80,   0.05f,  500),
            new EntropyTier("Infusion",  "15% of net worth",  220,  0.15f,  1500),
            new EntropyTier("Cascade",   "40% of net worth",  500,  0.40f,  4000),
            new EntropyTier("Flood",     "100% of net worth", 1100, 1.00f,  10000),
        };

        public static readonly PrestigeCurrencyTier[] PrestigeCurrencyTiers =
        {
            new PrestigeCurrencyTier("Residue",    100,   25),
            new PrestigeCurrencyTier("Fragment",   350,   100),
            new PrestigeCurrencyTier("Cache",      1200,  400),
            new PrestigeCurrencyTier("Reservoir",  4000,  1500),
        };

        public static readonly CrystalPackTier[] CrystalPackTiers =
        {
            new CrystalPackTier("Starter Pack",   "crystals_tier1", 600,   "$0.99"),
            new CrystalPackTier("Explorer Pack",  "crystals_tier2", 3200,  "$4.99"),
            new CrystalPackTier("Founder Pack",   "crystals_tier3", 7500,  "$9.99"),
            new CrystalPackTier("Quantum Pack",   "crystals_tier4", 20000, "$19.99"),
        };

        public const float SpeedBoostMultiplier = 2f;

        // ── Speed Boost Helpers ───────────────────────────────────────────────

        /// <summary>Returns new expiry: extends existing boost, or sets fresh expiry.</summary>
        public static string CalcSpeedBoostExpiry(string currentExpiryUtc, TimeSpan duration)
        {
            DateTime baseline = IsBoostActive(currentExpiryUtc)
                ? DateTime.Parse(currentExpiryUtc, null, DateTimeStyles.RoundtripKind)
                : DateTime.UtcNow;
            return (baseline + duration).ToString("O");
        }

        public static bool IsBoostActive(string expiryUtc)
        {
            if (string.IsNullOrEmpty(expiryUtc)) return false;
            if (!DateTime.TryParse(expiryUtc, null, DateTimeStyles.RoundtripKind, out var expiry))
                return false;
            return DateTime.UtcNow < expiry;
        }

        public static float GetSpeedBoostMultiplier(string expiryUtc)
            => IsBoostActive(expiryUtc) ? SpeedBoostMultiplier : 1f;

        // ── Entropy Helpers ───────────────────────────────────────────────────

        /// <summary>Calculates the entropy amount for a tier given the last known net worth.</summary>
        public static long CalcEntropyAmount(int tierIndex, float lastKnownNetWorth)
        {
            if (tierIndex < 0 || tierIndex >= EntropyTiers.Length)
                throw new ArgumentOutOfRangeException(nameof(tierIndex));

            var tier = EntropyTiers[tierIndex];
            long fromNetWorth = (long)(lastKnownNetWorth * tier.NetWorthFraction);
            return Math.Max(fromNetWorth, tier.MinimumFloor);
        }

        // ── Purchase Logic (pure — no MonoBehaviour side-effects) ─────────────

        /// <summary>
        /// Validates and applies a speed-up purchase.
        /// Mutates crystalBalance and expiryUtc via out params.
        /// </summary>
        public static PurchaseResult TryBuySpeedUp(
            int tierIndex,
            long currentCrystals,
            string currentExpiryUtc,
            out long newCrystals,
            out string newExpiryUtc)
        {
            newCrystals  = currentCrystals;
            newExpiryUtc = currentExpiryUtc;

            if (tierIndex < 0 || tierIndex >= SpeedUpTiers.Length)
                return PurchaseResult.InvalidTier;

            var tier = SpeedUpTiers[tierIndex];
            if (currentCrystals < tier.CrystalCost)
                return PurchaseResult.InsufficientCrystals;

            newCrystals  = currentCrystals - tier.CrystalCost;
            newExpiryUtc = CalcSpeedBoostExpiry(currentExpiryUtc, tier.Duration);
            return PurchaseResult.Success;
        }

        /// <summary>Validates and applies an entropy purchase. Mutates crystalBalance and entropy via out params.</summary>
        public static PurchaseResult TryBuyEntropy(
            int tierIndex,
            long currentCrystals,
            float lastKnownNetWorth,
            out long newCrystals,
            out long entropyGranted)
        {
            newCrystals    = currentCrystals;
            entropyGranted = 0;

            if (tierIndex < 0 || tierIndex >= EntropyTiers.Length)
                return PurchaseResult.InvalidTier;

            var tier = EntropyTiers[tierIndex];
            if (currentCrystals < tier.CrystalCost)
                return PurchaseResult.InsufficientCrystals;

            newCrystals    = currentCrystals - tier.CrystalCost;
            entropyGranted = CalcEntropyAmount(tierIndex, lastKnownNetWorth);
            return PurchaseResult.Success;
        }

        /// <summary>Validates and applies a prestige-currency purchase. Mutates both balances via out params.</summary>
        public static PurchaseResult TryBuyPrestigeCurrency(
            int tierIndex,
            long currentCrystals,
            out long newCrystals,
            out long pcGranted)
        {
            newCrystals = currentCrystals;
            pcGranted   = 0;

            if (tierIndex < 0 || tierIndex >= PrestigeCurrencyTiers.Length)
                return PurchaseResult.InvalidTier;

            var tier = PrestigeCurrencyTiers[tierIndex];
            if (currentCrystals < tier.CrystalCost)
                return PurchaseResult.InsufficientCrystals;

            newCrystals = currentCrystals - tier.CrystalCost;
            pcGranted   = tier.PrestigeCurrencyAmount;
            return PurchaseResult.Success;
        }
    }

    // ── Tier Data Structures ──────────────────────────────────────────────────

    public class SpeedUpTier
    {
        public string   Name;
        public string   Description;
        public long     CrystalCost;
        public TimeSpan Duration;

        public SpeedUpTier(string name, string desc, long cost, TimeSpan duration)
        {
            Name        = name;
            Description = desc;
            CrystalCost = cost;
            Duration    = duration;
        }
    }

    public class EntropyTier
    {
        public string Name;
        public string Description;
        public long   CrystalCost;
        public float  NetWorthFraction;
        public long   MinimumFloor;

        public EntropyTier(string name, string desc, long cost, float fraction, long floor)
        {
            Name             = name;
            Description      = desc;
            CrystalCost      = cost;
            NetWorthFraction = fraction;
            MinimumFloor     = floor;
        }
    }

    public class PrestigeCurrencyTier
    {
        public string Name;
        public long   CrystalCost;
        public long   PrestigeCurrencyAmount;

        public PrestigeCurrencyTier(string name, long cost, long amount)
        {
            Name                  = name;
            CrystalCost           = cost;
            PrestigeCurrencyAmount = amount;
        }
    }

    public class CrystalPackTier
    {
        public string Name;
        public string ProductId;
        public long   CrystalAmount;
        public string DisplayPrice;

        public CrystalPackTier(string name, string productId, long amount, string displayPrice)
        {
            Name         = name;
            ProductId    = productId;
            CrystalAmount = amount;
            DisplayPrice = displayPrice;
        }
    }

    public enum PurchaseResult
    {
        Success,
        InsufficientCrystals,
        InvalidTier,
    }
}
