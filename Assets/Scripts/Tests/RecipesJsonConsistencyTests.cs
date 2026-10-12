using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// recipes.json drives the Recipes panel: a recipe shows only once ResearchService marks it known,
    /// which it does for recipes whose requires_research matches the research just finished. Helium
    /// through iron had no requires_research, so Atomic Assembly never revealed them. These tests keep
    /// recipes.json in step with game_data.json, the source of truth for recipe gating.
    /// </summary>
    [TestFixture]
    public class RecipesJsonConsistencyTests
    {
        [System.Serializable] class GdItem   { public string id; }
        [System.Serializable] class GdRecipe { public string id; public string output_item; public string required_research; }
        [System.Serializable] class GameData { public List<GdItem> items; public List<GdRecipe> recipes; }

        GameData         _gd;
        List<RecipeJson> _recipes;

        [OneTimeSetUp]
        public void Load()
        {
            _gd      = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _recipes = RecipeDatabase.ParseValidRecipes(
                JsonUtility.FromJson<RecipeListJson>(File.ReadAllText("Assets/Data/recipes.json")).recipes);
        }

        [Test]
        public void EveryRecipeOutput_IsARealItem()
        {
            var ids = new HashSet<string>(_gd.items.Select(i => i.id));
            var bad = _recipes.Where(r => !ids.Contains(r.output.id)).Select(r => $"{r.id} -> {r.output.id}").ToList();
            CollectionAssert.IsEmpty(bad, "recipes.json outputs that are not items in game_data.json");
        }

        [Test]
        public void ResearchGate_MatchesGameData()
        {
            var bad = new List<string>();
            foreach (var r in _recipes)
            {
                var gates = _gd.recipes.Where(g => g.output_item == r.output.id)
                                       .Select(g => string.IsNullOrEmpty(g.required_research) ? null : g.required_research)
                                       .ToList();
                if (gates.Count == 0) continue;
                string mine = string.IsNullOrEmpty(r.requires_research) ? null : r.requires_research;
                if (!gates.Contains(mine)) bad.Add($"{r.id}: '{mine}' vs game_data {string.Join("/", gates)}");
            }
            CollectionAssert.IsEmpty(bad, "recipes.json requires_research disagrees with game_data.json");
        }

        [Test]
        public void AtomicAssembly_RevealsTheFirstHeavierElements()
        {
            var ids = ResearchService.RecipesGatedBy(_recipes, "atomic_assembly").ToList();
            CollectionAssert.IsSupersetOf(ids, new[] { "helium", "lithium", "carbon", "oxygen", "silicon", "iron" });
        }
    }
}
