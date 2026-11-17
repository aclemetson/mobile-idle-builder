using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="TeardropMeshBuilder"/> and the fork ribbon the Belt Splitter's river
    /// uses: the rock's point must face upstream, stay in its cell, and glow only at the tip; the fork must
    /// reach every exit from the shared inbound edge with the flow running the right way.
    /// </summary>
    [TestFixture]
    public class TeardropMeshBuilderTests
    {
        private const int LenSegs = 12, ArchSegs = 8;
        private const int North = 0, East = 1, South = 2, West = 3;

        private readonly List<Mesh> _meshes = new();

        private Mesh Track(Mesh mesh)
        {
            _meshes.Add(mesh);
            return mesh;
        }

        private Mesh Rock(IReadOnlyList<TeardropMeshBuilder.RockProfile> rocks) =>
            Track(TeardropMeshBuilder.Build(LenSegs, ArchSegs, rocks));

        [TearDown]
        public void TearDown()
        {
            foreach (var m in _meshes)
                if (m != null) Object.DestroyImmediate(m);
            _meshes.Clear();
        }

        [Test]
        public void Build_VertexCount_IsRowsTimesColumnsPerRock()
        {
            int perRock = (LenSegs + 1) * (ArchSegs + 1);
            Assert.AreEqual(perRock, Rock(TeardropMeshBuilder.TwoWay).vertexCount);
            Assert.AreEqual(perRock * 2, Rock(TeardropMeshBuilder.ThreeWay).vertexCount);
        }

        [Test]
        public void TipRow_CollapsesToOneUpstreamPoint_OnTheGround()
        {
            var rock  = TeardropMeshBuilder.TwoWay[0];
            var verts = Rock(TeardropMeshBuilder.TwoWay).vertices;
            for (int j = 0; j <= ArchSegs; j++)
            {
                Assert.AreEqual(rock.CenterX, verts[j].x, 1e-5f, "tip must be a single point");
                Assert.AreEqual(0f, verts[j].y, 1e-5f, "tip must touch the ground");
                Assert.AreEqual(rock.TipZ, verts[j].z, 1e-5f);
            }
            Assert.Less(rock.TipZ, 0f, "the tip faces upstream, which is local -Z");
        }

        [Test]
        public void WidestPoint_IsBehindTheCentreOfTheRock()
        {
            float bestU = 0f, best = 0f;
            for (int i = 1; i < 100; i++)
            {
                float u = i / 100f;
                float r = TeardropMeshBuilder.PlanRadius(u, 1f);
                if (r > best) { best = r; bestU = u; }
            }
            Assert.Greater(bestU, 0.5f, "a long taper to the point, not a symmetric lozenge");
            Assert.AreEqual(1f, best, 0.01f, "the widest point reaches HalfWidth");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Rocks_StayInsideTheCell_AndAboveTheGround(int ways)
        {
            var bounds = Rock(ways == 3 ? TeardropMeshBuilder.ThreeWay : TeardropMeshBuilder.TwoWay).bounds;
            Assert.GreaterOrEqual(bounds.min.x, -0.5f);
            Assert.LessOrEqual(bounds.max.x, 0.5f);
            Assert.GreaterOrEqual(bounds.min.z, -0.5f);
            Assert.LessOrEqual(bounds.max.z, 0.5f);
            Assert.GreaterOrEqual(bounds.min.y, -1e-5f);
        }

        [Test]
        public void ThreeWay_LeavesAChannelStraightAhead()
        {
            foreach (var rock in TeardropMeshBuilder.ThreeWay)
                Assert.Greater(Mathf.Abs(rock.CenterX) - rock.HalfWidth, 0.05f, "rocks must not close the centre");
        }

        [Test]
        public void TipGlowCoordinate_PeaksOnlyAtTheTip()
        {
            var mesh = Rock(TeardropMeshBuilder.TwoWay);
            var verts = mesh.vertices;
            var uvs   = mesh.uv;
            var rock  = TeardropMeshBuilder.TwoWay[0];

            Assert.AreEqual(1f, uvs[0].y, 1e-5f, "the tip carries the shader's full glow");
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.GreaterOrEqual(uvs[i].y, 0f);
                Assert.LessOrEqual(uvs[i].y, 1f + 1e-5f);
                if (verts[i].z > rock.TipZ + rock.Length * 0.5f)
                    Assert.Less(uvs[i].y, 0.9f, "the back half must not glow like the tip");
            }
        }

        [Test]
        public void Normals_AreUnitLength()
        {
            var normals = Rock(TeardropMeshBuilder.TwoWay).normals;
            int stride  = ArchSegs + 1;
            // The tip and back rows each collapse to one point, so they have no meaningful normal.
            for (int i = stride; i < normals.Length - stride; i++)
                Assert.AreEqual(1f, normals[i].magnitude, 1e-3f, $"vertex {i}");
        }

        [Test]
        public void Fork_FullyCoversTheInboundEdge_AndEveryExit()
        {
            const float cell = 1.5f, half = cell * 0.5f;
            float hw = cell * ConveyorFlowMeshBuilder.WaterWidthFrac * 0.5f;
            var mesh  = Track(ConveyorFlowMeshBuilder.BuildFork(cell, South, new[] { West, North, East }));
            var verts = mesh.vertices;
            var uvs   = mesh.uv;

            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (!Mathf.Approximately(uvs[i].y, 0f)) continue;
                Assert.AreEqual(-half, verts[i].z, 1e-4f, "uv.y = 0 belongs on the inbound edge");
                minX = Mathf.Min(minX, verts[i].x);
                maxX = Mathf.Max(maxX, verts[i].x);
            }
            Assert.AreEqual(-hw, minX, 1e-4f, "must match the width of the belt feeding it");
            Assert.AreEqual(hw, maxX, 1e-4f, "must match the width of the belt feeding it");

            AssertHasExit(verts, uvs, new Vector2(-half, 0f), "west");
            AssertHasExit(verts, uvs, new Vector2(0f, half), "north");
            AssertHasExit(verts, uvs, new Vector2(half, 0f), "east");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Fork_InboundSlices_ShareTheEdgeWithoutOverlap(int ways)
        {
            int[] exits = ways == 3 ? new[] { West, North, East } : new[] { West, East };
            var mesh = Track(ConveyorFlowMeshBuilder.BuildFork(1f, South, exits));
            var verts = mesh.vertices;
            var uvs   = mesh.uv;

            var slices = new List<(float a, float b)>();
            for (int i = 0; i + 1 < verts.Length; i += 2)
                if (Mathf.Approximately(uvs[i].y, 0f))
                    slices.Add((Mathf.Min(verts[i].x, verts[i + 1].x), Mathf.Max(verts[i].x, verts[i + 1].x)));

            Assert.AreEqual(ways, slices.Count);
            slices.Sort((p, q) => p.a.CompareTo(q.a));
            for (int i = 1; i < slices.Count; i++)
                Assert.AreEqual(slices[i - 1].b, slices[i].a, 1e-4f, "neighbouring slices meet exactly");
        }

        [Test]
        public void Fork_BranchesKeepTheirSides_WhateverOrderExitsAreGiven()
        {
            // Port order must not matter: the left slice has to go to the left exit or the streams would cross.
            var verts = Track(ConveyorFlowMeshBuilder.BuildFork(1f, South, new[] { East, West })).vertices;
            int rows = verts.Length / 4; // two branches, two vertices per row

            for (int branch = 0; branch < 2; branch++)
            {
                int start = branch * rows * 2;
                int end   = start + (rows - 1) * 2;
                float entryX = (verts[start].x + verts[start + 1].x) * 0.5f;
                float exitX  = (verts[end].x + verts[end + 1].x) * 0.5f;
                Assert.AreEqual(Mathf.Sign(entryX), Mathf.Sign(exitX), "a branch must exit on the side it entered");
            }
        }

        [Test]
        public void Fork_UsesTheRequestedHeight()
        {
            var verts = Track(ConveyorFlowMeshBuilder.BuildFork(1f, South, new[] { West, East },
                ConveyorFlowMeshBuilder.FloorY, ConveyorFlowMeshBuilder.FloorWidthFrac)).vertices;
            foreach (var v in verts)
                Assert.AreEqual(ConveyorFlowMeshBuilder.FloorY, v.y, 1e-5f);
        }

        // A branch's exit is a pair of vertices at uv.y = 1 straddling the edge's midpoint, a full belt wide.
        private static void AssertHasExit(Vector3[] verts, Vector2[] uvs, Vector2 point, string label)
        {
            for (int i = 0; i + 1 < verts.Length; i += 2)
            {
                if (!Mathf.Approximately(uvs[i].y, 1f)) continue;
                var mid = (verts[i] + verts[i + 1]) * 0.5f;
                if (Vector2.Distance(new Vector2(mid.x, mid.z), point) > 1e-4f) continue;
                Assert.AreEqual(0f, uvs[i].x, 1e-5f, "exit must map uv.x as a belt does");
                Assert.AreEqual(1f, uvs[i + 1].x, 1e-5f, "exit must map uv.x as a belt does");
                return;
            }
            Assert.Fail($"No branch ends at the {label} exit {point}");
        }
    }
}
