using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Research price after prestige discounts. Before these were wired, ResearchService charged the raw
    /// costBaseCurrency on every run (Memory Resonance and prestige_memory_discount did nothing), which is
    /// most of why play slowed after the first prestige.
    /// </summary>
    [TestFixture]
    public class ResearchCostTests
    {
        [Test]
        public void NoDiscounts_IsTheAuthoredCost()
            => Assert.AreEqual(4_000, ResearchCost.Effective(4_000, 0f, 0.15f, completedBefore: false));

        [Test]
        public void MemoryDiscount_OnlyAppliesToResearchFinishedBefore()
        {
            // recombination_i: 100e, 25% memory discount -> 75e on a repeat run (economy-balance.md phase 7).
            Assert.AreEqual(100, ResearchCost.Effective(100, 0f, 0.25f, completedBefore: false));
            Assert.AreEqual(75,  ResearchCost.Effective(100, 0f, 0.25f, completedBefore: true));
        }

        [Test]
        public void MemoryResonance_AppliesToEveryResearch()
            => Assert.AreEqual(9_000, ResearchCost.Effective(10_000, 0.10f, 0.5f, completedBefore: false));

        [Test]
        public void BothDiscounts_StackMultiplicatively()
            // 10,000 x (1 - 0.25) x (1 - 0.2) = 6,000
            => Assert.AreEqual(6_000, ResearchCost.Effective(10_000, 0.25f, 0.2f, completedBefore: true));

        [Test]
        public void Discounts_AreCapped_SoResearchNeverGoesFree()
        {
            Assert.AreEqual(10, ResearchCost.Effective(100, 5f, 0f, completedBefore: false));
            Assert.AreEqual(1,  ResearchCost.Effective(1, 0.9f, 0.9f, completedBefore: true));
        }

        [Test]
        public void LargeCosts_StayExact()
            => Assert.AreEqual(3_345_000_000L, ResearchCost.Effective(4_460_000_000L, 0.25f, 0f, false));

        [Test]
        public void ZeroCost_StaysZero()
            => Assert.AreEqual(0, ResearchCost.Effective(0, 0.25f, 0.25f, true));

        // ── Remembering research finished on an earlier run (legacy saves) ─────────

        static RecipeJson Recipe(string id, string research) => new RecipeJson { id = id, requires_research = research };

        [Test]
        public void AllGatedRecipesKnown_TrueOnlyWhenEveryGatedRecipeIsKnown()
        {
            var recipes = new List<RecipeJson> { Recipe("he4", "fusion_i"), Recipe("c12", "fusion_i"), Recipe("o16", "fusion_ii") };
            var known = new HashSet<string> { "he4", "c12" };

            Assert.IsTrue(ResearchService.AllGatedRecipesKnown(recipes, "fusion_i", known.Contains));
            Assert.IsFalse(ResearchService.AllGatedRecipesKnown(recipes, "fusion_ii", known.Contains));
        }

        [Test]
        public void AllGatedRecipesKnown_FalseForResearchThatGatesNoRecipe()
        {
            // Nothing to infer from: such research is remembered only once finished on a build with the list.
            var recipes = new List<RecipeJson> { Recipe("he4", "fusion_i") };
            Assert.IsFalse(ResearchService.AllGatedRecipesKnown(recipes, "transportation_1", _ => true));
        }

        [Test]
        public void ResearchCompletedEver_SurvivesASaveRoundTrip_AndLegacyLoadsEmpty()
        {
            var save = new SaveData();
            save.researchCompletedEver.Add("fusion_i");
            var loaded = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(save));
            CollectionAssert.AreEqual(new[] { "fusion_i" }, loaded.researchCompletedEver);

            var legacy = UnityEngine.JsonUtility.FromJson<SaveData>("{\"playerId\":\"legacy\"}");
            Assert.IsNotNull(legacy.researchCompletedEver);
            Assert.IsEmpty(legacy.researchCompletedEver);
        }
    }
}
