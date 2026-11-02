using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #313: the Nucleon Harvester can be placed from the Build menu or merged from two neighbouring
    /// Harvesters (Up + Down Quarks), and neither path is cheaper.
    /// </summary>
    [TestFixture]
    public class NucleonHarvesterTests
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        T Make<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _made.Add(o);
            return o;
        }

        BuildingSO Building(int cost)
        {
            var b = Make<BuildingSO>();
            b.entropyCost = cost;
            return b;
        }

        ItemSO Item(int id)
        {
            var i = Make<ItemSO>();
            i.itemId = id;
            return i;
        }

        RecipeSO Recipe(string id, ItemSO output, params ItemSO[] inputs)
        {
            var r = Make<RecipeSO>();
            r.id         = id;
            r.outputItem = output;
            r.inputs     = inputs.Select(i => new RecipeIngredient { item = i, quantity = 1 }).ToArray();
            return r;
        }

        // ── Price parity ─────────────────────────────────────────────────────

        [Test]
        public void Fee_TwoHarvestersPlusFee_EqualsOnePlacedNucleonHarvester()
        {
            var harvester = Building(100);
            var nucleon   = Building(500);

            long fee = NucleonHarvesterMerge.Fee(nucleon, harvester, 0f);

            Assert.AreEqual(300, fee);
            Assert.AreEqual(500, 2 * 100 + fee);
        }

        [Test]
        public void Fee_KeepsParityUnderThePrestigeCostReduction()
        {
            var harvester = Building(100);
            var nucleon   = Building(500);
            const float reduction = 0.3f;

            long fee   = NucleonHarvesterMerge.Fee(nucleon, harvester, reduction);
            long whole = EntropyWallet.ReducedCost(500, reduction);
            long one   = EntropyWallet.ReducedCost(100, reduction);

            Assert.AreEqual(whole, 2 * one + fee);
        }

        [Test]
        public void Fee_IsNeverNegative()
        {
            Assert.AreEqual(0, NucleonHarvesterMerge.Fee(Building(150), Building(100), 0f));
        }

        // ── Geometry and polarity ────────────────────────────────────────────

        [Test]
        public void TryPlacement_SideBySide_AnchorsLeftWithNoRotation()
        {
            Assert.IsTrue(NucleonHarvesterMerge.TryPlacement(new Vector2Int(5, 3), new Vector2Int(4, 3),
                                                             out var anchor, out int rotation));
            Assert.AreEqual(new Vector2Int(4, 3), anchor);
            Assert.AreEqual(0, rotation);
        }

        [Test]
        public void TryPlacement_Stacked_AnchorsLowerAndRotates()
        {
            Assert.IsTrue(NucleonHarvesterMerge.TryPlacement(new Vector2Int(2, 7), new Vector2Int(2, 8),
                                                             out var anchor, out int rotation));
            Assert.AreEqual(new Vector2Int(2, 7), anchor);
            Assert.AreEqual(1, rotation);
            Assert.AreEqual(new Vector2Int(1, 2), PortUtils.RotatedFootprint(new Vector2Int(2, 1), rotation),
                            "rotation 1 must turn the 2x1 footprint upright to cover both cells");
        }

        [TestCase(0, 0, 1, 1)]
        [TestCase(0, 0, 2, 0)]
        [TestCase(3, 3, 3, 3)]
        public void TryPlacement_RejectsCellsThatAreNotNeighbours(int ax, int ay, int bx, int by)
        {
            Assert.IsFalse(NucleonHarvesterMerge.TryPlacement(new Vector2Int(ax, ay), new Vector2Int(bx, by), out _, out _));
        }

        [Test]
        public void OpposingPolarity_NeedsOneUpAndOneDown()
        {
            Assert.IsTrue(NucleonHarvesterMerge.OpposingPolarity("collect_up_quark", "collect_down_quark"));
            Assert.IsTrue(NucleonHarvesterMerge.OpposingPolarity("collect_down_quark", "collect_up_quark"));
            Assert.IsFalse(NucleonHarvesterMerge.OpposingPolarity("collect_up_quark", "collect_up_quark"));
            Assert.IsFalse(NucleonHarvesterMerge.OpposingPolarity("collect_up_quark", "collect_electron"));
            Assert.IsFalse(NucleonHarvesterMerge.OpposingPolarity(null, "collect_down_quark"));
        }

        [Test]
        public void PreferBeltedRotation_TurnsTheOutputOntoAnExistingBelt()
        {
            var nh = Building(500);
            nh.footprint = new Vector2Int(2, 1);
            nh.ports = new[] { new BuildingPort { portType = PortType.Output, localCell = Vector2Int.zero, localFacing = OutputDirection.South } };
            var anchor = new Vector2Int(4, 9);

            // Rotation 0 outputs south of (4,9); rotation 2 turns the port to the other cell, facing north.
            var (cell0, facing0) = PortUtils.TransformPort(nh.ports[0], nh.footprint, false, 0);
            var (cell2, facing2) = PortUtils.TransformPort(nh.ports[0], nh.footprint, false, 2);
            var front0 = TutorialRouteGuide.OutputFront(anchor + cell0, facing0);
            var front2 = TutorialRouteGuide.OutputFront(anchor + cell2, facing2);
            Assert.AreNotEqual(front0, front2);

            Assert.AreEqual(2, NucleonHarvesterMerge.PreferBeltedRotation(nh, anchor, 0, c => c == front2));
            Assert.AreEqual(0, NucleonHarvesterMerge.PreferBeltedRotation(nh, anchor, 0, c => c == front0));
            Assert.AreEqual(0, NucleonHarvesterMerge.PreferBeltedRotation(nh, anchor, 0, _ => false), "no belt: keep the default");
        }

        [Test]
        public void CarriedLevel_IsTheLowerLevel_NeverBelowOne()
        {
            Assert.AreEqual(2, NucleonHarvesterMerge.CarriedLevel(3, 2));
            Assert.AreEqual(1, NucleonHarvesterMerge.CarriedLevel(0, 4));
        }

        // ── Field recipes ────────────────────────────────────────────────────

        FieldSO QuarkField(ItemSO up, ItemSO down)
        {
            var f = Make<FieldSO>();
            f.fieldType = "Quark";
            f.drops = new List<FieldDropEntry>
            {
                new FieldDropEntry { item = up, weight = 0.5f }, new FieldDropEntry { item = down, weight = 0.5f }
            };
            return f;
        }

        [Test]
        public void FieldRecipes_Harvester_OffersTheFieldsDrops()
        {
            ItemSO up = Item(1), down = Item(2), electron = Item(3);
            var harvester = Building(100);
            harvester.supportedRecipes = new[]
            {
                Recipe("collect_up_quark", up), Recipe("collect_down_quark", down), Recipe("collect_electron", electron)
            };

            var ids = FieldRecipes.For(harvester, QuarkField(up, down)).Select(r => r.id).ToList();

            CollectionAssert.AreEquivalent(new[] { "collect_up_quark", "collect_down_quark" }, ids);
        }

        [Test]
        public void FieldRecipes_NucleonHarvester_OffersProtonAndNeutron()
        {
            ItemSO up = Item(1), down = Item(2), proton = Item(4), neutron = Item(5);
            var nucleon = Building(500);
            nucleon.supportedRecipes = new[] { Recipe("harvest_proton", proton), Recipe("harvest_neutron", neutron) };

            var ids = FieldRecipes.For(nucleon, QuarkField(up, down)).Select(r => r.id).ToList();

            CollectionAssert.AreEquivalent(new[] { "harvest_proton", "harvest_neutron" }, ids);
        }

        [Test]
        public void FieldRecipes_FallbackSkipsRecipesThatNeedInputs()
        {
            ItemSO up = Item(1), down = Item(2), proton = Item(4);
            var b = Building(100);
            b.supportedRecipes = new[] { Recipe("craft_proton", proton, up, down) };

            Assert.IsEmpty(FieldRecipes.For(b, QuarkField(up, down)));
        }

        // ── game_data.json ───────────────────────────────────────────────────

        [System.Serializable] class GdBuilding { public string id; public int entropy_cost; public string required_research; public List<int> footprint; public List<string> compatible_fields; public string placement_rule; public bool is_obsoleteable; public string obsoleted_by_research; }
        [System.Serializable] class GdResearch { public string id; public List<string> unlocks_buildings; }
        [System.Serializable] class GameData   { public List<GdBuilding> buildings; public List<GdResearch> research; }

        [Test]
        public void Data_NucleonHarvester_IsAPlaceableQuarkFieldBuildingUnlockedByItsResearch()
        {
            var gd = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            var nh  = gd.buildings.Single(b => b.id == "nucleon_harvester");
            var h   = gd.buildings.Single(b => b.id == "harvester");
            var sfc = gd.buildings.Single(b => b.id == "strong_force_combiner");

            Assert.AreEqual("MustBeOnField", nh.placement_rule);
            CollectionAssert.AreEqual(new[] { 2, 1 }, nh.footprint);
            CollectionAssert.AreEqual(new[] { "Quark" }, nh.compatible_fields);
            Assert.AreEqual("nucleon_harvesting", nh.required_research);
            Assert.Greater(nh.entropy_cost, 2 * h.entropy_cost, "the merge must still cost something");
            CollectionAssert.Contains(gd.research.Single(r => r.id == "nucleon_harvesting").unlocks_buildings,
                                      "nucleon_harvester");
            Assert.IsTrue(sfc.is_obsoleteable);
            Assert.AreEqual("nucleon_harvesting", sfc.obsoleted_by_research);
        }
    }
}
