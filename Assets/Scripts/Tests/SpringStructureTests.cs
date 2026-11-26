using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The Spring's rock-pool meshes (concept A): an open side toward the output (+Z) for the spill.</summary>
    [TestFixture]
    public class SpringStructureTests
    {
        [Test]
        public void Rocks_LeaveTheOutputSideOpen()
        {
            var rocks = SpringStructure.BuildRocks();
            try
            {
                var verts = new List<Vector3>(); rocks.GetVertices(verts);
                Assert.IsNotEmpty(verts);
                // No stone sits in front of the spill: nothing within the lip's width on the +Z side.
                Assert.IsFalse(verts.Any(v => v.z > 0.25f && Mathf.Abs(v.x) < 0.08f), "a stone blocks the spill");
                Assert.IsTrue(verts.Any(v => v.z < -0.25f), "the ring closes behind the pool");
            }
            finally { Object.DestroyImmediate(rocks); }
        }

        [Test]
        public void Pool_IsDeepestInTheMiddle_AndRipplesOutward()
        {
            var pool = SpringStructure.BuildPool();
            try
            {
                var uv = new List<Vector2>(); pool.GetUVs(0, uv);
                Assert.AreEqual(0.5f, uv[0].x, 1e-4f, "centre: uv.x 0.5 is the water shader's deepest point");
                Assert.AreEqual(0f, uv[0].y, 1e-4f);
                Assert.IsTrue(uv.Skip(1).All(u => u.x < 0.1f && u.y > 0.5f), "rim fades, and is further along the flow");
            }
            finally { Object.DestroyImmediate(pool); }
        }

        [Test]
        public void Spill_ReachesTheCellEdgeOnTheOutputSide()
        {
            var spill = SpringStructure.BuildSpill();
            try { Assert.AreEqual(0.5f, spill.bounds.max.z, 1e-4f); }
            finally { Object.DestroyImmediate(spill); }
        }
    }
}
