using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for UIInputBlocker.IsBlockingElement — the pure ancestor-walking
    /// predicate that decides whether a picked VisualElement should swallow world input.
    /// The screen-space hit test (IsPointerOverUI) needs a live panel and can't run in
    /// edit mode, but the classification logic is the part that actually decides whether
    /// a tap/pan starting on a menu reaches the world, so that's what we lock down here.
    /// </summary>
    [TestFixture]
    public class UIInputBlockerTests
    {
        [Test]
        public void IsBlockingElement_Null_ReturnsFalse()
        {
            Assert.IsFalse(UIInputBlocker.IsBlockingElement(null));
        }

        [Test]
        public void IsBlockingElement_BareElement_ReturnsFalse()
        {
            Assert.IsFalse(UIInputBlocker.IsBlockingElement(new VisualElement()));
        }

        [Test]
        public void IsBlockingElement_UnrelatedClass_ReturnsFalse()
        {
            var ve = new VisualElement();
            ve.AddToClassList("some-decorative-thing");
            Assert.IsFalse(UIInputBlocker.IsBlockingElement(ve));
        }

        [Test]
        public void IsBlockingElement_MarkerClass_ReturnsTrue()
        {
            var ve = new VisualElement();
            ve.AddToClassList(UIInputBlocker.MarkerClass);
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(ve));
        }

        [Test]
        public void IsBlockingElement_SlidePanel_ReturnsTrue()
        {
            var ve = new VisualElement();
            ve.AddToClassList("slide-panel");
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(ve));
        }

        [Test]
        public void IsBlockingElement_ChildOfSlidePanel_ReturnsTrue()
        {
            var panel = new VisualElement();
            panel.AddToClassList("slide-panel");
            var child = new VisualElement();
            panel.Add(child);
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(child));
        }

        [Test]
        public void IsBlockingElement_ChildOfTopBar_ReturnsTrue()
        {
            var bar = new VisualElement();
            bar.AddToClassList("top-bar");
            var label = new Label("currency");
            bar.Add(label);
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(label));
        }

        [Test]
        public void IsBlockingElement_Button_ReturnsTrue()
        {
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(new Button()));
        }

        [Test]
        public void IsBlockingElement_DevConsole_ReturnsTrue()
        {
            var ve = new VisualElement();
            ve.AddToClassList("dev-console");
            Assert.IsTrue(UIInputBlocker.IsBlockingElement(ve));
        }

        // ── Modal short-circuit ──────────────────────────────────────────────
        // A registered modal blocks all world input regardless of panels/position.

        [Test]
        public void SetModal_WhenActive_IsPointerOverUI_ReturnsTrue()
        {
            var owner = new object();
            try
            {
                UIInputBlocker.SetModal(owner, true);
                Assert.IsTrue(UIInputBlocker.IsPointerOverUI(new Vector2(123f, 456f)));
            }
            finally
            {
                UIInputBlocker.SetModal(owner, false);
            }
        }

        [Test]
        public void SetModal_AfterCleared_NoPanels_ReturnsFalse()
        {
            var owner = new object();
            UIInputBlocker.SetModal(owner, true);
            UIInputBlocker.SetModal(owner, false);
            // No registered panels in the test rig, so with the modal cleared this is false.
            Assert.IsFalse(UIInputBlocker.IsPointerOverUI(new Vector2(123f, 456f)));
        }
    }
}
