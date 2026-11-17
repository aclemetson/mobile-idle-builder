using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Whether a building's next speed level outruns its belts. A belt carries
    /// <see cref="BeltSettings.ItemsPerTile"/> x the research belt-speed multiplier items per second
    /// (2/s base, 4/s with Transportation I, 8/s with Transportation II), and every building input and output is one
    /// belt. The inspector warns on a speed upgrade that would push past that; the upgrade stays buyable and pays off
    /// once the research is done. Pure (unit-tested).
    /// </summary>
    public static class BeltCapacity
    {
        /// <summary>Items per second one belt carries.</summary>
        public static float BeltItemsPerSecond(int itemsPerTile, float conveyorSpeedMult) =>
            BeltSettings.Clamp(itemsPerTile) * (conveyorSpeedMult > 0f ? conveyorSpeedMult : 1f);

        /// <summary>
        /// The busiest belt a crafter needs at <paramref name="speed"/>: its largest input, or its output, in items/s.
        /// </summary>
        public static float PeakItemsPerSecond(IReadOnlyList<int> inputQuantities, int outputQuantity,
                                               float craftTime, float speed)
        {
            if (craftTime <= 0f || speed <= 0f) return 0f;
            int peak = outputQuantity > 0 ? outputQuantity : 1;
            if (inputQuantities != null)
                foreach (int q in inputQuantities) if (q > peak) peak = q;
            return peak * speed / craftTime;
        }

        /// <summary>
        /// The warning for a speed upgrade to <paramref name="nextLevel"/> that needs <paramref name="needed"/> items/s
        /// on one belt, or null when the belts keep up. Names the research that would make it run full.
        /// </summary>
        public static string Warning(float needed, int nextLevel, int itemsPerTile, float conveyorSpeedMult)
        {
            float belt = BeltItemsPerSecond(itemsPerTile, conveyorSpeedMult);
            if (needed <= belt + 1e-4f) return null;

            string fix;
            float baseBelt = BeltItemsPerSecond(itemsPerTile, 1f);
            if (needed <= baseBelt * 2f + 1e-4f)      fix = " Research Transportation I.";
            else if (needed <= baseBelt * 4f + 1e-4f) fix = conveyorSpeedMult >= 2f - 1e-4f
                                                          ? " Research Transportation II."
                                                          : " Research Transportation I and II.";
            else                                      fix = "";
            return $"⚠ Belts carry {belt:0.#}/s; Lv{nextLevel} needs {needed:0.#}/s.{fix}";
        }
    }
}
