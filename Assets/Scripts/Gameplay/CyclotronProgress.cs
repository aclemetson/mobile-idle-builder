using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Cyclotron Kick meta rules on <see cref="SaveData"/>: unlock, the hourly cooldown and the Insight it pays.
    /// Static and pure apart from the save it is given, so every rule is unit-tested. All of it survives
    /// prestige (it is the reward for the stretch prestige leaves you in).
    ///
    /// <b>Insight</b> is a discount on the next research started (stacked in <see cref="ResearchCost"/>),
    /// used up by that purchase. It aims at the post-Carbon research wall instead of paying more entropy,
    /// which the Entropy Surge ad already does.
    /// </summary>
    public static class CyclotronProgress
    {
        public const int    UnlockPrestiges  = 2;        // available from the second prestige on
        public const double CooldownMinutes  = 60.0;     // one run an hour, counted from the run's start
        public const float  MinInsight       = 0.04f;    // a run with no energy still teaches something
        public const float  MaxInsightPerRun = 0.15f;
        public const float  MaxInsight       = 0.40f;    // banked cap; keeps research from going cheap
        public const float  BonusScore       = 0.90f;    // a run this good may also pay crystals
        public const float  BonusChance      = 0.25f;
        public const int    BonusCrystals    = 5;

        public static bool IsUnlocked(SaveData save) => save != null && save.prestigeCount >= UnlockPrestiges;

        /// <summary>Time until the next run is allowed; zero when one is ready (or none was ever played).</summary>
        public static TimeSpan CooldownLeft(SaveData save, DateTime now)
        {
            if (save == null || !TryParse(save.cyclotronLastRunUtc, out DateTime last)) return TimeSpan.Zero;
            var left = last.AddMinutes(CooldownMinutes) - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        public static bool IsReady(SaveData save, DateTime now) => save != null && CooldownLeft(save, now) <= TimeSpan.Zero;

        /// <summary>Starts a run if the cooldown is over; the next hour is counted from now. Offline time counts.</summary>
        public static bool TryStartRun(SaveData save, DateTime now)
        {
            if (!IsReady(save, now)) return false;
            save.cyclotronLastRunUtc = Iso(now);
            return true;
        }

        /// <summary>Insight a run pays for the energy it reached (0..1): 4% at nothing, 15% at full.</summary>
        public static float InsightFor(float energy)
        {
            float e = energy < 0f ? 0f : energy > 1f ? 1f : energy;
            float raw = MinInsight + (MaxInsightPerRun - MinInsight) * e;
            return (float)Math.Round(raw, 2, MidpointRounding.AwayFromZero);   // whole percentage points
        }

        /// <summary>Crystals a run pays besides Insight. <paramref name="roll"/> is a uniform 0..1 draw.</summary>
        public static int BonusCrystalsFor(float energy, double roll) =>
            energy >= BonusScore && roll < BonusChance ? BonusCrystals : 0;

        /// <summary>Banks a run's reward. Returns the Insight actually added (it is capped at <see cref="MaxInsight"/>).</summary>
        public static float Collect(SaveData save, float energy, double roll, out int crystals)
        {
            crystals = 0;
            if (save == null) return 0f;
            float before = save.researchInsight;
            save.researchInsight = Math.Min(MaxInsight, before + InsightFor(energy));
            crystals = BonusCrystalsFor(energy, roll);
            save.paidCurrency += crystals;
            return save.researchInsight - before;
        }

        /// <summary>The next research uses the banked Insight up. Called once its price is paid.</summary>
        public static void ConsumeInsight(SaveData save)
        {
            if (save != null) save.researchInsight = 0f;
        }

        static string Iso(DateTime t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        static bool TryParse(string s, out DateTime t) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t);
    }
}
