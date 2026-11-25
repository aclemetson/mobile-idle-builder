using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure rules for overpasses (<see cref="BeltBridge"/>, 2026-11-24): a raised straight span from a ramp-up tile to
    /// a ramp-down tile, crossing the tiles between. Unlocked and lengthened by research; they cross belts but not
    /// buildings, and never another overpass.
    /// </summary>
    public static class OverpassRules
    {
        public const string ResearchId   = "overpasses";
        public const string ResearchIdII = "overpasses_ii";
        public const int    SpanI  = 4;   // tiles crossed with Overpasses
        public const int    SpanII = 8;   // with Overpasses II

        /// <summary>How many tiles an overpass may cross: 0 (locked), 4 or 8.</summary>
        public static int MaxSpan(Func<string, bool> researched)
        {
            if (researched == null) return 0;
            if (researched(ResearchIdII)) return SpanII;
            return researched(ResearchId) ? SpanI : 0;
        }

        /// <summary>Tiles strictly between <paramref name="a"/> and <paramref name="b"/> on a straight line (0 = adjacent).</summary>
        public static int Crossed(Vector2Int a, Vector2Int b) => Mathf.Abs(b.x - a.x) + Mathf.Abs(b.y - a.y) - 1;

        public static bool IsStraight(Vector2Int a, Vector2Int b) => (a.x == b.x) != (a.y == b.y);

        /// <summary>
        /// True when consecutive belt cells <paramref name="a"/> → <paramref name="b"/> are an overpass span (not
        /// neighbours). A belt path stores a span as just its two ramps, so saves and placement need nothing else.
        /// </summary>
        public static bool IsSpan(Vector2Int a, Vector2Int b) => Mathf.Abs(b.x - a.x) + Mathf.Abs(b.y - a.y) > 1;

        /// <summary>Height of the overpass deck above the belts, in world units (clears the items underneath).</summary>
        public const float DeckHeight = 0.55f;

        public enum Check { Ok, Locked, NotStraight, TooShort, TooLong, OverBuilding, OverOverpass, RampUnderOverpass }

        /// <summary>
        /// Whether a span from <paramref name="from"/> to <paramref name="to"/> can be laid: straight, crossing 1 to
        /// <paramref name="maxSpan"/> tiles, none of them a building or under another overpass.
        /// </summary>
        public static Check CanSpan(Vector2Int from, Vector2Int to, int maxSpan,
                                    Func<Vector2Int, bool> isBuilding, Func<Vector2Int, bool> isUnderOverpass)
        {
            if (maxSpan <= 0) return Check.Locked;
            if (!IsStraight(from, to)) return Check.NotStraight;
            int crossed = Crossed(from, to);
            if (crossed < 1) return Check.TooShort;
            if (crossed > maxSpan) return Check.TooLong;
            if (isUnderOverpass != null && (isUnderOverpass(from) || isUnderOverpass(to))) return Check.RampUnderOverpass;
            foreach (var c in CrossedCells(from, to))
            {
                if (isBuilding != null && isBuilding(c)) return Check.OverBuilding;
                if (isUnderOverpass != null && isUnderOverpass(c)) return Check.OverOverpass;
            }
            return Check.Ok;
        }

        /// <summary>The tiles a span crosses, from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        public static System.Collections.Generic.IEnumerable<Vector2Int> CrossedCells(Vector2Int from, Vector2Int to)
        {
            var step = new Vector2Int(Math.Sign(to.x - from.x), Math.Sign(to.y - from.y));
            for (var c = from + step; c != to; c += step) yield return c;
        }

        /// <summary>Short banner text for a refused span (fits the one-line banner).</summary>
        public static string Message(Check c, int maxSpan) => c switch
        {
            Check.Locked       => "Research Overpasses to raise belts.",
            Check.NotStraight  => "Overpasses run in a straight line.",
            Check.TooShort     => "An overpass must cross a tile.",
            Check.TooLong      => $"Overpasses cross up to {maxSpan} tiles.",
            Check.OverBuilding => "Overpasses can't cross buildings.",
            Check.OverOverpass => "That tile already has an overpass.",
            Check.RampUnderOverpass => "Ramps can't sit under an overpass.",
            _                  => null,
        };

        /// <summary>
        /// The overpass height profile along its path (0 at the ramp-up tile's entry edge, <paramref name="length"/> at
        /// the ramp-down tile's entry edge): up over the first tile, level over the crossed tiles, down over the last.
        /// Returns 0..1 of the full height.
        /// </summary>
        public static float Height01(float s, float length)
        {
            if (s <= 0f || s >= length) return 0f;
            if (s < 1f) return Smooth(s);
            if (s > length - 1f) return Smooth(length - s);
            return 1f;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
