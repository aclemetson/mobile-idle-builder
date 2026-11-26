using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The nuclear side of the Item Timeline (2026-11-23): Uranium is mined and walked down to Iron, so
    /// splitting it into Barium + Krypton (worth 66x its fuel under the descent's prices) is gone. The Fission
    /// Reactor is a breeder instead: Uranium -> Neptunium -> Plutonium, the first transuranics.
    /// </summary>
    [TestFixture]
    public class FissionDataTests
    {
        [Serializable] class GdIng      { public string item; public int quantity; }
        [Serializable] class GdItem     { public string id; public string category; public int atomic_number; public float base_sell_value; }
        [Serializable] class GdRecipe   { public string id; public string output_item; public List<GdIng> inputs; public List<GdIng> byproducts; public List<string> valid_buildings; }
        [Serializable] class GdPort     { public string port_type; }
        [Serializable] class GdBuilding { public string id; public bool assignable_outputs; public List<GdPort> ports; }
        [Serializable] class GameData   { public List<GdItem> items; public List<GdRecipe> recipes; public List<GdBuilding> buildings; }

        GameData _gd;
        Dictionary<string, GdItem> _items;
        List<GdRecipe> _reactor;

        [OneTimeSetUp]
        public void Load()
        {
            _gd      = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items   = _gd.items.ToDictionary(i => i.id);
            _reactor = _gd.recipes.Where(r => r.valid_buildings.Contains("fission_reactor")).ToList();
        }

        [Test]
        public void FissionReactor_BreedsUraniumIntoNeptuniumThenPlutonium()
        {
            CollectionAssert.AreEquivalent(new[] { "neptunium", "plutonium" }, _reactor.Select(r => r.output_item));
            foreach (var r in _reactor)
            {
                var fuel = _items[r.inputs.Single().item];
                Assert.AreEqual(fuel.atomic_number + 1, _items[r.output_item].atomic_number,
                    $"{r.id}: one beta decay raises Z by one");
                Assert.IsTrue(r.byproducts.Any(b => b.item == "beta_particle"), $"{r.id}: emits the beta particle");
            }
            Assert.IsFalse(_gd.recipes.Any(r => r.id.StartsWith("fission_u235") || r.id.StartsWith("fission_pu239")),
                "splitting Uranium would skip the descent");
        }

        [Test]
        public void Plutonium_IsBred_NotMinedOrAssembled()
        {
            var makers = _gd.recipes.Where(r => r.output_item == "plutonium").ToList();
            Assert.AreEqual(1, makers.Count);
            Assert.AreEqual("fission_reactor", makers[0].valid_buildings.Single());
        }

        [Test]
        public void DecayChain_ValueRisesFromUraniumDownToLead()
        {
            // The natural alpha chain U -> Th -> Ra -> Rn -> Po -> Pb: each daughter is worth more.
            var chain = new[] { "uranium", "thorium", "radium", "radon", "polonium", "lead" };
            for (int i = 1; i < chain.Length; i++)
                Assert.Greater(_items[chain[i]].base_sell_value, _items[chain[i - 1]].base_sell_value, chain[i]);
        }

        /// <summary>
        /// Every nuclear processing step is worth doing on its own: values are inputs + wage x craft time
        /// (scripts/economy_plan.py for the planned Physics, scripts/rebalance_curve.py elsewhere).
        /// </summary>
        [Test]
        public void EveryNuclearStep_GainsValue()
        {
            var buildings = new[] { "fusion_reactor", "fission_reactor", "neutron_capture", "radioactive_containment", "isotopic_manipulator" };
            // Hydrogen + neutron in the Manipulator stays cheap on purpose: the Atom Generator is the Deuterium route (#320).
            var exempt = new HashSet<string> { "deuterium" };
            foreach (var r in _gd.recipes.Where(r => buildings.Contains(r.valid_buildings[0]) && !exempt.Contains(r.id)))
            {
                double inV  = r.inputs.Sum(i => _items[i.item].base_sell_value * i.quantity);
                double outV = _items[r.output_item].base_sell_value
                              + (r.byproducts ?? new List<GdIng>()).Sum(b => _items[b.item].base_sell_value * b.quantity);
                Assert.Greater(outV / inV, 1.0, $"{r.id}: {inV} -> {outV}");
            }
        }

        [Test]
        public void FissionReactor_HasOneInputAndThreeAssignableOutputs()
        {
            var fr = _gd.buildings.Single(b => b.id == "fission_reactor");
            Assert.IsTrue(fr.assignable_outputs);
            Assert.AreEqual(1, fr.ports.Count(p => p.port_type == "Input"));
            Assert.AreEqual(3, fr.ports.Count(p => p.port_type == "Output"));
        }
    }
}
