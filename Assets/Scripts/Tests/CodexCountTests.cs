using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Tests for RecipeKnowledgeService.CountCodexEntries, which feeds
    /// AchievementService.NotifyCodexUnlocked (prog_codex_10 / prog_codex_50).
    ///
    /// The rule under test is "distinct OUTPUT ITEMS across known recipes", matching what
    /// HUDController.BuildCodexList actually renders. A known-recipe count would over-report,
    /// completing "Unlock 10 codex entries" while the panel still shows fewer than 10 rows.
    /// </summary>
    public class CodexCountTests
    {
        // Minimal stub — the real service needs a MonoBehaviour and a file on disk.
        sealed class KnowledgeStub : IRecipeKnowledgeService
        {
            readonly HashSet<string> _known;
            public KnowledgeStub(params string[] known) => _known = new HashSet<string>(known);
            public bool IsKnown(string recipeId) => _known.Contains(recipeId);
            public void MarkKnown(string recipeId) => _known.Add(recipeId);
        }

        readonly List<ItemSO> _created = new();

        ItemSO MakeItem(string id, int itemId)
        {
            var item = ScriptableObject.CreateInstance<ItemSO>();
            item.id     = id;
            item.itemId = itemId;
            _created.Add(item);
            return item;
        }

        static RecipeJson Recipe(string id, string outputItemId) => new RecipeJson
        {
            id     = id,
            output = new RecipeOutputJson { id = outputItemId },
        };

        [TearDown]
        public void TearDown()
        {
            ItemDatabase.InjectForTesting(new ItemSO[0]);
            foreach (var i in _created) if (i != null) Object.DestroyImmediate(i);
            _created.Clear();
        }

        [Test]
        public void CountsOnlyKnownRecipes()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("proton", 1), MakeItem("neutron", 2) });
            var recipes = new List<RecipeJson> { Recipe("r_proton", "proton"), Recipe("r_neutron", "neutron") };

            int count = RecipeKnowledgeService.CountCodexEntries(recipes, new KnowledgeStub("r_proton"));

            Assert.AreEqual(1, count, "An unknown recipe must not contribute a codex entry");
        }

        [Test]
        public void DedupesRecipesSharingAnOutputItem()
        {
            // Two ways to make hydrogen — the codex shows one row, so this must count once.
            ItemDatabase.InjectForTesting(new[] { MakeItem("hydrogen", 7) });
            var recipes = new List<RecipeJson>
            {
                Recipe("r_hydrogen_fuse",   "hydrogen"),
                Recipe("r_hydrogen_combine","hydrogen"),
            };

            int count = RecipeKnowledgeService.CountCodexEntries(
                recipes, new KnowledgeStub("r_hydrogen_fuse", "r_hydrogen_combine"));

            Assert.AreEqual(1, count,
                "Distinct output items, not known recipes — two recipes for one item is one entry");
        }

        [Test]
        public void CountsDistinctItemsAcrossKnownRecipes()
        {
            ItemDatabase.InjectForTesting(new[]
            {
                MakeItem("proton", 1), MakeItem("neutron", 2), MakeItem("hydrogen", 7),
            });
            var recipes = new List<RecipeJson>
            {
                Recipe("r_proton",   "proton"),
                Recipe("r_neutron",  "neutron"),
                Recipe("r_hydrogen", "hydrogen"),
            };

            int count = RecipeKnowledgeService.CountCodexEntries(
                recipes, new KnowledgeStub("r_proton", "r_neutron", "r_hydrogen"));

            Assert.AreEqual(3, count);
        }

        [Test]
        public void SkipsRecipesWhoseOutputItemIsMissing()
        {
            // Only "proton" is registered; the electron recipe points at an absent item.
            ItemDatabase.InjectForTesting(new[] { MakeItem("proton", 1) });
            var recipes = new List<RecipeJson> { Recipe("r_proton", "proton"), Recipe("r_electron", "electron") };

            int count = RecipeKnowledgeService.CountCodexEntries(
                recipes, new KnowledgeStub("r_proton", "r_electron"));

            Assert.AreEqual(1, count, "A recipe with an unresolvable output must not be counted");
        }

        [Test]
        public void HandlesNullOutputAndNullRecipeEntries()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("proton", 1) });
            var recipes = new List<RecipeJson>
            {
                Recipe("r_proton", "proton"),
                new RecipeJson { id = "r_broken", output = null },
                null,
            };

            int count = RecipeKnowledgeService.CountCodexEntries(
                recipes, new KnowledgeStub("r_proton", "r_broken"));

            Assert.AreEqual(1, count);
        }

        [Test]
        public void ReturnsZeroOnNullInputs()
        {
            Assert.AreEqual(0, RecipeKnowledgeService.CountCodexEntries(null, new KnowledgeStub()));
            Assert.AreEqual(0, RecipeKnowledgeService.CountCodexEntries(new List<RecipeJson>(), null));
        }

        [Test]
        public void NothingKnown_IsZero()
        {
            ItemDatabase.InjectForTesting(new[] { MakeItem("proton", 1) });
            var recipes = new List<RecipeJson> { Recipe("r_proton", "proton") };

            Assert.AreEqual(0, RecipeKnowledgeService.CountCodexEntries(recipes, new KnowledgeStub()));
        }
    }
}
