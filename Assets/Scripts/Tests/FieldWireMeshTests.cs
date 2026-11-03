using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for the field wire-mesh overlay:
    ///   • FieldWireMeshBuilder — line-grid construction (counts, topology, bounds, clamping)
    ///   • WireBounce           — the tap-bounce decay envelope (pure math)
    /// </summary>
    [TestFixture]
    public class FieldWireMeshTests
    {
        private readonly List<Mesh> _meshes = new();

        private Mesh Build(int resolution, float size)
        {
            var m = FieldWireMeshBuilder.Build(resolution, size);
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

        // ── FieldWireMeshBuilder ─────────────────────────────────────────────

        [Test]
        public void Build_DefaultResolution_HasExpectedVertexAndLineCount()
        {
            const int res = 10;
            var mesh = Build(res, 1f);

            int expectedVerts = (res + 1) * (res + 1);          // 121
            int expectedIndices = 4 * res * (res + 1);          // 440 (2 verts per segment)

            Assert.AreEqual(expectedVerts, mesh.vertexCount, "vertex count = (res+1)^2");
            Assert.AreEqual(MeshTopology.Lines, mesh.GetTopology(0), "wire mesh must use line topology");
            Assert.AreEqual((uint)expectedIndices, mesh.GetIndexCount(0), "index count = 4*res*(res+1)");
        }

        [Test]
        public void Build_ResolutionClampedToMinimum()
        {
            // A zero/negative resolution must still yield a valid 1-cell mesh, not crash.
            var mesh = Build(0, 1f);

            Assert.AreEqual(4, mesh.vertexCount, "resolution clamps to 1 -> 2x2 grid points");
            Assert.AreEqual((uint)8, mesh.GetIndexCount(0), "1 cell -> 4 segments -> 8 indices");
        }

        [Test]
        public void Build_BoundsMatchRequestedSize()
        {
            const float size = 0.95f;
            var mesh = Build(8, size);
            var b = mesh.bounds;

            Assert.AreEqual(size, b.size.x, 0.0001f, "X extent spans the requested size");
            Assert.AreEqual(size, b.size.z, 0.0001f, "Z extent spans the requested size");
            Assert.AreEqual(0f, b.size.y, 0.0001f, "mesh is flat at rest (Y displacement happens in the shader)");
            Assert.AreEqual(Vector3.zero, b.center, "grid is centred on the local origin");
        }

        // ── WireBounce ───────────────────────────────────────────────────────

        [Test]
        public void Amplitude_AtStart_ReturnsInitialPeak()
        {
            Assert.AreEqual(0.18f, WireBounce.Amplitude(0f, 0.18f, 5f), 0.0001f);
        }

        [Test]
        public void Amplitude_Decays_TowardZeroOverTime()
        {
            float early = WireBounce.Amplitude(0.1f, 0.18f, 5f);
            float late  = WireBounce.Amplitude(1.0f, 0.18f, 5f);

            Assert.Less(early, 0.18f, "amplitude drops below the peak as time passes");
            Assert.Less(late, early, "amplitude keeps decaying");
            Assert.Greater(late, 0f, "exponential decay stays positive");
        }

        [Test]
        public void Amplitude_NegativeOrLargeElapsed_IsFinite()
        {
            // Negative elapsed clamps to the peak; a huge elapsed decays to ~0. Both finite.
            Assert.AreEqual(0.18f, WireBounce.Amplitude(-1f, 0.18f, 5f), 0.0001f);

            float huge = WireBounce.Amplitude(1000f, 0.18f, 5f);
            Assert.IsFalse(float.IsNaN(huge) || float.IsInfinity(huge), "must stay finite");
            Assert.AreEqual(0f, huge, 0.0001f, "decays to effectively zero");
        }
    }
}
