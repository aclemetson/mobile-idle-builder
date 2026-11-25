using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #284: net worth is a double. A float holds integers exactly only up to 2^24 (16,777,216),
    /// so above that net worth drifted, the prestige-wall comparison lost precision and
    /// NetWorthSystem's equality early-out stopped being reliable.
    /// </summary>
    [TestFixture]
    public class NetWorthPrecisionTests
    {
        [Test]
        public void Compute_IsExactAboveFloatIntegerLimit()
        {
            // 2^24 + 1 is the first integer a float cannot represent.
            Assert.AreEqual(16_777_217d, NetWorthSystem.Compute(0d, 0d, 16_777_217L, 0L, 0L, 0d));
            Assert.AreEqual(16_777_218d, NetWorthSystem.Compute(1d, 0d, 16_777_217L, 0L, 0L, 0d),
                "One more entropy-worth of items must still change net worth above 2^24");
        }

        [Test]
        public void Compute_SumsAllParts()
        {
            Assert.AreEqual(1_000_000_133.75d,
                NetWorthSystem.Compute(0.5d, 0.25d, 1_000_000_000L, 100L, 10L, 23d));
        }

        [Test]
        public void Compute_LargeValuesDifferByOne()
        {
            double a = NetWorthSystem.Compute(0d, 0d, 123_456_789_012L, 0L, 0L, 0d);
            double b = NetWorthSystem.Compute(0d, 0d, 123_456_789_013L, 0L, 0L, 0d);
            Assert.AreEqual(1d, b - a);
        }

        [Test]
        public void SaveData_OldFloatJson_LoadsIntoDoubleFields()
        {
            // Saves written before #284 stored these fields as floats; JSON numbers read into doubles.
            var save = JsonUtility.FromJson<SaveData>(
                "{\"lastKnownNetWorth\":1500.5,\"currentRun\":{\"baseNetWorth\":2.5E+07}}");
            Assert.AreEqual(1500.5d, save.lastKnownNetWorth);
            Assert.AreEqual(25_000_000d, save.currentRun.baseNetWorth);
        }

        [Test]
        public void SaveData_LargeNetWorth_RoundTripsExactly()
        {
            var save = new SaveData { lastKnownNetWorth = 123_456_789_013d };
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(123_456_789_013d, back.lastKnownNetWorth);
        }

        [Test]
        public void Compute_CountsDiscountsAndStoredItems()
        {
            // 1000 held + 750 paid + 250 discount = the bought thing at full price; 40 waiting in buildings.
            Assert.AreEqual(2040d, NetWorthSystem.Compute(0d, 40d, 1000L, 750L, 250L, 0d));
        }

        [Test]
        public void StoredValue_PricesEachItem_IgnoresEmptySlots()
        {
            System.Func<int, float> price = id => id == 7 ? 2.5f : 1f;
            Assert.AreEqual(25d, NetWorthSystem.StoredValue(7, 10, price));
            Assert.AreEqual(3d,  NetWorthSystem.StoredValue(1, 3, price));
            Assert.AreEqual(0d,  NetWorthSystem.StoredValue(7, 0, price));
            Assert.AreEqual(0d,  NetWorthSystem.StoredValue(7, -4, price));
        }

        [Test]
        public void SaveData_OldSaveWithoutDiscountField_LoadsAsZero()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"currentRun\":{\"totalEntropySpent\":500}}");
            Assert.AreEqual(500, save.currentRun.totalEntropySpent);
            Assert.AreEqual(0,   save.currentRun.totalEntropyDiscounted);
        }

        [Test]
        public void PrestigePreview_AcceptsLargeNetWorth()
        {
            // log10 formula: a huge net worth must give a finite, non-negative preview.
            long preview = HudText.CalculatePrestigePreview(1e15);
            Assert.GreaterOrEqual(preview, 0L);
        }
    }
}
