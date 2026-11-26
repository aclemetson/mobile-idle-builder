using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Item Timeline phase 3 (2026-11-23): the Metallurgy world. Ores are mined on its own site, the Materials Forge
    /// smelts them, and its research is one chain from Ore Prospecting to Metamaterials. Run Import Game Data first.
    /// </summary>
    [TestFixture]
    public class MetallurgyContentTests
    {
        ResearchSO[] _research;
        BuildingSO[] _buildings;

        [OneTimeSetUp]
        public void Load()
        {
            _research  = Resources.Load<ResearchDatabaseSO>("ResearchDatabase").allResearch.Where(r => r != null).ToArray();
            _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase").allBuildings.Where(b => b != null).ToArray();
        }

        [Test]
        public void ResearchIsOneChain_InsideMetallurgy_NeverCheaperThanTheNodeBefore()
        {
            var chain = new[] { "ore_prospecting", "base_metals", "roasting", "steelmaking", "electrolysis",
                                "light_alloys", "semiconductors", "superconductors", "metamaterials" };
            ResearchSO prev = null;
            foreach (var id in chain)
            {
                var r = _research.Single(x => x.id == id);
                Assert.AreEqual(WorldGate.Metallurgy, r.requiredWorld, id);
                Assert.AreEqual(WorldGate.Metallurgy, r.world, id);
                if (prev != null)
                {
                    Assert.IsTrue(r.prerequisites.Any(p => p != null && p.id == prev.id), $"{id} needs {prev.id}");
                    Assert.GreaterOrEqual(r.costBaseCurrency, prev.costBaseCurrency, id);
                }
                prev = r;
            }
        }

        [Test]
        public void MetallurgyBuildings_MineAndSmelt_WithoutPower()
        {
            var harvester = _buildings.Single(b => b.id == "metal_harvester");
            var forge     = _buildings.Single(b => b.id == "materials_forge");
            foreach (var b in new[] { harvester, forge })
            {
                Assert.AreEqual(WorldGate.Metallurgy, b.world, b.id);
                Assert.IsFalse(b.requiresPower, $"{b.id}: generators are Physics-only");
            }
            CollectionAssert.Contains(harvester.compatibleFields, "Ore");
            var forged = forge.supportedRecipes.Select(r => r.outputItem.id).ToList();
            foreach (var id in new[] { "iron_oxide", "steel", "aluminium_ingot", "copper_wire", "titanium_alloy",
                                       "carbon_fiber", "semiconductor_wafer", "aerogel", "superconductor", "metamaterial" })
                CollectionAssert.Contains(forged, id, $"the Materials Forge makes {id}");
        }

        [Test]
        public void BiologyBuildings_RunWithoutPower()
        {
            foreach (var id in new[] { "organic_harvester", "biosynthesizer", "cell_assembler", "tissue_culture", "bioreactor" })
                Assert.IsFalse(_buildings.Single(b => b.id == id).requiresPower, $"{id}: generators are Physics-only");
        }

        [Test]
        public void FoundrySite_ListsEveryOre_BoughtAfterItsResearch()
        {
            var site = Resources.Load<SiteDatabaseSO>("SiteDatabase").allSites.Single(s => s != null && s.id == "site_foundry");
            var ores = site.fieldOverrides.Where(o => o?.field != null && o.field.fieldType == "Ore").ToList();
            Assert.AreEqual(6, ores.Count);
            foreach (var o in ores)
            {
                Assert.AreEqual(0f, o.densityMultiplier, $"{o.field.id} is bought, not placed up front");
                Assert.AreEqual(WorldGate.Metallurgy, o.field.world);
                Assert.IsNotEmpty(o.field.purchaseResearchId, o.field.id);
            }
        }
    }
}
