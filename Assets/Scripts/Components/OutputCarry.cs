namespace MobileIdleBuilder
{
    /// <summary>
    /// Turns a fractional output multiplier into whole crafts without losing the fraction.
    /// A 1.1x bonus on a 1-item recipe used to floor to 1 every time, so small bonuses (Quantum Yield,
    /// megastructure, 1.5x managers) did nothing live. The carry keeps the leftover fraction on the
    /// building, so over many crafts the deposits average exactly <c>multiplier</c> crafts' worth,
    /// matching the idle model. Burst-safe: plain arithmetic on blittable values.
    /// </summary>
    public static class OutputCarry
    {
        /// <summary>
        /// Returns how many crafts' worth of output to deposit now and updates <paramref name="carry"/>
        /// with the fraction left over. A multiplier at or below 0 deposits nothing.
        /// </summary>
        public static int Take(float multiplier, ref float carry)
        {
            if (multiplier <= 0f) return 0;
            float total = multiplier + carry;
            int whole = (int)total;            // total >= 0, so the cast floors
            carry = total - whole;
            return whole;
        }
    }
}
