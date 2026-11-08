using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Field Synthesis: buying extra fields from the Build menu. Covers the escalating price, the
    /// per-run purchase count the price and the tutorial read, and the shipped game_data.json setup.
    /// </summary>
    [TestFixture]
    public class FieldPurchaseServiceTests
    {
        private static FieldSO Field(long baseCost, float mult = 10f, int group = 1, string research = "field_synthesis")
        {
            var so = ScriptableObject.CreateInstance<FieldSO>();
            so.id                     = "quark_field";
            so.purchaseCostBase       = baseCost;
            so.purchaseCostMultiplier = mult;
            so.purchaseGroupSize      = group;
            so.purchaseResearchId     = research;
            return so;
        }

        [Test]
        public void PriceFor_MultipliesEveryPurchase()
        {
            var f = Field(300);
            Assert.AreEqual(300,     FieldPurchaseService.PriceFor(f, 0));
            Assert.AreEqual(3_000,   FieldPurchaseService.PriceFor(f, 1));
            Assert.AreEqual(30_000,  FieldPurchaseService.PriceFor(f, 2));
            Assert.AreEqual(300_000, FieldPurchaseService.PriceFor(f, 3));
        }

        [Test]
        public void PriceFor_SaturatesInsteadOfOverflowing()
            => Assert.AreEqual(long.MaxValue, FieldPurchaseService.PriceFor(Field(300), 40));

        [Test]
        public void NotForSale_WithoutAPrice()
        {
            var f = Field(0);
            Assert.IsFalse(FieldPurchaseService.IsAvailable(f, _ => true));
            Assert.AreEqual(0, FieldPurchaseService.PriceFor(f, 0));
        }

        [Test]
        public void IsAvailable_WaitsForTheResearch()
        {
            var f = Field(300);
            Assert.IsFalse(FieldPurchaseService.IsAvailable(f, _ => false));
            Assert.IsTrue(FieldPurchaseService.IsAvailable(f, id => id == "field_synthesis"));
        }

        [Test]
        public void RecordPurchase_CountsPerField()
        {
            var save = new SaveData();
            Assert.AreEqual(0, FieldPurchaseService.PurchasedCount(save, "quark_field"));

            FieldPurchaseService.RecordPurchase(save, "quark_field");
            FieldPurchaseService.RecordPurchase(save, "quark_field");
            FieldPurchaseService.RecordPurchase(save, "electron_field");

            Assert.AreEqual(2, FieldPurchaseService.PurchasedCount(save, "quark_field"));
            Assert.AreEqual(1, FieldPurchaseService.PurchasedCount(save, "electron_field"));
        }

        [Test]
        public void PurchaseCounts_SurviveASaveRoundTrip_AndResetWithTheRun()
        {
            var save = new SaveData();
            FieldPurchaseService.RecordPurchase(save, "quark_field");

            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(1, FieldPurchaseService.PurchasedCount(loaded, "quark_field"));

            loaded.currentRun = new CurrentRunData(); // what prestige does
            Assert.AreEqual(0, FieldPurchaseService.PurchasedCount(loaded, "quark_field"));
        }

        [Test]
        public void LegacySave_WithoutPurchaseLists_CountsZero()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"currentRun\":{\"baseCurrency\":5}}");
            Assert.AreEqual(0, FieldPurchaseService.PurchasedCount(save, "quark_field"));
            FieldPurchaseService.RecordPurchase(save, "quark_field");
            Assert.AreEqual(1, FieldPurchaseService.PurchasedCount(save, "quark_field"));
        }

        // ── Shipped data ─────────────────────────────────────────────────────

        [Test]
        public void GameData_QuarkFieldsSellAsPairs_BehindFieldSynthesis()
        {
            var json = File.ReadAllText("Assets/Data/game_data.json");
            StringAssert.Contains("\"id\": \"field_synthesis\"", json);

            var quark = LoadField("quark_field");
            Assert.AreEqual(2, quark.purchaseGroupSize, "Quark fields must always come in pairs");
            Assert.AreEqual("field_synthesis", quark.purchaseResearchId);
            Assert.Greater(quark.purchaseCostBase, 0);
            Assert.GreaterOrEqual(quark.purchaseCostMultiplier, 5f, "Each field must cost far more than the last");

            var lepton = LoadField("electron_field");
            Assert.AreEqual(1, lepton.purchaseGroupSize);
            Assert.AreEqual("field_synthesis", lepton.purchaseResearchId);
        }

        [Test]
        public void Tutorial_BuysNucleosynthesis_ThenBuildsTheNeutronLineStraightIntoHelium()
        {
            var flow = UnityEditor.AssetDatabase.LoadAssetAtPath<TutorialFlowSO>("Assets/Data/tutorial/tutorial_flow.asset");
            Assert.IsNotNull(flow);
            int Index(string id) => System.Array.FindIndex(flow.steps, s => s.id == id);

            int loop = Index("hydrogen_loop_complete");
            Assert.AreEqual(loop + 1, Index("post_tutorial_intro"));
            Assert.AreEqual(loop + 2, Index("buy_atomic_assembly"));
            Assert.AreEqual(loop + 3, Index("atomic_assembly_unlocked"));
            Assert.AreEqual(loop + 4, Index("buy_field_synthesis"));
            Assert.AreEqual(loop + 5, Index("place_quark_field_pair"));
            Assert.AreEqual(loop + 6, Index("field_pair_placed"));
            // The new pair becomes the neutron line (#269, #275). Helium is unlocked by now, so the
            // neutrons have a use the moment they exist and never detour through the Demon.
            Assert.AreEqual(loop + 7, Index("place_neutron_harvesters"));
            Assert.AreEqual(loop + 11, Index("route_neutrons_to_atom_gen"));
            Assert.AreEqual(loop + 12, Index("craft_first_helium"));
            Assert.AreEqual(-1, Index("route_neutrons_to_demon"));

            var place = flow.steps[loop + 5].advanceCondition;
            Assert.AreEqual(ConditionType.FieldPurchasedMin, place.type);
            Assert.AreEqual("quark_field", place.fieldId);
            Assert.AreEqual(1, place.minCount);
        }

        private static FieldSO LoadField(string id)
        {
            var so = UnityEditor.AssetDatabase.LoadAssetAtPath<FieldSO>($"Assets/Data/fields/{id}.asset");
            Assert.IsNotNull(so, $"{id}.asset missing; run Import Game Data");
            return so;
        }
    }
}
