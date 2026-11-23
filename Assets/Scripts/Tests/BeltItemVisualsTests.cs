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
            Assert.IsFalse(BeltItemVisuals.Has((string)null));
            Assert.IsFalse(BeltItemVisuals.Has((ItemSO)null));
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
        public void TheFourOtherParticles_AreRegistered_WithTheirFacing()
        {
            foreach (var id in new[] { "alpha_particle", "beta_particle", "neutrino", "gamma_photon" })
                Assert.IsTrue(BeltItemVisuals.Has(id), id);

            ItemSO Item(string id) { var so = ScriptableObject.CreateInstance<ItemSO>(); so.id = id; return so; }
            var cases = new (string id, BeltItemVisuals.Facing facing)[]
            {
                ("proton", BeltItemVisuals.Facing.Fixed), ("neutrino", BeltItemVisuals.Facing.Fixed),
                ("alpha_particle", BeltItemVisuals.Facing.Camera),
                ("beta_particle", BeltItemVisuals.Facing.AlongBelt), ("gamma_photon", BeltItemVisuals.Facing.AlongBelt),
                ("steel", BeltItemVisuals.Facing.Camera),   // an icon tile
            };
            foreach (var (id, facing) in cases)
            {
                var so = Item(id);
                try { Assert.AreEqual(facing, BeltItemVisuals.FacingOf(so), id); }
                finally { Object.DestroyImmediate(so); }
            }
        }

        // ── Atoms ─────────────────────────────────────────────────────────────

        [TestCase(1,  new[] { 1 })]
        [TestCase(2,  new[] { 2 })]
        [TestCase(6,  new[] { 2, 4 })]
        [TestCase(8,  new[] { 2, 6 })]
        [TestCase(10, new[] { 2, 8 })]
        [TestCase(26, new[] { 2, 8, 14, 2 })]
        [TestCase(54, new[] { 2, 8, 18, 18, 8 })]
        [TestCase(92, new[] { 2, 8, 18, 32, 22, 8, 2 })]   // plain aufbau (real U puts one 5f electron in 6d)
        [TestCase(118, new[] { 2, 8, 18, 32, 32, 18, 8 })]
        public void ShellOccupancy_FollowsTheFillingOrder(int z, int[] expected)
        {
            CollectionAssert.AreEqual(expected, AtomStructure.ShellOccupancy(z));
            int sum = 0;
            foreach (int e in AtomStructure.ShellOccupancy(z)) sum += e;
            Assert.AreEqual(z, sum, "every electron is on a shell");
        }

        [Test]
        public void ShellRings_FaceTheViewer_AndGrowOutward()
        {
            for (int z = 1; z <= 118; z++)
            {
                var shells = AtomStructure.ShellOccupancy(z);
                float core = AtomStructure.NucleusRadius(z, z);
                float last = core;
                for (int k = 0; k < shells.Length; k++)
                {
                    // The atom faces the camera along local -Z; a ring tipped over 20 degrees would start to go edge-on.
                    Assert.Greater(Vector3.Dot(AtomStructure.ShellNormal(z, k), Vector3.back), Mathf.Cos(22f * Mathf.Deg2Rad),
                                   $"Z{z} shell {k} tips too far");
                    float r = AtomStructure.ShellRadius(k, shells.Length, core);
                    Assert.Greater(r, last, $"Z{z} shell {k} must sit outside the one inside it");
                    Assert.LessOrEqual(r, BeltItemVisuals.ItemSize * 0.5f, $"Z{z} shell {k} leaves the item's footprint");
                    last = r;
                }
            }
        }

        [Test]
        public void EveryElementAndIsotope_BuildsAnAtom()
        {
            var items = Resources.LoadAll<ItemSO>("Items");
            int atoms = 0;
            foreach (var so in items)
            {
                if (so == null || !BeltItemVisuals.IsAtom(so)) continue;
                atoms++;
                Assert.IsTrue(BeltItemVisuals.Has(so), so.id);
                Assert.AreEqual(BeltItemVisuals.Facing.Camera, BeltItemVisuals.FacingOf(so), so.id);
                var go = BeltItemVisuals.TryCreate(so, null);
                Assert.IsNotNull(go, $"{so.id}: shaders missing?");
                try
                {
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                        Assert.IsNotNull(r.sharedMaterial, $"{so.id}/{r.name} has no material");
                    Assert.IsNotNull(go.transform.Find("Nucleus"), so.id);
                    Assert.IsNotNull(go.transform.Find("Shells"), so.id);
                    Assert.IsNotNull(go.transform.Find("Orbit/Electrons"), so.id);
                    Assert.AreEqual(AtomStructure.IsNoble(so.atomicNumber), go.transform.Find("Glow") != null, $"{so.id} glow");
                    Assert.AreEqual(so.isRadioactive, go.transform.Find("Sparks") != null, $"{so.id} sparks");
                }
                finally { Object.DestroyImmediate(go); }
            }
            Assert.GreaterOrEqual(atoms, 126, "118 elements + 8 isotopes");
        }

        [Test]
        public void NonAtoms_KeepTheirIconTile()
        {
            foreach (var so in Resources.LoadAll<ItemSO>("Items"))
            {
                if (so == null) continue;
                bool expected = BeltItemVisuals.Has(so.id) || BeltItemVisuals.IsAtom(so) || BeltItemVisuals.IsMolecule(so);
                Assert.AreEqual(expected, BeltItemVisuals.Has(so), so.id);
                if (so.category is ItemCategory.Alloy or ItemCategory.Component)
                    Assert.IsNull(BeltItemVisuals.TryCreate(so, null), $"{so.id} should still be an icon tile");
            }
        }

        // ── Molecules ─────────────────────────────────────────────────────────

        [Test]
        public void EveryMoleculeAndOrganicCompound_BuildsABallAndStick_InsideTheItem()
        {
            int count = 0;
            foreach (var so in Resources.LoadAll<ItemSO>("Items"))
            {
                if (so == null || so.category is not (ItemCategory.Molecule or ItemCategory.OrganicCompound)) continue;
                // Organic-harvester recipes reuse the item, so only the item itself needs a structure.
                count++;
                Assert.IsTrue(BeltItemVisuals.IsMolecule(so), $"{so.id} has no structure in Resources/BeltMolecules.json");
                Assert.AreEqual(BeltItemVisuals.Facing.Fixed, BeltItemVisuals.FacingOf(so), so.id);
                var go = BeltItemVisuals.TryCreate(so, null);
                Assert.IsNotNull(go, so.id);
                try
                {
                    var mesh = go.transform.Find("Molecule").GetComponent<MeshFilter>().sharedMesh;
                    float reach = 0f;
                    foreach (var v in mesh.vertices) reach = Mathf.Max(reach, v.magnitude);
                    Assert.LessOrEqual(reach, BeltMoleculeData.FitRadius + 1e-4f, $"{so.id} sticks out of its item");
                    Assert.Greater(reach, 0.05f, $"{so.id} is too small to see");
                }
                finally { Object.DestroyImmediate(go); }
            }
            Assert.AreEqual(42, count, "38 molecules + 4 organic compounds (2026-11-23: life chemistry added 27, retired 6)");
        }

        [Test]
        public void EveryMoleculeStructure_IsARealItem()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Data", "game_data.json"));
            foreach (var id in BeltMoleculeData.Ids)
                StringAssert.Contains($"\"id\": \"{id}\"", json, $"'{id}' is not an item in game_data.json");
        }

        [Test]
        public void LightNucleus_HasOneBeadPerNucleon()
        {
            // Hydrogen is one proton; deuterium adds a neutron; carbon-12 is 12 beads.
            foreach (var (p, n) in new[] { (1, 0), (1, 1), (6, 6) })
            {
                var mesh = BeltParticleMeshes.NucleusCluster(p, n, Color.magenta, Color.gray);
                var single = BeltParticleMeshes.NucleusCluster(1, 0, Color.magenta, Color.gray);
                try { Assert.AreEqual(single.vertexCount * (p + n), mesh.vertexCount, $"{p}p{n}n"); }
                finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(single); }
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
