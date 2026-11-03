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
