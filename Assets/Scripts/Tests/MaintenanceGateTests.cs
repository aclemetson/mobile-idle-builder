using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Tests the pure formatting seam of the maintenance gate. The async UGS/fetch path is not
    /// unit-testable without live services, so only <see cref="MaintenanceGate.FormatEstimatedReturn"/>
    /// is covered here (parsing + graceful handling of blank/garbage). Assertions avoid the local
    /// timezone by checking structure and UTC-equivalence rather than an absolute clock value.
    /// </summary>
    [TestFixture]
    public class MaintenanceGateTests
    {
        [Test]
        public void FormatEstimatedReturn_Blank_ReturnsEmpty()
        {
            Assert.AreEqual("", MaintenanceGate.FormatEstimatedReturn(null));
            Assert.AreEqual("", MaintenanceGate.FormatEstimatedReturn(""));
            Assert.AreEqual("", MaintenanceGate.FormatEstimatedReturn("   "));
        }

        [Test]
        public void FormatEstimatedReturn_Garbage_ReturnsEmpty()
        {
            Assert.AreEqual("", MaintenanceGate.FormatEstimatedReturn("not-a-date"));
            Assert.AreEqual("", MaintenanceGate.FormatEstimatedReturn("2026-13-99"));
        }

        [Test]
        public void FormatEstimatedReturn_ValidIso_ReturnsEstimatedBackLine()
        {
            string s = MaintenanceGate.FormatEstimatedReturn("2026-06-21T15:00:00Z");
            Assert.IsNotEmpty(s);
            StringAssert.StartsWith("Estimated back:", s);
        }

        [Test]
        public void FormatEstimatedReturn_NoOffset_TreatedAsUtc_SameAsZ()
        {
            // Both should be interpreted as UTC, so they format to the same local string
            // regardless of the machine timezone.
            string withZ = MaintenanceGate.FormatEstimatedReturn("2026-06-21T15:00:00Z");
            string noOff = MaintenanceGate.FormatEstimatedReturn("2026-06-21T15:00:00");
            Assert.AreEqual(withZ, noOff);
        }
    }
}
