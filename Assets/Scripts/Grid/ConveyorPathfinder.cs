using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure, dependency-free path generation for conveyor placement.
    ///
    /// All methods are static and side-effect free so they can be unit tested without a
    /// scene, ECS world, or Unity input. Callers supply an <c>isCellFree</c> predicate that
    /// answers "may a NEW conveyor segment occupy this cell?" — it must return false for
    /// out-of-bounds cells and for cells occupied by buildings, fields, or existing conveyors.
    ///
    /// The START and END cells are treated as endpoints: they are always permitted (the caller
    /// is responsible for confirming they are a free cell or an existing conveyor cell), and the
    /// pathfinder never requires them to satisfy <c>isCellFree</c>. Every INTERMEDIATE cell must.
    ///
    /// Returned paths are ordered head -> tail (start first, end last) — the order
    /// <see cref="ConveyorPlacer.PlaceConveyorChain"/> expects.
    /// </summary>
    public static class ConveyorPathfinder
    {
        // 4-connected step directions: E, W, N, S.
        private static readonly int[] DX = { 1, -1, 0, 0 };
        private static readonly int[] DY = { 0, 0, 1, -1 };

        // Dijkstra cost weighting: a turn costs far more than a step, so the route minimizes
        // turns first and only then total length. Larger than any realistic path length.
        private const long TurnCost = 1_000_000L;
        private const long StepCost = 1L;

        /// <summary>
        /// Builds a single-turn L-path between two cells (or a straight line when they share a
        /// row/column). When <paramref name="horizontalFirst"/> is true the path travels along X
        /// before turning along Y; otherwise it travels along Y first.
        ///
        /// Returns the ordered cell list (start..end inclusive), or <c>null</c> if any
        /// intermediate cell fails <paramref name="isCellFree"/>.
        /// </summary>
        public static List<Vector2Int> BuildOneTurnPath(
            Vector2Int start, Vector2Int end, bool horizontalFirst, Func<int, int, bool> isCellFree)
        {
            if (isCellFree == null) throw new ArgumentNullException(nameof(isCellFree));

            int dx = end.x - start.x;
            int dy = end.y - start.y;

            List<Vector2Int> path;
            if (dx == 0 || dy == 0)
            {
                path = new List<Vector2Int>();
                AddLineCells(path, start, end, skipFirst: false);
            }
            else
            {
                Vector2Int corner = horizontalFirst
                    ? new Vector2Int(end.x, start.y)
                    : new Vector2Int(start.x, end.y);

                path = new List<Vector2Int>();
                AddLineCells(path, start, corner, skipFirst: false);
                AddLineCells(path, corner, end, skipFirst: true);
            }

            return AllIntermediatesFree(path, isCellFree) ? path : null;
        }

        /// <summary>
        /// Finds the best route between two cells when a simple one-turn L is blocked. Uses
        /// Dijkstra over (cell, incoming-direction) states so the result minimizes the number of
        /// turns first and total length second — a route that needs multiple turns to go around
        /// obstacles, but as few as possible.
        ///
        /// Returns the ordered cell list (start..end inclusive), or <c>null</c> if the end is
        /// unreachable.
        /// </summary>
        public static List<Vector2Int> FindRoute(
            Vector2Int start, Vector2Int end, Func<int, int, bool> isCellFree)
        {
            if (isCellFree == null) throw new ArgumentNullException(nameof(isCellFree));
            if (start == end) return new List<Vector2Int> { start };

            // State = (x, y, dir) where dir is the direction taken to ENTER the cell (0-3),
            // or 4 for the start cell which has no incoming direction.
            var dist = new Dictionary<(int x, int y, int dir), long>();
            var prev = new Dictionary<(int x, int y, int dir), (int x, int y, int dir)>();
            var heap = new MinHeap();

            var startState = (start.x, start.y, 4);
            dist[startState] = 0;
            heap.Push(0, start.x, start.y, 4);

            (int x, int y, int dir) endState = default;
            bool found = false;

            while (heap.TryPop(out long cost, out int cx, out int cy, out int cdir))
            {
                var state = (cx, cy, cdir);
                if (dist.TryGetValue(state, out long best) && cost > best) continue; // stale

                if (cx == end.x && cy == end.y)
                {
                    endState = state;
                    found = true;
                    break;
                }

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d];
                    int ny = cy + DY[d];

                    bool isGoal = nx == end.x && ny == end.y;
                    if (!isGoal && !isCellFree(nx, ny)) continue;

                    long turn = (cdir != 4 && cdir != d) ? TurnCost : 0L;
                    long ncost = cost + StepCost + turn;

                    var nstate = (nx, ny, d);
                    if (!dist.TryGetValue(nstate, out long existing) || ncost < existing)
                    {
                        dist[nstate] = ncost;
                        prev[nstate] = state;
                        heap.Push(ncost, nx, ny, d);
                    }
                }
            }

            if (!found) return null;

            // Reconstruct head -> tail.
            var reversed = new List<Vector2Int>();
            var cur = endState;
            while (true)
            {
                reversed.Add(new Vector2Int(cur.x, cur.y));
                if (cur.dir == 4) break; // reached start
                cur = prev[cur];
            }
            reversed.Reverse();
            return reversed;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static bool AllIntermediatesFree(List<Vector2Int> path, Func<int, int, bool> isCellFree)
        {
            for (int i = 1; i < path.Count - 1; i++)
                if (!isCellFree(path[i].x, path[i].y)) return false;
            return true;
        }

        private static void AddLineCells(List<Vector2Int> path, Vector2Int a, Vector2Int b, bool skipFirst)
        {
            int dx    = b.x - a.x;
            int dy    = b.y - a.y;
            int steps = Mathf.Abs(dx) + Mathf.Abs(dy);
            int sx    = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            int sy    = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

            int iStart = skipFirst ? 1 : 0;
            for (int i = iStart; i <= steps; i++)
                path.Add(new Vector2Int(a.x + sx * i, a.y + sy * i));
        }

        /// <summary>
        /// Minimal binary min-heap keyed on cost. Unity's runtime predates
        /// System.Collections.Generic.PriorityQueue, so we roll a small one. Supports lazy
        /// deletion: stale entries are filtered by the Dijkstra loop via the dist map.
        /// </summary>
        private sealed class MinHeap
        {
            private struct Node { public long Cost; public int X, Y, Dir; }

            private Node[] _items = new Node[64];
            private int    _count;

            public void Push(long cost, int x, int y, int dir)
            {
                if (_count == _items.Length)
                    Array.Resize(ref _items, _items.Length * 2);

                int i = _count++;
                _items[i] = new Node { Cost = cost, X = x, Y = y, Dir = dir };
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].Cost <= _items[i].Cost) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public bool TryPop(out long cost, out int x, out int y, out int dir)
            {
                if (_count == 0)
                {
                    cost = 0; x = 0; y = 0; dir = 0;
                    return false;
                }

                Node root = _items[0];
                cost = root.Cost; x = root.X; y = root.Y; dir = root.Dir;

                _items[0] = _items[--_count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                    if (l < _count && _items[l].Cost < _items[smallest].Cost) smallest = l;
                    if (r < _count && _items[r].Cost < _items[smallest].Cost) smallest = r;
                    if (smallest == i) break;
                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }
                return true;
            }
        }
    }
}
