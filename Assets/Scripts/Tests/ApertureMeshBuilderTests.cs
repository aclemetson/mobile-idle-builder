using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ApertureMeshBuilder"/> — the building port holes. Covers the convex
    /// emission door (outputs) and the recessed intake mouth (inputs). These meshes are double-sided with
    /// shared vertices, so per-vertex normals can cancel; normals are intentionally not asserted.
    /// </summary>
    [TestFixture]
    public class ApertureMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Track(Mesh m) { _meshes.Add(m); return m; }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in _meshes)
                if (m != null) Object.DestroyImmediate(m);
            _meshes.Clear();
        }

        [Test]
        public void ArchDoor_SpansHeight_CentredOnX_FacingZ()
        {
            const float w = 0.2f, hgt = 0.3f;
            var mesh = Track(ApertureMeshBuilder.BuildArchDoor(w, hgt, 12));

            float maxY = float.NegativeInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (var v in mesh.vertices)
            {
                maxY = Mathf.Max(maxY, v.y);
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                Assert.AreEqual(0f, v.z, 1e-4f, "the door is flat on the local XY plane (faces +Z)");
            }
            Assert.AreEqual(hgt, maxY, 1e-4f, "reaches the requested height");
            Assert.AreEqual(-w * 0.5f, minX, 1e-4f, "left edge at -width/2");
            Assert.AreEqual( w * 0.5f, maxX, 1e-4f, "right edge at +width/2");
        }

        [Test]
        public void IntakeMouth_VertexCount_IsTwoRingsPlusCap()
        {
            const int segs = 14;
            var mesh = Track(ApertureMeshBuilder.BuildIntakeMouth(0.16f, 0.12f, segs));
            Assert.AreEqual(2 * segs + 1, mesh.vertexCount, "outer rim + inner throat rings + one cap centre");
        }

        [Test]
        public void IntakeMouth_IsRecessed_RimAtZeroThroatBehind()
        {
            const float radius = 0.16f, depth = 0.12f;
            var mesh = Track(ApertureMeshBuilder.BuildIntakeMouth(radius, depth, 16));

            float maxZ = float.NegativeInfinity, minZ = float.PositiveInfinity, maxR = 0f;
            foreach (var v in mesh.vertices)
            {
                maxZ = Mathf.Max(maxZ, v.z);
                minZ = Mathf.Min(minZ, v.z);
                maxR = Mathf.Max(maxR, new Vector2(v.x, v.y).magnitude);
            }
            Assert.AreEqual(0f, maxZ, 1e-4f, "the rim sits at z=0");
            Assert.AreEqual(-depth, minZ, 1e-4f, "the throat is recessed to -depth (a concave hole)");
            Assert.AreEqual(radius, maxR, 1e-4f, "the outer rim radius matches the request");
        }

        [Test]
        public void IntakeMouth_ClampsDegenerateSegments()
        {
            var mesh = Track(ApertureMeshBuilder.BuildIntakeMouth(0.1f, 0.05f, 2)); // clamps to >= 6
            Assert.GreaterOrEqual(mesh.vertexCount, 2 * 6 + 1);
            Assert.Greater(mesh.triangles.Length, 0);
        }
    }
}
