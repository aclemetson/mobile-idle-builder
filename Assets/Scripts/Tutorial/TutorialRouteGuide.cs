using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure grid math behind the tutorial's world guidance (#202, #203). No scene or ECS access:
    /// <see cref="TutorialHighlighter"/> gathers port cells and occupancy, these functions choose.
    ///
    /// Port convention (matches ConveyorSystem):
    ///   an Output port's belt cell is the cell its Facing points at;
    ///   an Input port's belt cell is the cell behind its Facing (items travel along Facing into it).
    /// </summary>
    public static class TutorialRouteGuide
    {
        /// <summary>Cell a belt must start on to pull from an output port.</summary>
        public static Vector2Int OutputFront(Vector2Int portCell, OutputDirection facing)
            => PortUtils.AdjacentCell(portCell, facing);

        /// <summary>Cell a belt must end on to feed an input port.</summary>
        public static Vector2Int InputFront(Vector2Int portCell, OutputDirection facing)
            => PortUtils.AdjacentCell(portCell, Opposite(facing));

        public static OutputDirection Opposite(OutputDirection d) => (OutputDirection)(((int)d + 2) % 4);

        public static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        /// <summary>
        /// Picks the closest (start, end) pair for the next belt to draw, using only cells that are
        /// still free (a start or end that already has a belt is a route the player has built).
        /// Ties break on the lower start, then end, (y, x) so the choice is stable frame to frame.
        /// </summary>
        public static bool TryPickRoute(IReadOnlyList<Vector2Int> starts, IReadOnlyList<Vector2Int> ends,
                                        Func<Vector2Int, bool> isFree,
                                        out Vector2Int start, out Vector2Int end)
        {
            start = end = default;
            int best = int.MaxValue;
            bool found = false;

            foreach (var s in starts)
            {
                if (!isFree(s)) continue;
                foreach (var e in ends)
                {
                    if (e == s || !isFree(e)) continue;
                    int d = Manhattan(s, e);
                    if (d < best || (d == best && found && IsEarlier(s, e, start, end)))
                    {
                        best  = d;
                        start = s;
                        end   = e;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Suggests where to place a building (unrotated) so belts from <paramref name="sources"/>
        /// reach its inputs and a belt from its output reaches one of <paramref name="sinks"/>, with the
        /// least total belt. Every footprint cell must be buildable and every port's belt cell free.
        /// Returns null when nothing fits.
        /// </summary>
        /// <param name="inputFronts">Belt cells for each input port, relative to the anchor.</param>
        /// <param name="outputFronts">Belt cells for each output port, relative to the anchor.</param>
        /// <param name="isBuildable">In bounds, unoccupied, not a field tile.</param>
        /// <param name="isBeltFree">In bounds and unoccupied (belts may run over field tiles).</param>
        public static Vector2Int? SuggestAnchor(int gridWidth, int gridHeight, Vector2Int footprint,
                                                IReadOnlyList<Vector2Int> inputFronts,
                                                IReadOnlyList<Vector2Int> outputFronts,
                                                IReadOnlyList<Vector2Int> sources,
                                                IReadOnlyList<Vector2Int> sinks,
                                                Func<Vector2Int, bool> isBuildable,
                                                Func<Vector2Int, bool> isBeltFree)
        {
            Vector2Int? best = null;
            int bestScore = int.MaxValue;

            for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
            {
                var anchor = new Vector2Int(x, y);
                if (!Fits(anchor, footprint, inputFronts, outputFronts, isBuildable, isBeltFree)) continue;

                int score = 0;
                foreach (var src in sources)
                    score += NearestDistance(src, anchor, inputFronts);
                if (sinks.Count > 0 && outputFronts.Count > 0)
                {
                    int toSink = int.MaxValue;
                    foreach (var o in outputFronts)
                    foreach (var sink in sinks)
                        toSink = Mathf.Min(toSink, Manhattan(anchor + o, sink));
                    score += toSink;
                }

                if (score < bestScore) // scan order (y, then x) is the tie-break
                {
                    bestScore = score;
                    best      = anchor;
                }
            }
            return best;
        }

        private static bool Fits(Vector2Int anchor, Vector2Int footprint,
                                 IReadOnlyList<Vector2Int> inputFronts, IReadOnlyList<Vector2Int> outputFronts,
                                 Func<Vector2Int, bool> isBuildable, Func<Vector2Int, bool> isBeltFree)
        {
            for (int dx = 0; dx < footprint.x; dx++)
            for (int dy = 0; dy < footprint.y; dy++)
                if (!isBuildable(anchor + new Vector2Int(dx, dy))) return false;

            foreach (var f in inputFronts)  if (!isBeltFree(anchor + f)) return false;
            foreach (var f in outputFronts) if (!isBeltFree(anchor + f)) return false;
            return true;
        }

        private static int NearestDistance(Vector2Int from, Vector2Int anchor, IReadOnlyList<Vector2Int> fronts)
        {
            int best = int.MaxValue;
            foreach (var f in fronts) best = Mathf.Min(best, Manhattan(from, anchor + f));
            return best == int.MaxValue ? 0 : best;
        }

        private static bool IsEarlier(Vector2Int s, Vector2Int e, Vector2Int curS, Vector2Int curE)
        {
            if (s != curS) return s.y < curS.y || (s.y == curS.y && s.x < curS.x);
            return e.y < curE.y || (e.y == curE.y && e.x < curE.x);
        }

        // ── Target id parsing ───────────────────────────────────────────────

        /// <summary>
        /// Parses "route:&lt;from&gt;&gt;&lt;to&gt;" (e.g. "route:harvester&gt;maxwells_demon").
        /// </summary>
        public static bool TryParseRoute(string targetId, out string from, out string to)
        {
            from = to = null;
            const string prefix = "route:";
            if (targetId == null || !targetId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var parts = targetId.Substring(prefix.Length).Split('>');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            from = parts[0];
            to   = parts[1];
            return true;
        }

        /// <summary>
        /// Parses "suggest:&lt;building&gt;:&lt;from&gt;&gt;&lt;to&gt;"
        /// (e.g. "suggest:strong_force_combiner:harvester&gt;maxwells_demon"): where to place
        /// &lt;building&gt; so belts from &lt;from&gt; outputs feed it and its output reaches &lt;to&gt;.
        /// </summary>
        public static bool TryParseSuggest(string targetId, out string building, out string from, out string to)
        {
            building = from = to = null;
            const string prefix = "suggest:";
            if (targetId == null || !targetId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var rest  = targetId.Substring(prefix.Length);
            int colon = rest.IndexOf(':');
            if (colon <= 0) return false;
            building = rest.Substring(0, colon);
            return TryParseRoute("route:" + rest.Substring(colon + 1), out from, out to);
        }
    }
}
