using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chemistry streams (2026-11-22, scripts/chemistry_streams.py): Chemistry has no eV power. Belts there
    /// are water streams that start at a Spring; buildings sit beside them and draw flow; a Calorimeter is
    /// the sink. Guards the generated content those rules rely on.
    /// </summary>
    [TestFixture]
    public class ChemistryStreamContentTests
    {
        BuildingSO[] _buildings;
        RecipeSO[] _recipes;
        WorldSO[] _worlds;

        [OneTimeSetUp]
        public void Load()
        {
            _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings?.Where(b => b != null).ToArray();
            _recipes   = Resources.Load<RecipeDatabaseSO>("RecipeDatabase")?.allRecipes?.Where(r => r != null).ToArray();
            _worlds    = Resources.Load<WorldDatabaseSO>("WorldDatabase")?.allWorlds?.Where(w => w != null).ToArray();
            Assert.IsNotNull(_buildings, "BuildingDatabase missing: run Import Game Data");
            Assert.IsNotNull(_recipes, "RecipeDatabase missing: run Import Game Data");
            Assert.IsNotNull(_worlds, "WorldDatabase missing: run Import Game Data");
        }

        BuildingSO B(string id) => _buildings.Single(b => b.id == id);

        [Test]
        public void OnlyChemistry_HasStreamTransport()
        {
            foreach (var w in _worlds)
                Assert.AreEqual(w.id == WorldGate.Chemistry, w.streamTransport, $"{w.id} streamTransport");
        }

        [Test]
        public void ChemistryBuildings_UseFlow_NotPower()
        {
            foreach (var b in _buildings.Where(b => b.world == WorldGate.Chemistry))
            {
                Assert.IsFalse(b.requiresPower, $"{b.id} needs eV, but Chemistry has none");
                Assert.IsFalse(b.isPowerSource, $"{b.id} makes eV in Chemistry");
                if (b.supportedRecipes != null && b.supportedRecipes.Length > 0)
                    Assert.Greater(b.flowDraw, 0f, $"{b.id} makes something, so it should draw flow");
            }
        }

        [Test]
        public void Spring_SuppliesFlow()
        {
            var spring = B("spring");
            Assert.IsTrue(spring.isSpring);
            Assert.Greater(spring.streamFlow, 0f);
            Assert.AreEqual(WorldGate.Chemistry, spring.world);
            Assert.IsTrue(spring.ports.Any(p => p.portType == PortType.Output), "the stream starts at its output");
        }

        [Test]
        public void Calorimeter_IsAPlacedSink_NotTheDemon()
        {
            var cal = B("calorimeter");
            Assert.IsTrue(cal.isEntropySink);
            Assert.IsTrue(cal.isPlacedSink);
            Assert.IsFalse(cal.IsFixedSink);
            Assert.IsTrue(GridSaveService.IsRestorable(cal), "a placed sink comes back from the save");
            Assert.AreEqual(WorldGate.Chemistry, cal.world);
            Assert.IsTrue(B("maxwells_demon").IsFixedSink);
        }

        [Test]
        public void ReactionBuildings_HaveOneBankSide()
        {
            // The stream runs past one side: the input and the output sit on the same cell edge.
            foreach (var id in new[] { "molecular_synthesizer", "compound_synthesizer", "catalytic_reactor",
                                       "organic_synthesizer", "materials_forge" })
            {
                var ports = B(id).ports;
                var inputs  = ports.Where(p => p.portType == PortType.Input).ToArray();
                var outputs = ports.Where(p => p.portType == PortType.Output).ToArray();
                Assert.AreEqual(1, inputs.Length, $"{id}: one input");
                Assert.AreEqual(1, outputs.Length, $"{id}: one output");
                // Input facing is the travel direction into the building, so its belt side is the opposite.
                Assert.AreEqual(outputs[0].localFacing, (OutputDirection)(((int)inputs[0].localFacing + 2) % 4),
                    $"{id}: input and output must face the same stream");
            }
        }

        [Test]
        public void RecipeIds_AreUnique()
        {
            var dupes = _recipes.GroupBy(r => r.recipeId).Where(g => g.Count() > 1)
                                .Select(g => g.Key + ": " + string.Join(", ", g.Select(r => r.id))).ToArray();
            Assert.IsEmpty(dupes, "recipe ids shared by several recipes");
        }

        [Test]
        public void Byproducts_LeaveOnTheStream_AsRealMolecules()
        {
            // 2026-11-23 life chemistry: water and hydrogen leave reactions as byproducts (HCN, urea, glycine...)
            // and ride the stream to whoever uses them next.
            var r = _recipes.Single(x => x.id == "hydrogen_cyanide");
            Assert.AreEqual(3, r.byproducts.Single(i => i.item.id == "hydrogen_gas").quantity, "CH4 + NH3 -> HCN + 3 H2");
            var urea = _recipes.Single(x => x.id == "urea");
            Assert.AreEqual(1, urea.byproducts.Single(i => i.item.id == "water").quantity, "CO2 + 2 NH3 -> urea + H2O");
        }
    }
}
