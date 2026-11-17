using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Cyclotron Kick meta rules on <see cref="SaveData"/>: unlock, the cooldown, the entropy boost a run pays,
    /// the Insight it pays and the two upgrades Insight buys. Static and pure apart from the save it is given,
    /// so every rule is unit-tested. All of it survives prestige (it is the reward for the stretch prestige
    /// leaves you in).
    ///
    /// A run pays two things:
    ///  - an <b>entropy boost</b>: every entropy the factory earns (Demon sales, surplus autosell, escaping
    ///    byproducts) is multiplied by x1.5..x3 (by energy reached) for a few minutes. Applied live by the
    ///    systems through the <see cref="EntropyIncomeBoost"/> singleton; offline collection does not use it.
    ///  - <b>Insight</b> points (1..5 by energy), spent on the upgrades: <i>Longer Boost</i> (+1 min a level)
    ///    and <i>Faster Recharge</i> (-5 min of cooldown a level).
    /// </summary>
    public static class CyclotronProgress
    {
        public const int    UnlockPrestiges      = 2;      // available from the second prestige on

        // Cooldown (Faster Recharge): 60 min, -5 min a level, 6 levels -> 30 min.
        public const double BaseCooldownMinutes  = 60.0;
        public const double CooldownStepMinutes  = 5.0;
        public const int    MaxCooldownLevel     = 6;

        // Boost length (Longer Boost): 5 min, +1 min a level, 10 levels -> 15 min. Always shorter than the
        // shortest cooldown, so two boosts never overlap.
        public const double BaseBoostMinutes     = 5.0;
        public const double BoostStepMinutes     = 1.0;
        public const int    MaxBoostLevel        = 10;

        // Boost strength by energy reached.
        public const float  MinMultiplier        = 1.5f;   // a run with no energy still helps
        public const float  MaxMultiplier        = 3.0f;

        // Insight points a run pays: 1 at no energy, +1 per quarter of the bar, 5 at full.
        public const int    MinInsight           = 1;
        public const int    MaxInsightPerRun     = 5;

        // Upgrade prices in Insight: level L -> L+1 costs Base + Step x L.
        public const int    BoostCostBase        = 3, BoostCostStep    = 2;   // 3, 5, 7 ... 21 (120 in all)
        public const int    CooldownCostBase     = 4, CooldownCostStep = 3;   // 4, 7, 10 ... 19 (69 in all)

        public const float  BonusScore           = 0.90f;  // a run this good may also pay crystals
        public const float  BonusChance          = 0.25f;
        public const int    BonusCrystals        = 5;

        public enum Upgrade { Boost, Cooldown }

        public static bool IsUnlocked(SaveData save) => save != null && save.prestigeCount >= UnlockPrestiges;

        // ── Cooldown ────────────────────────────────────────────────────────

        public static double CooldownMinutes(int level) =>
            BaseCooldownMinutes - CooldownStepMinutes * Clamp(level, MaxCooldownLevel);

        public static double CooldownMinutes(SaveData save) => CooldownMinutes(save?.cyclotronCooldownLevel ?? 0);

        /// <summary>Time until the next run is allowed; zero when one is ready (or none was ever played).</summary>
        public static TimeSpan CooldownLeft(SaveData save, DateTime now)
        {
            if (save == null || !TryParse(save.cyclotronLastRunUtc, out DateTime last)) return TimeSpan.Zero;
            var left = last.AddMinutes(CooldownMinutes(save)) - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        public static bool IsReady(SaveData save, DateTime now) => save != null && CooldownLeft(save, now) <= TimeSpan.Zero;

        /// <summary>Starts a run if the cooldown is over; the next cooldown is counted from now. Offline time counts.</summary>
        public static bool TryStartRun(SaveData save, DateTime now)
        {
            if (!IsReady(save, now)) return false;
            save.cyclotronLastRunUtc = Iso(now);
            return true;
        }

        // ── Boost ───────────────────────────────────────────────────────────

        public static double BoostMinutes(int level) => BaseBoostMinutes + BoostStepMinutes * Clamp(level, MaxBoostLevel);

        public static double BoostMinutes(SaveData save) => BoostMinutes(save?.cyclotronBoostLevel ?? 0);

        /// <summary>Entropy multiplier a run pays for the energy it reached (0..1): x1.5 at nothing, x3 at full.</summary>
        public static float MultiplierFor(float energy)
        {
            float e = energy < 0f ? 0f : energy > 1f ? 1f : energy;
            float raw = MinMultiplier + (MaxMultiplier - MinMultiplier) * e;
            return (float)Math.Round(raw, 1, MidpointRounding.AwayFromZero);   // one decimal: x2.4
        }

        /// <summary>The live entropy multiplier: the last run's while its boost lasts, else 1.</summary>
        public static float BoostMultiplier(SaveData save, DateTime now) =>
            BoostLeft(save, now) > TimeSpan.Zero && save.cyclotronBoostMultiplier > 1f ? save.cyclotronBoostMultiplier : 1f;

        /// <summary>Time the current boost has left; zero when none is active.</summary>
        public static TimeSpan BoostLeft(SaveData save, DateTime now)
        {
            if (save == null || !TryParse(save.cyclotronBoostExpiryUtc, out DateTime end)) return TimeSpan.Zero;
            var left = end - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        // ── Insight ─────────────────────────────────────────────────────────

        /// <summary>Insight points a run pays for the energy it reached: 1 at nothing, +1 per 25%, 5 at full.</summary>
        public static int InsightFor(float energy)
        {
            float e = energy < 0f ? 0f : energy > 1f ? 1f : energy;
            return Math.Min(MaxInsightPerRun, MinInsight + (int)Math.Floor(e * 4f + 1e-4f));
        }

        // ── Collect ─────────────────────────────────────────────────────────

        /// <summary>
        /// Banks a run's reward: starts the boost now (replacing any older one), adds the Insight and maybe
        /// crystals. <paramref name="roll"/> is a uniform 0..1 draw for the crystal bonus.
        /// </summary>
        public static void Collect(SaveData save, float energy, double roll, DateTime now,
                                   out float multiplier, out int insight, out int crystals)
        {
            multiplier = 1f; insight = 0; crystals = 0;
            if (save == null) return;
            multiplier = MultiplierFor(energy);
            save.cyclotronBoostMultiplier = multiplier;
            save.cyclotronBoostExpiryUtc  = Iso(now.AddMinutes(BoostMinutes(save)));
            insight = InsightFor(energy);
            save.cyclotronInsight += insight;
            crystals = BonusCrystalsFor(energy, roll);
            save.paidCurrency += crystals;
        }

        /// <summary>Crystals a run pays besides the boost. <paramref name="roll"/> is a uniform 0..1 draw.</summary>
        public static int BonusCrystalsFor(float energy, double roll) =>
            energy >= BonusScore && roll < BonusChance ? BonusCrystals : 0;

        // ── Upgrades ────────────────────────────────────────────────────────

        public static int Level(SaveData save, Upgrade u) =>
            save == null ? 0 : u == Upgrade.Boost ? save.cyclotronBoostLevel : save.cyclotronCooldownLevel;

        public static int MaxLevel(Upgrade u) => u == Upgrade.Boost ? MaxBoostLevel : MaxCooldownLevel;

        public static bool IsMaxed(SaveData save, Upgrade u) => Level(save, u) >= MaxLevel(u);

        /// <summary>Insight to take <paramref name="u"/> from <paramref name="level"/> to the next level.</summary>
        public static int UpgradeCost(Upgrade u, int level) =>
            u == Upgrade.Boost ? BoostCostBase + BoostCostStep * level : CooldownCostBase + CooldownCostStep * level;

        public static bool CanUpgrade(SaveData save, Upgrade u) =>
            save != null && !IsMaxed(save, u) && save.cyclotronInsight >= UpgradeCost(u, Level(save, u));

        /// <summary>Spends Insight on one level of <paramref name="u"/>. False if maxed or short of Insight.</summary>
        public static bool TryUpgrade(SaveData save, Upgrade u)
        {
            if (!CanUpgrade(save, u)) return false;
            save.cyclotronInsight -= UpgradeCost(u, Level(save, u));
            if (u == Upgrade.Boost) save.cyclotronBoostLevel++;
            else                    save.cyclotronCooldownLevel++;
            return true;
        }

        static int Clamp(int level, int max) => level < 0 ? 0 : level > max ? max : level;

        static string Iso(DateTime t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        static bool TryParse(string s, out DateTime t) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t);
    }
}
