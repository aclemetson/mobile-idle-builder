using NUnit.Framework;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A tap on the drawer while it slides in must not open a panel (#256): the second tap of a double tap
    /// on the handle used to land on the nav row that had just slid under the finger.
    /// </summary>
    [TestFixture]
    public class DrawerTapGuardTests
    {
        double _now;
        DrawerTapGuard _guard;

        [SetUp]
        public void SetUp()
        {
            _now   = 100.0;
            _guard = new DrawerTapGuard(0.25, () => _now);
        }

        [Test]
        public void NeverOpened_DoesNotBlock()
        {
            Assert.IsFalse(_guard.ShouldBlock);
        }

        [Test]
        public void JustOpened_Blocks()
        {
            _guard.MarkOpened();
            _now += 0.1;
            Assert.IsTrue(_guard.ShouldBlock);
        }

        [Test]
        public void AfterSlideFinishes_DoesNotBlock()
        {
            _guard.MarkOpened();
            _now += 0.25;
            Assert.IsFalse(_guard.ShouldBlock);
        }

        [Test]
        public void Reopened_BlocksAgain()
        {
            _guard.MarkOpened();
            _now += 5.0;
            _guard.MarkOpened();
            Assert.IsTrue(_guard.ShouldBlock);
        }

        [Test]
        public void Attach_NullDrawer_IsSafe()
        {
            Assert.DoesNotThrow(() => _guard.Attach(null));
            Assert.DoesNotThrow(() => _guard.Attach(new VisualElement()));
        }
    }
}
