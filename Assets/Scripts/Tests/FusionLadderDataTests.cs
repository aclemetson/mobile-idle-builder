using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Atom Generator builds light nuclei only (H, D, He); every element from Lithium to Iron comes from
    /// the Fusion Reactor. When the Atom Generator could build Iron (82 particles, 167M entropy) for a 500e
    /// research, the fusion ladder was pointless. These tests keep game_data.json honest.
    /// </summary>
    [TestFixture]
    public class FusionLadderDataTests
    {
        [System.Serializable] class GdItem     { public string id; public string category; public int atomic_number; public float base_sell_value; }
        [System.Serializable] class GdRecipe   { public string id; public string output_item; public List<string> valid_buildings; public string required_research; }
        [System.Serializable] class GdResearch { public string id; public List<string> prerequisites; }
        [System.Serializable] class GameData   { public List<GdItem> items; public List<GdRecipe> recipes; public List<GdResearch> research; }

        GameData _gd;
        Dictionary<string, GdItem> _items;
        Dictionary<string, GdResearch> _research;

        [OneTimeSetUp]
        public void Load()
        {
            _gd       = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items    = _gd.items.ToDictionary(i => i.id);
            _research = _gd.research.ToDictionary(r => r.id);
        }

        int Z(GdRecipe r) => _items.TryGetValue(r.output_item ?? "", out var i) && i.category == "Element" ? i.atomic_number : 0;
        bool BuiltIn(GdRecipe r, string building) => r.valid_buildings != null && r.valid_buildings.Contains(building);

        /// <summary>True when <paramref name="research"/> is <paramref name="ancestor"/> or requires it, directly or not.</summary>
        bool Requires(string research, string ancestor)
        {
            var seen  = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(research);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id == ancestor) return true;
                if (id == null || !seen.Add(id) || !_research.TryGetValue(id, out var r) || r.prerequisites == null) continue;
                foreach (var p in r.prerequisites) stack.Push(p);
            }
            return false;
        }

        [Test]
        public void AtomGenerator_BuildsNoElementFromLithiumToIron()
        {
            var bad = _gd.recipes.Where(r => BuiltIn(r, "atomic_assembler") && Z(r) >= 3 && Z(r) <= 26)
                                 .Select(r => r.id).ToList();
            CollectionAssert.IsEmpty(bad, "Elements Li..Fe must come from fusion, not the Atom Generator");
        }

        [Test]
        public void EveryElementFromLithiumToIron_HasAFusionRecipe()
        {
            var fused = new HashSet<int>(_gd.recipes.Where(r => BuiltIn(r, "fusion_reactor")).Select(Z));
            var missing = Enumerable.Range(3, 24).Where(z => !fused.Contains(z)).ToList();
            CollectionAssert.IsEmpty(missing, "Atomic numbers with no Fusion Reactor recipe");
        }

        [Test]
        public void EveryFusionRecipe_IsGatedOnOrAfterStellarFusion()
        {
            var bad = _gd.recipes.Where(r => BuiltIn(r, "fusion_reactor") && !Requires(r.required_research, "fusion_i"))
                                 .Select(r => $"{r.id} ({r.required_research})").ToList();
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void AtomGeneratorElementsPastIron_NeedTheWholeStellarLadder()
        {
            // The cheap fission floor (Francium..Uranium, under 200e for 300+ particles) stays early: it
            // feeds the isotope and fission branches and is no shortcut. Anything worth more waits until
            // Explosive Burning has completed every element up to Iron.
            var bad = _gd.recipes.Where(r => BuiltIn(r, "atomic_assembler") && Z(r) > 26
                                             && _items[r.output_item].base_sell_value > 1000f
                                             && !Requires(r.required_research, "explosive_burning"))
                                 .Select(r => $"{r.id} ({r.required_research})").ToList();
            CollectionAssert.IsEmpty(bad, "Post-iron Atom Generator recipes must sit behind Explosive Burning");
        }

        [Test]
        public void ExplosiveBurning_UnlocksTheOddRungsChlorineToManganese()
        {
            var odd = new[] { "chlorine", "potassium", "scandium", "vanadium", "manganese" };
            foreach (var el in odd)
            {
                var r = _gd.recipes.Single(x => x.id == $"capture_{el}");
                Assert.AreEqual("explosive_burning", r.required_research, el);
            }
            Assert.IsTrue(Requires("explosive_burning", "fusion_iii"));
        }

        [Test]
        public void Deuterium_ComesBeforeStellarFusion()
        {
            var assemble = _gd.recipes.Single(r => r.id == "assemble_deuterium");
            Assert.IsTrue(BuiltIn(assemble, "atomic_assembler"));
            Assert.AreEqual("deuterium_synthesis", assemble.required_research);
            Assert.IsTrue(Requires("fusion_i", "deuterium_synthesis"), "Stellar Fusion must require Deuterium Synthesis");
            Assert.IsTrue(_gd.recipes.Any(r => r.id == "fuse_deuterium" && r.output_item == "helium_4"));
        }
    }
}
