using NUnit.Framework;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #282: the building inspector redraws every 0.5s. ElementReuse lets the redraw keep its elements, so an
    /// unchanged layout creates nothing, Buttons stay the same instances (a held tap is not lost) and the scroll
    /// does not jump.
    /// </summary>
    [TestFixture]
    public class ElementReuseTests
    {
        ElementReuse _ui;
        VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _ui   = new ElementReuse();
            _root = new VisualElement();
        }

        // One "row" with a label and a button, n times.
        (Label label, Button button) Draw(int rows, string text = "a", bool enabled = true)
        {
            _ui.Begin(_root);
            (Label, Button) last = default;
            for (int i = 0; i < rows; i++)
            {
                var row = _ui.Take<VisualElement>(_root, "row");
                row.AddToClassList("my-row");
                var l = _ui.Label(row, "name", text, "my-name");
                var b = _ui.Take<Button>(row, "btn");
                b.SetEnabled(enabled);
                last = (l, b);
            }
            _ui.End();
            return last;
        }

        [Test]
        public void SameLayout_CreatesNothing_AndKeepsTheInstances()
        {
            var (l1, b1) = Draw(3);
            Assert.AreEqual(9, _ui.Created);

            var (l2, b2) = Draw(3, text: "b");
            Assert.AreEqual(0, _ui.Created, "an unchanged layout reuses every element");
            Assert.AreSame(b1, b2, "the button under a finger stays the same instance");
            Assert.AreSame(l1, l2);
            Assert.AreEqual("b", l2.text, "values update in place");
        }

        [Test]
        public void FewerRows_TrimsTheRest()
        {
            Draw(3);
            Draw(1);
            Assert.AreEqual(1, _root.childCount);
            Assert.AreEqual(2, _root[0].childCount);
        }

        [Test]
        public void MoreRows_AddsOnlyTheNewOnes()
        {
            Draw(1);
            Draw(2);
            Assert.AreEqual(3, _ui.Created);
            Assert.AreEqual(2, _root.childCount);
        }

        [Test]
        public void DifferentKeyOrType_ReplacesTheElement()
        {
            _ui.Begin(_root);
            var a = _ui.Take<Label>(_root, "x");
            _ui.End();

            _ui.Begin(_root);
            var b = _ui.Take<Label>(_root, "y");
            var c = _ui.Take<Button>(_root, "y");
            _ui.End();

            Assert.AreNotSame(a, b);
            Assert.AreEqual(2, _root.childCount);
            Assert.AreSame(b, _root[0]);
            Assert.AreSame(c, _root[1]);
        }

        [Test]
        public void Reuse_ResetsClassesAndEnabled_ButKeepsBuiltInClasses()
        {
            var (_, b) = Draw(1, enabled: false);
            b.AddToClassList("stale");

            _ui.Begin(_root);
            var row = _ui.Take<VisualElement>(_root, "row");
            _ui.Label(row, "name", "a");
            var again = _ui.Take<Button>(row, "btn");
            _ui.End();

            Assert.AreSame(b, again);
            Assert.IsTrue(again.enabledSelf, "enabled again unless the caller disables it");
            Assert.IsFalse(again.ClassListContains("stale"), "caller classes are reapplied every pass");
            Assert.IsTrue(again.ClassListContains(Button.ussClassName), "Unity's own classes survive");
        }

        [Test]
        public void Own_DropsChildrenNotTakenThisPass()
        {
            _ui.Begin(_root);
            var btn = _ui.Take<Button>(_root, "b");
            _ui.Take<VisualElement>(btn, "icon");
            _ui.End();
            Assert.AreEqual(1, btn.childCount);

            _ui.Begin(_root);
            btn = _ui.Take<Button>(_root, "b");
            _ui.Own(btn);
            _ui.End();
            Assert.AreEqual(0, btn.childCount, "the icon this pass did not ask for is gone");
        }

        [Test]
        public void ARootWithNothingTaken_IsEmptied()
        {
            Draw(2);
            _ui.Begin(_root);
            _ui.End();
            Assert.AreEqual(0, _root.childCount);
        }
    }
}
