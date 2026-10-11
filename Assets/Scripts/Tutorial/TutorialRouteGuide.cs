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
            int bestScore = int.MaxValue, bestImbalance = int.MaxValue;

            for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
            {
                var anchor = new Vector2Int(x, y);
                if (!Fits(anchor, footprint, inputFronts, outputFronts, isBuildable, isBeltFree)) continue;
                if (SharesBeltCell(anchor, inputFronts, sources) || SharesBeltCell(anchor, outputFronts, sinks)) continue;

                int fromSources = AssignedDistance(sources, anchor, inputFronts);
                int toSink = 0;
                if (sinks.Count > 0 && outputFronts.Count > 0)
                {
                    toSink = int.MaxValue;
                    foreach (var o in outputFronts)
                    foreach (var sink in sinks)
                        toSink = Mathf.Min(toSink, Manhattan(anchor + o, sink));
                }

                // Least total belt first. Spots along a straight run cost the same, so the tie goes
                // to the one balanced between the sources and the sink (#236), then scan order (y, x).
                int score     = fromSources + toSink;
                int imbalance = Mathf.Abs(fromSources / Mathf.Max(1, sources.Count) - toSink);
                if (score < bestScore || (score == bestScore && imbalance < bestImbalance))
                {
                    bestScore     = score;
                    bestImbalance = imbalance;
                    best          = anchor;
                }
            }
            return best;
        }

        /// <summary>
        /// True when a port's belt cell would sit on one of <paramref name="ends"/> itself, i.e. the
        /// "belt" between them is a single shared tile. Rejected so every suggested route is a real
        /// belt with distinct glowing start and end tiles (#236).
        /// </summary>
        private static bool SharesBeltCell(Vector2Int anchor, IReadOnlyList<Vector2Int> fronts,
                                           IReadOnlyList<Vector2Int> ends)
        {
            foreach (var f in fronts)
            foreach (var e in ends)
                if (anchor + f == e) return true;
            return false;
        }

        /// <summary>
        /// Total belt distance with each source feeding its own input (each input takes one belt), the
        /// cheapest such assignment. With more sources than inputs, falls back to each source's nearest.
        /// </summary>
        private static int AssignedDistance(IReadOnlyList<Vector2Int> sources, Vector2Int anchor,
                                            IReadOnlyList<Vector2Int> fronts)
        {
            if (sources.Count == 0 || fronts.Count == 0) return 0;
            if (sources.Count > fronts.Count)
            {
                int sum = 0;
                foreach (var s in sources) sum += NearestDistance(s, anchor, fronts);
                return sum;
            }
            return BestAssignment(sources, 0, anchor, fronts, new bool[fronts.Count]);
        }

        private static int BestAssignment(IReadOnlyList<Vector2Int> sources, int i, Vector2Int anchor,
                                          IReadOnlyList<Vector2Int> fronts, bool[] used)
        {
            if (i == sources.Count) return 0;
            int best = int.MaxValue;
            for (int f = 0; f < fronts.Count; f++)
            {
                if (used[f]) continue;
                used[f] = true;
                int d = Manhattan(sources[i], anchor + fronts[f]) + BestAssignment(sources, i + 1, anchor, fronts, used);
                used[f] = false;
                best = Mathf.Min(best, d);
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

        // ── Built links (ConveyorLinked tutorial condition, #233) ────────────

        /// <summary>
        /// Counts the distinct sources with a belt that starts on one of their output fronts and whose
        /// chain ends (its tail) on one of <paramref name="inputFronts"/>, i.e. items really flow from
        /// that source into the target. Reads the belts as they are now, so a link built before the
        /// step began still counts.
        /// </summary>
        /// <param name="sourceFronts">(source id, output front cell) for every output port of every source.</param>
        /// <param name="belts">Every belt cell mapped to the cell it feeds, or null at a tail.</param>
        public static int CountLinkedSources(IReadOnlyList<(int source, Vector2Int front)> sourceFronts,
                                             ICollection<Vector2Int> inputFronts,
                                             IReadOnlyDictionary<Vector2Int, Vector2Int?> belts)
        {
            var linked = new HashSet<int>();
            foreach (var (source, front) in sourceFronts)
            {
                if (linked.Contains(source) || !belts.ContainsKey(front)) continue;

                var cur = front;
                for (int n = 0; n <= belts.Count; n++) // bounded: a looped belt never reaches a tail
                {
                    var next = belts[cur];
                    if (next == null || !belts.ContainsKey(next.Value))
                    {
                        if (inputFronts.Contains(cur)) linked.Add(source);
                        break;
                    }
                    cur = next.Value;
                }
            }
            return linked.Count;
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
