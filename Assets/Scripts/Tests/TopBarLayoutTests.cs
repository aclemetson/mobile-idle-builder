using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The top bar overflowed a phone screen once Prestige showed, hiding the Settings button.</summary>
    [TestFixture]
    public class TopBarLayoutTests
    {
        [Test]
        public void PowerReadout_LivesInItsOwnStrip_NotTheTopBar()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml").CloneTree();
            Assert.IsNotNull(root.Q("top-bar").Q("btn-settings"), "Settings belongs in the top bar");
            Assert.IsNull(root.Q("top-bar").Q("power-label"), "The readout's long text does not fit in the top bar");
            Assert.IsNotNull(root.Q("power-status").Q("power-label"));
        }

        [TestCase(0f, 0, false)]    // nothing built yet: no strip
        [TestCase(0f, 2, true)]     // buildings waiting for a generator: say so
        [TestCase(10f, 0, true)]
        public void PowerStrip_ShowsOnlyWhenThereIsSomethingToReport(float supply, int disconnected, bool shows) =>
            Assert.AreEqual(shows, HUDStatusBarController.ShowsPowerStrip(supply, disconnected));

        [Test]
        public void CraftStrip_SitsUnderTheTopBar_StyledLikeTheResearchStrip()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml").CloneTree();
            var strip = root.Q("craft-status");
            Assert.IsNotNull(strip, "The craft ticker must be in the HUD so it shows from any screen");
            Assert.IsNull(root.Q("top-bar").Q("craft-status"));
            Assert.IsTrue(strip.ClassListContains("research-status"));
            Assert.IsNotNull(strip.Q<Label>("craft-status-label"));
        }

        [Test]
        public void CraftStatus_NamesTheFrontRecipe_AndTheTimeLeft() =>
            Assert.AreEqual("⚗ Helium ×3   ⏳ 12s", HUDStatusBarController.FormatCraftStatus("Helium", 3, 0, 12));

        [Test]
        public void CraftStatus_CountsItemsQueuedBehindTheFrontRecipe() =>
            StringAssert.Contains("Helium ×3  +2 more", HUDStatusBarController.FormatCraftStatus("Helium", 3, 2, 65));
    }
}
