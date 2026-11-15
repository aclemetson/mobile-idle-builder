using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Research price after upgrade discounts (Memory Resonance, Cyclotron Insight). Research has no
    /// "finished on an earlier run" discount: discounts come from upgrades only.
    /// </summary>
    [TestFixture]
    public class ResearchCostTests
    {
        [Test]
        public void NoDiscounts_IsTheAuthoredCost()
            => Assert.AreEqual(4_000, ResearchCost.Effective(4_000, 0f));

        [Test]
        public void MemoryResonance_AppliesToEveryResearch()
            => Assert.AreEqual(9_000, ResearchCost.Effective(10_000, 0.10f));

        [Test]
        public void MemoryResonanceAndInsight_StackMultiplicatively()
            // 10,000 x (1 - 0.25) x (1 - 0.2) = 6,000
            => Assert.AreEqual(6_000, ResearchCost.Effective(10_000, 0.25f, 0.2f));

        [Test]
        public void Discounts_AreCapped_SoResearchNeverGoesFree()
        {
            Assert.AreEqual(10, ResearchCost.Effective(100, 5f));
            Assert.AreEqual(1,  ResearchCost.Effective(1, 0.9f, 0.9f));
        }

        [Test]
        public void LargeCosts_StayExact()
            => Assert.AreEqual(3_345_000_000L, ResearchCost.Effective(4_460_000_000L, 0.25f));

        [Test]
        public void ZeroCost_StaysZero()
            => Assert.AreEqual(0, ResearchCost.Effective(0, 0.25f, 0.25f));

        [Test]
        public void OldSave_WithTheRemovedResearchCompletedEverList_StillLoads()
        {
            // Saves written before 2026-11-15 carry researchCompletedEver; JsonUtility must ignore it.
            var loaded = UnityEngine.JsonUtility.FromJson<SaveData>(
                "{\"playerId\":\"legacy\",\"unlockedResearch\":[\"fusion_i\"],\"researchCompletedEver\":[\"fusion_i\",\"heavy_elements\"]}");
            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(new[] { "fusion_i" }, loaded.unlockedResearch);
            StringAssert.DoesNotContain("researchCompletedEver", UnityEngine.JsonUtility.ToJson(loaded));
        }
    }
}
