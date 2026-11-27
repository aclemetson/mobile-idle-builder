using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Research panel tabs (2026-11-22, scripts/research_worlds.py): every research belongs to one world's tab
    /// (ResearchSO.world). Also guards the retirement of Liquid Hydrogen (scripts/remove_liquid_hydrogen.py).
    /// </summary>
    [TestFixture]
    public class ResearchTabsTests
    {
        ResearchSO[] _research;

        [OneTimeSetUp]
        public void Load()
        {
            _research = Resources.Load<ResearchDatabaseSO>("ResearchDatabase")?.allResearch?.Where(r => r != null).ToArray();
            Assert.IsNotNull(_research, "ResearchDatabase missing: run Import Game Data");
        }

        string Tab(string id) => ResearchPanelSubController.TabOf(_research.Single(r => r.id == id));

        [Test]
        public void EveryResearch_HasAWorldTab()
        {
            var worlds = Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds.Where(w => w != null).Select(w => w.id).ToList();
            foreach (var r in _research)
                CollectionAssert.Contains(worlds, ResearchPanelSubController.TabOf(r), $"{r.id} has no world tab");
        }

        [Test]
        public void ResearchSitsInTheWorldItUnlocks()
        {
            Assert.AreEqual(WorldGate.Physics, Tab("recombination_i"));
            Assert.AreEqual(WorldGate.Physics, Tab("heavy_elements"), "gated by Chemistry, but Physics research");
            Assert.AreEqual(WorldGate.Chemistry, Tab("chemistry_lab"));
            Assert.AreEqual(WorldGate.Metallurgy, Tab("roasting"), "unlocks the Metallurgy-only Materials Forge (2026-11-23)");
            Assert.AreEqual(WorldGate.Chemistry, Tab("biology_lab"), "opens Biology, so it is researched from Chemistry");
            Assert.AreEqual("world_biology", Tab("cell_biology"));
        }

        [Test]
        public void WorldGateResearch_IsNotInTheTabItOpens()
        {
            // A world's tab only shows once the world is open, so the research that opens it must live elsewhere
            // (biology_lab sat in the Biology tab and could never be found, 2026-11-26).
            foreach (var w in Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds.Where(w => w != null))
                foreach (var gate in w.prereqUnlockIds ?? new System.Collections.Generic.List<string>())
                {
                    var r = _research.FirstOrDefault(x => x.id == gate);
                    if (r == null) continue;
                    Assert.AreNotEqual(w.id, ResearchPanelSubController.TabOf(r),
                        $"{gate} opens {w.id} but is listed in its tab, so it can never be seen");
                }
        }

        [Test]
        public void LiquidHydrogen_IsRetired()
        {
            var recipes = Resources.Load<RecipeDatabaseSO>("RecipeDatabase").allRecipes.Where(r => r != null).ToArray();
            Assert.IsFalse(recipes.Any(r => r.id == "liquid_hydrogen"));
            Assert.IsFalse(recipes.Any(r => r.inputs != null && r.inputs.Any(i => i.item != null && i.item.id == "liquid_hydrogen")),
                "no recipe still takes liquid hydrogen");
            Assert.IsNull(Resources.LoadAll<ItemSO>("Items").FirstOrDefault(i => i.id == "liquid_hydrogen"));
            Assert.AreEqual("water", LegacyRecipeIds.Resolve("liquid_hydrogen"), "old saves that knew it know water");
        }
    }
}
