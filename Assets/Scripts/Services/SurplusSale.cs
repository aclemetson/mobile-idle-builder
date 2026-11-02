using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Items a building can't use are sold automatically at part of the Demon price instead of piling up
    /// unseen in the inventory or an input buffer (#312): a belt's front item that jams a building (#200
    /// overflow), and inputs the newly set recipe no longer uses. The cut is reduced by the
    /// <see cref="UpgradeEffectType.SurplusResale"/> permanent upgrade, not by research.
    /// </summary>
    public static class SurplusSale
    {
        /// <summary>Share of the Demon price paid with no upgrade.</summary>
        public const float BaseFraction = 0.5f;

        /// <summary>Share of the Demon price paid for <paramref name="upgradeBonus"/> (from the permanent upgrade), capped at 100%.</summary>
        public static float Fraction(float upgradeBonus) => Mathf.Clamp01(BaseFraction + Mathf.Max(0f, upgradeBonus));

        /// <summary>The share paid right now, from the player's permanent upgrades.</summary>
        public static float CurrentFraction() =>
            Fraction(PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.SurplusResale) ?? 0f);

        /// <summary>The Demon price of one item: its sell value, or 1 when it has none (as EntropySinkSystem pays).</summary>
        public static float DemonPrice(int itemId)
        {
            float v = ItemDatabase.GetStatic(itemId)?.baseSellValue ?? 0f;
            return v > 0f ? v : 1f;
        }

        /// <summary>Entropy for <paramref name="quantity"/> items at <paramref name="demonPrice"/> each, paid at <paramref name="fraction"/>, before rounding.</summary>
        public static double ExactValue(float demonPrice, int quantity, float fraction) =>
            quantity <= 0 ? 0d : (double)Mathf.Max(0f, demonPrice) * quantity * Mathf.Clamp01(fraction);

        /// <summary><see cref="ExactValue"/> rounded down to whole entropy.</summary>
        public static long Value(float demonPrice, int quantity, float fraction) =>
            (long)System.Math.Floor(ExactValue(demonPrice, quantity, fraction));

        /// <summary>
        /// Removes every item from <paramref name="inputBuffer"/> that <paramref name="recipeInputs"/> doesn't
        /// consume and returns what they sell for. Items the recipe uses stay put.
        /// </summary>
        public static long SellUnusedInputs(DynamicBuffer<BuildingInputSlot> inputBuffer,
                                            DynamicBuffer<RecipeInputSlot> recipeInputs,
                                            System.Func<int, float> demonPrice, float fraction)
        {
            double total = 0;
            for (int i = inputBuffer.Length - 1; i >= 0; i--)
            {
                var slot = inputBuffer[i];
                if (InputAcceptance.RecipeUses(recipeInputs, slot.ItemID)) continue;
                total += ExactValue(demonPrice(slot.ItemID), slot.Quantity, fraction);
                inputBuffer.RemoveAt(i);
            }
            return (long)System.Math.Floor(total);
        }
    }
}
