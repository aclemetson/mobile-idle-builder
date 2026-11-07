using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// What products are worth, for the Recipes tab and the building inspector. Pure maths that mirrors
    /// ProductionSystem and CollectorSystem: price never changes (baseSellValue, or 1 when it has none);
    /// bonuses raise quantity (outMult, rounded down per output) and speed.
    /// PrestigeData.OutputMultiplier is deliberately not applied: no system applies it yet.
    /// </summary>
    public static class ItemValue
    {
        /// <summary>The Demon price of one item. One definition, shared with the automated sales.</summary>
        public static float PricePerItem(ItemSO item) =>
            item == null ? 1f : SurplusSale.DemonPrice(item.itemId);

        /// <summary>Entropy for one craft of one output slot, plus escaping byproduct entropy.</summary>
        public static double PerCraft(float price, int outputQty, float outMult, float escapeEntropy)
        {
            double sold = (double)price * Math.Max(0, (int)(outputQty * outMult));
            double escaped = escapeEntropy > 0f ? (long)(escapeEntropy * outMult) : 0d;
            return sold + escaped;
        }

        /// <summary>Entropy for one craft across every output slot, plus escaping byproduct entropy once.</summary>
        public static double PerCraft(IReadOnlyList<(int itemId, int quantity)> outputs,
                                      Func<int, float> priceOf, float outMult, float escapeEntropy)
        {
            double total = PerCraft(0f, 0, outMult, escapeEntropy);
            if (outputs == null) return total;
            for (int i = 0; i < outputs.Count; i++)
                total += PerCraft(priceOf(outputs[i].itemId), outputs[i].quantity, outMult, 0f);
            return total;
        }

        /// <summary>Entropy per second: perCraft x speed / craftTime. 0 when craftTime or speed is not positive.</summary>
        public static double PerSecond(double perCraft, float craftTime, float speed)
        {
            if (craftTime <= 0f || speed <= 0f) return 0d;
            return perCraft * speed / craftTime;
        }

        /// <summary>Entropy per second from a collector: floor(outMult) items per interval, outputRate intervals a second.</summary>
        public static double CollectorPerSecond(float price, float outputRate, float outMult)
        {
            float rate = outputRate > 0f ? outputRate : 1f;
            return (double)price * Math.Max(0, (int)outMult) * rate;
        }
    }
}
