using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// What products are worth, for the Recipes tab and the building inspector. Pure maths that mirrors
    /// ProductionSystem and CollectorSystem: price never changes (baseSellValue, or 1 when it has none);
    /// bonuses raise quantity (outMult) and speed. Deposits carry the fraction of a fractional outMult to
    /// the next craft (<see cref="OutputCarry"/>), so quantities here are the long-run average, not floored.
    /// Escaping byproduct entropy is still floored per craft, as ProductionSystem pays it.
    /// </summary>
    public static class ItemValue
    {
        /// <summary>The Demon price of one item. One definition, shared with the automated sales.</summary>
        public static float PricePerItem(ItemSO item) =>
            item == null ? 1f : SurplusSale.DemonPrice(item.itemId);

        /// <summary>Entropy for one craft of one output slot, plus escaping byproduct entropy.</summary>
        public static double PerCraft(float price, int outputQty, float outMult, float escapeEntropy)
        {
            double sold = (double)price * Math.Max(0d, (double)outputQty * outMult);
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

        /// <summary>Entropy per second from a collector: outMult items per interval (on average), outputRate x speed intervals a second.</summary>
        public static double CollectorPerSecond(float price, float outputRate, float outMult, float speed = 1f)
        {
            float rate = outputRate > 0f ? outputRate : 1f;
            if (speed <= 0f) speed = 1f;
            return (double)price * Math.Max(0d, outMult) * rate * speed;
        }
    }
}
