using NUnit.Framework;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Tests for ListTapRouter's registry and ancestor walk - the part that decides WHICH button a
    /// release belongs to. Deliberately does not try to simulate pointer events: dispatching real
    /// UITK input needs a live panel and an EventSystem, and the event plumbing is exactly what the
    /// device/Editor logs already established. What is worth pinning here is the resolution rule.
    /// </summary>
    public class ListTapRouterTests
    {
        ScrollView _list;

        [SetUp]
        public void SetUp()
        {
            ListTapRouter.Clear();
            _list = new ScrollView();
        }

        [TearDown]
        public void TearDown() => ListTapRouter.Clear();

        [Test]
        public void Register_ResolvesTheButtonToItsAction()
        {
            bool fired = false;
            var btn = new Button();
            ListTapRouter.Register(_list, btn, () => fired = true);

            ListTapRouter.Resolve(btn)?.Invoke();

            Assert.IsTrue(fired, "A release on the registered button must resolve to its action");
        }

        [Test]
        public void Resolve_WalksUpFromAChildOfTheButton()
        {
            // Real rows put an icon and a text label inside the button, so the release often lands
            // on a child element rather than the button itself.
            bool fired = false;
            var btn = new Button();
            var icon = new VisualElement();
            btn.Add(icon);
            ListTapRouter.Register(_list, btn, () => fired = true);

            ListTapRouter.Resolve(icon)?.Invoke();

            Assert.IsTrue(fired, "A tap landing on a button's child must still resolve to the button");
        }

        [Test]
        public void Resolve_ReturnsNullForADisabledButton()
        {
            var btn = new Button();
            ListTapRouter.Register(_list, btn, () => Assert.Fail("A disabled button must not fire"));
            btn.SetEnabled(false);

            Assert.IsNull(ListTapRouter.Resolve(btn),
                "A disabled button must swallow the tap rather than fall through to what is behind it");
        }

        [Test]
        public void Resolve_ReturnsNullForAnUnregisteredElement()
        {
            ListTapRouter.Register(_list, new Button(), () => Assert.Fail("wrong button"));

            Assert.IsNull(ListTapRouter.Resolve(new VisualElement()));
            Assert.IsNull(ListTapRouter.Resolve(null));
        }

        [Test]
        public void Resolve_PicksTheNearestRegisteredAncestor()
        {
            // Nested registrations should resolve to the innermost one, not the outer container.
            var outer = new Button();
            var inner = new Button();
            outer.Add(inner);

            string hit = null;
            ListTapRouter.Register(_list, outer, () => hit = "outer");
            ListTapRouter.Register(_list, inner, () => hit = "inner");

            ListTapRouter.Resolve(inner)?.Invoke();

            Assert.AreEqual("inner", hit, "The nearest registered ancestor wins");
        }

        [Test]
        public void Register_StripsTheBuiltInClickable()
        {
            // The whole reason the old manipulator failed: Button's Clickable consumes the
            // pointer-down before a later-registered handler runs. The router must remove it.
            var btn = new Button();
            Assert.IsNotNull(btn.clickable, "sanity: a fresh Button ships with a Clickable");

            ListTapRouter.Register(_list, btn, () => { });

            Assert.IsNull(btn.clickable,
                "Register must clear the built-in Clickable, or it eats the gesture first");
        }

        [Test]
        public void Register_LastRegistrationWinsForTheSameButton()
        {
            string hit = null;
            var btn = new Button();
            ListTapRouter.Register(_list, btn, () => hit = "first");
            ListTapRouter.Register(_list, btn, () => hit = "second");

            ListTapRouter.Resolve(btn)?.Invoke();

            Assert.AreEqual("second", hit,
                "Lists are rebuilt constantly; a re-registration must replace, not stack");
        }

        [Test]
        public void Clear_DropsEveryRegistration()
        {
            var btn = new Button();
            ListTapRouter.Register(_list, btn, () => Assert.Fail("cleared registration must not fire"));

            ListTapRouter.Clear();

            Assert.IsNull(ListTapRouter.Resolve(btn));
        }

        [Test]
        public void Register_IgnoresNullButtonAndNullAction()
        {
            Assert.DoesNotThrow(() => ListTapRouter.Register(_list, null, () => { }));
            Assert.DoesNotThrow(() => ListTapRouter.Register(_list, new Button(), null));
        }
    }
}
