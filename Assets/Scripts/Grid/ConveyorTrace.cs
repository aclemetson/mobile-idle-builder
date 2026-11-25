using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The belt being traced in conveyor mode: the nodes the player tapped, in order, and the route between
    /// each consecutive pair. Pure (no scene, no ECS), so the whole tap flow is unit-tested.
    ///
    /// Between two nodes the belt is auto-routed: straight when they share a row or column, else an L in the
    /// segment's preferred bend, then the other bend, else a detour (<see cref="ConveyorPathfinder"/>; the
    /// cheaper of the L and the detour by router cost wins, the same rule as the old start/end placement).
    /// Cells already on the trace count as blocked, so a belt never crosses itself. A node on an existing
    /// belt (other than the start) merges into it and ends the trace.
    ///
    /// A raised tap (<see cref="TryAdd(Vector2Int, bool)"/>) lays an overpass instead: a straight span from the last
    /// node to the tapped tile, crossing the tiles between. The path then holds just its two ramps, side by side in
    /// the list though not neighbours on the grid (<see cref="OverpassRules.IsSpan"/>).
    /// </summary>
    public sealed class ConveyorTrace
    {
        public enum AddResult
        {
            /// <summary>The first node: the start.</summary>
            Started,
            /// <summary>A node joined by a new segment.</summary>
            Added,
            /// <summary>A node on an existing belt: joined, and the trace ends there (it merges).</summary>
            Merged,
            /// <summary>Not a free cell or an existing belt.</summary>
            NotEndpoint,
            /// <summary>The cell is already on the trace.</summary>
            CrossesItself,
            /// <summary>No route from the last node that avoids buildings, belts and the trace.</summary>
            Unreachable,
            /// <summary>The joined belt would fork an existing belt or overfill a merge.</summary>
            Rejected,
            /// <summary>The trace already ended on an existing belt.</summary>
            AlreadyEnded,
            /// <summary>The last node tapped again; ignored so a double tap does nothing.</summary>
            SameAsLast,
        }

        private readonly Func<Vector2Int, bool> _isFree;
        private readonly Func<Vector2Int, bool> _isBelt;
        private readonly Func<Vector2Int, Vector2Int, Func<int, int, int>> _extraCostFor;
        private readonly Func<List<Vector2Int>, string> _rejectReason;
        private readonly Func<Vector2Int, Vector2Int, string> _spanReject;

        private readonly List<Vector2Int>       _nodes    = new();
        private readonly List<List<Vector2Int>> _segments = new();
        private readonly List<bool>             _bends    = new(); // per segment: horizontal-first L
        private readonly List<bool>             _spans    = new(); // per segment: an overpass

        /// <param name="isFree">A cell a new belt may be laid on (in bounds, unoccupied).</param>
        /// <param name="isBelt">A cell holding an existing belt.</param>
        /// <param name="extraCostFor">Router extra cost for a segment from a to b (may return null).</param>
        /// <param name="rejectReason">Why a joined path can't be placed, or null when it can.</param>
        /// <param name="spanReject">Why an overpass from a to b can't be laid (locked, too long, over a building), or null.</param>
        public ConveyorTrace(Func<Vector2Int, bool> isFree, Func<Vector2Int, bool> isBelt,
                             Func<Vector2Int, Vector2Int, Func<int, int, int>> extraCostFor = null,
                             Func<List<Vector2Int>, string> rejectReason = null,
                             Func<Vector2Int, Vector2Int, string> spanReject = null)
        {
            _isFree       = isFree ?? throw new ArgumentNullException(nameof(isFree));
            _isBelt       = isBelt ?? throw new ArgumentNullException(nameof(isBelt));
            _extraCostFor = extraCostFor;
            _rejectReason = rejectReason;
            _spanReject   = spanReject;
        }

        /// <summary>Player-facing reasons a raised tap is refused (short: the banner doesn't wrap).</summary>
        public const string SpanStartsOnBeltMessage = "Start an overpass on a new tile.";
        public const string SpanAfterSpanMessage    = "Lay a belt tile between overpasses.";
        public const string SpanLandsOnBeltMessage  = "An overpass lands on a free tile.";

        /// <summary>True when the last segment is an overpass.</summary>
        public bool LastIsSpan => _spans.Count > 0 && _spans[_spans.Count - 1];

        /// <summary>Every tile the trace's overpasses cross (none of them on the path itself).</summary>
        public List<Vector2Int> CrossedCells()
        {
            var cells = new List<Vector2Int>();
            for (int s = 0; s < _segments.Count; s++)
                if (_spans[s]) cells.AddRange(OverpassRules.CrossedCells(_segments[s][0], _segments[s][1]));
            return cells;
        }

        public IReadOnlyList<Vector2Int> Nodes => _nodes;
        public bool HasStart     => _nodes.Count > 0;
        public int  SegmentCount => _segments.Count;
        public Vector2Int Start  => _nodes[0];
        public Vector2Int Last   => _nodes[_nodes.Count - 1];

        /// <summary>True once the trace ends on an existing belt: no more nodes, only ✓.</summary>
        public bool IsEnded => _nodes.Count >= 2 && _isBelt(Last);

        /// <summary>Why the last refused tap was refused (a <see cref="AddResult.Rejected"/> reason), or null.</summary>
        public string LastRejectReason { get; private set; }

        /// <summary>The whole belt, upstream to downstream, each cell once.</summary>
        public List<Vector2Int> Path() => Join(_segments, _nodes);

        /// <summary>Every cell on the trace (nodes and route cells).</summary>
        public HashSet<Vector2Int> Cells() => new(Path());

        public AddResult TryAdd(Vector2Int cell) => TryAdd(cell, false);

        /// <summary>Adds a node; <paramref name="raised"/> lays the segment to it as an overpass.</summary>
        public AddResult TryAdd(Vector2Int cell, bool raised)
        {
            LastRejectReason = null;
            if (raised && _nodes.Count > 0) return TryAddSpan(cell);

            if (_nodes.Count == 0)
            {
                if (!_isFree(cell) && !_isBelt(cell)) return AddResult.NotEndpoint;
                _nodes.Add(cell);
                return AddResult.Started;
            }

            if (IsEnded)        return AddResult.AlreadyEnded;
            if (cell == Last)   return AddResult.SameAsLast;

            var onTrace = Cells();
            if (onTrace.Contains(cell)) return AddResult.CrossesItself;
            if (!_isFree(cell) && !_isBelt(cell)) return AddResult.NotEndpoint;

            const bool defaultBend = true;
            var segment = Route(Last, cell, defaultBend, onTrace);
            if (segment == null) return AddResult.Unreachable;

            var segments = new List<List<Vector2Int>>(_segments) { segment };
            var nodes    = new List<Vector2Int>(_nodes) { cell };
            string reason = _rejectReason?.Invoke(Join(segments, nodes));
            if (reason != null) { LastRejectReason = reason; return AddResult.Rejected; }

            _segments.Add(segment);
            _bends.Add(defaultBend);
            _spans.Add(false);
            _nodes.Add(cell);
            return _isBelt(cell) ? AddResult.Merged : AddResult.Added;
        }

        private AddResult TryAddSpan(Vector2Int cell)
        {
            if (IsEnded)      return AddResult.AlreadyEnded;
            if (cell == Last) return AddResult.SameAsLast;
            if (Cells().Contains(cell)) return AddResult.CrossesItself;

            string reason =
                _isBelt(Last)  ? SpanStartsOnBeltMessage :
                LastIsSpan     ? SpanAfterSpanMessage :
                !_isFree(cell) ? SpanLandsOnBeltMessage :
                _spanReject?.Invoke(Last, cell);
            if (reason == null)
            {
                var segment  = new List<Vector2Int> { Last, cell };
                var segments = new List<List<Vector2Int>>(_segments) { segment };
                var nodes    = new List<Vector2Int>(_nodes) { cell };
                reason = _rejectReason?.Invoke(Join(segments, nodes));
                if (reason == null)
                {
                    _segments.Add(segment);
                    _bends.Add(true);
                    _spans.Add(true);
                    _nodes.Add(cell);
                    return AddResult.Added;
                }
            }
            LastRejectReason = reason;
            return AddResult.Rejected;
        }

        /// <summary>Removes the last node and its segment (or the start, when it is the only node).</summary>
        public bool Undo()
        {
            if (_nodes.Count == 0) return false;
            _nodes.RemoveAt(_nodes.Count - 1);
            if (_segments.Count > 0)
            {
                _segments.RemoveAt(_segments.Count - 1);
                _bends.RemoveAt(_bends.Count - 1);
                _spans.RemoveAt(_spans.Count - 1);
            }
            return true;
        }

        /// <summary>
        /// Rotate: re-routes the last segment with its L bent the other way. Earlier segments stay as drawn.
        /// Returns false (and keeps the segment) when the other bend gives no placeable route.
        /// </summary>
        public bool FlipLastBend()
        {
            if (_segments.Count == 0 || LastIsSpan) return false;   // an overpass is straight
            int i = _segments.Count - 1;
            bool bend = !_bends[i];

            // Blocked: every trace cell except the ones on the segment being redrawn (its start node stays).
            var blocked = new HashSet<Vector2Int>(Join(_segments.GetRange(0, i), _nodes.GetRange(0, i + 1)));
            var segment = Route(_nodes[i], _nodes[i + 1], bend, blocked);
            if (segment == null) return false;

            var segments = new List<List<Vector2Int>>(_segments) { [i] = segment };
            string reason = _rejectReason?.Invoke(Join(segments, _nodes));
            if (reason != null) { LastRejectReason = reason; return false; }

            _segments[i] = segment;
            _bends[i]    = bend;
            return true;
        }

        public void Clear()
        {
            _nodes.Clear();
            _segments.Clear();
            _bends.Clear();
            _spans.Clear();
            LastRejectReason = null;
        }

        // ----------------------------------------------------------------

        private List<Vector2Int> Route(Vector2Int from, Vector2Int to, bool horizontalFirst, HashSet<Vector2Int> blocked)
        {
            bool Free(int x, int y)
            {
                var c = new Vector2Int(x, y);
                return _isFree(c) && !blocked.Contains(c);
            }

            var extra = _extraCostFor?.Invoke(from, to);
            var path  = ConveyorPathfinder.BuildOneTurnPath(from, to, horizontalFirst, Free)
                     ?? ConveyorPathfinder.BuildOneTurnPath(from, to, !horizontalFirst, Free);
            var route = ConveyorPathfinder.FindRoute(from, to, Free, extra);
            if (path == null || (route != null &&
                ConveyorPathfinder.PathCost(route, extra) < ConveyorPathfinder.PathCost(path, extra)))
                path = route;
            return path;
        }

        /// <summary>Segments joined head to tail; each joint node appears once. One node alone is itself.</summary>
        private static List<Vector2Int> Join(List<List<Vector2Int>> segments, List<Vector2Int> nodes)
        {
            var path = new List<Vector2Int>();
            if (segments.Count == 0)
            {
                if (nodes.Count > 0) path.Add(nodes[0]);
                return path;
            }
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                for (int i = s == 0 ? 0 : 1; i < seg.Count; i++) path.Add(seg[i]);
            }
            return path;
        }
    }
}
