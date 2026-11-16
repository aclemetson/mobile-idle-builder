using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Red reminder dots (MenuReminderController) and the banner tap rule (BannerSwipe.IsTap).
    /// </summary>
    [TestFixture]
    public class MenuReminderTests
    {
        [Test]
        public void HandleDot_LitByAnyMenuRow()
        {
            Assert.IsTrue(MenuReminderController.AnyMenuReminder(new[] { (true, false), (true, true) }));
        }

        [Test]
        public void HandleDot_OffWhenNoMenuRowHasAReminder()
        {
            Assert.IsFalse(MenuReminderController.AnyMenuReminder(new[] { (true, false), (true, false) }));
        }

        [Test]
        public void HandleDot_IgnoresButtonsInsidePanels()
        {
            // An achievements tab with a reward is not a drawer row: it must not light the handle by itself.
            Assert.IsFalse(MenuReminderController.AnyMenuReminder(new[] { (false, true) }));
        }

        [Test]
        public void AttachDot_AddsOneHiddenDotThatIgnoresTaps()
        {
            var btn = new Button();
            var dot = MenuReminderController.AttachDot(btn);
            Assert.IsNotNull(dot);
            Assert.IsTrue(dot.ClassListContains(MenuReminderController.DotClass));
            Assert.IsTrue(dot.ClassListContains("hidden"));
            Assert.AreEqual(PickingMode.Ignore, dot.pickingMode);
            Assert.AreSame(dot, MenuReminderController.AttachDot(btn), "attaching twice reuses the dot");
            Assert.AreEqual(1, btn.Query(className: MenuReminderController.DotClass).ToList().Count);
        }

        [Test]
        public void AttachDot_NullButton_ReturnsNull() => Assert.IsNull(MenuReminderController.AttachDot(null));

        [Test]
        public void BannerTap_SmallMoveIsATap_SwipeIsNot()
        {
            Assert.IsTrue(BannerSwipe.IsTap(Vector2.zero));
            Assert.IsTrue(BannerSwipe.IsTap(new Vector2(5f, 5f)));
            Assert.IsFalse(BannerSwipe.IsTap(new Vector2(BannerSwipe.DismissDistance, 0f)));
            Assert.IsFalse(BannerSwipe.Dismisses(new Vector2(BannerSwipe.TapSlop, 0f)), "a tap never dismisses as a swipe");
        }

        [Test]
        public void GameHud_HasTheReminderTargetsAndAcceptButton()
        {
            var hud = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");
            Assert.IsNotNull(hud);
            var root = hud.Instantiate();
            foreach (var name in new[] { "btn-achievements", MenuReminderController.HandleButton, "ach-tab-daily",
                                         "ach-tab-weekly", "ach-tab-monthly", "ach-tab-progression" })
                Assert.IsNotNull(root.Q<Button>(name), name);
            Assert.IsNotNull(root.Q<Button>("notification-banner__accept"));
        }
    }
}
