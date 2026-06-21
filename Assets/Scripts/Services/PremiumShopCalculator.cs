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

        // Crystal value anchor: $1 ~ 4 hours of progress => 150 crystals = 1 "skip-hour".
        // A 2x offline boost over duration D ~ D skip-hours of bonus production; priced at
        // 150 crystals/skip-hr with a bulk discount on the larger tiers.
        public static readonly SpeedUpTier[] SpeedUpTiers =
        {
            new SpeedUpTier("Quick Burst",      "30 min at 2×",  75,    TimeSpan.FromMinutes(30)),
            new SpeedUpTier("Production Surge", "2 hr at 2×",    270,   TimeSpan.FromHours(2)),
            new SpeedUpTier("Overclocked",      "8 hr at 2×",    950,   TimeSpan.FromHours(8)),
            new SpeedUpTier("Hyperdrive",       "24 hr at 2×",   2500,  TimeSpan.FromHours(24)),
        };

        // Self-scaling time-skip: grants a % of the current build's net worth.
        public static readonly EntropyTier[] EntropyTiers =
        {
            new EntropyTier("Trickle",   "10% of net worth",  300,   0.10f,  1000),
            new EntropyTier("Infusion",  "25% of net worth",  650,   0.25f,  2500),
            new EntropyTier("Cascade",   "50% of net worth",  1200,  0.50f,  5000),
            new EntropyTier("Flood",     "100% of net worth", 2000,  1.00f,  10000),
        };

        // Flat grants: prestige currency persists across runs (permanent power, not a time-skip),
        // so a fixed amount is clearer than a net-worth percentage. Bigger tiers give better value.
        public static readonly PrestigeCurrencyTier[] PrestigeCurrencyTiers =
        {
            new PrestigeCurrencyTier("Residue",    500,   50),
            new PrestigeCurrencyTier("Fragment",   900,   150),
            new PrestigeCurrencyTier("Cache",      1900,  500),
            new PrestigeCurrencyTier("Reservoir",  3600,  1500),
        };

        // Instant offline collection ("Time Warp"): banks N hours of offline production at the
        // current rate, no waiting. Priced ~150◆/skip-hr with a small "instant" premium over the
        // equivalent speed boost.
        public static readonly TimeWarpTier[] TimeWarpTiers =
        {
            new TimeWarpTier("Time Warp I",   "Instantly bank 2 hours of production",   350,  2f),
            new TimeWarpTier("Time Warp II",  "Instantly bank 8 hours of production",   1300, 8f),
            new TimeWarpTier("Time Warp III", "Instantly bank 24 hours of production",  3500, 24f),
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

        /// <summary>
        /// Validates affordability for a Time Warp tier and deducts the crystal cost. The actual
        /// production payout is computed by PremiumShopService (it needs live save/ECS state), so
        /// this only handles the pure crystal side.
        /// </summary>
        public static PurchaseResult TryBuyTimeWarp(
            int tierIndex,
            long currentCrystals,
            out long newCrystals,
            out float hours)
        {
            newCrystals = currentCrystals;
            hours       = 0f;

            if (tierIndex < 0 || tierIndex >= TimeWarpTiers.Length)
                return PurchaseResult.InvalidTier;

            var tier = TimeWarpTiers[tierIndex];
            if (currentCrystals < tier.CrystalCost)
                return PurchaseResult.InsufficientCrystals;

            newCrystals = currentCrystals - tier.CrystalCost;
            hours       = tier.Hours;
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
        /// <summary>Flat prestige currency (✦) granted by this tier. Persists across prestige runs.</summary>
        public long   PrestigeCurrencyAmount;

        public PrestigeCurrencyTier(string name, long cost, long amount)
        {
            Name                   = name;
            CrystalCost            = cost;
            PrestigeCurrencyAmount = amount;
        }
    }

    public class TimeWarpTier
    {
        public string Name;
        public string Description;
        public long   CrystalCost;
        /// <summary>Hours of offline production this tier instantly banks.</summary>
        public float  Hours;

        public TimeWarpTier(string name, string desc, long cost, float hours)
        {
            Name        = name;
            Description = desc;
            CrystalCost = cost;
            Hours       = hours;
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
        NothingToCollect,
    }
}
