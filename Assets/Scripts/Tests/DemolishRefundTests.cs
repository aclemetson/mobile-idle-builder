using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class DemolishRefundTests
    {
        BuildingSO _so;

        [SetUp]
        public void SetUp()
        {
            _so = ScriptableObject.CreateInstance<BuildingSO>();
            _so.entropyCost   = 200;
            _so.upgradeLevels = new[]
            {
                new BuildingUpgradeLevel { level = 2, costBaseCurrency = 300 },
                new BuildingUpgradeLevel { level = 3, costBaseCurrency = 1200 },
            };
            _so.storageUpgradeLevels = new[] { new BuildingStorageUpgradeLevel { level = 2, costBaseCurrency = 200 } };
            _so.inputUpgradeLevels   = new[] { new BuildingInputUpgradeLevel   { level = 2, costBaseCurrency = 200 } };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_so);

        [Test]
        public void Fraction_StartsAtHalf_AndSalvageRaisesItToAFullRefund()
        {
            Assert.AreEqual(0.5f, DemolishRefund.Fraction(0f), 1e-6f);
            Assert.AreEqual(0.8f, DemolishRefund.Fraction(0.3f), 1e-6f);
            Assert.AreEqual(1f,   DemolishRefund.Fraction(0.5f), 1e-6f, "5 levels of +10% reach 100%");
            Assert.AreEqual(1f,   DemolishRefund.Fraction(2f),   1e-6f, "never refunds more than was spent");
        }

        [Test]
        public void SalvageProtocols_MaxedOut_GivesAFullRefund()
        {
            var def = System.Array.Find(PersistentUpgradeService.All, d => d.id == "salvage_protocols");
            Assert.IsNotNull(def);
            Assert.AreEqual(UpgradeEffectType.DemolishRefund, def.effectType);
            Assert.AreEqual(def.maxLevel, def.levelCosts.Length);
            Assert.AreEqual(1f, DemolishRefund.Fraction(def.effectPerLevel * def.maxLevel), 1e-5f);
        }

        [Test]
        public void EntropySpent_CountsPlacementAndEveryUpgradeBought()
        {
            // Base levels (1) cost nothing; speed 3 bought L2 + L3, input 2 bought L2.
            Assert.AreEqual(200, DemolishRefund.EntropySpent(_so, 1, 1, 1, 0f));
            Assert.AreEqual(200 + 300 + 1200 + 200, DemolishRefund.EntropySpent(_so, 3, 1, 2, 0f));
            Assert.AreEqual(200 + 300 + 200 + 200, DemolishRefund.EntropySpent(_so, 2, 2, 2, 0f));
            // Saves store the base storage/input level as 0 or 1.
            Assert.AreEqual(200, DemolishRefund.EntropySpent(_so, 0, 0, 0, 0f));
        }

        [Test]
        public void EntropySpent_PlacementUsesThePrestigeDiscount()
        {
            Assert.AreEqual(150, DemolishRefund.EntropySpent(_so, 1, 1, 1, 0.25f));
            // Truncates like HUDController.OnBuildingPlaced charged it: 200 * (1 - 0.3f) = 139.99...
            Assert.AreEqual(139, DemolishRefund.EntropySpent(_so, 1, 1, 1, 0.3f));
        }

        [Test]
        public void Amount_RoundsDown()
        {
            Assert.AreEqual(50,  DemolishRefund.Amount(101, 0.5f));
            Assert.AreEqual(101, DemolishRefund.Amount(101, 1f));
            Assert.AreEqual(0,   DemolishRefund.Amount(0, 0.5f));
        }

        [Test]
        public void IsProtected_OnlyTheLastDemon()
        {
            Assert.IsTrue(DemolishRefund.IsProtected(true, 1));
            Assert.IsFalse(DemolishRefund.IsProtected(true, 2), "a second Demon can go");
            Assert.IsFalse(DemolishRefund.IsProtected(false, 1));
        }

        [Test]
        public void CollectorFraction_FollowsTheHarvesterTimer()
        {
            Assert.AreEqual(0.5f, HUDBuildingInspectorSubController.CollectorFraction(
                new CollectorData { OutputRate = 2f, Timer = 0.25f }), 1e-6f);
            Assert.AreEqual(0.25f, HUDBuildingInspectorSubController.CollectorFraction(
                new CollectorData { OutputRate = 0f, Timer = 0.25f }), 1e-6f, "rate 0 falls back to 1/s");
            Assert.AreEqual(1f, HUDBuildingInspectorSubController.CollectorFraction(
                new CollectorData { OutputRate = 1f, Timer = 3f }), 1e-6f);
        }

        [Test]
        public void BlockedFieldTap_AlwaysHasAMessage()
        {
            StringAssert.Contains("Quark", ManualFieldCollector.BlockedMessage("Quark"));
            Assert.IsNotEmpty(ManualFieldCollector.BlockedMessage(null));
        }
    }
}
