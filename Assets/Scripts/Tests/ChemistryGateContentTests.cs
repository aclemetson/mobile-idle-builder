using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// 2026-11-19 Chemistry gate content split: before Chemistry opens, Physics only reaches Hydrogen to Silicon
    /// and makes no molecules; every molecule, and the buildings that make them, live in the Chemistry world.
    /// </summary>
    [TestFixture]
    public class ChemistryGateContentTests
    {
        ResearchSO[] _research;
        BuildingSO[] _buildings;
        RecipeSO[]   _recipes;

        [OneTimeSetUp]
        public void Load()
        {
            _research  = Resources.Load<ResearchDatabaseSO>("ResearchDatabase").allResearch.Where(r => r != null).ToArray();
            _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase").allBuildings.Where(b => b != null).ToArray();
            _recipes   = Resources.Load<RecipeDatabaseSO>("RecipeDatabase").allRecipes.Where(r => r != null).ToArray();
        }

        // Research reachable before the gate: no required world and no prerequisite (transitively) that needs one.
        bool BeforeGate(ResearchSO r) =>
            string.IsNullOrEmpty(r.requiredWorld) &&
            (r.prerequisites ?? new ResearchSO[0]).All(p => p == null || BeforeGate(p));

        [Test]
        public void PhysicsBeforeTheGate_StopsAtSilicon_AndMakesNoMolecules()
        {
            foreach (var r in _research.Where(BeforeGate))
            {
                foreach (var item in r.unlocksItems ?? new ItemSO[0])
                {
                    if (item == null) continue;
                    Assert.AreNotEqual(ItemCategory.Molecule, item.category, $"{r.id} unlocks molecule {item.id}");
                    if (item.category == ItemCategory.Element)
                        Assert.LessOrEqual(item.atomicNumber, 14, $"{r.id} unlocks {item.id} (Z {item.atomicNumber}) before Chemistry");
                }
                foreach (var rec in r.unlocksRecipes ?? new RecipeSO[0])
                    if (rec?.outputItem != null)
                        Assert.AreNotEqual(ItemCategory.Molecule, rec.outputItem.category, $"{r.id} unlocks recipe {rec.id}");
            }
        }

        [Test]
        public void EveryMoleculeRecipe_IsGatedByChemistryResearch()
        {
            foreach (var rec in _recipes.Where(r => r.outputItem != null && r.outputItem.category == ItemCategory.Molecule))
            {
                Assert.IsNotNull(rec.requiredResearch, rec.id);
                Assert.IsFalse(BeforeGate(rec.requiredResearch), $"{rec.id} unlocks before Chemistry ({rec.requiredResearch.id})");
            }
        }

        [Test]
        public void MoleculeBuildings_AreBuiltInChemistry()
        {
            foreach (var id in new[] { "molecular_synthesizer", "element_harvester", "compound_synthesizer" })
                Assert.AreEqual(WorldGate.Chemistry, _buildings.Single(b => b.id == id).world, id);
            Assert.IsTrue(string.IsNullOrEmpty(_buildings.Single(b => b.isEntropySink).world), "the Demon is shared");
        }

        [Test]
        public void ChemistryOpensWithLabThenMolecules()
        {
            var lab = _research.Single(r => r.id == "chemistry_lab");
            var mol = _research.Single(r => r.id == "molecular_synthesis");
            Assert.AreEqual(WorldGate.Chemistry, lab.requiredWorld);
            Assert.AreEqual(WorldGate.Chemistry, mol.requiredWorld);
            CollectionAssert.Contains(mol.prerequisites.Select(p => p.id).ToList(), "chemistry_lab");
            // 2026-11-19 natural feedstock: the lab starts with hydrogen and oxygen gas and water; graphite comes with
            // Carbon Chemistry (molecular_synthesis), nitrogen gas with Nitrogen Fixation, silicon with the minerals.
            foreach (var rid in new[] { "collect_hydrogen_gas", "collect_oxygen_gas", "water" })
                Assert.IsTrue(lab.unlocksRecipes.Any(r => r != null && r.id == rid), $"chemistry_lab unlocks {rid}");
            Assert.IsTrue(mol.unlocksRecipes.Any(r => r != null && r.id == "collect_graphite"), "Carbon Chemistry mines graphite");
            Assert.IsTrue(mol.unlocksRecipes.Any(r => r != null && r.id == "methane"), "and makes methane");
            var n2 = _research.Single(r => r.id == "nitrogen_fixation");
            Assert.IsTrue(n2.unlocksRecipes.Any(r => r != null && r.id == "collect_nitrogen_gas"), "Chemistry harvests its own nitrogen for ammonia");
            Assert.IsTrue(n2.unlocksRecipes.Any(r => r != null && r.id == "ammonia"), "Nitrogen Fixation makes ammonia");
            var re = _research.Single(r => r.id == "reaction_engineering");
            Assert.IsTrue(re.unlocksRecipes.Any(r => r != null && r.id == "collect_silicon"), "silicon for silica comes with the minerals");
        }
    }
}
