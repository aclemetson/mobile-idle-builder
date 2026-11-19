using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The 2026-11-19 isotope trim: Tritium, Carbon-14 and the Isotopic Manipulator's duplicate Deuterium
    /// recipe are gone, the Manipulator unlocks with Radioactive Isotopes (where it first has work), and a
    /// save still holding a retired item gets its entropy instead.
    /// </summary>
    [TestFixture]
    public class IsotopeTrimTests
    {
        [System.Serializable] class GdItem     { public string id; public int item_id; }
        [System.Serializable] class GdBuilding { public string id; public string required_research; public List<string> supported_recipes; }
        [System.Serializable] class GdResearch { public string id; public List<string> prerequisites; public List<string> unlocks_buildings; }
        [System.Serializable] class GdRoot
        {
            public List<GdItem> items = new();
            public List<GdBuilding> buildings = new();
            public List<GdResearch> research = new();
        }

        GdRoot _data;

        [OneTimeSetUp]
        public void Load() =>
            _data = JsonUtility.FromJson<GdRoot>(System.IO.File.ReadAllText("Assets/Data/game_data.json"));

        [Test]
        public void RetiredItems_AreNotInTheGameData()
        {
            foreach (var id in ECSLoadBridge.RetiredItems.Keys)
                Assert.IsFalse(_data.items.Any(i => i.item_id == id), $"item_id {id} is retired but still defined");
        }

        [Test]
        public void IsotopeEngineeringResearch_IsGone() =>
            Assert.IsFalse(_data.research.Any(r => r.id == "isotopes"));

        [Test]
        public void IsotopicManipulator_UnlocksWithRadioactiveIsotopes()
        {
            var b = _data.buildings.Single(x => x.id == "isotopic_manipulator");
            Assert.AreEqual("radioactive_isotopes", b.required_research);
            var r = _data.research.Single(x => x.id == "radioactive_isotopes");
            CollectionAssert.Contains(r.unlocks_buildings, "isotopic_manipulator");
            CollectionAssert.AreEquivalent(new[] { "heavy_elements" }, r.prerequisites);
        }

        [Test]
        public void IsotopicManipulator_OnlyMakesU235AndDecayChainIsotopes()
        {
            var b = _data.buildings.Single(x => x.id == "isotopic_manipulator");
            foreach (var id in b.supported_recipes)
                Assert.IsTrue(id == "uranium_235" || id.StartsWith("irradiate_") || id.StartsWith("strip_"), id);
        }

        [Test]
        public void HeavyElements_HangsOffAtomicAssembly() =>
            CollectionAssert.AreEquivalent(new[] { "atomic_assembly" },
                _data.research.Single(x => x.id == "heavy_elements").prerequisites);

        [TestCase(653, 3, 1959)]
        [TestCase(808, 0, 0)]
        [TestCase(0, 5, 0)]
        public void SellRetired_PaysPriceTimesQuantity(long price, int qty, long expected) =>
            Assert.AreEqual(expected, ECSLoadBridge.SellRetired(price, qty));

        [Test]
        public void SellRetired_SaturatesInsteadOfOverflowing() =>
            Assert.AreEqual(long.MaxValue, ECSLoadBridge.SellRetired(long.MaxValue / 2, 3));
    }
}
