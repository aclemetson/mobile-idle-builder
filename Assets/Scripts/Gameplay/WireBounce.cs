using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Decay envelope for the field wire-mesh tap bounce. <see cref="FieldWireMesh"/> feeds the
    /// returned value into the shader's <c>_BounceAmp</c> each frame; the shader turns it into a
    /// radial pulse. Kept pure (no Unity state) so the decay can be unit-tested.
    /// </summary>
    public static class WireBounce
    {
        /// <summary>
        /// Bounce amplitude <paramref name="elapsed"/> seconds after a tap. Peaks at
        /// <paramref name="initial"/> at the moment of the tap and decays exponentially toward 0
        /// at rate <paramref name="decay"/>. Always finite and non-negative for non-negative inputs.
        /// </summary>
        public static float Amplitude(float elapsed, float initial, float decay)
        {
            if (elapsed <= 0f) return initial;        // peak at (and before) the tap moment
            float v = initial * Mathf.Exp(-decay * elapsed);
            return float.IsNaN(v) ? 0f : v;
        }
    }
}
