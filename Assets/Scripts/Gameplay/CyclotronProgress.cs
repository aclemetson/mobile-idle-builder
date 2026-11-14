using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Cyclotron Kick meta rules on <see cref="SaveData"/>: unlock, play charges and the Insight it pays.
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
        public const int    MaxCharges       = 3;
        public const double ChargeMinutes    = 25.0;
        public const float  MinInsight       = 0.04f;    // a run with no energy still teaches something
        public const float  MaxInsightPerRun = 0.15f;
        public const float  MaxInsight       = 0.40f;    // banked cap; keeps research from going cheap
        public const float  BonusScore       = 0.90f;    // a run this good may also pay crystals
        public const float  BonusChance      = 0.25f;
        public const int    BonusCrystals    = 5;

        public static bool IsUnlocked(SaveData save) => save != null && save.prestigeCount >= UnlockPrestiges;

        /// <summary>
        /// Brings the charges up to date: one more every <see cref="ChargeMinutes"/> since the last count, up to
        /// <see cref="MaxCharges"/>. The first time the feature is seen it starts full. Offline time counts.
        /// </summary>
        public static void RefreshCharges(SaveData save, DateTime now)
        {
            if (save == null) return;
            if (!TryParse(save.cyclotronChargeUtc, out DateTime last))
            {
                save.cyclotronCharges   = MaxCharges;
                save.cyclotronChargeUtc = Iso(now);
                return;
            }
            if (save.cyclotronCharges >= MaxCharges)
            {
                save.cyclotronCharges   = MaxCharges;
                save.cyclotronChargeUtc = Iso(now);   // a full store doesn't bank time toward the next charge
                return;
            }
            if (now <= last) return;

            int gained = (int)((now - last).TotalMinutes / ChargeMinutes);
            if (gained <= 0) return;
            int charges = Math.Min(MaxCharges, save.cyclotronCharges + gained);
            save.cyclotronCharges   = charges;
            save.cyclotronChargeUtc = charges >= MaxCharges ? Iso(now) : Iso(last.AddMinutes(gained * ChargeMinutes));
        }

        /// <summary>Time until the next charge, or zero when the store is full.</summary>
        public static TimeSpan NextChargeIn(SaveData save, DateTime now)
        {
            if (save == null || save.cyclotronCharges >= MaxCharges) return TimeSpan.Zero;
            if (!TryParse(save.cyclotronChargeUtc, out DateTime last)) return TimeSpan.Zero;
            var left = last.AddMinutes(ChargeMinutes) - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>Spends one charge to start a run. False when none is left.</summary>
        public static bool TryStartRun(SaveData save, DateTime now)
        {
            RefreshCharges(save, now);
            if (save == null || save.cyclotronCharges <= 0) return false;
            if (save.cyclotronCharges >= MaxCharges) save.cyclotronChargeUtc = Iso(now);   // recharge starts now
            save.cyclotronCharges--;
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
