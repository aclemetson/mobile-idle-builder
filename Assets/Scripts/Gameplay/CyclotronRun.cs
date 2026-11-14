using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// One Cyclotron Kick run (the post-Carbon downtime minigame). Pure, no Unity types, so it is unit-tested.
    ///
    /// The physics it shows: a cyclotron's particle circles between two D-shaped electrodes ("dees") and is
    /// accelerated each time it crosses the gap between them, twice per lap. Here the gap is vertical, so the
    /// particle crosses it at the top (270°) and the bottom (90°; angles are screen angles, y down). The player
    /// taps as it crosses: a tap inside the window "kicks" it, adding energy and speeding it up, and a tap
    /// outside the window is a mistimed kick that costs energy. Energy also sets the orbit radius, so the
    /// particle visibly spirals outward as it gains energy, as in the real machine.
    /// </summary>
    public sealed class CyclotronRun
    {
        public const float DurationSeconds  = 30f;
        public const float WindowDegrees    = 18f;   // half-width of the gap window around a crossing
        public const float PerfectDegrees   = 5f;    // half-width that counts as a perfect kick
        public const float StartSpeed       = 180f;  // degrees per second (one lap every 2 s)
        public const float MaxSpeed         = 540f;
        public const float SpeedUpPerKick   = 1.035f;
        public const float PerfectGain      = 0.02f;
        public const float GoodGainMax      = 0.013f;
        public const float MissPenalty      = 0.03f;
        public const int   MaxCombo         = 5;      // each kick in a row adds 10% to the gain, up to +50%

        static readonly float[] GapAngles = { 90f, 270f };

        public enum Kick { Perfect, Good, Miss }

        public float Angle    { get; private set; }  // 0..360, screen degrees, clockwise
        public float Speed    { get; private set; } = StartSpeed;
        public float Energy   { get; private set; }  // 0..1, the score
        public float Elapsed  { get; private set; }
        public int   Combo    { get; private set; }
        public int   Kicks    { get; private set; }
        public int   Perfects { get; private set; }
        public bool  Done     => Elapsed >= DurationSeconds;
        public float TimeLeft => Math.Max(0f, DurationSeconds - Elapsed);

        // Whether the current pass through a gap window has been kicked already (one kick per crossing).
        bool _passKicked;

        public CyclotronRun(float startAngle = 0f) => Angle = Wrap(startAngle);

        /// <summary>
        /// Advances the particle. Entering a gap window starts a new pass; leaving one un-kicked breaks the
        /// combo. Steps are kept small enough (by the caller's frame rate) that a window is never skipped.
        /// </summary>
        public void Tick(float dt)
        {
            if (Done || dt <= 0f) return;
            dt = Math.Min(dt, DurationSeconds - Elapsed);
            Elapsed += dt;
            bool wasIn = InWindow(Angle);
            Angle = Wrap(Angle + Speed * dt);
            bool nowIn = InWindow(Angle);

            if (!wasIn && nowIn) _passKicked = false;               // a new crossing begins
            if (wasIn && !nowIn && !_passKicked) Combo = 0;          // it went by without a kick
        }

        /// <summary>The player's tap. Inside the gap window it kicks the particle; outside, it costs energy.</summary>
        public Kick Tap()
        {
            if (Done) return Kick.Miss;
            float d = DistanceToGap(Angle, out _);
            if (d > WindowDegrees || _passKicked)
            {
                Energy = Math.Max(0f, Energy - MissPenalty);
                Combo = 0;
                return Kick.Miss;
            }

            _passKicked = true;
            bool perfect = d <= PerfectDegrees;
            float quality = 1f - d / WindowDegrees;
            float gain = perfect ? PerfectGain : GoodGainMax * quality;
            gain *= 1f + 0.1f * Math.Min(Combo, MaxCombo);
            Energy = Math.Min(1f, Energy + gain);
            Speed  = Math.Min(MaxSpeed, Speed * SpeedUpPerKick);
            Combo++;
            Kicks++;
            if (perfect) Perfects++;
            return perfect ? Kick.Perfect : Kick.Good;
        }

        /// <summary>True while the particle is inside a gap window (the gap glows then).</summary>
        public bool InGap => InWindow(Angle);

        /// <summary>Orbit radius as a fraction of the ring: grows with energy, as in a real cyclotron.</summary>
        public float OrbitFraction => 0.3f + 0.62f * Energy;

        internal static bool InWindow(float angle) => DistanceToGap(angle, out _) <= WindowDegrees;

        /// <summary>Angular distance (degrees) to the nearest gap crossing.</summary>
        internal static float DistanceToGap(float angle, out int gapIndex)
        {
            float best = float.MaxValue;
            gapIndex = -1;
            for (int i = 0; i < GapAngles.Length; i++)
            {
                float d = Math.Abs(Wrap(angle - GapAngles[i] + 180f) - 180f);
                if (d < best) { best = d; gapIndex = i; }
            }
            return best;
        }

        static float Wrap(float a)
        {
            a %= 360f;
            return a < 0f ? a + 360f : a;
        }
    }
}
