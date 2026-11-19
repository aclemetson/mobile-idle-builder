using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Saves from before 0.6.14 never recorded purchase discounts, so on load the discount total is raised
    /// until spent + discounted covers the list price of the research and buildings the save holds.
    /// </summary>
    [TestFixture]
    public class NetWorthBackfillTests
    {
        ResearchSO _resA, _resB;
        BuildingSO _bld, _demon;
        readonly List<Object> _made = new();

        [SetUp]
        public void SetUp()
        {
            _resA = Make<ResearchSO>(); _resA.id = "a"; _resA.costBaseCurrency = 1000;
            _resB = Make<ResearchSO>(); _resB.id = "b"; _resB.costBaseCurrency = 4000;
            _bld = Make<BuildingSO>(); _bld.buildingId = 7; _bld.entropyCost = 500;
            _bld.upgradeLevels        = new[] { new BuildingUpgradeLevel { level = 2, costBaseCurrency = 300 } };
            _bld.storageUpgradeLevels = new[] { new BuildingStorageUpgradeLevel { level = 2, costBaseCurrency = 200 } };
            _bld.inputUpgradeLevels   = new[] { new BuildingInputUpgradeLevel { level = 2, costBaseCurrency = 100 } };
            _demon = Make<BuildingSO>(); _demon.buildingId = 1; _demon.entropyCost = 9999; _demon.isEntropySink = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        T Make<T>() where T : ScriptableObject { var o = ScriptableObject.CreateInstance<T>(); _made.Add(o); return o; }

        ResearchSO R(string id) => id == "a" ? _resA : id == "b" ? _resB : null;
        BuildingSO B(int id) => id == 7 ? _bld : id == 1 ? _demon : null;

        static BuildingSaveData Placed(int id, int level = 1, int storage = 1, int input = 1) =>
            new() { buildingId = id, level = level, storageLevel = storage, inputLevel = input };

        [Test]
        public void ListValue_SumsResearch_AndBuildingsWithUpgrades_AcrossSites()
        {
            var run = new CurrentRunData
            {
                grids = new List<GridSaveData>
                {
                    new() { buildings = new List<BuildingSaveData> { Placed(7, 2, 2, 2) } },
                    new() { buildings = new List<BuildingSaveData> { Placed(7) } },
                },
            };
            long list = NetWorthBackfill.ListValue(new[] { "a", "b", "a" }, run, R, B);
            Assert.AreEqual(1000 + 4000 + (500 + 300 + 200 + 100) + 500, list);
        }

        [Test]
        public void ListValue_SkipsDemon_UnknownIds_AndNulls()
        {
            var run = new CurrentRunData
            {
                grid = new GridSaveData { buildings = new List<BuildingSaveData> { Placed(1), Placed(42), null } },
            };
            Assert.AreEqual(0, NetWorthBackfill.ListValue(new[] { "gone", null, "" }, run, R, B));
            Assert.AreEqual(0, NetWorthBackfill.ListValue(null, null, R, B));
        }

        [Test]
        public void ListValue_UsesLegacyGrid_WhenNoSiteGrids()
        {
            var run = new CurrentRunData { grid = new GridSaveData { buildings = new List<BuildingSaveData> { Placed(7) } } };
            Assert.AreEqual(500, NetWorthBackfill.ListValue(null, run, R, B));
        }

        [Test]
        public void Discounted_RaisesToCoverListValue() =>
            Assert.AreEqual(2500, NetWorthBackfill.Discounted(listValue: 10_000, totalSpent: 7_500, totalDiscounted: 0));

        [Test]
        public void Discounted_NeverLowers_WhenPaidMoreThanToday() =>
            Assert.AreEqual(300, NetWorthBackfill.Discounted(listValue: 5_000, totalSpent: 8_000, totalDiscounted: 300));

        [Test]
        public void Discounted_IsIdempotent()
        {
            long once  = NetWorthBackfill.Discounted(10_000, 7_500, 0);
            long twice = NetWorthBackfill.Discounted(10_000, 7_500, once);
            Assert.AreEqual(once, twice);
        }
    }
}
