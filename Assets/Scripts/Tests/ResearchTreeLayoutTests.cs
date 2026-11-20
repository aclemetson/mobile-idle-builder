using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The in-game research tree's layout (#337): ResearchTreeView.Layout, the editor's Tidy rule.</summary>
    [TestFixture]
    public class ResearchTreeLayoutTests
    {
        ResearchDatabaseSO _db;

        [OneTimeSetUp]
        public void Load()
        {
            _db = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(_db, "ResearchDatabase missing: run Import Game Data");
        }

        [Test]
        public void EveryResearch_IsPlaced_WithoutOverlapping()
        {
            var pos = ResearchTreeView.Layout(_db.allResearch, _db.blocks);
            Assert.AreEqual(_db.allResearch.Count(r => r != null), pos.Count);
            var rects = pos.ToDictionary(kv => kv.Key, kv => new Rect(kv.Value, new Vector2(ResearchTreeView.NodeW, ResearchTreeView.NodeH)));
            var ids = rects.Keys.ToList();
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++)
                    Assert.IsFalse(rects[ids[i]].Overlaps(rects[ids[j]]), $"{ids[i]} overlaps {ids[j]}");
        }

        [Test]
        public void AutoLayout_PutsPrerequisitesToTheLeft_AndBlocksInOrder()
        {
            if (_db.allResearch.Any(r => r != null && r.treeX >= 0)) Assert.Ignore("editor positions are in use; auto layout not exercised");
            var pos = ResearchTreeView.Layout(_db.allResearch, _db.blocks);

            foreach (var r in _db.allResearch.Where(r => r != null))
                foreach (var p in r.prerequisites ?? new ResearchSO[0])
                    if (p != null && p.block == r.block)
                        Assert.Less(pos[p.id].x, pos[r.id].x, $"{p.id} should sit left of {r.id}");

            var blocks = _db.blocks.OrderBy(b => b.order).ToList();
            for (int i = 1; i < blocks.Count; i++)
            {
                var left  = _db.allResearch.Where(r => r != null && r.block == blocks[i - 1].id).Select(r => pos[r.id].x).DefaultIfEmpty().Max();
                var right = _db.allResearch.Where(r => r != null && r.block == blocks[i].id).Select(r => pos[r.id].x).DefaultIfEmpty().Min();
                Assert.Less(left, right, $"{blocks[i - 1].id} should be left of {blocks[i].id}");
            }
        }

        /// <summary>
        /// A line that ran under a node made that node look like a prerequisite (2026-11-19 device report).
        /// Every line, curved or routed, must stay clear of every node except its own two ends.
        /// </summary>
        [Test]
        public void PrerequisiteLines_NeverPassUnderAnotherNode()
        {
            var pos = ResearchTreeView.Layout(_db.allResearch, _db.blocks);
            var edges = _db.allResearch.Where(r => r != null && r.prerequisites != null)
                .SelectMany(r => r.prerequisites.Where(p => p != null).Select(p => (from: p.id, to: r.id))).ToList();
            var routes = ResearchTreeView.Route(pos, edges);
            Assert.AreEqual(edges.Count, routes.Count);

            for (int i = 0; i < edges.Count; i++)
            {
                foreach (var pt in Sample(routes[i]))
                    foreach (var kv in pos)
                    {
                        if (kv.Key == edges[i].from || kv.Key == edges[i].to) continue;
                        var node = new Rect(kv.Value, new Vector2(ResearchTreeView.NodeW, ResearchTreeView.NodeH));
                        Assert.IsFalse(node.Contains(pt), $"{edges[i].from} -> {edges[i].to} passes under {kv.Key} at {pt}");
                    }
            }
        }

        [Test]
        public void RoutedLines_FromDifferentSources_NeverShareASegment()
        {
            var pos = ResearchTreeView.Layout(_db.allResearch, _db.blocks);
            var edges = _db.allResearch.Where(r => r != null && r.prerequisites != null)
                .SelectMany(r => r.prerequisites.Where(p => p != null).Select(p => (from: p.id, to: r.id))).ToList();
            var routes = ResearchTreeView.Route(pos, edges);
            var segs = new List<(string src, Vector2 a, Vector2 b)>();
            for (int i = 0; i < edges.Count; i++)
                for (int j = 1; j + 2 < routes[i].Count; j++)        // inner segments only: the stubs into a shared target may meet
                    segs.Add((edges[i].from, routes[i][j], routes[i][j + 1]));

            for (int i = 0; i < segs.Count; i++)
                for (int j = i + 1; j < segs.Count; j++)
                    if (segs[i].src != segs[j].src)
                        Assert.IsFalse(CollinearOverlap(segs[i].a, segs[i].b, segs[j].a, segs[j].b),
                            $"lines from {segs[i].src} and {segs[j].src} run on top of each other at {segs[i].a}-{segs[i].b}");
        }

        static IEnumerable<Vector2> Sample(List<Vector2> pts)
        {
            if (pts.Count == 2)
            {
                // Same curve the view draws: horizontal tangents at both ends.
                Vector2 a = pts[0], d = pts[1];
                float dx = Mathf.Max(30f, (d.x - a.x) / 2f);
                Vector2 b = new(a.x + dx, a.y), c = new(d.x - dx, d.y);
                for (int k = 1; k < 40; k++)
                {
                    float t = k / 40f, u = 1 - t;
                    yield return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
                }
                yield break;
            }
            for (int s = 0; s + 1 < pts.Count; s++)
                for (int k = 0; k <= 20; k++)
                    yield return Vector2.Lerp(pts[s], pts[s + 1], k / 20f);
        }

        static bool CollinearOverlap(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            const float e = 0.5f;
            bool aH = Mathf.Abs(a1.y - a2.y) < e, bH = Mathf.Abs(b1.y - b2.y) < e;
            bool aV = Mathf.Abs(a1.x - a2.x) < e, bV = Mathf.Abs(b1.x - b2.x) < e;
            if (aH && bH && Mathf.Abs(a1.y - b1.y) < e)
                return Mathf.Min(Mathf.Max(a1.x, a2.x), Mathf.Max(b1.x, b2.x)) - Mathf.Max(Mathf.Min(a1.x, a2.x), Mathf.Min(b1.x, b2.x)) > e;
            if (aV && bV && Mathf.Abs(a1.x - b1.x) < e)
                return Mathf.Min(Mathf.Max(a1.y, a2.y), Mathf.Max(b1.y, b2.y)) - Mathf.Max(Mathf.Min(a1.y, a2.y), Mathf.Min(b1.y, b2.y)) > e;
            return false;
        }

        [Test]
        public void EditorPositions_AreUsedAsIs()
        {
            var r = ScriptableObject.CreateInstance<ResearchSO>();
            try
            {
                r.id = "probe"; r.block = "x"; r.treeX = 1234; r.treeY = 567;
                var pos = ResearchTreeView.Layout(new[] { r }, new[] { new ResearchBlock { id = "x", order = 1 } });
                Assert.AreEqual(new Vector2(1234, 567), pos["probe"]);
            }
            finally { Object.DestroyImmediate(r); }
        }
    }
}
