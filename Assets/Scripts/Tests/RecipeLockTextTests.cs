using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #490: a recipe known from an earlier run but gated by research now names that research
    /// ("Research Fusion I to unlock") instead of "[Requires research]".
    /// </summary>
    public class RecipeLockTextTests
    {
        [Test]
        public void LockText_NamesTheResearch() =>
            Assert.AreEqual("Research Proton Synthesis to unlock", HUDController.ResearchLockText("Proton Synthesis"));

        [Test]
        public void EveryRecipeGate_IsAResearchWithADisplayName()
        {
            var research = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(research);
            var names = new Dictionary<string, string>();
            foreach (var r in research.allResearch)
                if (r != null && !string.IsNullOrEmpty(r.id)) names[r.id] = r.displayName;

            var recipes = Resources.Load<RecipeDatabaseSO>("RecipeDatabase");
            Assert.IsNotNull(recipes);
            int gated = 0;
            foreach (var recipe in recipes.allRecipes)
            {
                if (recipe == null || recipe.requiredResearch == null) continue;
                gated++;
                Assert.IsTrue(names.ContainsKey(recipe.requiredResearch.id), $"{recipe.id}: gate {recipe.requiredResearch.id} not in the database");
                Assert.IsFalse(string.IsNullOrEmpty(names[recipe.requiredResearch.id]), $"{recipe.requiredResearch.id} has no display name");
            }
            Assert.Greater(gated, 0, "no research-gated recipes found: check the data");
        }
    }
}
