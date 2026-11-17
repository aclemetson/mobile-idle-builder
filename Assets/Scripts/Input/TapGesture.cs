using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Decides whether one press on the map was a deliberate tap, or part of a pan, pinch, rotate or
    /// glide-stop. Shared by every world-tap consumer (conveyor and building placement, deconstruct, the
    /// input router) so they all agree, and pure (fed time, positions and finger counts) so it is unit-tested.
    ///
    /// A press is NOT a tap when:
    ///  - it started over UI;
    ///  - the finger got farther than <see cref="SlopPixels"/> from where it pressed. The farthest distance
    ///    counts, not the summed path, so jitter in place doesn't add up into a pan;
    ///  - a second finger touched down at any point (pinch zoom, rotate or tilt). Only the first finger used
    ///    to be watched, so lifting after a pinch, with that finger barely moved, read as a tap;
    ///  - the map was still gliding from a pan when it pressed: that touch stops the map;
    ///  - it was held longer than <see cref="MaxTapSeconds"/>: a resting finger, not a tap.
    ///
    /// Usage per frame: <see cref="Begin"/> on press, <see cref="Track"/> while held, <see cref="End"/> on
    /// release (true = tap).
    /// </summary>
    public sealed class TapGesture
    {
        /// <summary>Smallest slop in pixels (the old fixed threshold).</summary>
        public const float MinSlopPixels = 20f;

        /// <summary>Slop in density-independent pixels (Android's touch slop is 8 dp).</summary>
        public const float SlopDp = 10f;

        /// <summary>Longest press that still counts as a tap.</summary>
        public const float MaxTapSeconds = 1f;

        /// <summary>
        /// How far a finger may move and still tap, in screen pixels: <see cref="SlopDp"/> scaled to the screen
        /// density, never below <see cref="MinSlopPixels"/>. CameraController starts a pan at the same distance,
        /// so one press never both pans the map and taps it.
        /// </summary>
        public static float SlopPixels => SlopFor(Screen.dpi);

        /// <summary><see cref="SlopPixels"/> for a given dpi (0 = unknown, as in the Editor).</summary>
        public static float SlopFor(float dpi) =>
            Mathf.Max(MinSlopPixels, dpi > 0f ? SlopDp * dpi / 160f : 0f);

        private readonly float? _slopOverride;

        private Vector2 _pressPos;
        private float   _pressTime;
        private float   _maxDistance;
        private bool    _overUI;
        private bool    _multiTouch;
        private bool    _glideStop;

        /// <param name="slopPixels">Fixed slop for tests; null uses <see cref="SlopPixels"/>.</param>
        public TapGesture(float? slopPixels = null) => _slopOverride = slopPixels;

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Where the press started.</summary>
        public Vector2 PressPosition => _pressPos;

        /// <summary>
        /// True once this press can no longer be a tap whatever happens next (it moved too far, a second
        /// finger joined, or it started over UI or on a gliding map). Long-press timers watch this too.
        /// </summary>
        public bool IsDisqualified =>
            _overUI || _multiTouch || _glideStop || _maxDistance > Slop;

        private float Slop => _slopOverride ?? SlopPixels;

        public void Begin(Vector2 position, float time, bool overUI, bool cameraGliding)
        {
            IsActive     = true;
            _pressPos    = position;
            _pressTime   = time;
            _maxDistance = 0f;
            _overUI      = overUI;
            _glideStop   = cameraGliding;
            _multiTouch  = false;
        }

        /// <summary>Call every frame the press is held.</summary>
        public void Track(Vector2 position, int touchCount)
        {
            if (!IsActive) return;
            _maxDistance = Mathf.Max(_maxDistance, Vector2.Distance(position, _pressPos));
            if (touchCount >= 2) _multiTouch = true;
        }

        /// <summary>Ends the press. True when it was a deliberate tap.</summary>
        public bool End(Vector2 position, float time)
        {
            if (!IsActive) return false;
            Track(position, 1);
            IsActive = false;
            return !IsDisqualified && time - _pressTime <= MaxTapSeconds;
        }

        /// <summary>Drops the press without a result (mode change, app backgrounded).</summary>
        public void Cancel() => IsActive = false;
    }
}
