using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Version text shown on the loading screen and in Settings > About.
    /// </summary>
    public class VersionLabelTests
    {
        [Test]
        public void Format_PrefixesV()
            => Assert.AreEqual("v0.4.17", VersionLabel.Format("0.4.17"));

        [Test]
        public void Format_EmptyVersion_ReturnsEmpty()
        {
            Assert.AreEqual("", VersionLabel.Format(""));
            Assert.AreEqual("", VersionLabel.Format(null));
        }
    }
}
