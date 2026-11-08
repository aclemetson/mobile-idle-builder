using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The top notification banner must not cover the mode bars, and can be swiped away. The layout tests run
    /// against the real GameHUD.uxml, so a renamed element or class fails here, not on device.
    /// </summary>
    [TestFixture]
    public class NotificationBannerTests
    {
        GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        static VisualElement LoadHud()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");
            Assert.IsNotNull(tree, "GameHUD.uxml not found");
            return tree.CloneTree();
        }

        [Test]
        public void Push_MovesEveryModeBarDown_AndBack()
        {
            var root = LoadHud();
            _go = new GameObject("NotificationBannerTest");
            var banner = _go.AddComponent<HUDBannerController>();
            banner.Init(root);

            string[] overlays = { "placement-overlay", "conveyor-overlay", "deconstruct-overlay" };

            banner.ApplyControlsPush(48f);
            foreach (var name in overlays)
            {
                var overlay = root.Q(name);
                Assert.IsNotNull(overlay, $"{name} missing from GameHUD.uxml");
                Assert.AreEqual(48f, overlay.style.paddingTop.value.value, $"{name} was not pushed down");
            }

            banner.ApplyControlsPush(0f);
            foreach (var name in overlays)
                Assert.AreEqual(0f, root.Q(name).style.paddingTop.value.value, $"{name} did not return");
        }

        [Test]
        public void Banner_BlocksWorldInput_SoASwipeDoesNotPanTheMap()
        {
            var banner = LoadHud().Q("notification-banner");
            Assert.IsNotNull(banner, "notification-banner missing from GameHUD.uxml");
            Assert.IsTrue(banner.ClassListContains(UIInputBlocker.MarkerClass));
        }

        [TestCase(-60f, 0f)]    // left
        [TestCase(60f, 0f)]     // right
        [TestCase(0f, -60f)]    // up
        public void Swipe_FarEnough_Dismisses(float x, float y) =>
            Assert.IsTrue(BannerSwipe.Dismisses(new Vector2(x, y)));

        [TestCase(0f, 0f)]      // a tap
        [TestCase(20f, 5f)]     // too short
        [TestCase(0f, 200f)]    // down has nowhere to go
        public void Swipe_ShortOrDown_Stays(float x, float y) =>
            Assert.IsFalse(BannerSwipe.Dismisses(new Vector2(x, y)));

        [Test]
        public void Follow_LocksToTheMainAxis_AndNeverGoesBelowHome()
        {
            Assert.AreEqual(new Vector2(30f, 0f), BannerSwipe.Follow(new Vector2(30f, 10f)));
            Assert.AreEqual(new Vector2(0f, -30f), BannerSwipe.Follow(new Vector2(10f, -30f)));
            Assert.AreEqual(Vector2.zero, BannerSwipe.Follow(new Vector2(10f, 30f)));
        }

        [Test]
        public void Exit_ContinuesTheSwipe()
        {
            Assert.Greater(BannerSwipe.ExitOffset(new Vector2(60f, 0f), 300f).x, 300f);
            Assert.Less(BannerSwipe.ExitOffset(new Vector2(-60f, 0f), 300f).x, -300f);
            Assert.Less(BannerSwipe.ExitOffset(new Vector2(0f, -60f), 300f).y, 0f);
        }
    }
}
