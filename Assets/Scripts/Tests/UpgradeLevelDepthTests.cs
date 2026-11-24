using System.Linq;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #501: the prestige shop keeps something to buy deep into the game. The first pass to the Dyson Node earns
    /// about 24k prestige currency and the 4-year Ascension plan about 700k (scripts/item_timeline.py WALLS,
    /// ASCENSION), so the uncapped, live tracks got longer tails. Costs must still rise every level, and no single
    /// level may cost more than about two Ascension loops' earnings.
    /// </summary>
    public class UpgradeLevelDepthTests
    {
        [TestCase("memory_resonance", 10)]
        [TestCase("assembly_line", 20)]
        [TestCase("quantum_yield", 20)]
        [TestCase("entropy_echo", 10)]
        [TestCase("fission_yield", 15)]
        [TestCase("neutron_economy", 15)]
        [TestCase("spring_pressure", 12)]
        [TestCase("catalysis", 15)]
        [TestCase("growth_media", 15)]
        [TestCase("furnace_heat", 15)]
        [TestCase("precision", 15)]
        public void DeepTracks_HaveTheirExtendedLevels(string id, int levels)
        {
            var def = PersistentUpgradeService.FindDef(id);
            Assert.AreEqual(levels, def.maxLevel, id);
            Assert.AreEqual(def.maxLevel, def.levelCosts.Length, id + ": one cost per level");
        }

        [Test]
        public void EveryUpgrade_CostsMoreEachLevel_AndNoLevelPricesOutTheLateGame()
        {
            foreach (var def in PersistentUpgradeService.All)
            {
                Assert.AreEqual(def.maxLevel, def.levelCosts.Length, def.id);
                for (int i = 1; i < def.levelCosts.Length; i++)
                    Assert.Greater(def.levelCosts[i], def.levelCosts[i - 1], $"{def.id} level {i + 1}");
                Assert.LessOrEqual(def.levelCosts.Max(), 50_000, def.id);
            }
        }

        [Test]
        public void Shop_HoldsAboutTheAscensionPlansEarnings()
        {
            long total = PersistentUpgradeService.All.Sum(d => d.levelCosts.Sum(c => (long)c));
            Assert.Greater(total, 600_000, "deep enough for years of Ascension loops");
        }

        [Test]
        public void MemoryResonance_StaysUnderTheResearchDiscountCap()
        {
            var def = PersistentUpgradeService.FindDef("memory_resonance");
            Assert.LessOrEqual(def.effectPerLevel * def.maxLevel, ResearchCost.MaxDiscount);
        }
    }
}
