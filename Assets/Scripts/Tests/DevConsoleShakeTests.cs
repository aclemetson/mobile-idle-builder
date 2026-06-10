#if UNITY_EDITOR
using NUnit.Framework;
using MobileIdleBuilder.Dev;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class DevConsoleShakeTests
    {
        private ShakePeakDetector Make() =>
            new ShakePeakDetector(threshold: 2.5f, window: 1.5f, peaksRequired: 3);

        // ── Happy path ────────────────────────────────────────────────────────

        [Test]
        public void ThreePeaksWithinWindow_Triggers()
        {
            var d = Make();
            Assert.IsFalse(d.Feed(3f, 0.0f));   // peak 1
            Assert.IsFalse(d.Feed(1f, 0.2f));   // fall below
            Assert.IsFalse(d.Feed(3f, 0.4f));   // peak 2
            Assert.IsFalse(d.Feed(1f, 0.6f));
            Assert.IsTrue (d.Feed(3f, 0.8f),    // peak 3 — should trigger
                "Three peaks within 1.5 s should trigger the console");
        }

        [Test]
        public void TriggerResetsQueue_SecondTriggeredByFreshPeaks()
        {
            var d = Make();
            d.Feed(3f, 0f); d.Feed(1f, 0.2f);
            d.Feed(3f, 0.4f); d.Feed(1f, 0.6f);
            d.Feed(3f, 0.8f); // first trigger (cleared)

            // Now accumulate 3 more fresh peaks
            d.Feed(1f, 1.0f);
            Assert.IsFalse(d.Feed(3f, 1.2f)); // peak 1
            d.Feed(1f, 1.3f);
            Assert.IsFalse(d.Feed(3f, 1.5f)); // peak 2
            d.Feed(1f, 1.6f);
            Assert.IsTrue (d.Feed(3f, 1.8f), // peak 3
                "Detector should be retriggerable after the queue is cleared");
        }

        // ── Window expiry ─────────────────────────────────────────────────────

        [Test]
        public void OldPeaksExpireBeforeThirdArrives_NoTrigger()
        {
            var d = Make();
            d.Feed(3f, 0.0f); d.Feed(1f, 0.1f); // peak 1 at t=0
            d.Feed(3f, 0.5f); d.Feed(1f, 0.6f); // peak 2 at t=0.5

            // Third peak arrives after the window; peak 1 has expired (1.8 - 0.0 > 1.5)
            Assert.IsFalse(d.Feed(3f, 1.8f),
                "Peaks older than the window should be discarded, preventing a trigger");
        }

        [Test]
        public void ExactlyAtWindowBoundary_OldPeakDiscarded()
        {
            var d = Make();
            d.Feed(3f, 0.0f); d.Feed(1f, 0.1f); // peak 1 at t=0
            d.Feed(3f, 0.5f); d.Feed(1f, 0.6f); // peak 2 at t=0.5

            // Third peak at t=1.501: peak 1 is exactly expired (1.501 - 0 > 1.5)
            Assert.IsFalse(d.Feed(3f, 1.501f),
                "A peak exactly outside the window should not count toward the required total");
        }

        // ── Edge cases ────────────────────────────────────────────────────────

        [Test]
        public void BelowThreshold_NeverTriggers()
        {
            var d = Make();
            for (int i = 0; i < 20; i++)
            {
                bool result = d.Feed(2.4f, i * 0.1f); // always below 2.5
                Assert.IsFalse(result, $"Below-threshold feed should never trigger (frame {i})");
            }
        }

        [Test]
        public void OnlyTwoPeaks_DoesNotTrigger()
        {
            var d = Make();
            d.Feed(3f, 0f); d.Feed(1f, 0.2f);
            d.Feed(3f, 0.4f); d.Feed(1f, 0.6f);
            Assert.IsFalse(d.Feed(1f, 0.8f),
                "Two peaks are not enough to trigger");
        }

        [Test]
        public void Reset_ClearsPendingPeaks()
        {
            var d = Make();
            d.Feed(3f, 0f); d.Feed(1f, 0.2f);
            d.Feed(3f, 0.4f); d.Feed(1f, 0.6f); // 2 peaks accumulated
            d.Reset();

            // After reset, the same two peaks followed by a third should NOT trigger
            // unless three fresh peaks come in (the previous two are gone).
            Assert.IsFalse(d.Feed(3f, 0.8f), "After Reset, previous peaks should be discarded");
        }

        [Test]
        public void RisingEdgeOnly_Counts_NotContinuousAboveThreshold()
        {
            var d = Make();
            // Stay above threshold for many frames without crossing back down
            for (int i = 0; i < 10; i++)
                Assert.IsFalse(d.Feed(3f, i * 0.1f),
                    $"Continuous high reading should only count as one peak (frame {i})");
        }
    }
}
#endif
