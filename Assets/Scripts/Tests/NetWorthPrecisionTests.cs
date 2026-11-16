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
            Assert.AreEqual(16_777_217d, NetWorthSystem.Compute(0d, 16_777_217L, 0L, 0d));
            Assert.AreEqual(16_777_218d, NetWorthSystem.Compute(1d, 16_777_217L, 0L, 0d),
                "One more entropy-worth of items must still change net worth above 2^24");
        }

        [Test]
        public void Compute_SumsAllParts()
        {
            Assert.AreEqual(1_000_000_123.5d,
                NetWorthSystem.Compute(0.5d, 1_000_000_000L, 100L, 23d));
        }

        [Test]
        public void Compute_LargeValuesDifferByOne()
        {
            double a = NetWorthSystem.Compute(0d, 123_456_789_012L, 0L, 0d);
            double b = NetWorthSystem.Compute(0d, 123_456_789_013L, 0L, 0d);
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
        public void PrestigePreview_AcceptsLargeNetWorth()
        {
            // log10 formula: a huge net worth must give a finite, non-negative preview.
            long preview = HUDController.CalculatePrestigePreview(1e15);
            Assert.GreaterOrEqual(preview, 0L);
        }
    }
}
