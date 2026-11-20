using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The research tree (#337): every research node, laid out inside its research block (game phase), with lines
    /// to its prerequisites. Pan by dragging, zoom by pinch or mouse wheel, tap a node to show its card below.
    ///
    /// Positions come from the research tree editor (docs/research-tree.html → ResearchSO.treeX/treeY). Nodes
    /// without one get the editor's own Tidy layout: blocks left to right, and inside a block one column per
    /// prerequisite depth. Sizes match the editor so its coordinates map one to one.
    /// </summary>
    public class ResearchTreeView : VisualElement
    {
        // Rows and columns leave real gutters (48px / 74px) so prerequisite lines that skip columns can run
        // between nodes instead of under them (see Route). docs/research-tree.html's Tidy uses the same sizes.
        public const float NodeW = 196f, NodeH = 64f, ColW = 270f, RowH = 112f, BlockGap = 90f, BandTop = 70f;
        const float TrackGap = 6f, LaneInset = 10f, LaneStep = 6f, CornerR = 8f;
        const float MinScale = 0.25f, MaxScale = 1.6f, DragThreshold = 10f;

        public enum NodeState { Locked, Available, Active, Done }

        readonly VisualElement _viewport, _content, _bands;
        readonly TreeLines _lines;
        public readonly ScrollView Detail;

        readonly Dictionary<string, VisualElement> _nodes = new();
        Dictionary<string, Vector2> _pos = new();
        Vector2 _offset = new(24, 24);
        float _scale = 0.8f;
        bool _placed;
        string _focusOnLayout;
        Vector2 _contentSize;
        const float EdgePad = 8f;

        /// <summary>Raised when the player taps a node.</summary>
        public event Action<ResearchSO> NodeSelected;
        public string SelectedId { get; private set; }

        public ResearchTreeView()
        {
            AddToClassList("research-tree");
            _viewport = new VisualElement { name = "research-tree-viewport" };
            _viewport.AddToClassList("rt-viewport");
            _content = new VisualElement();
            _content.AddToClassList("rt-content");
            _content.style.transformOrigin = new TransformOrigin(0, 0);
            _bands = new VisualElement { pickingMode = PickingMode.Ignore };
            _lines = new TreeLines { pickingMode = PickingMode.Ignore };
            _content.Add(_bands);
            _content.Add(_lines);
            _viewport.Add(_content);
            Add(_viewport);

            Detail = new ScrollView(ScrollViewMode.Vertical) { name = "research-tree-detail" };
            Detail.AddToClassList("rt-detail");
            Detail.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(Detail);

            RegisterPanZoom();
            _viewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_placed && _pos.Count > 0 && _viewport.resolvedStyle.height > 0) { _placed = true; FitHeight(); }
                if (_focusOnLayout != null && _viewport.resolvedStyle.width > 0) { CenterOn(_focusOnLayout); _focusOnLayout = null; }
                ApplyTransform();   // re-clamp after a resize (rotation, landscape side-by-side layout)
            });
        }

        // ── Layout ────────────────────────────────────────────────────────────

        /// <summary>
        /// Node positions in tree space. Uses each node's editor position when it has one; the rest follow the
        /// editor's Tidy layout (blocks in order, prerequisite-depth columns inside a block, rows by branch then cost).
        /// </summary>
        public static Dictionary<string, Vector2> Layout(IReadOnlyList<ResearchSO> research, IReadOnlyList<ResearchBlock> blocks)
        {
            var pos = new Dictionary<string, Vector2>();
            var all = research.Where(r => r != null).ToList();
            var byId = all.ToDictionary(r => r.id);
            var order = (blocks ?? Array.Empty<ResearchBlock>()).OrderBy(b => b.order).Select(b => b.id).ToList();
            var groups = order.Select(b => all.Where(r => r.block == b).ToList()).ToList();
            var loose = all.Where(r => !order.Contains(r.block)).ToList();
            if (loose.Count > 0) groups.Add(loose);

            float bx = 0f;
            foreach (var g in groups)
            {
                if (g.Count == 0) continue;
                var ids = new HashSet<string>(g.Select(r => r.id));
                var local = new Dictionary<string, int>();
                int Depth(string id)
                {
                    if (local.TryGetValue(id, out int d)) return d;
                    local[id] = 0;    // guards against a cycle
                    var ps = (byId[id].prerequisites ?? Array.Empty<ResearchSO>())
                             .Where(p => p != null && ids.Contains(p.id)).ToList();
                    return local[id] = ps.Count == 0 ? 0 : 1 + ps.Max(p => Depth(p.id));
                }
                int maxD = 0;
                var cols = new SortedDictionary<int, List<ResearchSO>>();
                foreach (var r in g)
                {
                    int d = Depth(r.id);
                    maxD = Math.Max(maxD, d);
                    if (!cols.TryGetValue(d, out var c)) cols[d] = c = new List<ResearchSO>();
                    c.Add(r);
                }
                foreach (var kv in cols)
                {
                    var col = kv.Value.OrderBy(r => (int)r.branch).ThenBy(r => r.costBaseCurrency).ToList();
                    for (int i = 0; i < col.Count; i++)
                    {
                        var r = col[i];
                        pos[r.id] = r.treeX >= 0 && r.treeY >= 0
                            ? new Vector2(r.treeX, r.treeY)
                            : new Vector2(bx + kv.Key * ColW, BandTop + i * RowH);
                    }
                }
                bx += (maxD + 1) * ColW + BlockGap;
            }
            return pos;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Rebuilds every node. Pan and zoom are kept, so a refresh never moves the player's view.</summary>
        public void Rebuild(IReadOnlyList<ResearchSO> research, IReadOnlyList<ResearchBlock> blocks,
                            Func<ResearchSO, NodeState> stateOf, Func<ResearchSO, string> metaOf, string selectedId)
        {
            SelectedId = selectedId;
            foreach (var n in _nodes.Values) n.RemoveFromHierarchy();
            _nodes.Clear();
            _bands.Clear();
            _pos = Layout(research, blocks);

            float maxX = 0, maxY = 0;
            foreach (var r in research)
            {
                if (r == null || !_pos.TryGetValue(r.id, out var p)) continue;
                var node = new VisualElement { userData = r, name = "rt-node-" + r.id };
                node.AddToClassList("rt-node");
                node.AddToClassList("rt-node--" + stateOf(r).ToString().ToLowerInvariant());
                if (r.id == selectedId) node.AddToClassList("rt-node--selected");
                node.style.left = p.x; node.style.top = p.y; node.style.width = NodeW; node.style.height = NodeH;
                var name = new Label(r.displayName); name.AddToClassList("rt-node-name");
                var meta = new Label(metaOf(r));     meta.AddToClassList("rt-node-meta");
                node.Add(name); node.Add(meta);
                _content.Add(node);
                _nodes[r.id] = node;
                maxX = Mathf.Max(maxX, p.x + NodeW); maxY = Mathf.Max(maxY, p.y + NodeH);
            }

            // Block bands with their names, behind the nodes.
            var bandRects = new List<Rect>();
            int k = 0;
            foreach (var b in (blocks ?? Array.Empty<ResearchBlock>()).OrderBy(b => b.order))
            {
                var members = research.Where(r => r != null && r.block == b.id && _pos.ContainsKey(r.id)).Select(r => _pos[r.id]).ToList();
                if (members.Count == 0) continue;
                float x0 = members.Min(v => v.x) - 24, x1 = members.Max(v => v.x) + NodeW + 24;
                bandRects.Add(new Rect(x0, 0, x1 - x0, maxY + 30));
                var label = new Label(b.displayName) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("rt-band-label");
                if (k++ % 2 == 1) label.AddToClassList("rt-band-label--alt");
                label.style.left = x0 + 14; label.style.top = 14;
                _bands.Add(label);
            }

            var pairs = new List<(string from, string to)>();
            foreach (var r in research)
            {
                if (r?.prerequisites == null || !_pos.ContainsKey(r.id)) continue;
                foreach (var p in r.prerequisites)
                    if (p != null && _pos.ContainsKey(p.id)) pairs.Add((p.id, r.id));
            }
            var hotSet = HotSet(research, selectedId);
            var routes = Route(_pos, pairs);
            var edges = new List<(List<Vector2> pts, bool hot)>(routes.Count);
            for (int i = 0; i < pairs.Count; i++)
                edges.Add((routes[i], hotSet != null && hotSet.Contains(pairs[i].to) && hotSet.Contains(pairs[i].from)));
            _lines.Set(edges, bandRects);
            _contentSize = new Vector2(maxX + 60, maxY + 60);
            _content.style.width = maxX + 60; _content.style.height = maxY + 60;
            _lines.style.width = maxX + 60;   _lines.style.height = maxY + 60;
            _bands.style.width = maxX + 60;   _bands.style.height = maxY + 60;
            ApplyTransform();
        }

        /// <summary>
        /// The path of each prerequisite line (<paramref name="edges"/> are prerequisite -> research ids). A line
        /// between neighbouring columns is two points, drawn as a curve through the column gap. A line that skips
        /// columns would pass straight under the nodes between and read as if they were prerequisites too, so it
        /// is routed as right-angled segments instead: out into the gap right of its source, along the gutter
        /// between two rows (its own track there), and down the gap left of its target. Pure (tested).
        /// </summary>
        public static List<List<Vector2>> Route(IReadOnlyDictionary<string, Vector2> pos, IReadOnlyList<(string from, string to)> edges)
        {
            var columns = pos.Values.Select(v => v.x).Distinct().OrderBy(x => x).ToList();
            var result = new List<List<Vector2>>(edges.Count);
            // Routed edges, grouped by gutter, for track assignment once all are known.
            var byGutter = new Dictionary<int, List<(int edge, float x0, float x1)>>();
            var srcSlots = new Dictionary<float, int>();
            var dstSlots = new Dictionary<float, int>();

            for (int i = 0; i < edges.Count; i++)
            {
                var a = pos[edges[i].from]; var b = pos[edges[i].to];
                var from = new Vector2(a.x + NodeW, a.y + NodeH / 2);
                var to   = new Vector2(b.x, b.y + NodeH / 2);
                bool skips = columns.Any(x => x > a.x + 1f && x < b.x - 1f);
                if (!skips) { result.Add(new List<Vector2> { from, to }); continue; }

                // Lanes in the column gaps: each edge gets its own, stepping in from the nodes' edges.
                int s = srcSlots.TryGetValue(from.x, out var sn) ? sn : 0; srcSlots[from.x] = s + 1;
                int d = dstSlots.TryGetValue(to.x,   out var dn) ? dn : 0; dstSlots[to.x]   = d + 1;
                float x0 = from.x + LaneInset + s * LaneStep;
                float x1 = to.x   - LaneInset - d * LaneStep;

                // Gutter k lies between row k-1 and row k: below the row when both ends share it, else just
                // above the lower end, so the line never needs the strip under the block labels.
                int ra = Mathf.RoundToInt((a.y - BandTop) / RowH), rb = Mathf.RoundToInt((b.y - BandTop) / RowH);
                int k = ra == rb ? ra + 1 : Mathf.Max(ra, rb);
                if (!byGutter.TryGetValue(k, out var list)) byGutter[k] = list = new List<(int, float, float)>();
                list.Add((i, x0, x1));
                result.Add(new List<Vector2> { from, new(x0, from.y), new(x0, 0), new(x1, 0), new(x1, to.y), to });
            }

            // Tracks inside each gutter: edges whose horizontal runs overlap get different heights.
            float gutter = RowH - NodeH;
            foreach (var kv in byGutter)
            {
                var trackEnds = new List<float>();
                var track = new Dictionary<int, int>();
                foreach (var (edge, x0, x1) in kv.Value.OrderBy(e => e.x0))
                {
                    int t = trackEnds.FindIndex(end => end + TrackGap < x0);
                    if (t < 0) { t = trackEnds.Count; trackEnds.Add(x1); } else trackEnds[t] = x1;
                    track[edge] = t;
                }
                int n = trackEnds.Count;
                float step = n <= 1 ? 0f : Mathf.Min(TrackGap, (gutter - 2 * TrackGap) / (n - 1));
                float mid = BandTop + kv.Key * RowH - gutter / 2;
                foreach (var (edge, _, _) in kv.Value)
                {
                    float y = mid + (track[edge] - (n - 1) / 2f) * step;
                    var pts = result[edge];
                    pts[2] = new Vector2(pts[2].x, y);
                    pts[3] = new Vector2(pts[3].x, y);
                }
            }
            return result;
        }

        static HashSet<string> HotSet(IReadOnlyList<ResearchSO> research, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var byId = research.Where(r => r != null).ToDictionary(r => r.id);
            if (!byId.ContainsKey(id)) return null;
            var hot = new HashSet<string> { id };
            var q = new Stack<string>(new[] { id });
            while (q.Count > 0)
                foreach (var p in byId[q.Pop()].prerequisites ?? Array.Empty<ResearchSO>())
                    if (p != null && hot.Add(p.id)) q.Push(p.id);
            foreach (var r in byId.Values)
                if (r.prerequisites != null && r.prerequisites.Any(p => p != null && p.id == id)) hot.Add(r.id);
            return hot;
        }

        /// <summary>Centres the view on a node once the panel has a size (or now, if it already has one).</summary>
        public void FocusOn(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_viewport.resolvedStyle.width > 0 && !float.IsNaN(_viewport.resolvedStyle.width)) CenterOn(id);
            else _focusOnLayout = id;
        }

        void CenterOn(string id)
        {
            if (!_pos.TryGetValue(id, out var p)) return;
            _offset = new Vector2(_viewport.resolvedStyle.width / 2 - (p.x + NodeW / 2) * _scale,
                                  _viewport.resolvedStyle.height / 2 - (p.y + NodeH / 2) * _scale);
            ApplyTransform();
        }

        void FitHeight()
        {
            if (_pos.Count == 0) return;
            float h = _pos.Values.Max(v => v.y) + NodeH + 40;
            _scale = Mathf.Clamp((_viewport.resolvedStyle.height - 20) / h, 0.45f, 1f);
            ApplyTransform();
        }

        void ApplyTransform()
        {
            float vw = _viewport.resolvedStyle.width, vh = _viewport.resolvedStyle.height;
            if (vw > 0 && vh > 0 && _contentSize.x > 0)
            {
                _offset.x = ClampAxis(_offset.x, _contentSize.x * _scale, vw, EdgePad);
                _offset.y = ClampAxis(_offset.y, _contentSize.y * _scale, vh, EdgePad);
            }
            _content.style.translate = new Translate(_offset.x, _offset.y);
            _content.style.scale = new Scale(new Vector2(_scale, _scale));
        }

        /// <summary>
        /// Keeps the tree in view along one axis. Content that fits is pinned to the start edge: centring a node
        /// in a tree only two rows tall left a third of the viewport empty above it. Larger content can pan only
        /// until its far edge reaches the viewport's, so it can't be dragged out of sight.
        /// </summary>
        internal static float ClampAxis(float offset, float content, float view, float pad)
        {
            if (content + 2 * pad <= view) return pad;
            return Mathf.Clamp(offset, view - content - pad, pad);
        }

        // ── Pan, zoom, tap ────────────────────────────────────────────────────

        readonly Dictionary<int, Vector2> _pointers = new();
        Vector2 _downPos, _downOffset;
        bool _dragging;
        float _pinchDist, _pinchScale;
        Vector2 _pinchMid;

        void RegisterPanZoom()
        {
            _viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                _pointers[e.pointerId] = e.position;
                if (_pointers.Count == 1) { _downPos = e.position; _downOffset = _offset; _dragging = false; }
                else if (_pointers.Count == 2)
                {
                    var pts = _pointers.Values.ToArray();
                    _pinchDist = Vector2.Distance(pts[0], pts[1]); _pinchScale = _scale; _pinchMid = (pts[0] + pts[1]) / 2; _dragging = true;
                }
            }, TrickleDown.TrickleDown);

            _viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_pointers.ContainsKey(e.pointerId)) return;
                _pointers[e.pointerId] = e.position;
                if (_pointers.Count >= 2)
                {
                    var pts = _pointers.Values.ToArray();
                    float d = Vector2.Distance(pts[0], pts[1]);
                    if (_pinchDist > 1f) ZoomAt(_pinchMid, Mathf.Clamp(_pinchScale * d / _pinchDist, MinScale, MaxScale));
                    return;
                }
                Vector2 delta = (Vector2)e.position - _downPos;
                if (!_dragging && delta.magnitude > DragThreshold)
                {
                    _dragging = true;
                    _viewport.CapturePointer(e.pointerId);
                }
                if (_dragging) { _offset = _downOffset + delta; ApplyTransform(); }
            });

            void End(IPointerEvent e, VisualElement target)
            {
                bool tap = _pointers.Count == 1 && !_dragging;
                _pointers.Remove(e.pointerId);
                if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId);
                if (!tap) return;
                for (var t = target; t != null && t != _viewport; t = t.parent)
                    if (t.userData is ResearchSO r) { NodeSelected?.Invoke(r); return; }
            }
            _viewport.RegisterCallback<PointerUpEvent>(e => End(e, e.target as VisualElement));
            _viewport.RegisterCallback<PointerCancelEvent>(e => { _pointers.Remove(e.pointerId); _dragging = false; });

            _viewport.RegisterCallback<WheelEvent>(e =>
            {
                ZoomAt(e.mousePosition, Mathf.Clamp(_scale * Mathf.Exp(-e.delta.y * 0.05f), MinScale, MaxScale));
                e.StopPropagation();
            });
        }

        void ZoomAt(Vector2 panelPoint, float newScale)
        {
            Vector2 local = _viewport.WorldToLocal(panelPoint);
            float f = newScale / _scale;
            _offset = local - (local - _offset) * f;
            _scale = newScale;
            ApplyTransform();
        }

        /// <summary>Prerequisite lines and block bands, drawn in one pass behind the nodes.</summary>
        class TreeLines : VisualElement
        {
            List<(List<Vector2> pts, bool hot)> _edges = new();
            List<Rect> _bands = new();
            // The project renders in linear colour space and Painter2D takes linear values, so these sRGB colours
            // go through .linear; without it they draw far brighter than written.
            static readonly Color Line  = new Color(0.55f, 0.62f, 0.72f, 0.6f).linear;
            static readonly Color Hot   = new Color(0f, 0.9f, 1f, 0.95f).linear;
            static readonly Color BandA = new Color(0.20f, 0.23f, 0.29f, 1f).linear;
            static readonly Color BandB = new Color(0.22f, 0.21f, 0.31f, 1f).linear;

            public TreeLines()
            {
                style.position = Position.Absolute;
                style.left = 0; style.top = 0;
                generateVisualContent += Draw;
            }

            public void Set(List<(List<Vector2>, bool)> edges, List<Rect> bands)
            {
                _edges = edges; _bands = bands;
                MarkDirtyRepaint();
            }

            void Draw(MeshGenerationContext mgc)
            {
                var p = mgc.painter2D;
                for (int i = 0; i < _bands.Count; i++)
                {
                    var r = _bands[i];
                    p.fillColor = i % 2 == 0 ? BandA : BandB;
                    p.BeginPath();
                    p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                    p.Fill();
                }
                p.lineCap = LineCap.Round;
                foreach (var hotPass in new[] { false, true })      // highlighted lines on top
                    foreach (var (pts, hot) in _edges)
                    {
                        if (hot != hotPass || pts.Count < 2) continue;
                        p.strokeColor = hot ? Hot : Line;
                        p.lineWidth = hot ? 3f : 1.5f;
                        p.BeginPath();
                        p.MoveTo(pts[0]);
                        if (pts.Count == 2)
                        {
                            Vector2 from = pts[0], to = pts[1];
                            float dx = Mathf.Max(30f, (to.x - from.x) / 2f);
                            p.BezierCurveTo(new Vector2(from.x + dx, from.y), new Vector2(to.x - dx, to.y), to);
                        }
                        else
                        {
                            // Right angles with rounded corners. A corner too tight for the radius is a plain join.
                            for (int i = 1; i < pts.Count - 1; i++)
                            {
                                Vector2 a = pts[i - 1], c = pts[i], n = pts[i + 1];
                                float r = Mathf.Min(CornerR, Vector2.Distance(a, c) / 2, Vector2.Distance(c, n) / 2);
                                if (r < 0.5f) p.LineTo(c); else p.ArcTo(c, n, r);
                            }
                            p.LineTo(pts[pts.Count - 1]);
                        }
                        p.Stroke();
                    }
            }
        }
    }
}
