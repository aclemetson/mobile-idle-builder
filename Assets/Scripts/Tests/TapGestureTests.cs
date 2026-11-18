using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// <see cref="TapGesture"/>: which presses on the map count as deliberate taps, and which are pans,
    /// pinches, glide stops or resting fingers. Fixed 20 px slop so the cases don't depend on screen dpi.
    /// </summary>
    [TestFixture]
    public class TapGestureTests
    {
        const float Slop = 20f;
        static readonly Vector2 P = new(500, 800);

        static TapGesture Pressed(bool overUI = false, bool gliding = false)
        {
            var g = new TapGesture(Slop);
            g.Begin(P, 0f, overUI, gliding);
            return g;
        }

        [Test]
        public void StillPress_IsATap() =>
            Assert.IsTrue(Pressed().End(P, 0.15f));

        [Test]
        public void JitterInsideTheSlop_IsATap_EvenWhenItAddsUp()
        {
            var g = Pressed();
            // Back and forth: 30 px of summed movement, but never more than 15 px from the press point.
            g.Track(P + new Vector2(15, 0), 1);
            g.Track(P, 1);
            g.Track(P + new Vector2(0, 15), 1);
            Assert.IsTrue(g.End(P + new Vector2(5, 5), 0.2f));
        }

        [Test]
        public void MovingPastTheSlop_IsAPan_EvenIfItComesBack()
        {
            var g = Pressed();
            g.Track(P + new Vector2(40, 0), 1);
            Assert.IsTrue(g.IsDisqualified);
            Assert.IsFalse(g.End(P, 0.3f), "a pan that returns to where it started is still a pan");
        }

        [Test]
        public void ASecondFinger_IsAPinch_NotATap()
        {
            var g = Pressed();
            g.Track(P, 2);   // second finger down: pinch zoom or rotate
            g.Track(P, 1);   // it lifts first; the primary finger never moved
            Assert.IsFalse(g.End(P, 0.4f), "lifting after a pinch used to read as a tap");
        }

        [Test]
        public void PressWhileTheMapGlides_OnlyStopsTheMap() =>
            Assert.IsFalse(Pressed(gliding: true).End(P, 0.1f));

        [Test]
        public void PressThatStartedOverUI_IsNotATap() =>
            Assert.IsFalse(Pressed(overUI: true).End(P, 0.1f));

        [Test]
        public void LongRest_IsNotATap()
        {
            Assert.IsTrue(Pressed().End(P, TapGesture.MaxTapSeconds));
            Assert.IsFalse(Pressed().End(P, TapGesture.MaxTapSeconds + 0.01f));
        }

        [Test]
        public void ReleaseMovesPastTheSlop_IsNotATap() =>
            Assert.IsFalse(Pressed().End(P + new Vector2(0, 30), 0.1f), "the release point counts too");

        [Test]
        public void EndWithoutBegin_OrAfterCancel_IsNotATap()
        {
            Assert.IsFalse(new TapGesture(Slop).End(P, 0.1f), "a press that began before the mode started");
            var g = Pressed();
            g.Cancel();
            Assert.IsFalse(g.End(P, 0.1f));
        }

        [Test]
        public void Slop_ScalesWithDensity_WithAFloor()
        {
            Assert.AreEqual(TapGesture.MinSlopPixels, TapGesture.SlopFor(0f), "unknown dpi (Editor)");
            Assert.AreEqual(TapGesture.MinSlopPixels, TapGesture.SlopFor(160f), "10 dp at 1x is below the floor");
            Assert.AreEqual(28.125f, TapGesture.SlopFor(450f), 0.01f, "the user's phone");
            Assert.Greater(TapGesture.SlopFor(640f), TapGesture.SlopFor(450f));
        }
    }
}
