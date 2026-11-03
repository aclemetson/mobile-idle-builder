using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="RoundedBoxMeshBuilder"/> — the superellipsoid dome used as a
    /// footprint-filling building body (Strong Force Combiner). Asserts vertex count, a single apex on the
    /// axis, the base sitting at y=0, the footprint half-extents, bounds, and unit normals.
    /// </summary>
    [TestFixture]
    public class RoundedBoxMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Build(int s, int t, RoundedBoxMeshBuilder.RoundedBoxProfile p)
        {
            var m = RoundedBoxMeshBuilder.Build(s, t, p);
            _meshes.Add(m);
            return m;
        }

        private static RoundedBoxMeshBuilder.RoundedBoxProfile Profile(float hw, float hd, float h) =>
            new RoundedBoxMeshBuilder.RoundedBoxProfile { HalfWidth = hw, HalfDepth = hd, Height = h, Roundness = 0.35f };

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
            const int s = 16, t = 8;
            var mesh = Build(s, t, Profile(0.9f, 0.45f, 0.8f));
            Assert.AreEqual(t * (s + 1) + 1, mesh.vertexCount, "t rings of (s+1) cols, plus one apex");
        }

        [Test]
        public void Build_HasExactlyOneApexVertex_OnTheAxis()
        {
            var mesh = Build(16, 10, Profile(0.9f, 0.45f, 0.8f));

            float maxY = float.NegativeInfinity;
            foreach (var v in mesh.vertices) maxY = Mathf.Max(maxY, v.y);
            Assert.AreEqual(0.8f, maxY, 1e-4f, "apex reaches the profile height");

            int atTop = 0;
            foreach (var v in mesh.vertices)
                if (Mathf.Abs(v.y - maxY) < 1e-4f)
                {
                    atTop++;
                    Assert.Less(new Vector2(v.x, v.z).magnitude, 1e-4f, "the apex sits on the central axis");
                }
            Assert.AreEqual(1, atTop, "the dome closes to a single shared apex vertex");
        }

        [Test]
        public void Build_BaseSitsAtGround_AndFillsFootprint()
        {
            // s a multiple of 4 so vertices land exactly on the +x and +z face centres.
            var p = Profile(0.9f, 0.45f, 0.8f);
            float minY = float.PositiveInfinity, maxAbsX = 0f, maxAbsZ = 0f;
            foreach (var v in Build(16, 10, p).vertices)
            {
                minY    = Mathf.Min(minY, v.y);
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(v.x));
                maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(v.z));
            }
            Assert.AreEqual(0f, minY, 1e-4f, "the base sits on the ground plane (y=0)");
            Assert.AreEqual(p.HalfWidth, maxAbsX, 1e-3f, "reaches the footprint half-width");
            Assert.AreEqual(p.HalfDepth, maxAbsZ, 1e-3f, "reaches the footprint half-depth");
        }

        [Test]
        public void Build_Bounds_MatchProfile()
        {
            var p = Profile(0.9f, 0.45f, 0.8f);
            var b = Build(24, 12, p).bounds;
            Assert.AreEqual(2f * p.HalfWidth, b.size.x, 0.02f, "width spans the footprint");
            Assert.AreEqual(2f * p.HalfDepth, b.size.z, 0.02f, "depth spans the footprint");
            Assert.AreEqual(p.Height, b.size.y, 0.01f, "height spans the profile height");
            Assert.AreEqual(0f, b.center.x, 0.01f, "centred on X");
            Assert.AreEqual(0f, b.center.z, 0.01f, "centred on Z");
        }

        [Test]
        public void Build_NormalsAreUnitLength()
        {
            var mesh = Build(16, 10, Profile(0.9f, 0.45f, 0.8f));
            foreach (var n in mesh.normals)
                Assert.AreEqual(1f, n.magnitude, 0.01f, "RecalculateNormals yields unit normals");
        }

        [Test]
        public void Build_ClampsDegenerateSegmentCounts()
        {
            var mesh = Build(0, 0, Profile(0.9f, 0.45f, 0.8f)); // clamps to s>=8, t>=2
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(mesh.triangles.Length, 0);
        }
    }
}
