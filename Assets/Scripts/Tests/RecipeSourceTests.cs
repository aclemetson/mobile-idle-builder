using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// game_data.json is the only recipe source (#287, #336 phase B): the importer writes RecipeSO assets and
    /// Resources/RecipeDatabase.asset, and RecipeDatabase converts them to the RecipeJson runtime view.
    /// Replaces the old recipes.json consistency tests.
    /// </summary>
    [TestFixture]
    public class RecipeSourceTests
    {
        [System.Serializable] class GdIngredient { public string item; public int quantity; }
        [System.Serializable] class GdRecipe
        {
            public string id;
            public string output_item;
            public string required_research;
            public bool   can_craft_manually;
            public List<GdIngredient> inputs = new();
        }
        [System.Serializable] class GdItem { public string id; }
        [System.Serializable] class GameData { public List<GdItem> items = new(); public List<GdRecipe> recipes = new(); }

        GameData         _gd;
        List<RecipeJson> _runtime;

        [OneTimeSetUp]
        public void Load()
        {
            _gd      = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _runtime = RecipeTestData.LoadRuntimeRecipes();
        }

        static List<(string id, int qty)> Real(IEnumerable<GdIngredient> src) =>
            src.Where(i => i.quantity > 0).Select(i => (i.item, i.quantity)).OrderBy(t => t.item).ToList();

        [Test]
        public void EveryGameDataRecipeWithInputs_IsInTheRuntimeList()
        {
            var runtimeIds = new HashSet<string>(_runtime.Select(r => r.id));
            var missing = _gd.recipes
                .Where(g => g.inputs.Any(i => i.quantity > 0) && !string.IsNullOrEmpty(g.output_item))
                .Where(g => !runtimeIds.Contains(g.id))
                .Select(g => g.id).ToList();
            CollectionAssert.IsEmpty(missing, "game_data.json recipes missing from RecipeDatabase.asset (re-import?)");
        }

        [Test]
        public void EveryManualRecipe_MatchesGameData_AndIsCraftableFromTheMenu()
        {
            var bad = new List<string>();
            foreach (var g in _gd.recipes.Where(g => g.can_craft_manually && g.inputs.Any(i => i.quantity > 0)))
            {
                var r = _runtime.FirstOrDefault(x => x.id == g.id);
                if (r == null) { bad.Add($"{g.id}: not in the runtime list"); continue; }
                if (r.output.id != g.output_item) bad.Add($"{g.id}: output '{r.output.id}' vs '{g.output_item}'");
                var mine = r.inputs.Select(i => (i.id, i.quantity)).OrderBy(t => t.id).ToList();
                if (!mine.SequenceEqual(Real(g.inputs))) bad.Add($"{g.id}: inputs differ");
                if (r.requiresBuilding) bad.Add($"{g.id}: should be craftable from the menu (no inventory_menu in crafted_in)");
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void EveryRuntimeRecipeOutput_IsARealItem()
        {
            var ids = new HashSet<string>(_gd.items.Select(i => i.id));
            var bad = _runtime.Where(r => !ids.Contains(r.output.id)).Select(r => $"{r.id} -> {r.output.id}").ToList();
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void ResearchGate_MatchesGameData()
        {
            var bad = new List<string>();
            foreach (var r in _runtime)
            {
                var g = _gd.recipes.First(x => x.id == r.id);
                string want = string.IsNullOrEmpty(g.required_research) ? null : g.required_research;
                string mine = string.IsNullOrEmpty(r.requires_research) ? null : r.requires_research;
                if (want != mine) bad.Add($"{r.id}: '{mine}' vs game_data '{want}'");
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void Helium4_HasThreeMethods_InOneGroup()
        {
            var group = RecipeGrouping.Group(_runtime).First(g => g.outputId == "helium_4");
            CollectionAssert.AreEquivalent(new[] { "helium_4", "fuse_helium", "fuse_deuterium" },
                group.methods.Select(m => m.id).ToList());
        }

        // ---- Legacy ids ----

        [Test]
        public void LegacyIds_EveryTargetExistsInGameData()
        {
            var ids = new HashSet<string>(_gd.recipes.Select(r => r.id));
            foreach (var kv in LegacyRecipeIds.Map)
            {
                Assert.IsTrue(ids.Contains(kv.Value), $"legacy '{kv.Key}' maps to '{kv.Value}', which is not a game_data.json recipe");
                Assert.IsFalse(ids.Contains(kv.Key), $"'{kv.Key}' is mapped but still exists in game_data.json");
            }
        }

        [Test]
        public void LegacyKnowledge_RenamesTheEntry()
        {
            var save = new RecipeKnowledgeSave();
            save.entries.Add(new RecipeKnowledgeEntry { id = "helium", previous_research = true });
            save.entries.Add(new RecipeKnowledgeEntry { id = "proton", previous_research = true });

            Assert.IsTrue(LegacyRecipeIds.Migrate(save));

            CollectionAssert.AreEquivalent(new[] { "helium_4", "proton" }, save.entries.Select(e => e.id).ToList());
            Assert.IsTrue(save.entries.First(e => e.id == "helium_4").previous_research);
            Assert.IsFalse(LegacyRecipeIds.Migrate(save), "a second pass changes nothing");
        }

        [Test]
        public void LegacyKnowledge_MergesWithAnExistingNewEntry()
        {
            var save = new RecipeKnowledgeSave();
            save.entries.Add(new RecipeKnowledgeEntry { id = "helium_4", previous_research = false });
            save.entries.Add(new RecipeKnowledgeEntry { id = "helium", previous_research = true });

            LegacyRecipeIds.Migrate(save);

            Assert.AreEqual(1, save.entries.Count);
            Assert.AreEqual("helium_4", save.entries[0].id);
            Assert.IsTrue(save.entries[0].previous_research, "known if either entry was known");
        }

        [Test]
        public void LegacyCraftQueue_RewritesRecipeIds()
        {
            var q = new CraftQueueSaveData();
            q.entries.Add(new CraftQueueEntry { recipeId = "helium", count = 3, unitSeconds = 4f });
            q.entries.Add(new CraftQueueEntry { recipeId = "proton", count = 1, unitSeconds = 1f });

            Assert.IsTrue(LegacyRecipeIds.Migrate(q));

            Assert.AreEqual("helium_4", q.entries[0].recipeId);
            Assert.AreEqual(3, q.entries[0].count);
            Assert.AreEqual("proton", q.entries[1].recipeId);
        }

        [Test]
        public void Resolve_LeavesCurrentIdsAlone()
        {
            Assert.AreEqual("helium_4", LegacyRecipeIds.Resolve("helium"));
            Assert.AreEqual("proton", LegacyRecipeIds.Resolve("proton"));
            Assert.IsNull(LegacyRecipeIds.Resolve(null));
        }
    }

    /// <summary>The RecipeSO to RecipeJson conversion, built from in-memory assets.</summary>
    [TestFixture]
    public class RecipeConversionTests
    {
        readonly List<Object> _made = new();

        T Make<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _made.Add(o);
            return o;
        }

        ItemSO Item(string id) { var i = Make<ItemSO>(); i.id = id; return i; }
        BuildingSO Building(string id) { var b = Make<BuildingSO>(); b.id = id; return b; }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        RecipeSO Recipe(bool manual, params string[] buildings)
        {
            var so = Make<RecipeSO>();
            so.id = "r";
            so.displayName = "Helium-4";
            so.methodLabel = "p-p chain";
            so.tier = 2;
            so.inputs = new[] { new RecipeIngredient { item = Item("hydrogen"), quantity = 4 } };
            so.outputItem = Item("helium_4");
            so.outputQuantity = 1;
            so.byproducts = new[] { new RecipeIngredient { item = Item("neutrino"), quantity = 2 } };
            so.baseCraftTime = 12f;
            so.canCraftManually = manual;
            so.validBuildings = buildings.Select(Building).ToArray();
            so.requiredResearch = Make<ResearchSO>();
            so.requiredResearch.id = "fusion_i";
            return so;
        }

        [Test]
        public void FromSO_CopiesFields()
        {
            var r = RecipeDatabase.FromSO(Recipe(false, "fusion_reactor"));

            Assert.AreEqual("r", r.id);
            Assert.AreEqual("Helium-4", r.name);
            Assert.AreEqual("p-p chain", r.method_label);
            Assert.AreEqual(2, r.tier);
            Assert.AreEqual("helium_4", r.output.id);
            Assert.AreEqual(1, r.output.quantity);
            Assert.AreEqual(1, r.inputs.Count);
            Assert.AreEqual("hydrogen", r.inputs[0].id);
            Assert.AreEqual(4, r.inputs[0].quantity);
            Assert.AreEqual("neutrino", r.byproducts[0].id);
            Assert.AreEqual(12f, r.base_craft_time, 0.001f);
            Assert.AreEqual("fusion_i", r.requires_research);
            Assert.IsNull(r.unlocks_research);
        }

        [Test]
        public void FromSO_BuildingOnlyRecipe_HasNoInventoryMenu()
        {
            var r = RecipeDatabase.FromSO(Recipe(false, "fusion_reactor"));
            CollectionAssert.AreEqual(new[] { "fusion_reactor" }, r.crafted_in);
            Assert.IsTrue(r.requiresBuilding);
        }

        [Test]
        public void FromSO_ManualRecipe_ListsInventoryMenuFirst()
        {
            var r = RecipeDatabase.FromSO(Recipe(true, "strong_force_combiner"));
            CollectionAssert.AreEqual(new[] { "inventory_menu", "strong_force_combiner" }, r.crafted_in);
            Assert.IsFalse(r.requiresBuilding);
        }

        [Test]
        public void FromSO_Null_IsNull() => Assert.IsNull(RecipeDatabase.FromSO(null));

        [Test]
        public void ConvertAll_DropsRecipesWithNoInputs()
        {
            var harvest = Recipe(true);
            harvest.inputs = new RecipeIngredient[0];
            var real = Recipe(true);

            var list = RecipeDatabase.ConvertAll(new[] { harvest, real, null });

            Assert.AreEqual(1, list.Count);
        }
    }

    [TestFixture]
    public class RecipeGroupingTests
    {
        static RecipeJson R(string id, string output, params string[] craftedIn) => new RecipeJson
        {
            id = id,
            output = new RecipeOutputJson { id = output, quantity = 1 },
            inputs = new List<RecipeIngredientJson> { new RecipeIngredientJson { id = "x", quantity = 1 } },
            crafted_in = craftedIn.ToList(),
        };

        [Test]
        public void SingleMethod_IsOneGroupOfOne()
        {
            var groups = RecipeGrouping.Group(new[] { R("proton", "proton", "inventory_menu") });
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual("proton", groups[0].outputId);
            Assert.AreEqual(1, groups[0].methods.Count);
        }

        [Test]
        public void ThreeHelium4Methods_AreOneGroup_InFileOrder()
        {
            var groups = RecipeGrouping.Group(new[]
            {
                R("helium_4", "helium_4", "atomic_assembler"),
                R("proton", "proton", "inventory_menu"),
                R("fuse_helium", "helium_4", "fusion_reactor"),
                R("fuse_deuterium", "helium_4", "fusion_reactor"),
            });

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("helium_4", groups[0].outputId, "groups keep first-appearance order");
            Assert.AreEqual("proton", groups[1].outputId);
            CollectionAssert.AreEqual(new[] { "helium_4", "fuse_helium", "fuse_deuterium" },
                groups[0].methods.Select(m => m.id).ToList());
        }

        [Test]
        public void NullAndOutputlessRecipes_AreSkipped()
        {
            var groups = RecipeGrouping.Group(new RecipeJson[] { null, new RecipeJson { id = "x" }, R("a", "a") });
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(0, RecipeGrouping.Group(null).Count);
        }

        [Test]
        public void PickMethod_PrefersRemembered_ThenFirstUnlockedManual()
        {
            var g = RecipeGrouping.Group(new[]
            {
                R("building_first", "o", "assembler"),
                R("manual", "o", "inventory_menu", "assembler"),
                R("other", "o", "reactor"),
            })[0];

            Assert.AreEqual("manual", RecipeGrouping.PickMethod(g, null, _ => true).id);
            Assert.AreEqual("other", RecipeGrouping.PickMethod(g, "other", _ => true).id);
            Assert.AreEqual("manual", RecipeGrouping.PickMethod(g, "other", m => m.id != "other").id,
                "a remembered method that is now locked falls back");
            Assert.AreEqual("other", RecipeGrouping.PickMethod(g, null, m => m.id == "other").id,
                "only unlocked methods count");
            Assert.AreEqual("building_first", RecipeGrouping.PickMethod(g, null, _ => false).id,
                "all locked: the first method");
        }

        [Test]
        public void MadeInText_NamesTheBuilding_AndIsEmptyForMenuMethods()
        {
            string Name(string id) => id == "fusion_reactor" ? "Fusion Reactor" : "Atomic Assembler";

            Assert.AreEqual("Made in a Fusion Reactor",
                RecipeGrouping.MadeInText(R("a", "o", "fusion_reactor"), Name));
            Assert.AreEqual("Made in an Atomic Assembler or a Fusion Reactor",
                RecipeGrouping.MadeInText(R("a", "o", "atomic_assembler", "fusion_reactor"), Name));
            Assert.AreEqual("", RecipeGrouping.MadeInText(R("a", "o", "inventory_menu", "fusion_reactor"), Name));
        }
    }
}
