using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #322 phase 2. Splitting Uranium used to turn a 128e U-235 into ~18M of Krypton + Barium. The heavy-element
    /// values were rescaled so fission gains a few times its input, and every split is physically honest.
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
        List<GdRecipe> _fission;

        [OneTimeSetUp]
        public void Load()
        {
            _gd      = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items   = _gd.items.ToDictionary(i => i.id);
            _fission = _gd.recipes.Where(r => r.valid_buildings.Contains("fission_reactor")).ToList();
        }

        [Test]
        public void EverySplit_HasTwoFragmentsWhoseProtonsAddUpToTheFuel()
        {
            Assert.GreaterOrEqual(_fission.Count, 8);
            foreach (var r in _fission)
            {
                var fuel      = _items[r.inputs.Single().item];
                var fragments = new[] { r.output_item }
                    .Concat(r.byproducts.Where(b => b.item != "neutron").Select(b => b.item))
                    .Select(id => _items[id]).ToList();
                Assert.AreEqual(2, fragments.Count, r.id);
                Assert.AreEqual(fuel.atomic_number, fragments.Sum(f => f.atomic_number), $"{r.id}: protons are conserved");
                Assert.Greater(r.byproducts.Where(b => b.item == "neutron").Sum(b => b.quantity), 0, $"{r.id}: releases neutrons");
            }
        }

        [Test]
        public void EverySplit_GainsAFewTimesItsFuel_NotThousands()
        {
            foreach (var r in _fission)
            {
                var fuel   = r.inputs.Single();
                double inV  = _items[fuel.item].base_sell_value * fuel.quantity;
                double outV = _items[r.output_item].base_sell_value
                              + r.byproducts.Sum(b => _items[b.item].base_sell_value * b.quantity);
                double gain = outV / inV;
                Assert.That(gain, Is.InRange(2.0, 8.0), $"{r.id}: {inV} -> {outV} (x{gain:0.0})");
            }
        }

        [Test]
        public void ValueRisesTowardIron_FromPlutoniumDownToCobalt()
        {
            var byZ = _gd.items.Where(i => i.category == "Element" && i.atomic_number >= 26 && i.atomic_number <= 94)
                               .ToDictionary(i => i.atomic_number);
            for (int z = 27; z <= 94; z++)
                Assert.Greater(byZ[z - 1].base_sell_value, byZ[z].base_sell_value,
                    $"Z{z - 1} {byZ[z - 1].id} must be worth more than Z{z} {byZ[z].id} (iron is the peak)");
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
