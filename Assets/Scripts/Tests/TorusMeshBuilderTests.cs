using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="TorusMeshBuilder"/> — the flat gold torus used as Maxwell's Demon's
    /// structure. Asserts the construction invariants the visual relies on: vertex/triangle counts, a
    /// genuine central hole (it is a ring, not a disc), bounds, and unit normals.
    /// </summary>
    [TestFixture]
    public class TorusMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Build(int tub, int rad)
        {
            var m = TorusMeshBuilder.Build(tub, rad);
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
        public void Build_VertexCount_IsTubularRowsTimesRadialRows()
        {
            const int tub = 16, rad = 8;
            var mesh = Build(tub, rad);
            Assert.AreEqual((tub + 1) * (rad + 1), mesh.vertexCount,
                "duplicated ring and tube seams: (tubular+1) * (radial+1)");
        }

        [Test]
        public void Build_TriangleCount_IsTwoPerQuad()
        {
            const int tub = 16, rad = 8;
            var mesh = Build(tub, rad);
            Assert.AreEqual(tub * rad * 6, mesh.triangles.Length, "two triangles (6 indices) per quad");
        }

        [Test]
        public void Build_HasCentralHole_RingNotDisc()
        {
            var p = TorusMeshBuilder.TorusProfile.Default;
            var mesh = Build(32, 16);

            float minXZ = float.PositiveInfinity, maxXZ = float.NegativeInfinity;
            foreach (var v in mesh.vertices)
            {
                float radial = new Vector2(v.x, v.z).magnitude;
                minXZ = Mathf.Min(minXZ, radial);
                maxXZ = Mathf.Max(maxXZ, radial);
            }

            Assert.AreEqual(p.MajorRadius - p.MinorRadius, minXZ, 0.01f,
                "inner edge of the ring leaves a hole at the centre");
            Assert.Greater(minXZ, 0f, "no vertex on the central axis — it is a ring, not a disc");
            Assert.AreEqual(p.MajorRadius + p.MinorRadius, maxXZ, 0.01f, "outer edge spans the full ring");
        }

        [Test]
        public void Build_IsFlat_LowProfileInY()
        {
            var p = TorusMeshBuilder.TorusProfile.Default;
            var b = Build(32, 16).bounds;

            Assert.AreEqual(2f * (p.MajorRadius + p.MinorRadius), b.size.x, 0.01f, "width spans outer diameter");
            Assert.AreEqual(2f * (p.MajorRadius + p.MinorRadius), b.size.z, 0.01f, "depth spans outer diameter");
            Assert.AreEqual(2f * p.MinorRadius, b.size.y, 0.01f, "height is just the tube diameter (lies flat)");
            Assert.AreEqual(Vector3.zero, b.center, "centred on the origin");
        }

        [Test]
        public void Build_NormalsAreUnitLength()
        {
            var mesh = Build(24, 12);
            foreach (var n in mesh.normals)
                Assert.AreEqual(1f, n.magnitude, 0.01f, "RecalculateNormals yields unit normals");
        }

        [Test]
        public void Build_ClampsDegenerateSegmentCounts()
        {
            var mesh = Build(0, 0); // clamps to tub>=3, rad>=3 instead of crashing
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(mesh.triangles.Length, 0);
        }
    }
}
