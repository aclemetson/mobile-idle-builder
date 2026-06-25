using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure math for the field tap cooldown. Kept free of MonoBehaviour/scene state so it can be
    /// unit-tested directly.
    ///
    /// The base cooldown comes from <see cref="FieldSO.tapCooldownSeconds"/> and is shortened by two
    /// independent tracks that combine multiplicatively:
    ///   • per-run research (a product of <c>fieldCooldownMult</c> across unlocked research, ≤ 1)
    ///   • permanent prestige (<see cref="UpgradeEffectType.FieldCooldownReduction"/>, an additive
    ///     fraction summed across levels).
    /// </summary>
    public static class FieldCooldownCalculator
    {
        /// <summary>Cooldowns never drop below this, so the wheel stays readable and taps stay deliberate.</summary>
        public const float MinCooldownSeconds = 0.25f;

        /// <summary>
        /// Effective tap cooldown in seconds.
        /// </summary>
        /// <param name="baseCooldown">Field's configured base cooldown (seconds).</param>
        /// <param name="researchMult">Product of unlocked research multipliers (1 = none, &lt;1 = faster).</param>
        /// <param name="prestigeReduction">Summed prestige reduction fraction (0 = none, 0.5 = -50%).</param>
        public static float Effective(float baseCooldown, float researchMult, float prestigeReduction)
        {
            if (baseCooldown <= 0f) return MinCooldownSeconds;

            researchMult      = Mathf.Clamp(researchMult, 0f, 1f);
            prestigeReduction = Mathf.Clamp01(prestigeReduction);

            float effective = baseCooldown * researchMult * (1f - prestigeReduction);
            return Mathf.Clamp(effective, MinCooldownSeconds, baseCooldown);
        }
    }
}
