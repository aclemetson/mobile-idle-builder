using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// How much entropy removing a building gives back: a fraction of what was spent on it (its
    /// placement plus every speed, storage and input upgrade bought), starting at
    /// <see cref="BaseFraction"/> and raised to 100% by the Salvage Protocols prestige upgrade.
    /// Pure, so the rules are unit-tested; DeconstructController applies them.
    /// </summary>
    public static class DemolishRefund
    {
        public const float BaseFraction = 0.5f;

        /// <summary>Refund fraction for a purchased <see cref="UpgradeEffectType.DemolishRefund"/> bonus, capped at 100%.</summary>
        public static float Fraction(float bonus) => Mathf.Clamp01(BaseFraction + Mathf.Max(0f, bonus));

        /// <summary>
        /// Entropy spent on a building at these levels. Placement is charged with the prestige
        /// cost reduction (as HUDController.OnBuildingPlaced does); upgrades at their listed price.
        /// Levels at or below 1 are the base level and cost nothing.
        /// </summary>
        public static long EntropySpent(BuildingSO so, int speedLevel, int storageLevel, int inputLevel,
                                        float costReduction)
        {
            if (so == null) return 0;
            long spent = Mathf.Max(0, (int)(so.entropyCost * (1f - costReduction)));

            if (so.upgradeLevels != null)
                foreach (var u in so.upgradeLevels)
                    if (u.level > 1 && u.level <= speedLevel) spent += u.costBaseCurrency;
            if (so.storageUpgradeLevels != null)
                foreach (var u in so.storageUpgradeLevels)
                    if (u.level > 1 && u.level <= storageLevel) spent += u.costBaseCurrency;
            if (so.inputUpgradeLevels != null)
                foreach (var u in so.inputUpgradeLevels)
                    if (u.level > 1 && u.level <= inputLevel) spent += u.costBaseCurrency;
            return spent;
        }

        /// <summary>The refund for <paramref name="spent"/> at <paramref name="fraction"/>, rounded down.</summary>
        public static long Amount(long spent, float fraction) =>
            spent <= 0 ? 0 : (long)System.Math.Floor(spent * (double)Mathf.Clamp01(fraction));

        /// <summary>
        /// True when the building is the grid's only Maxwell's Demon: removing it would leave no way
        /// to earn entropy, so it is protected.
        /// </summary>
        public static bool IsProtected(bool isEntropySink, int entropySinkCount) =>
            isEntropySink && entropySinkCount <= 1;
    }
}
