using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>The rewarded-ad placements. The enum name doubles as the SaveData counter id.</summary>
    public enum AdPlacement
    {
        EntropyBoost,   // grant a % of net worth in entropy (the core regular-currency placement)
        DoubleOffline,  // re-grant the just-collected offline earnings (offered on the idle-return modal)
        IdleRateBoost,  // +50% offline collection RATE for a few hours
        SpeedBoost,     // 2x production for a short window (reuses the speed-boost expiry)
        TimeWarp,       // instantly bank ~1h of offline production
        Crystals,       // small premium-currency drop
    }

    /// <summary>How a placement's reward is delivered. Drives AdService.GrantReward's switch.</summary>
    public enum AdRewardKind { Entropy, DoubleOffline, IdleRateBoost, SpeedBoost, TimeWarp, Crystals }

    /// <summary>
    /// One rewarded-ad placement: its display copy, daily cap, and the reward it pays. Reward magnitudes
    /// are intentionally smaller than the equivalent premium-shop tiers (ads are free), and tuned to be
    /// balance-safe. Keep in sync with docs/agents/economy-balance.md.
    /// </summary>
    public class AdPlacementDef
    {
        public AdPlacement  Placement;
        public AdRewardKind Kind;
        public string       Name;
        public string       Description;
        public int          DailyCap;

        // Reward params (only the fields relevant to Kind are used):
        public float NetWorthFraction;  // Entropy: fraction of last-known net worth
        public long  MinimumFloor;      // Entropy: floor so early-game grants are not zero
        public long  CrystalAmount;     // Crystals
        public float DurationHours;     // IdleRateBoost / TimeWarp window length

        /// <summary>Id used as the SaveData daily-counter key (stable across builds).</summary>
        public string Id => Placement.ToString();
    }

    /// <summary>
    /// Pure static rewarded-ad logic: placement table, reward-amount math, and per-day cap/reset
    /// bookkeeping over SaveData. No MonoBehaviour dependency — fully unit-testable, mirroring
    /// <see cref="PremiumShopCalculator"/>. AdService orchestrates the SDK + currency grants on top.
    /// </summary>
    public static class AdRewardCalculator
    {
        /// <summary>Multiplier applied to the offline collection rate while the idle-rate boost is active.</summary>
        public const float IdleRateBoostMultiplier = 1.5f;

        /// <summary>2x production window granted by the SpeedBoost placement (reuses speed-boost expiry).</summary>
        public static readonly TimeSpan SpeedBoostDuration = TimeSpan.FromMinutes(30);

        // ── Placement table ───────────────────────────────────────────────────
        public static readonly AdPlacementDef[] Placements =
        {
            new AdPlacementDef {
                Placement = AdPlacement.EntropyBoost, Kind = AdRewardKind.Entropy,
                Name = "Entropy Boost", Description = "Watch an ad for a burst of entropy.",
                DailyCap = 5, NetWorthFraction = 0.10f, MinimumFloor = 500,
            },
            new AdPlacementDef {
                Placement = AdPlacement.DoubleOffline, Kind = AdRewardKind.DoubleOffline,
                Name = "Double Offline", Description = "Watch an ad to double your offline earnings.",
                DailyCap = 3,
            },
            new AdPlacementDef {
                Placement = AdPlacement.IdleRateBoost, Kind = AdRewardKind.IdleRateBoost,
                Name = "+50% Idle", Description = "+50% offline collection rate for 4 hours.",
                DailyCap = 3, DurationHours = 4f,
            },
            new AdPlacementDef {
                Placement = AdPlacement.SpeedBoost, Kind = AdRewardKind.SpeedBoost,
                Name = "Production Surge", Description = "2x production for 30 minutes.",
                DailyCap = 2,
            },
            new AdPlacementDef {
                Placement = AdPlacement.TimeWarp, Kind = AdRewardKind.TimeWarp,
                Name = "Time Warp", Description = "Instantly bank 1 hour of production.",
                DailyCap = 2, DurationHours = 1f,
            },
            new AdPlacementDef {
                Placement = AdPlacement.Crystals, Kind = AdRewardKind.Crystals,
                Name = "Crystal Drop", Description = "Watch an ad for a few crystals.",
                DailyCap = 1, CrystalAmount = 25,
            },
        };

        public static AdPlacementDef Def(AdPlacement p)
        {
            foreach (var d in Placements)
                if (d.Placement == p) return d;
            throw new ArgumentOutOfRangeException(nameof(p));
        }

        public static int DailyCap(AdPlacement p) => Def(p).DailyCap;

        // ── Reward amounts ────────────────────────────────────────────────────

        /// <summary>Entropy granted by the EntropyBoost placement: a % of net worth with a floor.</summary>
        public static long CalcEntropyReward(float lastKnownNetWorth)
        {
            var def = Def(AdPlacement.EntropyBoost);
            long fromNetWorth = (long)(lastKnownNetWorth * def.NetWorthFraction);
            return Math.Max(fromNetWorth, def.MinimumFloor);
        }

        // ── Daily cap bookkeeping (operate on SaveData; injected `now` for tests) ──

        /// <summary>
        /// Zeroes the per-placement counters when the current UTC day has rolled over. Copies the
        /// ParseOrEpoch / NextMidnightUtc pattern from AchievementService / DailyEventService.
        /// Safe to call repeatedly; only mutates on an actual day change.
        /// </summary>
        public static void ApplyDailyReset(SaveData save, DateTime now)
        {
            if (save == null) return;
            save.adWatchCounts ??= new System.Collections.Generic.List<AchievementProgressEntry>();

            DateTime nextReset = ParseOrEpoch(save.adWatchResetUtc);
            if (!string.IsNullOrEmpty(save.adWatchResetUtc) && now < nextReset) return;

            save.adWatchCounts.Clear();
            save.adWatchResetUtc = NextMidnightUtc(now).ToString("o");
        }

        public static int GetWatchCount(SaveData save, AdPlacement p)
        {
            if (save?.adWatchCounts == null) return 0;
            string id = p.ToString();
            foreach (var e in save.adWatchCounts)
                if (e.id == id) return e.count;
            return 0;
        }

        public static int Remaining(SaveData save, AdPlacement p)
            => Math.Max(0, DailyCap(p) - GetWatchCount(save, p));

        public static bool IsAtCap(SaveData save, AdPlacement p)
            => GetWatchCount(save, p) >= DailyCap(p);

        /// <summary>Increments today's watch count for a placement (called after a reward is granted).</summary>
        public static void IncrementWatch(SaveData save, AdPlacement p)
        {
            if (save == null) return;
            save.adWatchCounts ??= new System.Collections.Generic.List<AchievementProgressEntry>();

            string id = p.ToString();
            foreach (var e in save.adWatchCounts)
                if (e.id == id) { e.count++; return; }
            save.adWatchCounts.Add(new AchievementProgressEntry { id = id, count = 1 });
        }

        // ── Internals (shared UTC helpers, same shape as DailyEventService) ─────

        static DateTime ParseOrEpoch(string iso) =>
            DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue;

        static DateTime NextMidnightUtc(DateTime from) => from.Date.AddDays(1); // 00:00 UTC tomorrow
    }
}
