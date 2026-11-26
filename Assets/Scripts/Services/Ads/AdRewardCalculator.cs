using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>The rewarded-ad placements. The enum name doubles as the SaveData counter id.</summary>
    public enum AdPlacement
    {
        EntropyBoost,   // retired 2026-11-24 (#489): folded into the Entropy Surge. Kept so old save counters still parse.
        DoubleOffline,  // re-grant the just-collected offline earnings (offered on the idle-return modal)
        IdleRateBoost,  // +50% offline collection RATE for 4 hours (name kept: it is the save counter id)
        SpeedBoost,     // 2x production for a short window (reuses the speed-boost expiry)
        TimeWarp,       // instantly bank ~1h of offline production
        Crystals,       // small premium-currency drop
        EntropySurge,   // a roll: 10% of net worth in entropy or a few crystals, every 15 min, no daily cap (#489)
        IdleRateBoostShort, // +50% offline collection RATE for 2 hours (2026-11-20: pick 2 h or 4 h by time away)
    }

    /// <summary>How a placement's reward is delivered. Drives AdService.GrantReward's switch.</summary>
    public enum AdRewardKind { Entropy, DoubleOffline, IdleRateBoost, SpeedBoost, TimeWarp, Crystals, SurgeRoll }

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
        public float NetWorthFraction;  // Entropy / SurgeRoll: fraction of live net worth
        public long  MinimumFloor;      // Entropy / SurgeRoll: floor so early-game grants are not zero
        public long  CrystalAmount;     // Crystals / SurgeRoll
        public float CrystalChance;     // SurgeRoll: chance the roll pays crystals instead of entropy
        public float DurationHours;     // IdleRateBoost / TimeWarp window length
        public float CooldownMinutes;   // > 0: minimum time between two watches of this placement

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

        /// <summary>Time between two watches of any placement without its own cooldown (Entropy Surge has 15 min).</summary>
        public const float StandardCooldownMinutes = 5f;

        /// <summary>DailyCap of a placement with no daily limit (the Entropy Surge, #489).</summary>
        public const int Uncapped = int.MaxValue;

        /// <summary>2x production window granted by the SpeedBoost placement (reuses speed-boost expiry).</summary>
        public static readonly TimeSpan SpeedBoostDuration = TimeSpan.FromMinutes(30);

        // ── Placement table ───────────────────────────────────────────────────
        public static readonly AdPlacementDef[] Placements =
        {
            new AdPlacementDef {
                Placement = AdPlacement.DoubleOffline, Kind = AdRewardKind.DoubleOffline,
                Name = "Double Offline", Description = "Watch an ad to double your offline earnings.",
                DailyCap = 3, CooldownMinutes = StandardCooldownMinutes,
            },
            // Two lengths so the player picks by how long they'll be away (2026-11-20); each 2 a day.
            new AdPlacementDef {
                Placement = AdPlacement.IdleRateBoostShort, Kind = AdRewardKind.IdleRateBoost,
                Name = "+50% Idle (2h)", Description = "+50% offline collection rate for 2 hours.",
                DailyCap = 2, DurationHours = 2f, CooldownMinutes = StandardCooldownMinutes,
            },
            new AdPlacementDef {
                Placement = AdPlacement.IdleRateBoost, Kind = AdRewardKind.IdleRateBoost,
                Name = "+50% Idle (4h)", Description = "+50% offline collection rate for 4 hours.",
                DailyCap = 2, DurationHours = 4f, CooldownMinutes = StandardCooldownMinutes,
            },
            new AdPlacementDef {
                Placement = AdPlacement.SpeedBoost, Kind = AdRewardKind.SpeedBoost,
                Name = "Production Surge", Description = "2x production for 30 minutes.",
                DailyCap = 2, CooldownMinutes = StandardCooldownMinutes,
            },
            new AdPlacementDef {
                Placement = AdPlacement.TimeWarp, Kind = AdRewardKind.TimeWarp,
                Name = "Time Warp", Description = "Instantly bank 1 hour of production.",
                DailyCap = 2, DurationHours = 1f, CooldownMinutes = StandardCooldownMinutes,
            },
            new AdPlacementDef {
                Placement = AdPlacement.Crystals, Kind = AdRewardKind.Crystals,
                Name = "Crystal Drop", Description = "Watch an ad for a few crystals.",
                DailyCap = 1, CrystalAmount = 25, CooldownMinutes = StandardCooldownMinutes,
            },
            // #489 (2026-11-24): the only entropy ad. Each watch rolls: 80% pays 10% of net worth in entropy,
            // 20% pays 10 crystals. No daily cap; the 15-minute cooldown paces it.
            new AdPlacementDef {
                Placement = AdPlacement.EntropySurge, Kind = AdRewardKind.SurgeRoll,
                Name = "Entropy Surge",
                Description = "Watch an ad: usually 10% of your net worth in entropy, sometimes 10 crystals.",
                DailyCap = Uncapped, NetWorthFraction = 0.10f, MinimumFloor = 500,
                CrystalAmount = 10, CrystalChance = 0.20f, CooldownMinutes = 15f,
            },
        };

        public static AdPlacementDef Def(AdPlacement p)
        {
            foreach (var d in Placements)
                if (d.Placement == p) return d;
            throw new ArgumentOutOfRangeException(nameof(p));
        }

        public static int DailyCap(AdPlacement p) => Def(p).DailyCap;

        /// <summary>True when the placement has a daily limit (every one but the Entropy Surge).</summary>
        public static bool HasDailyCap(AdPlacement p) => DailyCap(p) != Uncapped;

        // ── Reward amounts ────────────────────────────────────────────────────

        /// <summary>Entropy the Entropy Surge's entropy roll pays: a % of net worth with a floor.</summary>
        public static long CalcEntropyReward(double netWorth)
        {
            var def = Def(AdPlacement.EntropySurge);
            double fromNetWorth = netWorth > 0d && !double.IsNaN(netWorth) ? netWorth * AsDouble(def.NetWorthFraction) : 0d;
            long amount = fromNetWorth >= long.MaxValue ? long.MaxValue : (long)fromNetWorth;
            return Math.Max(amount, def.MinimumFloor);
        }

        /// <summary>
        /// Whether a surge pays crystals: <paramref name="roll01"/> is a uniform value in [0, 1); below the
        /// placement's crystal chance (20%) it is crystals, otherwise entropy.
        /// </summary>
        public static bool SurgeRollsCrystals(double roll01, SaveData save = null) => roll01 < AsDouble(SurgeCrystalChance(save));

        /// <summary>The surge's crystal chance: 20% plus Surge Luck from the tournament track (#500), at most 1.</summary>
        public static float SurgeCrystalChance(SaveData save) =>
            Math.Min(1f, Def(AdPlacement.EntropySurge).CrystalChance + TournamentTrack.SurgeCrystalBonus(save));

        // 0.1f widens to 0.10000000149: round a table fraction back to what was written before using it as a double.
        static double AsDouble(float fraction) => Math.Round(fraction, 6);

        // ── Cooldown bookkeeping (placements with CooldownMinutes > 0) ──────────

        /// <summary>Time left before <paramref name="p"/> may be watched again; zero when ready or uncooled.</summary>
        public static TimeSpan CooldownRemaining(SaveData save, AdPlacement p, DateTime now)
        {
            var def = Def(p);
            if (def.CooldownMinutes <= 0f || save?.adLastWatch == null) return TimeSpan.Zero;

            string id = p.ToString();
            foreach (var e in save.adLastWatch)
            {
                if (e == null || e.id != id) continue;
                DateTime last = ParseOrEpoch(e.utc);
                if (last == DateTime.MinValue) return TimeSpan.Zero;
                TimeSpan left = last.AddMinutes(def.CooldownMinutes) - now;
                // A clock that moved backwards (last watch "in the future") never locks the ad for longer
                // than one cooldown.
                TimeSpan max = TimeSpan.FromMinutes(def.CooldownMinutes);
                return left <= TimeSpan.Zero ? TimeSpan.Zero : (left > max ? max : left);
            }
            return TimeSpan.Zero;
        }

        public static bool IsOnCooldown(SaveData save, AdPlacement p, DateTime now)
            => CooldownRemaining(save, p, now) > TimeSpan.Zero;

        /// <summary>Stamps the watch time that starts the placement's cooldown (persisted, ISO 8601 UTC).</summary>
        public static void RecordWatch(SaveData save, AdPlacement p, DateTime now)
        {
            if (save == null) return;
            save.adLastWatch ??= new System.Collections.Generic.List<AdLastWatchEntry>();
            string id = p.ToString(), stamp = now.ToString("o");
            foreach (var e in save.adLastWatch)
                if (e != null && e.id == id) { e.utc = stamp; return; }
            save.adLastWatch.Add(new AdLastWatchEntry { id = id, utc = stamp });
        }

        /// <summary>
        /// Restarts a placement's cooldown without counting a watch (the player declined the offer).
        /// Same stamp as a watch, so the offer returns one cooldown later; the daily count is untouched.
        /// </summary>
        public static void RestartCooldown(SaveData save, AdPlacement p, DateTime now) => RecordWatch(save, p, now);

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
