using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class BuildingUpgradeTests
    {
        private BuildingSO _so;

        [SetUp]
        public void Setup()
        {
            _so = ScriptableObject.CreateInstance<BuildingSO>();
            _so.baseMaxOutputItems = 20;
            _so.upgradeLevels = new[]
            {
                new BuildingUpgradeLevel { level = 2, outputRate = 2f, costBaseCurrency = 500 },
                new BuildingUpgradeLevel { level = 3, outputRate = 4f, costBaseCurrency = 2000 },
            };
            _so.storageUpgradeLevels = new[]
            {
                new BuildingStorageUpgradeLevel { level = 2, maxOutputItems = 150, costBaseCurrency = 300 },
                new BuildingStorageUpgradeLevel { level = 3, maxOutputItems = 750, costBaseCurrency = 1200 },
            };
        }

        [TearDown]
        public void Teardown()
        {
            Object.DestroyImmediate(_so);
        }

        // ── ProductionSpeedForLevel ───────────────────────────────────────────

        [Test]
        public void ProductionSpeedForLevel_Level1_ReturnsOne()
        {
            Assert.AreEqual(1f, BuildingSO.ProductionSpeedForLevel(_so, 1));
        }

        [Test]
        public void ProductionSpeedForLevel_Level2_ReturnsUpgradeRate()
        {
            Assert.AreEqual(2f, BuildingSO.ProductionSpeedForLevel(_so, 2));
        }

        [Test]
        public void ProductionSpeedForLevel_NullSO_ReturnsOne()
        {
            Assert.AreEqual(1f, BuildingSO.ProductionSpeedForLevel(null, 2));
        }

        // ── OutputCapacityForLevel ────────────────────────────────────────────

        [Test]
        public void OutputCapacityForLevel_Level1_ReturnsBaseMaxOutputItems()
        {
            Assert.AreEqual(20, BuildingSO.OutputCapacityForLevel(_so, 1));
        }

        [Test]
        public void OutputCapacityForLevel_Level2_ReturnsStorageUpgradeValue()
        {
            Assert.AreEqual(150, BuildingSO.OutputCapacityForLevel(_so, 2));
        }

        // ── NextSpeedUpgrade ──────────────────────────────────────────────────

        [Test]
        public void NextSpeedUpgrade_WhenMaxed_ReturnsNull()
        {
            // Max speed level = 1 + 2 entries = 3; currentLevel 3 is maxed
            Assert.IsNull(_so.NextSpeedUpgrade(3));
        }

        [Test]
        public void NextSpeedUpgrade_WhenNoUpgradesArray_ReturnsNull()
        {
            var empty = ScriptableObject.CreateInstance<BuildingSO>();
            try
            {
                Assert.IsNull(empty.NextSpeedUpgrade(1));
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        [Test]
        public void NextSpeedUpgrade_Level1_ReturnsLevel2Entry()
        {
            var result = _so.NextSpeedUpgrade(1);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(2, result.Value.level);
            Assert.AreEqual(500, result.Value.costBaseCurrency);
        }

        // ── NextStorageUpgrade ────────────────────────────────────────────────

        [Test]
        public void NextStorageUpgrade_WhenMaxed_ReturnsNull()
        {
            Assert.IsNull(_so.NextStorageUpgrade(3));
        }

        [Test]
        public void NextStorageUpgrade_Level1_ReturnsLevel2Entry()
        {
            var result = _so.NextStorageUpgrade(1);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(2, result.Value.level);
            Assert.AreEqual(150, result.Value.maxOutputItems);
        }
    }
}
