using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Item Timeline phase 1 (2026-11-23): per-world prestige-shop tracks (Fission Yield, Neutron Economy in
    /// Physics) and the removed Physics research merging into its replacements on load.
    /// </summary>
    [TestFixture]
    public class WorldUpgradeTracksTests
    {
        [Test]
        public void Catalogue_EveryUpgradeHasACostPerLevel()
        {
            foreach (var d in PersistentUpgradeService.All)
            {
                Assert.AreEqual(d.maxLevel, d.levelCosts.Length, d.id);
                for (int i = 1; i < d.levelCosts.Length; i++)
                    Assert.GreaterOrEqual(d.levelCosts[i], d.levelCosts[i - 1], $"{d.id} level {i + 1}");
                foreach (var (req, _) in d.prereqs)
                    Assert.IsNotNull(PersistentUpgradeService.FindDef(req), $"{d.id} needs unknown {req}");
            }
            Assert.AreEqual(PersistentUpgradeService.All.Length,
                PersistentUpgradeService.All.Select(d => d.id).Distinct().Count(), "ids are unique");
        }

        [Test]
        public void QuantumYield_HasTwentyLevels()
        {
            // 10 since the Item Timeline (2026-11-23), 20 since #501 (deep-game tail; see UpgradeLevelDepthTests).
            Assert.AreEqual(20, PersistentUpgradeService.FindDef("quantum_yield").maxLevel);
        }

        [TestCase("fission_yield",   UpgradeEffectType.WorldOutput)]
        [TestCase("neutron_economy", UpgradeEffectType.WorldCraftSpeed)]
        public void PhysicsTracks_ApplyOnlyInPhysics(string id, UpgradeEffectType type)
        {
            var d = PersistentUpgradeService.FindDef(id);
            Assert.IsNotNull(d, id);
            Assert.AreEqual(type, d.effectType);
            Assert.AreEqual(WorldGate.Physics, d.world);
        }

        [TestCase("spring_pressure", UpgradeEffectType.WorldStreamFlow)]
        [TestCase("catalysis",       UpgradeEffectType.WorldCraftSpeed)]
        public void ChemistryTracks_ApplyOnlyInChemistry(string id, UpgradeEffectType type)
        {
            var d = PersistentUpgradeService.FindDef(id);
            Assert.IsNotNull(d, id);
            Assert.AreEqual(type, d.effectType);
            Assert.AreEqual(WorldGate.Chemistry, d.world);
        }

        [Test]
        public void SpringFlow_CountsOnlyInChemistry()
        {
            var go  = new UnityEngine.GameObject("ups");
            var ups = go.AddComponent<PersistentUpgradeService>();
            try
            {
                ups.LoadFromSave(new List<string> { "spring_pressure:3" });
                Assert.AreEqual(0.60f, ups.GetWorldEffect(UpgradeEffectType.WorldStreamFlow, WorldGate.Chemistry), 1e-5f);
                Assert.AreEqual(0f, ups.GetWorldEffect(UpgradeEffectType.WorldStreamFlow, WorldGate.Physics), 1e-6f);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RetiredChemistryItems_AreRefundedOnLoad()
        {
            // Item Timeline phase 2: industrial products left Chemistry; a save holding them is paid their old price.
            Assert.AreEqual(6300, ECSLoadBridge.RetiredItems[155], "table_salt");
            Assert.AreEqual(21000, ECSLoadBridge.RetiredItems[162], "octane");
            Assert.AreEqual("hydrocarbons", ResearchService.MergedResearch["organic_chemistry"]);
            Assert.AreEqual("spall_gold", LegacyRecipeIds.Resolve("r_capture_gold"));
            Assert.AreEqual("iron_convergence", LegacyRecipeIds.Resolve("fuse_iron"));
        }

        [Test]
        public void WorldEffect_CountsOnlyTheMatchingWorld()
        {
            var go  = new UnityEngine.GameObject("ups");
            var ups = go.AddComponent<PersistentUpgradeService>();
            try
            {
                ups.LoadFromSave(new List<string> { "fission_yield:2", "neutron_economy:3" });
                Assert.AreEqual(0.30f, ups.GetWorldEffect(UpgradeEffectType.WorldOutput, WorldGate.Physics), 1e-5f);
                Assert.AreEqual(0.30f, ups.GetWorldEffect(UpgradeEffectType.WorldOutput, null), 1e-5f, "null = Physics");
                Assert.AreEqual(0.30f, ups.GetWorldEffect(UpgradeEffectType.WorldCraftSpeed, WorldGate.Physics), 1e-5f);
                Assert.AreEqual(0f, ups.GetWorldEffect(UpgradeEffectType.WorldOutput, WorldGate.Chemistry), 1e-6f);
                Assert.AreEqual(0f, ups.GetEffect(UpgradeEffectType.OutputQuantityMultiplier), 1e-6f,
                    "a world track is not the global Quantum Yield");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void WorldMultipliers_AreOne_WithoutTheShop()
        {
            if (PersistentUpgradeService.Instance != null) Assert.Ignore("a shop instance is live");
            Assert.AreEqual((1f, 1f), WorldRules.WorldMultipliers(WorldGate.Physics));
        }

        [Test]
        public void RemovedPhysicsResearch_MergesIntoItsReplacement()
        {
            var save = new SaveData { unlockedResearch = new List<string> { "fusion_ii", "transition_metals", "fissile_extraction" } };
            ResearchService.MigrateMergedResearch(save);
            CollectionAssert.AreEquivalent(new[] { "fusion_ii", "iron_convergence", "heavy_elements" }, save.unlockedResearch);
        }
    }
}
