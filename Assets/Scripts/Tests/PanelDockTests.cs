using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Where the "Show hint" pill and the tutorial dialogue go beside an open slide panel (PanelDock), the
    /// research tree's pan clamp, and the landscape switch (2026-11-19 device screenshots).
    /// </summary>
    public class PanelDockTests
    {
        static readonly Vector2 Portrait  = new(540, 1120);
        static readonly Vector2 Landscape = new(1170, 520);

        [Test]
        public void PortraitBottomSheet_DocksAboveThePanel()
        {
            var panel = new Rect(0, 1120 * 0.35f, 540, 1120 * 0.65f);
            var dock = PanelDock.For(Portrait, panel, 60, 200, 240);
            Assert.AreEqual(PanelDock.Side.Above, dock.Where);
            Assert.LessOrEqual(dock.Free.yMax, panel.yMin - PanelDock.Gap + 0.01f, "overlaps the panel");
            Assert.GreaterOrEqual(dock.Free.yMin, 60f, "overlaps the top bar");
            Assert.AreEqual(1120 - panel.yMin + PanelDock.Gap, dock.BottomInset(Portrait), 0.01f);
        }

        [Test]
        public void LandscapeSideSheet_DocksLeftOfThePanel()
        {
            var panel = new Rect(1170 * 0.4f, 60, 1170 * 0.6f, 460);
            var dock = PanelDock.For(Landscape, panel, 60, 200, 240);
            Assert.AreEqual(PanelDock.Side.Left, dock.Where);
            Assert.LessOrEqual(dock.Free.xMax, panel.xMin - PanelDock.Gap + 0.01f, "overlaps the panel");
            Assert.AreEqual(1170 - panel.xMin + PanelDock.Gap, dock.RightInset(Landscape), 0.01f);
        }

        [Test]
        public void NoRoom_KeepsTheNormalPlace()
        {
            var fullScreen = new Rect(0, 60, 540, 1060);
            Assert.AreEqual(PanelDock.Side.None, PanelDock.For(Portrait, fullScreen, 60, 200, 240).Where);
        }

        [Test]
        public void SmallPill_FitsWhereTheDialogueDoesNot()
        {
            // A short landscape bottom sheet leaves ~110px above it: enough for the pill, not the dialogue.
            var panel = new Rect(0, 520 * 0.35f, 1170, 520 * 0.65f);
            Assert.AreEqual(PanelDock.Side.Above, PanelDock.For(Landscape, panel, 60, 52, 120).Where);
            Assert.AreEqual(PanelDock.Side.None,  PanelDock.For(Landscape, panel, 60, 200, 240).Where);
        }

        [Test]
        public void UnsizedPanel_DocksNowhere()
            => Assert.AreEqual(PanelDock.Side.None, PanelDock.For(Portrait, Rect.zero, 60, 52, 120).Where);

        [TestCase(-500f, 300f, 600f, 8f)]    // fits: pinned to the start, never centred with a gap above
        [TestCase(250f, 300f, 600f, 8f)]
        [TestCase(-5000f, 2000f, 600f, -1408f)]  // far edge stops at the viewport's
        [TestCase(400f, 2000f, 600f, 8f)]     // near edge stops at the viewport's
        [TestCase(-300f, 2000f, 600f, -300f)] // in range: unchanged
        public void ResearchTree_ClampAxis(float offset, float content, float view, float expected)
            => Assert.AreEqual(expected, ResearchTreeView.ClampAxis(offset, content, view, 8f), 0.01f);

        [Test]
        public void Landscape_IsWiderThanTall()
        {
            Assert.IsTrue(HUDController.IsLandscape(1170, 520));
            Assert.IsFalse(HUDController.IsLandscape(540, 1120));
            Assert.IsFalse(HUDController.IsLandscape(100, 0));
        }
    }
}
