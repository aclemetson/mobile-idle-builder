using NUnit.Framework;
using UnityEngine;
using MobileIdleBuilder.UI;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class SafeAreaAdapterTests
    {
        // Galaxy S25+-like portrait screen: 1440x3120, 110 px status bar, 60 px gesture bar.
        private static readonly Rect Safe = new Rect(0f, 60f, 1440f, 3120f - 110f - 60f);

        [Test]
        public void ComputeInsets_ConvertsPixelsToPanelUnits()
        {
            // panel 720 wide on a 1440 px screen -> 0.5 panel units per pixel
            var inset = SafeAreaAdapter.ComputeInsets(Safe, 1440, 3120, 0.5f);

            Assert.AreEqual(55f, inset.top,    1e-3f, "status bar inset");
            Assert.AreEqual(30f, inset.bottom, 1e-3f, "gesture bar inset");
            Assert.AreEqual(0f,  inset.left,   1e-3f);
            Assert.AreEqual(0f,  inset.right,  1e-3f);
        }

        [Test]
        public void ComputeInsets_TopInset_DoesNotDependOnScreenWidth()
        {
            // Regression for #188: the old percent padding resolved against WIDTH, so the same
            // status bar produced a different inset on a narrower screen.
            var wide   = SafeAreaAdapter.ComputeInsets(new Rect(0, 0, 1440, 3010), 1440, 3120, 1f);
            var narrow = SafeAreaAdapter.ComputeInsets(new Rect(0, 0, 1080, 3010), 1080, 3120, 1f);
            Assert.AreEqual(wide.top, narrow.top, 1e-3f);
            Assert.AreEqual(110f, wide.top, 1e-3f);
        }

        [Test]
        public void ComputeInsets_LandscapeCutout_PadsTheSide()
        {
            var inset = SafeAreaAdapter.ComputeInsets(new Rect(100f, 0f, 3020f, 1440f), 3120, 1440, 1f);
            Assert.AreEqual(100f, inset.left,  1e-3f);
            Assert.AreEqual(0f,   inset.right, 1e-3f);
            Assert.AreEqual(0f,   inset.top,   1e-3f);
        }

        [Test]
        public void ComputeInsets_FullScreenSafeArea_IsZero()
        {
            var inset = SafeAreaAdapter.ComputeInsets(new Rect(0, 0, 1080, 1920), 1080, 1920, 0.5f);
            Assert.AreEqual(0f, inset.left + inset.right + inset.top + inset.bottom, 1e-5f);
        }
    }
}
