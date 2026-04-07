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
    /// Only real recipe entries are tested — _section markers and byproduct-only
    /// entries (alpha/beta particles) are filtered out during setup.
    /// Recipes that intentionally deviate from real-world science must have a
    /// "simplification" field documented in the JSON.
    /// </summary>
    [TestFixture]
    public class RecipeValidationTests
    {
        private List<RecipeJson> _recipes;

        [OneTimeSetUp]
        public void LoadRecipes()
        {
            string path = Path.Combine(Application.dataPath, "Data", "recipes.json");
            Assert.IsTrue(File.Exists(path), $"recipes.json not found at: {path}");

            var raw = JsonUtility.FromJson<RecipeListJson>(File.ReadAllText(path));
            Assert.IsNotNull(raw,          "Failed to deserialize recipes.json");
            Assert.IsNotNull(raw.recipes,  "recipes array is null");

            _recipes = RecipeDatabase.ParseValidRecipes(raw.recipes);
            Assert.IsNotEmpty(_recipes, "No valid recipes found after filtering");
        }

        // ── Structural integrity ──────────────────────────────────────────────

        [Test]
        public void AllRecipes_HaveNonEmptyId()
        {
            foreach (var r in _recipes)
                Assert.IsFalse(string.IsNullOrEmpty(r.id),
                    $"Recipe '{r.name}' has an empty id");
        }

        [Test]
        public void AllRecipes_HaveUniqueIds()
        {
            var duplicates = _recipes
                .GroupBy(r => r.id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            Assert.IsEmpty(duplicates, $"Duplicate recipe IDs: {string.Join(", ", duplicates)}");
        }

        [Test]
        public void AllRecipes_HaveAtLeastOneInput()
        {
            foreach (var r in _recipes)
                Assert.IsNotEmpty(r.inputs,
                    $"Recipe '{r.name}' (id '{r.id}') has no inputs");
        }

        [Test]
        public void AllRecipes_HaveNonEmptyOutputId()
        {
            foreach (var r in _recipes)
                Assert.IsFalse(string.IsNullOrEmpty(r.output?.id),
                    $"Recipe '{r.name}' (id '{r.id}') has no output id");
        }

        [Test]
        public void AllRecipes_HavePositiveCraftTime()
        {
            foreach (var r in _recipes)
                Assert.Greater(r.base_craft_time, 0f,
                    $"Recipe '{r.name}' (id '{r.id}') has base_craft_time <= 0");
        }

        // ── Science correctness (Tier 1 — Subatomic) ─────────────────────────

        [Test]
        public void Recipe_Proton_Is2UpQuarks1DownQuark()
        {
            // Real: proton = uud (2 up quarks, 1 down quark)
            var r = RequireRecipe("proton");
            RequireInput(r, "up_quark",   quantity: 2);
            RequireInput(r, "down_quark", quantity: 1);
            Assert.AreEqual("proton", r.output.id, "Proton recipe output should be 'proton'");
        }

        [Test]
        public void Recipe_Neutron_Is1UpQuark2DownQuarks()
        {
            // Real: neutron = udd (1 up quark, 2 down quarks)
            var r = RequireRecipe("neutron");
            RequireInput(r, "up_quark",   quantity: 1);
            RequireInput(r, "down_quark", quantity: 2);
            Assert.AreEqual("neutron", r.output.id, "Neutron recipe output should be 'neutron'");
        }

        // ── Science correctness (Tier 2 — Atomic) ────────────────────────────

        [Test]
        public void Recipe_Hydrogen_Is1Proton1Electron()
        {
            // Real: hydrogen-1 = 1 proton + 1 electron (no neutrons in protium)
            var r = RequireRecipe("hydrogen");
            RequireInput(r, "proton",   quantity: 1);
            RequireInput(r, "electron", quantity: 1);
            Assert.AreEqual("hydrogen", r.output.id);
        }

        [Test]
        public void Recipe_Helium_Is2Protons2Neutrons2Electrons()
        {
            // Real: He-4 = 2 protons + 2 neutrons + 2 electrons
            var r = RequireRecipe("helium");
            RequireInput(r, "proton",   quantity: 2);
            RequireInput(r, "neutron",  quantity: 2);
            RequireInput(r, "electron", quantity: 2);
            Assert.AreEqual("helium", r.output.id);
        }

        [Test]
        public void Recipe_Helium_RequiresBuilding()
        {
            var r = RequireRecipe("helium");
            Assert.IsTrue(r.requiresBuilding,
                "Helium should require a building — it is not craftable in the inventory menu");
        }

        [Test]
        public void Recipe_Deuterium_IsHydrogenPlus1Neutron()
        {
            var r = RequireRecipe("deuterium");
            RequireInput(r, "hydrogen", quantity: 1);
            RequireInput(r, "neutron",  quantity: 1);
            Assert.AreEqual("deuterium", r.output.id);
        }

        [Test]
        public void Recipe_Tritium_IsHydrogenPlus2Neutrons()
        {
            var r = RequireRecipe("tritium");
            RequireInput(r, "hydrogen", quantity: 1);
            RequireInput(r, "neutron",  quantity: 2);
            Assert.AreEqual("tritium", r.output.id);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private RecipeJson RequireRecipe(string id)
        {
            var r = _recipes.FirstOrDefault(x => x.id == id);
            Assert.IsNotNull(r, $"Recipe with id '{id}' not found in recipes.json");
            return r;
        }

        private static void RequireInput(RecipeJson recipe, string inputId, int quantity)
        {
            var match = recipe.inputs?.FirstOrDefault(i => i.id == inputId);
            Assert.IsNotNull(match,
                $"Recipe '{recipe.name}': expected input '{inputId}' not found");
            Assert.AreEqual(quantity, match.quantity,
                $"Recipe '{recipe.name}': input '{inputId}' quantity should be {quantity}, got {match.quantity}");
        }
    }
}
