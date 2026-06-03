using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests that verify baseCraftTime values for key recipes across all
    /// 5 tiers, and confirm ingredient counts for tutorial-critical recipes.
    ///
    /// Source of truth: Assets/Data/recipes.json.
    /// If a test fails, check the JSON entry for the recipe and re-import via
    /// 'MobileIdleBuilder > Import Game Data' if needed.
    /// </summary>
    [TestFixture]
    public class RecipeTimingTests
    {
        private List<RecipeJson> _recipes;

        [OneTimeSetUp]
        public void LoadRecipes()
        {
            string path = Path.Combine(Application.dataPath, "Data", "recipes.json");
            Assert.IsTrue(File.Exists(path), $"recipes.json not found at: {path}");

            var raw = JsonUtility.FromJson<RecipeListJson>(File.ReadAllText(path));
            Assert.IsNotNull(raw,         "Failed to deserialize recipes.json");
            Assert.IsNotNull(raw.recipes, "recipes array is null in recipes.json");

            _recipes = RecipeDatabase.ParseValidRecipes(raw.recipes);
            Assert.IsNotEmpty(_recipes, "No valid recipes after filtering recipes.json");
        }

        // ── Tier 1 — Subatomic craft times ───────────────────────────────────

        [Test]
        public void Proton_CraftTime_Is1s()
        {
            Assert.AreEqual(1f, RequireRecipe("proton").base_craft_time, 0.001f);
        }

        [Test]
        public void Neutron_CraftTime_Is1s()
        {
            Assert.AreEqual(1f, RequireRecipe("neutron").base_craft_time, 0.001f);
        }

        // ── Tier 2 — Element craft times (= atomic_mass × 1s for Assembler) ──

        [Test]
        public void Hydrogen_CraftTime_Is2s()
        {
            Assert.AreEqual(2f, RequireRecipe("hydrogen").base_craft_time, 0.001f);
        }

        [Test]
        public void Helium_CraftTime_Is4s()
        {
            Assert.AreEqual(4f, RequireRecipe("helium").base_craft_time, 0.001f);
        }

        [Test]
        public void Iron_CraftTime_Is56s()
        {
            Assert.AreEqual(56f, RequireRecipe("iron").base_craft_time, 0.001f);
        }

        // ── Tier 3 — Molecular craft times ───────────────────────────────────

        [Test]
        public void LiquidHydrogen_CraftTime_Is20s()
        {
            Assert.AreEqual(20f, RequireRecipe("liquid_hydrogen").base_craft_time, 0.001f);
        }

        [Test]
        public void Water_CraftTime_Is60s()
        {
            Assert.AreEqual(60f, RequireRecipe("water").base_craft_time, 0.001f);
        }

        // ── Tier 4 — Materials craft times ───────────────────────────────────

        [Test]
        public void Steel_CraftTime_Is300s()
        {
            Assert.AreEqual(300f, RequireRecipe("steel").base_craft_time, 0.001f);
        }

        // ── Tier 5 — Component craft times ───────────────────────────────────

        [Test]
        public void QuantumProcessor_CraftTime_Is1200s()
        {
            Assert.AreEqual(1200f, RequireRecipe("quantum_processor").base_craft_time, 0.001f);
        }

        // ── Ingredient counts for tutorial-critical recipes ───────────────────

        [Test]
        public void Proton_Has3Inputs_2UpQuark_1DownQuark()
        {
            var r = RequireRecipe("proton");
            RequireInput(r, "up_quark",   quantity: 2);
            RequireInput(r, "down_quark", quantity: 1);
        }

        [Test]
        public void Hydrogen_Has2Inputs_1Proton_1Electron()
        {
            // Neutron input exists in the recipe but with quantity 0 — filter to qty > 0
            var r    = RequireRecipe("hydrogen");
            var real = r.inputs?.Where(i => i.quantity > 0).ToList();
            Assert.AreEqual(2, real?.Count,
                "Hydrogen should have exactly 2 inputs with quantity > 0 (proton, electron)");
            RequireInput(r, "proton",   quantity: 1);
            RequireInput(r, "electron", quantity: 1);
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
                $"Recipe '{recipe.name}': input '{inputId}' should be {quantity}, got {match.quantity}");
        }
    }
}
