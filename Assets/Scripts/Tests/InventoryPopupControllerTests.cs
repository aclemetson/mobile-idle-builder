using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class InventoryPopupControllerTests
    {
        [Test]
        public void EaseOutCubic_Endpoints()
        {
            Assert.AreEqual(0f, InventoryPopupController.EaseOutCubic(0f), 1e-6f);
            Assert.AreEqual(1f, InventoryPopupController.EaseOutCubic(1f), 1e-6f);
        }

        [Test]
        public void EaseOutCubic_IsAheadOfLinear_AndClamped()
        {
            Assert.Greater(InventoryPopupController.EaseOutCubic(0.25f), 0.25f);
            Assert.AreEqual(1f, InventoryPopupController.EaseOutCubic(3f), 1e-6f);
            Assert.AreEqual(0f, InventoryPopupController.EaseOutCubic(-1f), 1e-6f);
        }

        [Test]
        public void ReadableTint_LightensAndIsOpaque()
        {
            var dark = new Color(0.1f, 0.2f, 0.6f, 0.3f);
            var t    = InventoryPopupController.ReadableTint(dark);

            Assert.AreEqual(1f, t.a, 1e-6f);
            Assert.Greater(t.r, dark.r);
            Assert.Greater(t.g, dark.g);
            Assert.Greater(t.b, dark.b);
            Assert.Greater(t.b, t.r, "keeps the hue");
        }
    }
}
