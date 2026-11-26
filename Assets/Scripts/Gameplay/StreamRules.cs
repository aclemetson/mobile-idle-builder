using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure rules for Chemistry's streams (unit-tested). See <see cref="StreamSegment"/>.
    /// </summary>
    public static class StreamRules
    {
        /// <summary>
        /// Shares <paramref name="supply"/> L/s among buildings in stream order, upstream first: each takes
        /// its full draw while the water lasts; the one where it runs out gets the part left; the rest get
        /// nothing. Writes each building's share (0..1) to <paramref name="ratios"/> and returns the flow left.
        /// A building with no draw always runs (ratio 1) as long as the stream has a source.
        /// </summary>
        public static float Allocate(float supply, IList<float> draws, IList<float> ratios, bool hasSource = true)
        {
            float left = supply > 0f ? supply : 0f;
            for (int i = 0; i < draws.Count; i++)
            {
                float d = draws[i];
                if (d <= 0f) { ratios[i] = hasSource ? 1f : 0f; continue; }
                float got = left >= d ? d : left;
                ratios[i] = got / d;
                left -= got;
            }
            return left;
        }

        /// <summary>Single-building form of <see cref="Allocate"/>: the share it gets and the flow left after it.</summary>
        public static float Take(ref float left, float draw, bool hasSource = true)
        {
            if (draw <= 0f) return hasSource ? 1f : 0f;
            if (left <= 0f) { left = 0f; return 0f; }
            float got = left >= draw ? draw : left;
            left -= got;
            return got / draw;
        }

        /// <summary>
        /// Where a building beside a stream drops a product onto a segment: the middle of the tile, if no
        /// item there is closer than <paramref name="spacing"/>. <paramref name="progresses"/> is the
        /// segment's items front first (highest Progress first). Returns the index to insert at, or -1 when
        /// there is no gap (the product waits in the building).
        /// </summary>
        public static int DropIndex(IList<float> progresses, float at, float spacing)
        {
            int idx = progresses.Count;
            for (int i = 0; i < progresses.Count; i++)
            {
                float p = progresses[i];
                float gap = p > at ? p - at : at - p;
                if (gap < spacing - 1e-4f) return -1;
                if (p < at && idx == progresses.Count) idx = i;
            }
            return idx;
        }

        /// <summary>Where a dropped product enters the tile.</summary>
        public const float DropAt = 0.5f;

        // ── Drawing a stream (#543): water only flows away from its source ──────────

        public enum Orientation { Keep, Reverse, Refuse }

        /// <summary>The banner line when a drawn stream has no source at either end.</summary>
        public const string NoSourceMessage = "Start a stream at a Spring";

        /// <summary>
        /// Which way a drawn stream run must flow. <paramref name="isFedEnd"/> says whether a cell can start a stream:
        /// a Spring's output faces it, or it is an existing stream's mouth (extending that stream). The run as drawn
        /// (first cell upstream) is kept when its first cell is fed, reversed when only its last cell is, and refused
        /// when neither is: a stream with no source is dry and would only look like a belt.
        /// </summary>
        public static Orientation Orient<T>(IReadOnlyList<T> path, Func<T, bool> isFedEnd)
        {
            if (path == null || path.Count == 0 || isFedEnd == null) return Orientation.Refuse;
            if (isFedEnd(path[0])) return Orientation.Keep;
            if (path.Count > 1 && isFedEnd(path[path.Count - 1])) return Orientation.Reverse;
            return Orientation.Refuse;
        }

        /// <summary>A lone stream tile's facing: never back into the Spring that feeds it.</summary>
        public static int SingleTileFacing(int facing, Func<int, bool> springOnSide) =>
            springOnSide != null && springOnSide(facing) ? (facing + 2) % 4 : facing;
    }
}
