using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Belt speed research (docs/agents/tasks/feature-belt-speed-research.md): the inspector warns when a speed
    /// upgrade would outrun the belts and names the research that fixes it.
    /// </summary>
    [TestFixture]
    public class BeltCapacityTests
    {
        [TestCase(2, 1f, 2f)]
        [TestCase(2, 2f, 4f)]
        [TestCase(2, 4f, 8f)]
        [TestCase(1, 1f, 1f)]
        public void BeltThroughput_IsItemsPerTileTimesSpeed(int itemsPerTile, float mult, float expected) =>
            Assert.AreEqual(expected, BeltCapacity.BeltItemsPerSecond(itemsPerTile, mult), 1e-5f);

        [Test]
        public void Peak_IsTheBusiestInputOrOutput()
        {
            // Proton: 2 up + 1 down, 2 s. At Lv2 (x2) a Combiner crafts 1/s and pulls 2 up quarks/s.
            Assert.AreEqual(2f, BeltCapacity.PeakItemsPerSecond(new[] { 2, 1 }, 1, 2f, 2f), 1e-5f);
            Assert.AreEqual(4f, BeltCapacity.PeakItemsPerSecond(new[] { 2, 1 }, 1, 2f, 4f), 1e-5f);
            Assert.AreEqual(0f, BeltCapacity.PeakItemsPerSecond(new[] { 2 }, 1, 0f, 1f));
        }

        [Test]
        public void Lv2Combiner_FitsBaseBelts_NoWarning() =>
            Assert.IsNull(BeltCapacity.Warning(2f, 2, 2, 1f));

        [Test]
        public void Lv3Combiner_OnBaseBelts_PointsAtTransportationI() =>
            Assert.AreEqual("⚠ Belts carry 2/s; Lv3 needs 4/s. Research Transportation I.",
                            BeltCapacity.Warning(4f, 3, 2, 1f));

        [Test]
        public void Lv3_WithTransportationI_NoWarning() =>
            Assert.IsNull(BeltCapacity.Warning(4f, 3, 2, 2f));

        [Test]
        public void Lv4_PointsAtTheResearchStillMissing()
        {
            Assert.AreEqual("⚠ Belts carry 4/s; Lv4 needs 8/s. Research Transportation II.",
                            BeltCapacity.Warning(8f, 4, 2, 2f));
            Assert.AreEqual("⚠ Belts carry 2/s; Lv4 needs 8/s. Research Transportation I and II.",
                            BeltCapacity.Warning(8f, 4, 2, 1f));
            Assert.IsNull(BeltCapacity.Warning(8f, 4, 2, 4f));
        }

        [Test]
        public void BeyondTheFastestBelts_SaysSoWithoutNamingResearch() =>
            Assert.AreEqual("⚠ Belts carry 8/s; Lv5 needs 16/s.", BeltCapacity.Warning(16f, 5, 2, 4f));
    }
}
