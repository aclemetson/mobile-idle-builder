using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="RevolveMeshBuilder"/>: the profile must land where it says, the shader's
    /// height coordinate must span it, and a pie slice must stay in its half so a chamber can't cross the wall.
    /// </summary>
    [TestFixture]
    public class RevolveMeshBuilderTests
    {
        private readonly List<Mesh> _meshes = new();

        private static readonly Vector2[] Profile =
        {
            new Vector2(0.4f, 0f), new Vector2(0.2f, 0.5f), new Vector2(0f, 1f),
        };

        private Mesh Track(Mesh mesh)
        {
            _meshes.Add(mesh);
            return mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in _meshes)
                if (m != null) Object.DestroyImmediate(m);
            _meshes.Clear();
        }

        [Test]
        public void Build_VertexCount_IsRowsTimesSeamedColumns()
        {
            const int segments = 12;
            Assert.AreEqual(Profile.Length * (segments + 1), Track(RevolveMeshBuilder.Build(Profile, segments)).vertexCount);
        }

        [Test]
        public void Build_FollowsTheProfile()
        {
            var bounds = Track(RevolveMeshBuilder.Build(Profile, 64)).bounds;
            Assert.AreEqual(0f, bounds.min.y, 1e-5f);
            Assert.AreEqual(1f, bounds.max.y, 1e-5f);
            Assert.AreEqual(0.4f, bounds.extents.x, 1e-3f);
            Assert.AreEqual(0.4f, bounds.extents.z, 1e-3f);
        }

        [Test]
        public void Build_TopRow_CollapsesOntoTheAxis_WithFullHeightCoordinate()
        {
            const int segments = 12;
            var mesh = Track(RevolveMeshBuilder.Build(Profile, segments));
            var verts = mesh.vertices;
            var uvs   = mesh.uv;
            int top   = (Profile.Length - 1) * (segments + 1);
            for (int i = top; i < verts.Length; i++)
            {
                Assert.AreEqual(0f, new Vector2(verts[i].x, verts[i].z).magnitude, 1e-5f);
                Assert.AreEqual(1f, uvs[i].y, 1e-5f, "the top drives the shader's tip glow");
            }
            Assert.AreEqual(0f, uvs[0].y, 1e-5f);
        }

        [Test]
        public void Sector_StaysInItsHalf_AndAtItsHeight()
        {
            const float y = 0.02f;
            foreach (var v in Track(RevolveMeshBuilder.BuildSector(0.5f, y, -90f, 90f, 16)).vertices)
            {
                Assert.GreaterOrEqual(v.x, -1e-5f, "a +X half must not cross the wall");
                Assert.AreEqual(y, v.y, 1e-5f);
                Assert.LessOrEqual(new Vector2(v.x, v.z).magnitude, 0.5f + 1e-5f);
            }
        }

        [Test]
        public void Sector_FacesUp()
        {
            // Unlit shaders here cull back faces unless they say otherwise, so winding decides visibility.
            foreach (var n in Track(RevolveMeshBuilder.BuildSector(0.5f, 0f, 90f, 270f, 16)).normals)
                Assert.Greater(n.y, 0.99f);
        }

        [Test]
        public void StretchBetween_SpansTheTwoPoints()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            try
            {
                var a = new Vector3(1f, 0f, 0f);
                var b = new Vector3(1f, 3f, 4f);
                StructureParts.StretchBetween(go.transform, a, b, 0.2f);

                Assert.AreEqual(((a + b) * 0.5f).x, go.transform.localPosition.x, 1e-5f);
                Assert.AreEqual(2.5f, go.transform.localScale.y, 1e-5f, "a cylinder primitive is 2 units tall");
                Assert.AreEqual(0.2f, go.transform.localScale.x, 1e-5f);
                Assert.Greater(Vector3.Dot(go.transform.up, (b - a).normalized), 0.999f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
