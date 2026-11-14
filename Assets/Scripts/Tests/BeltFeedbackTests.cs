using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Belt-drawing feedback helpers (#301a): refusal reasons, detour test, blockers, toast limiter.</summary>
    [TestFixture]
    public class BeltFeedbackTests
    {
        // ---- RefusalReason ----

        [Test]
        public void NoStart_FreeCell_Accepted() =>
            Assert.IsNull(BeltFeedback.RefusalReason(false, false, true, false));

        [Test]
        public void NoStart_BeltCell_Accepted() =>
            Assert.IsNull(BeltFeedback.RefusalReason(false, false, false, true));

        [Test]
        public void NoStart_BlockedCell_ExplainsStart() =>
            Assert.AreEqual(BeltFeedback.BadStartMessage, BeltFeedback.RefusalReason(false, false, false, false));

        [Test]
        public void HasStart_BlockedCell_ExplainsEnd() =>
            Assert.AreEqual(BeltFeedback.BadEndMessage, BeltFeedback.RefusalReason(true, false, false, false));

        [Test]
        public void HasStart_FreeOrBeltEnd_Accepted()
        {
            Assert.IsNull(BeltFeedback.RefusalReason(true, false, true, false));
            Assert.IsNull(BeltFeedback.RefusalReason(true, false, false, true));
        }

        [Test]
        public void RetapStart_OnBelt_Hints_OnFreeCell_Accepted()
        {
            Assert.AreEqual(BeltFeedback.RetapStartMessage, BeltFeedback.RefusalReason(true, true, false, true));
            Assert.IsNull(BeltFeedback.RefusalReason(true, true, true, false));
        }

        [Test]
        public void Messages_FitOneLineBanner()
        {
            Assert.LessOrEqual(BeltFeedback.BadStartMessage.Length, 45);
            Assert.LessOrEqual(BeltFeedback.BadEndMessage.Length, 45);
            Assert.LessOrEqual(BeltFeedback.RetapStartMessage.Length, 45);
        }

        // ---- IsDetour ----

        [Test]
        public void IsDetour_StraightRoute_False() =>
            Assert.IsFalse(BeltFeedback.IsDetour(6, new Vector2Int(0, 0), new Vector2Int(5, 0)));

        [Test]
        public void IsDetour_LongLoop_True() =>
            Assert.IsTrue(BeltFeedback.IsDetour(14, new Vector2Int(0, 0), new Vector2Int(5, 0)));

        [Test]
        public void IsDetour_ExactlyTwiceManhattan_False() =>
            Assert.IsFalse(BeltFeedback.IsDetour(11, new Vector2Int(0, 0), new Vector2Int(5, 0)));

        // ---- BlockingBeltCells ----

        [Test]
        public void BlockingBeltCells_ReturnsBeltsOnTheL_ExcludingEndpoints()
        {
            var belts = new HashSet<Vector2Int> { new(0, 0), new(2, 0), new(5, 0), new(9, 9) };
            var result = BeltFeedback.BlockingBeltCells(
                new Vector2Int(0, 0), new Vector2Int(5, 0), true, (x, y) => belts.Contains(new Vector2Int(x, y)));
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(new Vector2Int(2, 0), result[0]);
        }

        [Test]
        public void BlockingBeltCells_NoBelts_Empty()
        {
            var result = BeltFeedback.BlockingBeltCells(
                new Vector2Int(0, 0), new Vector2Int(3, 3), true, (x, y) => false);
            Assert.AreEqual(0, result.Count);
        }

        // ---- ToastRateLimiter ----

        [Test]
        public void Limiter_SameKeyWithinWindow_Blocked_AfterWindow_Allowed()
        {
            double now = 10.0;
            var lim = new ToastRateLimiter(1.5, () => now);
            Assert.IsTrue(lim.TryAcquire("a"));
            now += 1.0;
            Assert.IsFalse(lim.TryAcquire("a"));
            now += 0.6;
            Assert.IsTrue(lim.TryAcquire("a"));
        }

        [Test]
        public void Limiter_DifferentKeys_Independent()
        {
            double now = 10.0;
            var lim = new ToastRateLimiter(1.5, () => now);
            Assert.IsTrue(lim.TryAcquire("a"));
            Assert.IsTrue(lim.TryAcquire("b"));
        }

        [Test]
        public void Limiter_Reset_ForgetsHistory()
        {
            double now = 10.0;
            var lim = new ToastRateLimiter(1.5, () => now);
            lim.TryAcquire("a");
            lim.Reset();
            Assert.IsTrue(lim.TryAcquire("a"));
        }
    }
}
