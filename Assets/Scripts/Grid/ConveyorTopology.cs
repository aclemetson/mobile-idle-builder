using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure (scene-free, ECS-free) conveyor connectivity logic, so the merge graph can be unit
    /// tested directly. Connectivity is derived entirely from each cell's output direction
    /// (<c>ExitDir</c>): a cell outputs to the neighbour its ExitDir points at, and its inbounds are
    /// the neighbours whose ExitDir points back at it. <see cref="ConveyorPlacer"/> feeds the live
    /// ECS state through <see cref="Compute"/> and writes the result back onto the segment entities.
    /// </summary>
    public static class ConveyorTopology
    {
        /// <summary>Derived links for one cell (all caches of the ExitDir geometry).</summary>
        public struct CellLinks
        {
            public bool HasOutput;             // ExitDir neighbour is itself a conveyor
            public int2 OutputCell;            // that neighbour's cell (valid when HasOutput)
            public int  InboundCount;          // how many neighbours flow into this cell (0..4)
            public bool HasPrimaryInbound;
            public int2 PrimaryInboundCell;    // first inbound (N,E,S,W order)
            public int  PrimaryInboundExitDir; // its ExitDir == travel dir into this cell (this cell's EntryDir); -1 if head
            public bool IsHead;                // no inbound — a source / chain head
            public int  InboundMask;           // bit d set = flow enters from the neighbour in direction d
        }

        /// <summary>
        /// Computes the derived links for every cell in <paramref name="cellExit"/> (cell → ExitDir).
        /// Cells in <paramref name="outputBlocked"/> are treated as dead-ends: they have no output and
        /// are not counted as any neighbour's inbound, so a belt that merely runs up to another (without
        /// an endpoint coinciding) stays separate instead of auto-merging. Pass null for no blocks.
        /// </summary>
        /// <param name="splitMasks">Belt splits: cell → output mask (<see cref="BeltSplit.Mask"/>). A split feeds every
        /// neighbour on its mask, and its primary link (NextSegment) prefers its ExitDir, else its first output.</param>
        /// <param name="bridges">Overpasses: ramp-up cell → ramp-down cell (<see cref="BeltBridge"/>). A ramp-up outputs
        /// only to its ramp-down, which takes it in on the side facing the span. Neither ramp links to a belt on its
        /// span side: that belt runs underneath.</param>
        public static Dictionary<int2, CellLinks> Compute(IReadOnlyDictionary<int2, int> cellExit,
                                                          HashSet<int2> outputBlocked = null,
                                                          IReadOnlyDictionary<int2, int> splitMasks = null,
                                                          IReadOnlyDictionary<int2, int2> bridges = null)
        {
            var result = new Dictionary<int2, CellLinks>(cellExit.Count);

            bool IsBlocked(int2 c) => outputBlocked != null && outputBlocked.Contains(c);
            int SplitMask(int2 c) => splitMasks != null && splitMasks.TryGetValue(c, out int m) ? m : 0;
            var ramps = new Ramps(bridges);

            foreach (var kv in cellExit)
            {
                int2 cell = kv.Key;
                int  exit = kv.Value;
                var  link = new CellLinks();
                int  mask = SplitMask(cell);

                bool rampUp  = ramps.UpTo(cell, out int2 rampDown);
                int2 outCell = rampUp ? rampDown : Adjacent(cell, exit);
                if (!IsBlocked(cell) &&
                    !(outCell.x == cell.x && outCell.y == cell.y) && cellExit.ContainsKey(outCell)
                    && (rampUp || !ramps.UnderSpanSide(cell, outCell)))
                {
                    link.HasOutput  = true;
                    link.OutputCell = outCell;
                }
                else if (mask != 0 && !rampUp)
                {
                    // A split whose primary side has no belt yet still links to its first output that does.
                    for (int d = 0; d < 4 && !link.HasOutput; d++)
                    {
                        if (!BeltSplitRules.Has(mask, d)) continue;
                        int2 oc = Adjacent(cell, d);
                        if (!cellExit.ContainsKey(oc) || ramps.UnderSpanSide(cell, oc)) continue;
                        link.HasOutput  = true;
                        link.OutputCell = oc;
                    }
                }

                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    int2 nc   = Adjacent(cell, d);
                    int  toUs = Opposite(d);                                    // the direction from nc to this cell
                    bool feeds;
                    if (ramps.DownFrom(cell, out int2 up, out int spanSide) && d == spanSide)
                    {
                        // A ramp-down takes the overpass in on its span side (and nothing from under it).
                        nc    = up;
                        feeds = cellExit.ContainsKey(up) && !IsBlocked(up);
                    }
                    else
                    {
                        if (!cellExit.TryGetValue(nc, out int nExit)) continue;
                        if (ramps.UpTo(nc, out _) || ramps.UnderSpanSide(nc, cell)) continue; // a ramp-up feeds only its span
                        feeds = !IsBlocked(nc) && nExit == toUs;                 // its ExitDir points here
                        if (!feeds && BeltSplitRules.Has(SplitMask(nc), toUs)) feeds = true; // a split output points here
                    }
                    if (!feeds) continue;

                    count++;
                    link.InboundMask |= 1 << d;
                    if (!link.HasPrimaryInbound)
                    {
                        link.HasPrimaryInbound     = true;
                        link.PrimaryInboundCell    = nc;
                        link.PrimaryInboundExitDir = toUs;   // travel direction into this cell
                    }
                }

                link.InboundCount = count;
                link.IsHead       = count == 0;
                if (link.IsHead) link.PrimaryInboundExitDir = -1;

                result[cell] = link;
            }

            return result;
        }

        /// <summary>
        /// Returns whether a run along <paramref name="path"/> (upstream → downstream) may be placed
        /// given the existing <paramref name="cellExit"/> geometry: it must not fork an existing
        /// cell's live output toward a second successor, nor push the destination cell past
        /// <paramref name="maxInbounds"/> inbounds. <paramref name="reason"/> describes a rejection.
        ///
        /// The run's START cell (path[0]) is exempt from fork protection: when the player begins a new
        /// run ON an existing belt and draws off in a new direction, that start cell is intentionally
        /// OVERWRITTEN to bend into the new run (its old downstream is severed and becomes its own
        /// chain). Only cells the route merely PASSES THROUGH (i >= 1) are protected from hijacking.
        /// </summary>
        public static bool CanPlace(IReadOnlyDictionary<int2, int> cellExit, IReadOnlyList<int2> path,
                                    int maxInbounds, out string reason,
                                    IReadOnlyDictionary<int2, int> splitMasks = null,
                                    IReadOnlyDictionary<int2, int2> bridges = null)
        {
            reason = null;
            if (path == null || path.Count == 0) { reason = "empty path"; return false; }

            int count = path.Count;

            // Overpasses: a ramp-up keeps its span, and neither ramp joins a belt on its span side (that runs under).
            var ramps = new Ramps(bridges);
            for (int i = 0; i + 1 < count; i++)
            {
                int2 a = path[i], b = path[i + 1];
                if (ramps.UpTo(a, out int2 down) && !(down.x == b.x && down.y == b.y))
                {
                    reason = $"cell ({a.x},{a.y}) is an overpass ramp; its span can't be redrawn";
                    return false;
                }
                bool adjacent = math.abs(a.x - b.x) + math.abs(a.y - b.y) == 1;
                if (adjacent && ramps.UnderSpanSide(a, b))
                {
                    reason = $"cell ({b.x},{b.y}) is an overpass ramp; join it from the side";
                    return false;
                }
            }

            for (int i = 1; i < count - 1; i++)
            {
                int2 cell = path[i];
                if (!cellExit.TryGetValue(cell, out int exit)) continue;
                int2 outCell = Adjacent(cell, exit);
                if (!cellExit.ContainsKey(outCell)) continue; // no live successor — free to redirect

                int desired = DirBetween(path[i], path[i + 1]);
                // A split may be passed through along any of its outputs.
                if (splitMasks != null && splitMasks.TryGetValue(cell, out int sm) && BeltSplitRules.Has(sm, desired))
                    continue;
                if (exit != desired)
                {
                    reason = $"cell ({cell.x},{cell.y}) already outputs {(OutputDirection)exit}; cannot fork output";
                    return false;
                }
            }

            if (count >= 2)
            {
                int2 dest = path[count - 1];
                int2 prev = path[count - 2];
                int  inb  = 0;
                bool prevFeeds = false;

                for (int d = 0; d < 4; d++)
                {
                    int2 nc = Adjacent(dest, d);
                    if (!cellExit.TryGetValue(nc, out int nExit)) continue;
                    int2 nExitCell = Adjacent(nc, nExit);
                    if (nExitCell.x != dest.x || nExitCell.y != dest.y) continue;
                    inb++;
                    if (nc.x == prev.x && nc.y == prev.y) prevFeeds = true;
                }

                int resulting = inb + (prevFeeds ? 0 : 1);
                if (resulting > maxInbounds)
                {
                    reason = $"cell ({dest.x},{dest.y}) would have {resulting} inbounds (max {maxInbounds})";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Overpass lookups for <see cref="Compute"/> and <see cref="CanPlace"/>.</summary>
        private readonly struct Ramps
        {
            private readonly IReadOnlyDictionary<int2, int2> _up;     // ramp-up -> ramp-down
            private readonly Dictionary<int2, int2>         _down;   // ramp-down -> ramp-up

            public Ramps(IReadOnlyDictionary<int2, int2> bridges)
            {
                _up   = bridges;
                _down = null;
                if (bridges == null || bridges.Count == 0) return;
                _down = new Dictionary<int2, int2>(bridges.Count);
                foreach (var kv in bridges) _down[kv.Value] = kv.Key;
            }

            public bool UpTo(int2 cell, out int2 rampDown)
            {
                rampDown = default;
                return _up != null && _up.TryGetValue(cell, out rampDown);
            }

            /// <summary>A ramp-down: its ramp-up, and the side the span arrives on.</summary>
            public bool DownFrom(int2 cell, out int2 rampUp, out int spanSide)
            {
                spanSide = -1;
                rampUp   = default;
                if (_down == null || !_down.TryGetValue(cell, out rampUp)) return false;
                spanSide = DirBetween(cell, rampUp);
                return true;
            }

            /// <summary>
            /// True when adjacent <paramref name="from"/> and <paramref name="to"/> meet across a ramp's span side: one of
            /// them runs under the overpass, so they never link.
            /// </summary>
            public bool UnderSpanSide(int2 from, int2 to)
            {
                if (_down == null) return false;
                int dir = DirBetween(from, to);
                if (UpTo(to, out int2 d1) && DirBetween(to, d1) == Opposite(dir)) return true;
                if (DownFrom(to, out _, out int s1) && s1 == Opposite(dir)) return true;
                if (UpTo(from, out int2 d2) && DirBetween(from, d2) == dir) return true;
                if (DownFrom(from, out _, out int s2) && s2 == dir) return true;
                return false;
            }
        }

        /// <summary>OutputDirection of the cardinal step from 'from' to 'to'.</summary>
        public static int DirBetween(int2 from, int2 to)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            if (math.abs(dx) >= math.abs(dy))
                return dx >= 0 ? (int)OutputDirection.East : (int)OutputDirection.West;
            return dy >= 0 ? (int)OutputDirection.North : (int)OutputDirection.South;
        }

        public static int2 Adjacent(int2 cell, int dir)
        {
            return (OutputDirection)dir switch
            {
                OutputDirection.North => new int2(cell.x,     cell.y + 1),
                OutputDirection.East  => new int2(cell.x + 1, cell.y    ),
                OutputDirection.South => new int2(cell.x,     cell.y - 1),
                OutputDirection.West  => new int2(cell.x - 1, cell.y    ),
                _                    => cell
            };
        }

        public static int Opposite(int dir) => (dir + 2) % 4;

        /// <summary>
        /// The "not connected" seams a run along <paramref name="path"/> would leave, before it is placed (#208):
        /// a dry run of ConveyorPlacer's adjacency block. Every NEW cell of the run (not already a belt) that
        /// points at an existing belt beside it, and every existing belt that points into a new cell, stays a
        /// dead end; each is added to <paramref name="into"/> as (cell, exit direction).
        /// Belts in the run itself, a split feeding the new cell (a drawn branch) and the sides of an overpass
        /// span never count. <paramref name="singleDir"/> is a lone belt's facing.
        /// </summary>
        public static void PreviewSeams(IReadOnlyDictionary<int2, int> cellExit, IReadOnlyList<int2> path,
                                        IReadOnlyDictionary<int2, int> splitMasks,
                                        IReadOnlyDictionary<int2, int2> bridges,
                                        List<(int2 cell, int exit)> into, int singleDir = 0)
        {
            into.Clear();
            if (path == null || path.Count == 0) return;
            int count = path.Count;

            var pathSet = new HashSet<int2>(path);
            var spanSides = new HashSet<(int2, int)>();
            void AddSpan(int2 a, int2 b)
            {
                int d = DirBetween(a, b);
                spanSides.Add((a, d));
                spanSides.Add((b, Opposite(d)));
            }
            if (bridges != null) foreach (var kv in bridges) AddSpan(kv.Key, kv.Value);
            for (int i = 0; i + 1 < count; i++)
                if (math.abs(path[i].x - path[i + 1].x) + math.abs(path[i].y - path[i + 1].y) > 1)
                    AddSpan(path[i], path[i + 1]);

            for (int i = 0; i < count; i++)
            {
                var p = path[i];
                if (cellExit.ContainsKey(p)) continue;           // an existing belt the run reuses
                int pExit = count == 1 ? singleDir
                          : i < count - 1 ? DirBetween(p, path[i + 1])
                          : DirBetween(path[i - 1], p);

                for (int d = 0; d < 4; d++)
                {
                    int2 q = Adjacent(p, d);
                    if (pathSet.Contains(q)) continue;
                    if (spanSides.Contains((p, d)) || spanSides.Contains((q, Opposite(d)))) continue;
                    if (!cellExit.TryGetValue(q, out int qExit)) continue;

                    if (pExit == d) into.Add((p, pExit));
                    if (splitMasks != null && splitMasks.TryGetValue(q, out int sm) && (sm & (1 << Opposite(d))) != 0)
                        continue;
                    if (qExit == Opposite(d)) into.Add((q, qExit));
                }
            }
        }
    }

    /// <summary>
    /// Pure auto-turn decisions, so the "flow always enters/leaves a building naturally" rule can be
    /// unit tested. The predicates abstract the building-port scan that <see cref="ConveyorPlacer"/>
    /// performs against ECS; directions are 0..3 (N,E,S,W).
    /// </summary>
    public static class ConveyorAutoTurn
    {
        /// <summary>
        /// New ExitDir for a tail: turn toward the first direction (N,E,S,W) that has a building input
        /// facing this cell, so the current flows in; otherwise keep <paramref name="currentExit"/>.
        /// </summary>
        public static int TailExit(int currentExit, Func<int, bool> inputFacingAtDir)
        {
            for (int d = 0; d < 4; d++)
                if (inputFacingAtDir(d)) return d;
            return currentExit;
        }

        /// <summary>
        /// New EntryDir for a head: if a building output in direction <c>d</c> faces this cell, the
        /// current emerges travelling Opposite(d); otherwise keep <paramref name="currentEntry"/>.
        /// </summary>
        public static int HeadEntry(int currentEntry, Func<int, bool> outputFacingAtDir)
        {
            for (int d = 0; d < 4; d++)
                if (outputFacingAtDir(d)) return ConveyorTopology.Opposite(d);
            return currentEntry;
        }
    }
}
