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
        }

        /// <summary>
        /// Computes the derived links for every cell in <paramref name="cellExit"/> (cell → ExitDir).
        /// Cells in <paramref name="outputBlocked"/> are treated as dead-ends: they have no output and
        /// are not counted as any neighbour's inbound, so a belt that merely runs up to another (without
        /// an endpoint coinciding) stays separate instead of auto-merging. Pass null for no blocks.
        /// </summary>
        public static Dictionary<int2, CellLinks> Compute(IReadOnlyDictionary<int2, int> cellExit,
                                                          HashSet<int2> outputBlocked = null)
        {
            var result = new Dictionary<int2, CellLinks>(cellExit.Count);

            bool IsBlocked(int2 c) => outputBlocked != null && outputBlocked.Contains(c);

            foreach (var kv in cellExit)
            {
                int2 cell = kv.Key;
                int  exit = kv.Value;
                var  link = new CellLinks();

                int2 outCell = Adjacent(cell, exit);
                if (!IsBlocked(cell) &&
                    !(outCell.x == cell.x && outCell.y == cell.y) && cellExit.ContainsKey(outCell))
                {
                    link.HasOutput  = true;
                    link.OutputCell = outCell;
                }

                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    int2 nc = Adjacent(cell, d);
                    if (!cellExit.TryGetValue(nc, out int nExit)) continue;
                    if (IsBlocked(nc)) continue;                                // its output is disconnected
                    int2 nExitCell = Adjacent(nc, nExit);
                    if (nExitCell.x != cell.x || nExitCell.y != cell.y) continue; // not an inbound

                    count++;
                    if (!link.HasPrimaryInbound)
                    {
                        link.HasPrimaryInbound     = true;
                        link.PrimaryInboundCell    = nc;
                        link.PrimaryInboundExitDir = nExit;
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
                                    int maxInbounds, out string reason)
        {
            reason = null;
            if (path == null || path.Count == 0) { reason = "empty path"; return false; }

            int count = path.Count;

            for (int i = 1; i < count - 1; i++)
            {
                int2 cell = path[i];
                if (!cellExit.TryGetValue(cell, out int exit)) continue;
                int2 outCell = Adjacent(cell, exit);
                if (!cellExit.ContainsKey(outCell)) continue; // no live successor — free to redirect

                int desired = DirBetween(path[i], path[i + 1]);
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
