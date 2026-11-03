using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The panel-scale formula shared by UIScaler and the splash screen (#195).
    /// </summary>
    public class UIScalerTests
    {
        [Test]
        public void ComputeScale_DesignWidthGivesScaleFactor()
            => Assert.AreEqual(2f, UIScaler.ComputeScale(1080), 1e-5f);

        [Test]
        public void ComputeScale_ScalesLinearlyWithWidth()
            => Assert.AreEqual(4f, UIScaler.ComputeScale(2160), 1e-5f);

        [Test]
        public void ComputeScale_HonorsCustomDesignWidthAndFactor()
            => Assert.AreEqual(1f, UIScaler.ComputeScale(720, 1440f, 2f), 1e-5f);
    }
}
