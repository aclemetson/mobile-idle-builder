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
        public const float NodeW = 196f, NodeH = 64f, ColW = 250f, RowH = 84f, BlockGap = 90f, BandTop = 70f;
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

            var edges = new List<(Vector2 from, Vector2 to, bool hot)>();
            var hotSet = HotSet(research, selectedId);
            foreach (var r in research)
            {
                if (r?.prerequisites == null || !_pos.TryGetValue(r.id, out var to)) continue;
                foreach (var p in r.prerequisites)
                    if (p != null && _pos.TryGetValue(p.id, out var from))
                        edges.Add((new Vector2(from.x + NodeW, from.y + NodeH / 2), new Vector2(to.x, to.y + NodeH / 2),
                                   hotSet != null && hotSet.Contains(r.id) && hotSet.Contains(p.id)));
            }
            _lines.Set(edges, bandRects);
            _content.style.width = maxX + 60; _content.style.height = maxY + 60;
            _lines.style.width = maxX + 60;   _lines.style.height = maxY + 60;
            _bands.style.width = maxX + 60;   _bands.style.height = maxY + 60;
            ApplyTransform();
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
            _content.style.translate = new Translate(_offset.x, _offset.y);
            _content.style.scale = new Scale(new Vector2(_scale, _scale));
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
            List<(Vector2 from, Vector2 to, bool hot)> _edges = new();
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

            public void Set(List<(Vector2, Vector2, bool)> edges, List<Rect> bands)
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
                    foreach (var (from, to, hot) in _edges)
                    {
                        if (hot != hotPass) continue;
                        p.strokeColor = hot ? Hot : Line;
                        p.lineWidth = hot ? 3f : 1.5f;
                        float dx = Mathf.Max(30f, (to.x - from.x) / 2f);
                        p.BeginPath();
                        p.MoveTo(from);
                        p.BezierCurveTo(new Vector2(from.x + dx, from.y), new Vector2(to.x - dx, to.y), to);
                        p.Stroke();
                    }
            }
        }
    }
}
