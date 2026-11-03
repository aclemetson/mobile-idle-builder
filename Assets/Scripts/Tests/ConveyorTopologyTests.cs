using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Pure unit tests for the conveyor connectivity solver: merge detection (2-3 inbounds → one
    /// output), head detection, placement validation (fork / merge-cap rejection), and the auto-turn
    /// decisions. No ECS or scene needed.
    /// </summary>
    [TestFixture]
    public class ConveyorTopologyTests
    {
        private const int N = (int)OutputDirection.North;
        private const int E = (int)OutputDirection.East;
        private const int S = (int)OutputDirection.South;
        private const int W = (int)OutputDirection.West;

        private static int2 C(int x, int y) => new int2(x, y);

        // ── Merge / head detection ───────────────────────────────────────────

        [Test]
        public void Compute_TwoInbounds_MergeIntoOneOutput()
        {
            // (0,0)→E and (1,1)→S both flow into (1,0), which outputs E to the tail (2,0).
            var exit = new Dictionary<int2, int>
            {
                { C(0, 0), E },
                { C(1, 1), S },
                { C(1, 0), E },
                { C(2, 0), E },
            };

            var links = ConveyorTopology.Compute(exit);

            var merge = links[C(1, 0)];
            Assert.AreEqual(2, merge.InboundCount, "merge cell takes two inbounds");
            Assert.IsFalse(merge.IsHead);
            Assert.IsTrue(merge.HasOutput);
            Assert.AreEqual(C(2, 0), merge.OutputCell, "merge cell still has exactly one output");

            Assert.IsTrue(links[C(0, 0)].IsHead, "a source with no inbound is a head");
            Assert.IsTrue(links[C(1, 1)].IsHead);
            Assert.AreEqual(1, links[C(2, 0)].InboundCount);
            Assert.IsFalse(links[C(2, 0)].HasOutput, "the tail outputs to no conveyor");
        }

        [Test]
        public void Compute_ThreeInbounds_AllCounted()
        {
            // W,S,E neighbours all flow into (1,1); its own exit goes north.
            var exit = new Dictionary<int2, int>
            {
                { C(0, 1), E },
                { C(1, 0), N },
                { C(2, 1), W },
                { C(1, 1), N },
            };

            var links = ConveyorTopology.Compute(exit);

            Assert.AreEqual(3, links[C(1, 1)].InboundCount);
            Assert.IsFalse(links[C(1, 1)].IsHead);
        }

        [Test]
        public void Compute_BlockedOutput_DoesNotFeedOrMerge()
        {
            // (0,0)→E points straight at (1,0), but (0,0) is a blocked dead-end (a belt drawn up to,
            // but not onto, an existing belt). No link must form in either direction.
            var exit = new Dictionary<int2, int> { { C(0, 0), E }, { C(1, 0), E } };
            var blocked = new HashSet<int2> { C(0, 0) };

            var links = ConveyorTopology.Compute(exit, blocked);

            Assert.IsFalse(links[C(0, 0)].HasOutput, "a blocked cell has no output");
            Assert.AreEqual(0, links[C(1, 0)].InboundCount, "a blocked neighbour is not counted as an inbound");
            Assert.IsTrue(links[C(1, 0)].IsHead, "with no real inbound the downstream belt is its own head");
        }

        // ── Placement validation ─────────────────────────────────────────────

        [Test]
        public void CanPlace_OverwritingStartCellOutput_IsAllowed()
        {
            // The player starts a new run ON the existing belt (1,0) (which outputs East to a live
            // successor) and draws North. The START cell is intentionally overwritten to bend into the
            // new run, so this is allowed — its old eastward downstream is severed.
            var exit = new Dictionary<int2, int> { { C(1, 0), E }, { C(2, 0), E } };
            var path = new[] { C(1, 0), C(1, 1) };

            Assert.IsTrue(ConveyorTopology.CanPlace(exit, path, 3, out _),
                "starting a run on an existing belt may overwrite that cell's output direction");
        }

        [Test]
        public void CanPlace_ForkingAPassThroughCell_IsRejected()
        {
            // The run passes THROUGH the existing belt (1,0) (i >= 1, not the start) which outputs East
            // to a live successor (2,0), but the route turns it West — that would fork/hijack a belt the
            // route merely crosses, so it is rejected.
            var exit = new Dictionary<int2, int> { { C(1, 0), E }, { C(2, 0), E } };
            var path = new[] { C(1, 1), C(1, 0), C(0, 0) };

            Assert.IsFalse(ConveyorTopology.CanPlace(exit, path, 3, out string reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void CanPlace_MergingIntoExistingCell_IsAllowed()
        {
            // A new run ending at the existing belt (1,0) just adds an inbound — allowed.
            var exit = new Dictionary<int2, int> { { C(1, 0), E }, { C(2, 0), E } };
            var path = new[] { C(1, 1), C(1, 0) };

            Assert.IsTrue(ConveyorTopology.CanPlace(exit, path, 3, out _));
        }

        [Test]
        public void CanPlace_FourthInbound_IsRejected()
        {
            // (1,1) already has three inbounds (W,S,E); routing a fourth from the north exceeds the cap.
            var exit = new Dictionary<int2, int>
            {
                { C(0, 1), E },
                { C(1, 0), N },
                { C(2, 1), W },
                { C(1, 1), N },
            };
            var path = new[] { C(1, 2), C(1, 1) };

            Assert.IsFalse(ConveyorTopology.CanPlace(exit, path, 3, out string reason));
            Assert.IsNotNull(reason);
        }

        // ── Auto-turn ────────────────────────────────────────────────────────

        [Test]
        public void TailExit_TurnsTowardAdjacentInput()
        {
            // Flowing north, but a building input faces this cell from the east → turn the output east.
            int newExit = ConveyorAutoTurn.TailExit(N, dir => dir == E);
            Assert.AreEqual(E, newExit);
        }

        [Test]
        public void TailExit_NoAdjacentInput_KeepsExit()
        {
            Assert.AreEqual(S, ConveyorAutoTurn.TailExit(S, _ => false));
        }

        [Test]
        public void HeadEntry_ReceivesFromAdjacentOutput()
        {
            // A building output faces this head from the east → the current enters travelling west.
            int newEntry = ConveyorAutoTurn.HeadEntry(N, dir => dir == E);
            Assert.AreEqual(W, newEntry);
        }

        [Test]
        public void HeadEntry_NoAdjacentOutput_KeepsEntry()
        {
            Assert.AreEqual(S, ConveyorAutoTurn.HeadEntry(S, _ => false));
        }
    }
}
