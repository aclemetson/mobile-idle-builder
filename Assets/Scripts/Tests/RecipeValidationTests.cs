using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Validates recipes.json on every CI build.
    /// Recipes that intentionally deviate from real-world science must have an
    /// "override_reason" field documented in the JSON.
    /// </summary>
    [TestFixture]
    public class RecipeValidationTests
    {
        private RecipeListJson _data;

        [OneTimeSetUp]
        public void LoadRecipes()
        {
            string path = Path.Combine(Application.dataPath, "Data", "recipes.json");
            Assert.IsTrue(File.Exists(path), $"recipes.json not found at: {path}");
            _data = JsonUtility.FromJson<RecipeListJson>(File.ReadAllText(path));
            Assert.IsNotNull(_data, "Failed to deserialize recipes.json");
            Assert.IsNotNull(_data.recipes, "recipes array is null");
        }

        // ── Structural integrity ────────────────────────────────────────────

        [Test]
        public void AllRecipes_HavePositiveIds()
        {
            foreach (var r in _data.recipes)
                Assert.Greater(r.recipeId, 0, $"Recipe '{r.recipeName}' has recipeId <= 0");
        }

        [Test]
        public void AllRecipes_HaveUniqueIds()
        {
            var ids = _data.recipes.Select(r => r.recipeId).ToList();
            var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key);
            Assert.IsEmpty(duplicates, $"Duplicate recipe IDs: {string.Join(", ", duplicates)}");
        }

        [Test]
        public void AllRecipes_HaveAtLeastOneInput()
        {
            foreach (var r in _data.recipes)
                Assert.IsNotEmpty(r.inputs, $"Recipe '{r.recipeName}' (id {r.recipeId}) has no inputs");
        }

        [Test]
        public void AllRecipes_HavePositiveOutputItemId()
        {
            foreach (var r in _data.recipes)
                Assert.Greater(r.outputItemId, 0,
                    $"Recipe '{r.recipeName}' (id {r.recipeId}) has invalid outputItemId");
        }

        [Test]
        public void AllRecipes_HavePositiveCraftTime()
        {
            foreach (var r in _data.recipes)
                Assert.Greater(r.craftTime, 0f,
                    $"Recipe '{r.recipeName}' (id {r.recipeId}) has craftTime <= 0");
        }

        // ── Science correctness (Tier 0–2) ─────────────────────────────────

        [Test]
        public void Recipe_Proton_Is2UpQuarks1DownQuark()
        {
            // Real: proton = uud (2 up, 1 down)
            var r = RequireRecipe(1, "Proton");
            RequireInput(r, itemId: 1, quantity: 2, label: "up quark");
            RequireInput(r, itemId: 2, quantity: 1, label: "down quark");
            Assert.AreEqual(4, r.outputItemId, "Proton recipe output should be item 4 (proton)");
        }

        [Test]
        public void Recipe_Neutron_Is1UpQuark2DownQuarks()
        {
            // Real: neutron = udd (1 up, 2 down)
            var r = RequireRecipe(2, "Neutron");
            RequireInput(r, itemId: 1, quantity: 1, label: "up quark");
            RequireInput(r, itemId: 2, quantity: 2, label: "down quark");
            Assert.AreEqual(5, r.outputItemId, "Neutron recipe output should be item 5 (neutron)");
        }

        [Test]
        public void Recipe_Hydrogen_Is1Proton1Electron()
        {
            // Real: hydrogen-1 = 1 proton + 1 electron (no neutrons)
            var r = RequireRecipe(3, "Hydrogen");
            RequireInput(r, itemId: 4, quantity: 1, label: "proton");
            RequireInput(r, itemId: 3, quantity: 1, label: "electron");
            Assert.AreEqual(6, r.outputItemId, "Hydrogen recipe output should be item 6 (hydrogen)");
        }

        [Test]
        public void Recipe_Helium4_Is2Protons2Neutrons2Electrons()
        {
            // Real: He-4 nucleus = 2 protons + 2 neutrons; atom adds 2 electrons
            var r = RequireRecipe(4, "Helium-4");
            RequireInput(r, itemId: 4, quantity: 2, label: "proton");
            RequireInput(r, itemId: 5, quantity: 2, label: "neutron");
            RequireInput(r, itemId: 3, quantity: 2, label: "electron");
            Assert.AreEqual(7, r.outputItemId, "Helium-4 recipe output should be item 7 (helium-4)");
        }

        [Test]
        public void Recipe_Helium4_RequiresBuilding()
        {
            var r = RequireRecipe(4, "Helium-4");
            Assert.IsTrue(r.requiresBuilding, "Helium-4 should require a building (Tier 2 recipe)");
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private RecipeJson RequireRecipe(int id, string name)
        {
            var r = _data.recipes.FirstOrDefault(x => x.recipeId == id);
            Assert.IsNotNull(r, $"Recipe '{name}' (id {id}) not found in recipes.json");
            return r;
        }

        private static void RequireInput(RecipeJson recipe, int itemId, int quantity, string label)
        {
            var match = recipe.inputs?.FirstOrDefault(i => i.itemId == itemId);
            Assert.IsNotNull(match,
                $"Recipe '{recipe.recipeName}': expected input '{label}' (itemId {itemId}) not found");
            Assert.AreEqual(quantity, match.quantity,
                $"Recipe '{recipe.recipeName}': input '{label}' quantity should be {quantity}, got {match.quantity}");
        }
    }
}
