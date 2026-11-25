using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>The timed boosts that lock every other shop and ad bonus while they run.</summary>
    public enum TimedBoost
    {
        None,
        SpeedBurst,     // premium-shop Speed-Ups or the Production Surge ad (speedBoostExpiryUtc): 2x factory clock
        IdleRate,       // +50% Idle ad (adsIdleBoostExpiryUtc)
        CyclotronKick,  // Cyclotron Kick minigame entropy boost (cyclotronBoostExpiryUtc)
    }

    /// <summary>
    /// One boost at a time: while any timed boost runs, no shop bonus (Speed-Ups, Time Warp, entropy
    /// packs, prestige-currency packs) and no ad reward can be taken. Still allowed: crystal packs (IAP,
    /// currency not a boost), the research crystal skip, the Double Offline ad on the idle-return screen,
    /// the Entropy Surge (a one-off payout priced on net worth, so it stacks with any boost, 2026-11-24)
    /// and the Cyclotron Kick minigame itself (gameplay, not a purchase). Pure over SaveData so the rule is
    /// unit-tested; PremiumShopService and AdService enforce it, the shop and ad panels display it.
    /// </summary>
    public static class BoostLock
    {
        /// <summary>The running timed boost with the most time left, or None.</summary>
        public static TimedBoost Active(SaveData save, DateTime now)
        {
            var best = TimedBoost.None;
            var bestLeft = TimeSpan.Zero;
            Consider(TimedBoost.SpeedBurst,    save?.speedBoostExpiryUtc,      now, ref best, ref bestLeft);
            Consider(TimedBoost.IdleRate,      save?.adsIdleBoostExpiryUtc,    now, ref best, ref bestLeft);
            if (save != null && save.cyclotronBoostMultiplier > 1f)
                Consider(TimedBoost.CyclotronKick, save.cyclotronBoostExpiryUtc, now, ref best, ref bestLeft);
            return best;
        }

        /// <summary>Time until every running timed boost has ended (the lock lifts); zero when none runs.</summary>
        public static TimeSpan Remaining(SaveData save, DateTime now)
        {
            var best = TimedBoost.None;
            var left = TimeSpan.Zero;
            Consider(TimedBoost.SpeedBurst, save?.speedBoostExpiryUtc,   now, ref best, ref left);
            Consider(TimedBoost.IdleRate,   save?.adsIdleBoostExpiryUtc, now, ref best, ref left);
            if (save != null && save.cyclotronBoostMultiplier > 1f)
                Consider(TimedBoost.CyclotronKick, save.cyclotronBoostExpiryUtc, now, ref best, ref left);
            return left;
        }

        public static bool IsLocked(SaveData save, DateTime now) => Active(save, now) != TimedBoost.None;

        /// <summary>
        /// Whether the lock applies to this ad placement. Double Offline and the Entropy Surge stay available:
        /// the surge pays a share of net worth, which no running boost inflates, so stacking it is harmless.
        /// </summary>
        public static bool LocksAd(AdPlacement p) => p != AdPlacement.DoubleOffline && p != AdPlacement.EntropySurge;

        /// <summary>Whether <paramref name="p"/> is blocked right now.</summary>
        public static bool BlocksAd(SaveData save, AdPlacement p, DateTime now) => LocksAd(p) && IsLocked(save, now);

        /// <summary>Player-facing name of a boost.</summary>
        public static string Name(TimedBoost b) => b switch
        {
            TimedBoost.SpeedBurst    => "Speed burst",
            TimedBoost.IdleRate      => "Idle boost",
            TimedBoost.CyclotronKick => "Cyclotron boost",
            _                        => "Boost",
        };

        /// <summary>"Speed burst active: other boosts unlock in 12:34", or null when nothing is locked.</summary>
        public static string Message(SaveData save, DateTime now)
        {
            var b = Active(save, now);
            if (b == TimedBoost.None) return null;
            return $"{Name(b)} active: other boosts unlock in {FormatLeft(Remaining(save, now))}";
        }

        /// <summary>h:mm:ss above an hour, else m:ss.</summary>
        public static string FormatLeft(TimeSpan left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return left.TotalHours >= 1
                ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
                : $"{left.Minutes}:{left.Seconds:00}";
        }

        /// <summary>Parses an ISO 8601 round-trip UTC stamp; false for null, empty or garbage.</summary>
        public static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(iso)) return false;
            if (!DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var t)) return false;
            utc = t.ToUniversalTime();
            return true;
        }

        private static void Consider(TimedBoost kind, string expiryIso, DateTime now, ref TimedBoost best, ref TimeSpan bestLeft)
        {
            if (!TryParseUtc(expiryIso, out var end)) return;
            var left = end - now.ToUniversalTime();
            if (left <= TimeSpan.Zero || left <= bestLeft) return;
            best = kind;
            bestLeft = left;
        }
    }
}
