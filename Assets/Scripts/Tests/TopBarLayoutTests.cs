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
    }
}
