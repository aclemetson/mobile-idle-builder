using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>Kinds of research effect. Only ConveyorSpeed is wired to gameplay so far.</summary>
    public enum ResearchEffectType
    {
        ConveyorSpeed,
        /// <summary>Raises the largest stream stack (Chemistry) by this many: Gas Compression adds 4, so stacks of 1-5.</summary>
        StreamStack,
    }

    /// <summary>One effect of a research node. Values of the same type add up across unlocked research.</summary>
    [Serializable]
    public struct ResearchEffect
    {
        public ResearchEffectType type;
        public float value;
    }

    /// <summary>Player-facing text for research effects.</summary>
    public static class ResearchEffectText
    {
        public static string Describe(ResearchEffect effect)
        {
            switch (effect.type)
            {
                case ResearchEffectType.ConveyorSpeed:
                    return "+" + Percent(effect.value) + "% belt speed";
                case ResearchEffectType.StreamStack:
                    return "Stack stream items up to " + StreamStackRules.Clamp(1 + (int)effect.value) + " high";
                default:
                    return effect.type + " " + effect.value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string Percent(float fraction) =>
            Math.Round(fraction * 100f, 1).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
