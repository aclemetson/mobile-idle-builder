using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #322 phase 3: Neutron Capture. Slow (s-process) climbs one element at a time; Fast (r-process) is the only
    /// route to the elements neutron-star mergers make. Fast comes later, costs more power and takes longer.
    /// </summary>
    [TestFixture]
    public class NeutronCaptureTests
    {
        [Serializable] class GdIng      { public string item; public int quantity; }
        [Serializable] class GdItem     { public string id; public string category; public int atomic_number; }
        [Serializable] class GdRecipe   { public string id; public string output_item; public List<GdIng> inputs; public List<string> valid_buildings; public string required_research; public float base_craft_time; public float fixed_power_cost_ev; }
        [Serializable] class GdBuilding { public string id; public bool power_cost_is_dynamic; public List<string> recipe_mode_prefixes; public List<string> recipe_mode_labels; }
        [Serializable] class GdResearch { public string id; public List<string> prerequisites; }
        [Serializable] class GameData   { public List<GdItem> items; public List<GdRecipe> recipes; public List<GdBuilding> buildings; public List<GdResearch> research; }

        static readonly string[] ROnly = { "europium", "osmium", "iridium", "platinum", "gold" };

        GameData _gd;
        Dictionary<string, GdItem> _items;
        List<GdRecipe> _slow, _fast;

        [OneTimeSetUp]
        public void Load()
        {
            _gd    = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items = _gd.items.ToDictionary(i => i.id);
            var capture = _gd.recipes.Where(r => r.valid_buildings.Contains("neutron_capture")).ToList();
            _slow = capture.Where(r => r.id.StartsWith("s_capture_")).ToList();
            _fast = capture.Where(r => r.id.StartsWith("r_capture_")).ToList();
        }

        [Test]
        public void EveryCapture_MakesTheNextElementUp()
        {
            foreach (var r in _slow.Concat(_fast))
            {
                var src = _items[r.inputs.Single(i => i.item != "neutron").item];
                Assert.AreEqual(src.atomic_number + 1, _items[r.output_item].atomic_number, r.id);
                Assert.Greater(r.inputs.Single(i => i.item == "neutron").quantity, 0, r.id);
            }
        }

        [Test]
        public void FastIsTheOnlyRouteToTheRProcessElements()
        {
            CollectionAssert.AreEquivalent(ROnly, _fast.Select(r => r.output_item));
            foreach (var el in ROnly)
                Assert.IsFalse(_slow.Any(r => r.output_item == el), $"{el} must not be reachable by slow capture");
        }

        [Test]
        public void Fast_ComesLater_TakesLonger_AndDrawsMorePower()
        {
            var rp = _gd.research.Single(r => r.id == "rapid_neutron_capture");
            CollectionAssert.Contains(rp.prerequisites, "slow_neutron_capture");
            // Capture groups are gated by the old element-group nodes (#322 phase 4); fast ones must still
            // sit behind the r-Process.
            foreach (var r in _fast)
                Assert.IsTrue(Requires(r.required_research, "rapid_neutron_capture"), $"{r.id} ({r.required_research})");

            Assert.Greater(_fast.Min(r => r.base_craft_time), _slow.Max(r => r.base_craft_time));
            Assert.Greater(_fast.Min(r => r.fixed_power_cost_ev), _slow.Max(r => r.fixed_power_cost_ev));

            var nc = _gd.buildings.Single(b => b.id == "neutron_capture");
            Assert.IsTrue(nc.power_cost_is_dynamic, "the building's draw follows its recipe");
            CollectionAssert.AreEqual(new[] { "s_capture_", "r_capture_" }, nc.recipe_mode_prefixes);
            Assert.AreEqual(2, nc.recipe_mode_labels.Count);
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
