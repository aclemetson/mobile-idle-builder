using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="CollectorMeshBuilder"/> — the revolved spindle used as the
    /// field-collector structure. Asserts the construction invariants the visual relies on: vertex
    /// count, a single sharp apex on the axis, a round body, bounds, and unit normals.
    /// </summary>
    [TestFixture]
    public class CollectorMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Build(int s, int h)
        {
            var m = CollectorMeshBuilder.Build(s, h);
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
            var p = CollectorMeshBuilder.SpindleProfile.Default;
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
            Assert.AreEqual(1, atTop, "the sharp point is a single shared apex vertex");
        }

        [Test]
        public void Radius_TapersToPointAtApex_RoundInBody_PlantedAtBase()
        {
            var p = CollectorMeshBuilder.SpindleProfile.Default;
            Assert.AreEqual(0f, CollectorMeshBuilder.Radius(1f, p), 1e-4f, "tapers to a point at the apex");
            Assert.Greater(CollectorMeshBuilder.Radius(0.35f, p), 0.1f, "round bulb in the body");
            Assert.Greater(CollectorMeshBuilder.Radius(0f, p), 0f, "small planted footprint at the base");
        }

        [Test]
        public void Build_Bounds_MatchHeightAndAreSymmetricInX()
        {
            var p = CollectorMeshBuilder.SpindleProfile.Default;
            var b = Build(24, 16).bounds;
            Assert.AreEqual(p.Height, b.size.y, 0.01f, "height spans the profile height");
            Assert.AreEqual(0f, b.center.x, 0.01f, "symmetric across the X axis (front flatten is on Z)");
            Assert.LessOrEqual(b.size.x, 2f * p.BulbRadius + 0.01f, "width bounded by the bulb diameter");
            Assert.Greater(b.size.x, 0f, "the body has width");
        }

        [Test]
        public void Build_FlattensFront_KeepsBackRound()
        {
            var   p     = CollectorMeshBuilder.SpindleProfile.Default;
            float flatZ = CollectorMeshBuilder.FrontFlatZ(p);

            float maxZ = float.NegativeInfinity, minZ = float.PositiveInfinity, maxAbsX = 0f;
            foreach (var v in Build(24, 16).vertices)
            {
                maxZ    = Mathf.Max(maxZ, v.z);
                minZ    = Mathf.Min(minZ, v.z);
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(v.x));
            }

            Assert.LessOrEqual(maxZ, flatZ + 1e-4f, "front (+Z) is sliced flat — no vertex past the plane");
            Assert.AreEqual(flatZ, maxZ, 0.01f, "the flat front reaches the slice plane");
            Assert.Less(minZ, -flatZ, "the back stays rounded, deeper than the flat front");
            Assert.Greater(maxAbsX, flatZ, "the round sides are wider than the flat slice");
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
