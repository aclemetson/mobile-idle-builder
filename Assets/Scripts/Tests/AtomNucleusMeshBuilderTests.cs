using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="AtomNucleusMeshBuilder"/> — the revolved nucleus used as the Atom
    /// Generator structure. Asserts the construction invariants the visual relies on: vertex count, a
    /// single apex on the axis, a ROUND (un-flattened) cross-section (unlike the collector spindle),
    /// bounds, and unit normals.
    /// </summary>
    [TestFixture]
    public class AtomNucleusMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Build(int s, int h)
        {
            var m = AtomNucleusMeshBuilder.Build(s, h);
            _meshes.Add(m);
            return m;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in _meshes)
                if (m != null) Object.DestroyImmediate(m);
            _meshes.Clear();
        }

        [Test]
        public void Build_VertexCount_IsRingsPlusApex()
        {
            const int s = 12, h = 8;
            var mesh = Build(s, h);
            Assert.AreEqual(h * (s + 1) + 1, mesh.vertexCount, "h rings of (s+1) cols, plus one apex");
        }

        [Test]
        public void Build_HasExactlyOneApexVertex_OnTheAxis()
        {
            var p = AtomNucleusMeshBuilder.NucleusProfile.Default;
            var mesh = Build(16, 12);

            float maxY = float.NegativeInfinity;
            foreach (var v in mesh.vertices) maxY = Mathf.Max(maxY, v.y);
            Assert.AreEqual(p.Height, maxY, 0.0001f, "apex reaches the profile height");

            int atTop = 0;
            foreach (var v in mesh.vertices)
                if (Mathf.Abs(v.y - maxY) < 1e-4f)
                {
                    atTop++;
                    Assert.Less(new Vector2(v.x, v.z).magnitude, 1e-4f, "the apex sits on the central axis");
                }
            Assert.AreEqual(1, atTop, "the top closes to a single shared apex vertex");
        }

        [Test]
        public void Radius_RoundBulbAtEquator_PointAtApex_PlantedAtBase()
        {
            var p = AtomNucleusMeshBuilder.NucleusProfile.Default;
            Assert.AreEqual(p.EquatorRadius, AtomNucleusMeshBuilder.Radius(0.5f, p), 1e-4f, "full radius at the equator");
            Assert.AreEqual(0f, AtomNucleusMeshBuilder.Radius(1f, p), 1e-4f, "tapers to a point at the apex");
            Assert.Greater(AtomNucleusMeshBuilder.Radius(0f, p), 0f, "small planted footprint at the base");
        }

        [Test]
        public void Build_CrossSectionIsRound_NotFlattened()
        {
            // s divisible by 4 so vertices land exactly on the +Z and +X axes.
            float maxZ = float.NegativeInfinity, minZ = float.PositiveInfinity, maxAbsX = 0f;
            foreach (var v in Build(16, 16).vertices)
            {
                maxZ    = Mathf.Max(maxZ, v.z);
                minZ    = Mathf.Min(minZ, v.z);
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(v.x));
            }

            Assert.AreEqual(maxAbsX, maxZ, 0.01f, "round: front depth equals side width (no flat facade)");
            Assert.AreEqual(-maxZ, minZ, 0.01f, "round: the back is as deep as the front");
        }

        [Test]
        public void Build_Bounds_MatchHeightAndAreSymmetric()
        {
            var p = AtomNucleusMeshBuilder.NucleusProfile.Default;
            var b = Build(24, 16).bounds;
            Assert.AreEqual(p.Height, b.size.y, 0.01f, "height spans the profile height");
            Assert.AreEqual(0f, b.center.x, 0.01f, "symmetric across the X axis");
            Assert.AreEqual(2f * p.EquatorRadius, b.size.x, 0.02f, "width is the nucleus diameter");
        }

        [Test]
        public void Build_NormalsAreUnitLength()
        {
            var mesh = Build(16, 10);
            foreach (var n in mesh.normals)
                Assert.AreEqual(1f, n.magnitude, 0.01f, "RecalculateNormals yields unit normals");
        }

        [Test]
        public void Build_ClampsDegenerateSegmentCounts()
        {
            var mesh = Build(0, 0); // clamps to s>=3, h>=2 instead of crashing
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(mesh.triangles.Length, 0);
        }
    }
}
