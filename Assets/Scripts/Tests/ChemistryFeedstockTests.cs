using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chemistry runs on natural feedstock (2026-11-19, scripts/chemistry_feedstock.py): hydrogen, oxygen and
    /// nitrogen as gases and carbon as graphite, mined from Chemistry's own fields, never loose atoms. Items and
    /// machines belong to one world: Physics production buildings are built in Physics only, Chemistry's in
    /// Chemistry only, and each world sells only its own fields.
    /// </summary>
    [TestFixture]
    public class ChemistryFeedstockTests
    {
        RecipeSO[] _recipes;
        BuildingSO[] _buildings;
        FieldSO[] _fields;

        [OneTimeSetUp]
        public void Load()
        {
            _recipes   = Resources.Load<RecipeDatabaseSO>("RecipeDatabase")?.allRecipes?.Where(r => r != null).ToArray();
            _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings?.Where(b => b != null).ToArray();
            Assert.IsNotNull(_recipes, "RecipeDatabase missing: run Import Game Data");
            Assert.IsNotNull(_buildings, "BuildingDatabase missing: run Import Game Data");
            var sites = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            _fields = sites != null
                ? sites.allSites.Where(s => s?.fieldOverrides != null).SelectMany(s => s.fieldOverrides)
                       .Select(o => o.field).Where(f => f != null).Distinct().ToArray()
                : new FieldSO[0];
        }

        static bool InWorld(BuildingSO b, string world) => b.world == world;

        IEnumerable<RecipeSO> MadeIn(string world) =>
            _recipes.Where(r => r.validBuildings != null && r.validBuildings.Any(b => b != null && InWorld(b, world)));

        [Test]
        public void ChemistryAndBiology_NeverUseLooseAtoms()
        {
            var atoms = new HashSet<string> { "hydrogen", "carbon", "nitrogen", "oxygen" };
            foreach (var r in MadeIn(WorldGate.Chemistry).Concat(MadeIn("world_biology")))
            {
                foreach (var i in r.inputs ?? new RecipeIngredient[0])
                    Assert.IsFalse(i.item != null && atoms.Contains(i.item.id),
                        $"{r.id} takes atomic {i.item?.id}; use hydrogen_gas / oxygen_gas / nitrogen_gas / graphite");
                Assert.IsFalse(r.outputItem != null && atoms.Contains(r.outputItem.id), $"{r.id} mines atomic {r.outputItem?.id}");
            }
        }

        // H, C, N, O atoms per unit of each feedstock or product the balance check knows.
        static readonly Dictionary<string, (int h, int c, int n, int o)> Formula = new()
        {
            ["hydrogen_gas"] = (2, 0, 0, 0), ["oxygen_gas"] = (0, 0, 0, 2), ["nitrogen_gas"] = (0, 0, 2, 0),
            ["graphite"] = (0, 1, 0, 0),
            ["water"] = (2, 0, 0, 1), ["methane"] = (4, 1, 0, 0), ["ammonia"] = (3, 0, 1, 0),
            ["carbon_dioxide"] = (0, 1, 0, 2), ["ethane"] = (6, 2, 0, 0), ["ethanol"] = (6, 2, 0, 1),
            ["glucose"] = (12, 6, 0, 6), ["nitric_acid"] = (1, 0, 1, 3),
        };

        [TestCase("water")] [TestCase("methane")] [TestCase("ammonia")] [TestCase("carbon_dioxide")]
        [TestCase("ethane")] [TestCase("ethanol")] [TestCase("glucose")] [TestCase("nitric_acid")]
        public void FirstReactions_AreBalanced(string recipeId)
        {
            var r = _recipes.Single(x => x.id == recipeId);
            (int h, int c, int n, int o) sum = default;
            foreach (var i in r.inputs)
            {
                var f = Formula[i.item.id];
                sum = (sum.h + f.h * i.quantity, sum.c + f.c * i.quantity, sum.n + f.n * i.quantity, sum.o + f.o * i.quantity);
            }
            var p = Formula[r.outputItem.id];
            int q = Mathf.Max(1, r.outputQuantity);
            Assert.AreEqual((p.h * q, p.c * q, p.n * q, p.o * q), sum, $"{recipeId}: inputs and outputs don't balance");
        }

        [Test]
        public void PhysicsProductionBuildings_StayInPhysics()
        {
            foreach (var id in new[] { "harvester", "nucleon_harvester", "strong_force_combiner", "atomic_assembler",
                                       "isotopic_manipulator", "radioactive_containment", "fusion_reactor",
                                       "fission_reactor", "neutron_capture" })
            {
                var b = _buildings.Single(x => x.id == id);
                Assert.IsTrue(WorldGate.BuildingInWorld(b, WorldGate.Physics), $"{id} is built in Physics");
                Assert.IsFalse(WorldGate.BuildingInWorld(b, WorldGate.Chemistry), $"{id} is not built in Chemistry");
            }
            // Logistics, power and the Demon work everywhere.
            foreach (var b in _buildings.Where(x => x.isSplitter || x.isItemFilter || x.isEntropySink || x.isPowerSource))
                Assert.IsTrue(string.IsNullOrEmpty(b.world), $"{b.id} is shared by every world");
        }

        [Test]
        public void NoMolecule_IsMadeInPhysics()
        {
            foreach (var r in _recipes.Where(r => r.outputItem != null && r.outputItem.category == ItemCategory.Molecule))
                Assert.IsFalse(r.validBuildings.Any(b => b != null && WorldGate.BuildingInWorld(b, WorldGate.Physics)),
                    $"{r.id} makes a molecule in a building Physics can build");
        }

        [Test]
        public void EveryField_BelongsToOneWorld()
        {
            Assert.IsNotEmpty(_fields, "no site fields found");
            foreach (var f in _fields)
                Assert.IsFalse(string.IsNullOrEmpty(f.world), $"{f.id} has no world, so every world would sell it");
            var quarks = _fields.Single(f => f.id == "quark_field");
            Assert.IsTrue(WorldGate.FieldInWorld(quarks, null));
            Assert.IsFalse(WorldGate.FieldInWorld(quarks, WorldGate.Chemistry), "quark pairs aren't sold in Chemistry");
            var graphite = _fields.Single(f => f.id == "graphite_field");
            Assert.IsFalse(WorldGate.FieldInWorld(graphite, WorldGate.Physics), "graphite isn't sold in Physics");
        }
    }
}
