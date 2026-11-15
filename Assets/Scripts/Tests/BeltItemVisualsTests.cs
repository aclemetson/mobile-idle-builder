using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The 3D belt particles: registry ids, fallback, and the jitter channel the shader reads.</summary>
    [TestFixture]
    public class BeltItemVisualsTests
    {
        [Test]
        public void EveryRegisteredId_IsARealItem()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Data", "game_data.json"));
            foreach (var id in BeltItemVisuals.RegisteredIds)
                StringAssert.Contains($"\"id\": \"{id}\"", json, $"'{id}' is not an item in game_data.json");
        }

        [Test]
        public void TheFiveBasicParticles_AreRegistered()
        {
            foreach (var id in new[] { "electron", "up_quark", "down_quark", "proton", "neutron" })
                Assert.IsTrue(BeltItemVisuals.Has(id), id);
        }

        [Test]
        public void UnregisteredId_FallsBack()
        {
            Assert.IsFalse(BeltItemVisuals.Has("hydrogen"));
            Assert.IsFalse(BeltItemVisuals.Has(null));
            Assert.IsNull(BeltItemVisuals.TryCreate("hydrogen", null));
        }

        [Test]
        public void RegisteredId_BuildsRenderers()
        {
            foreach (var id in BeltItemVisuals.RegisteredIds)
            {
                var go = BeltItemVisuals.TryCreate(id, null);
                Assert.IsNotNull(go, $"{id}: shaders missing?");
                try
                {
                    var renderers = go.GetComponentsInChildren<MeshRenderer>();
                    Assert.Greater(renderers.Length, 0, id);
                    foreach (var r in renderers)
                        Assert.IsNotNull(r.sharedMaterial, $"{id}/{r.name} has no material");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        [Test]
        public void NucleonCore_EveryVertexHasColourAndJitter()
        {
            var mesh = BeltParticleMeshes.NucleonCore(Color.red, Color.red, Color.blue, Color.yellow);
            try
            {
                int n = mesh.vertexCount;
                Assert.Greater(n, 0);
                Assert.AreEqual(n, mesh.colors.Length);
                var jitter = new List<Vector4>();
                mesh.GetUVs(1, jitter);
                Assert.AreEqual(n, jitter.Count);

                // Three quarks with seeds 1..3; gluon tubes blend between two of them.
                var seeds = new HashSet<float>();
                bool anyBlend = false;
                foreach (var j in jitter)
                {
                    seeds.Add(j.x);
                    if (j.z > 0f) { anyBlend = true; Assert.AreNotEqual(j.x, j.y); }
                }
                CollectionAssert.AreEquivalent(new[] { 1f, 2f, 3f }, seeds);
                Assert.IsTrue(anyBlend, "gluon tubes should blend toward the next quark's jitter");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ElectronCloud_IsDeterministic()
        {
            var a = BeltParticleMeshes.ElectronCloud();
            var b = BeltParticleMeshes.ElectronCloud();
            try
            {
                Assert.AreEqual(a.vertexCount, b.vertexCount);
                Assert.AreEqual(a.vertices[0], b.vertices[0]);
                Assert.AreEqual(a.vertices[a.vertexCount - 1], b.vertices[b.vertexCount - 1]);
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
