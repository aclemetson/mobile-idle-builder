using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The descent (Item Timeline, 2026-11-23). Fusion climbs to Manganese; Uranium is mined and decays to
    /// Lead and Thallium; the Spallation Chamber (the old Neutron Capture building, id kept for saves) knocks
    /// one proton off at a time from Mercury down to Cobalt; Iron joins Manganese and Cobalt.
    /// </summary>
    [TestFixture]
    public class NeutronCaptureTests
    {
        [Serializable] class GdIng      { public string item; public int quantity; }
        [Serializable] class GdItem     { public string id; public string category; public int atomic_number; }
        [Serializable] class GdRecipe   { public string id; public string output_item; public List<GdIng> inputs; public List<string> valid_buildings; public string required_research; }
        [Serializable] class GdBuilding { public string id; public string display_name; public string required_research; public List<string> supported_recipes; public List<string> recipe_mode_prefixes; }
        [Serializable] class GdResearch { public string id; public List<string> prerequisites; public List<string> unlocks_items; }
        [Serializable] class GameData   { public List<GdItem> items; public List<GdRecipe> recipes; public List<GdBuilding> buildings; public List<GdResearch> research; }

        GameData _gd;
        Dictionary<string, GdItem> _items;
        List<GdRecipe> _spall;

        [OneTimeSetUp]
        public void Load()
        {
            _gd    = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items = _gd.items.ToDictionary(i => i.id);
            _spall = _gd.recipes.Where(r => r.valid_buildings.Contains("neutron_capture")).ToList();
        }

        [Test]
        public void EverySpallation_MakesTheNextElementDown_FromMercuryToCobalt()
        {
            CollectionAssert.AreEquivalent(Enumerable.Range(27, 54), _spall.Select(r => _items[r.output_item].atomic_number),
                "one step for every element from Cobalt (27) to Mercury (80)");
            foreach (var r in _spall)
            {
                var src = _items[r.inputs.Single(i => i.item != "neutron").item];
                Assert.AreEqual(src.atomic_number - 1, _items[r.output_item].atomic_number, r.id);
                Assert.AreEqual(1, r.inputs.Single(i => i.item == "neutron").quantity, r.id);
            }
        }

        [Test]
        public void NoRecipeClimbsAboveIron_ExceptTheTutorialAndTheTransuranics()
        {
            // The old s/r-process captures climbed from Iron to Gold; they would lose value under the descent's
            // prices (Iron is now worth more than Cobalt), so they are gone.
            Assert.IsFalse(_gd.recipes.Any(r => r.id.StartsWith("s_capture_") || r.id.StartsWith("r_capture_")));
            Assert.IsFalse(_gd.recipes.Any(r => r.id == "fuse_iron"), "Iron comes from both ends now");
        }

        [Test]
        public void Iron_JoinsManganeseAndCobalt()
        {
            var iron = _gd.recipes.Single(r => r.output_item == "iron" && r.valid_buildings.Contains("fusion_reactor"));
            CollectionAssert.AreEquivalent(new[] { "manganese", "cobalt" }, iron.inputs.Select(i => i.item));
            Assert.AreEqual("iron_convergence", iron.required_research);
        }

        [Test]
        public void Uranium_IsMined_AfterManganese()
        {
            Assert.IsFalse(_gd.recipes.Any(r => r.output_item == "uranium" && r.inputs.Count > 0), "no assembled Uranium");
            var mine = _gd.recipes.Single(r => r.id == "collect_uranium");
            Assert.AreEqual("heavy_elements", mine.required_research);
            Assert.IsTrue(Requires("heavy_elements", "silicon_burning"), "the descent starts after fusion reaches Manganese");
            Assert.IsTrue(Requires("spallation_1", "decay_branches"), "spallation starts below Thallium");
            Assert.IsTrue(Requires("iron_convergence", "spallation_11"), "Iron is last");
        }

        [Test]
        public void SpallationChamber_IsTheOldNeutronCaptureBuilding_WithOneMode()
        {
            var nc = _gd.buildings.Single(b => b.id == "neutron_capture");
            Assert.AreEqual("Spallation Chamber", nc.display_name);
            Assert.AreEqual("spallation_1", nc.required_research);
            CollectionAssert.AreEquivalent(_spall.Select(r => r.id), nc.supported_recipes);
            Assert.IsTrue(nc.recipe_mode_prefixes == null || nc.recipe_mode_prefixes.Count == 0, "no Slow / Fast modes");
        }

        bool Requires(string research, string ancestor)
        {
            var byId  = _gd.research.ToDictionary(r => r.id);
            var stack = new Stack<string>(new[] { research });
            var seen  = new HashSet<string>();
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id == ancestor) return true;
                if (id == null || !seen.Add(id) || !byId.TryGetValue(id, out var r) || r.prerequisites == null) continue;
                foreach (var p in r.prerequisites) stack.Push(p);
            }
            return false;
        }

        [Test]
        public void PowerDrawFor_UsesTheRecipeWhenHigher_OnlyForDynamicBuildings()
        {
            var b = ScriptableObject.CreateInstance<BuildingSO>();
            var slow = ScriptableObject.CreateInstance<RecipeSO>();
            var fast = ScriptableObject.CreateInstance<RecipeSO>();
            try
            {
                b.basePowerCostEV = 40f;
                slow.fixedPowerCostEV = 40f;
                fast.fixedPowerCostEV = 180f;

                Assert.AreEqual(40f, BuildingSO.PowerDrawFor(b, 1, fast), "static buildings ignore the recipe");
                b.powerCostIsDynamic = true;
                Assert.AreEqual(40f,  BuildingSO.PowerDrawFor(b, 1, slow));
                Assert.AreEqual(180f, BuildingSO.PowerDrawFor(b, 1, fast));
                Assert.AreEqual(40f,  BuildingSO.PowerDrawFor(b, 1, null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(b);
                UnityEngine.Object.DestroyImmediate(slow);
                UnityEngine.Object.DestroyImmediate(fast);
            }
        }

        [Test]
        public void RecipeModeOf_MatchesByPrefix()
        {
            var b = ScriptableObject.CreateInstance<BuildingSO>();
            try
            {
                b.recipeModePrefixes = new[] { "s_capture_", "r_capture_" };
                Assert.AreEqual(0, b.RecipeModeOf("s_capture_cobalt"));
                Assert.AreEqual(1, b.RecipeModeOf("r_capture_gold"));
                Assert.AreEqual(-1, b.RecipeModeOf("fuse_carbon"));
                Assert.AreEqual(-1, b.RecipeModeOf(null));
            }
            finally { UnityEngine.Object.DestroyImmediate(b); }
        }
    }
}
