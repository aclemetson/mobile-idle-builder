using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// <see cref="ConveyorTrace"/>: tracing a belt point by point on a 20x20 grid with walls (buildings) and
    /// existing belts. Covers the joined path, never crossing itself, merging into a belt, undo, rotating
    /// only the last bend, and refusing a tap the placer would reject instead of failing at ✓.
    /// </summary>
    [TestFixture]
    public class ConveyorTraceTests
    {
        private HashSet<Vector2Int> _walls;
        private HashSet<Vector2Int> _belts;
        private string _reject; // what the placer would say about any joined path (null = fine)

        [SetUp]
        public void SetUp()
        {
            _walls  = new HashSet<Vector2Int>();
            _belts  = new HashSet<Vector2Int>();
            _reject = null;
        }

        private ConveyorTrace NewTrace() => new(
            c => c.x >= 0 && c.y >= 0 && c.x < 20 && c.y < 20 && !_walls.Contains(c) && !_belts.Contains(c),
            c => _belts.Contains(c),
            rejectReason: _ => _reject);

        private static Vector2Int V(int x, int y) => new(x, y);

        private static void AssertConnected(List<Vector2Int> path)
        {
            var seen = new HashSet<Vector2Int>();
            for (int i = 0; i < path.Count; i++)
            {
                Assert.IsTrue(seen.Add(path[i]), $"cell {path[i]} appears twice");
                if (i > 0)
                    Assert.AreEqual(1, Mathf.Abs(path[i].x - path[i - 1].x) + Mathf.Abs(path[i].y - path[i - 1].y),
                        $"{path[i - 1]} -> {path[i]} is not one step");
            }
        }

        [Test]
        public void TwoTaps_MakeTheSameBeltAsTheOldStartEndPlacement()
        {
            var t = NewTrace();
            Assert.AreEqual(ConveyorTrace.AddResult.Started, t.TryAdd(V(2, 2)));
            Assert.AreEqual(ConveyorTrace.AddResult.Added,   t.TryAdd(V(6, 5)));

            var expected = ConveyorPathfinder.BuildOneTurnPath(V(2, 2), V(6, 5), true, (x, y) => true);
            CollectionAssert.AreEqual(expected, t.Path());
        }

        [Test]
        public void ThreeAndFourPoints_JoinIntoOneBelt_EachJointOnce()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(5, 1));
            t.TryAdd(V(5, 6));
            Assert.AreEqual(ConveyorTrace.AddResult.Added, t.TryAdd(V(9, 6)));

            var path = t.Path();
            AssertConnected(path);
            Assert.AreEqual(V(1, 1), path[0]);
            Assert.AreEqual(V(9, 6), path[path.Count - 1]);
            Assert.AreEqual(4 + 5 + 4 + 1, path.Count, "three straight runs sharing their joints");
            Assert.AreEqual(3, t.SegmentCount);
        }

        [Test]
        public void ALaterSegment_NeverRoutesBackThroughTheTrace()
        {
            var t = NewTrace();
            t.TryAdd(V(5, 1));
            t.TryAdd(V(5, 8));   // a straight run up column 5
            t.TryAdd(V(3, 8));   // step left at the top
            // (7,3) is on the far side of the column: both straight Ls would cross it.
            Assert.AreEqual(ConveyorTrace.AddResult.Added, t.TryAdd(V(7, 3)));

            var path = t.Path();
            AssertConnected(path); // no cell twice: it went around the column instead
        }

        [Test]
        public void TappingACellAlreadyOnTheTrace_IsRefused()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(6, 1));
            Assert.AreEqual(ConveyorTrace.AddResult.CrossesItself, t.TryAdd(V(3, 1)));
            Assert.AreEqual(ConveyorTrace.AddResult.CrossesItself, t.TryAdd(V(1, 1)));
            Assert.AreEqual(1, t.SegmentCount, "a refused tap changes nothing");
        }

        [Test]
        public void TappingTheLastPointAgain_DoesNothing()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(4, 1));
            Assert.AreEqual(ConveyorTrace.AddResult.SameAsLast, t.TryAdd(V(4, 1)));
            Assert.AreEqual(2, t.Nodes.Count);
        }

        [Test]
        public void APointOnAnExistingBelt_MergesAndEndsTheTrace()
        {
            _belts.Add(V(8, 1));
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            Assert.AreEqual(ConveyorTrace.AddResult.Merged, t.TryAdd(V(8, 1)));
            Assert.IsTrue(t.IsEnded);
            Assert.AreEqual(ConveyorTrace.AddResult.AlreadyEnded, t.TryAdd(V(8, 5)));
        }

        [Test]
        public void StartingOnAnExistingBelt_IsAllowedAndDoesNotEndTheTrace()
        {
            _belts.Add(V(1, 1));
            var t = NewTrace();
            Assert.AreEqual(ConveyorTrace.AddResult.Started, t.TryAdd(V(1, 1)));
            Assert.IsFalse(t.IsEnded);
            Assert.AreEqual(ConveyorTrace.AddResult.Added, t.TryAdd(V(4, 1)));
        }

        [Test]
        public void ABuildingCell_IsNotAPoint()
        {
            _walls.Add(V(4, 4));
            var t = NewTrace();
            Assert.AreEqual(ConveyorTrace.AddResult.NotEndpoint, t.TryAdd(V(4, 4)));
            t.TryAdd(V(1, 4));
            Assert.AreEqual(ConveyorTrace.AddResult.NotEndpoint, t.TryAdd(V(4, 4)));
        }

        [Test]
        public void AWalledOffPoint_IsUnreachable()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            // Box in (10,10) on all four sides.
            _walls.Add(V(9, 10)); _walls.Add(V(11, 10)); _walls.Add(V(10, 9)); _walls.Add(V(10, 11));
            Assert.AreEqual(ConveyorTrace.AddResult.Unreachable, t.TryAdd(V(10, 10)));
        }

        [Test]
        public void ATapThePlacerWouldReject_IsRefusedWhenTapped_NotAtConfirm()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            _reject = "A belt can't split. Pick another path.";
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(5, 1)));
            Assert.AreEqual(_reject, t.LastRejectReason);
            Assert.AreEqual(0, t.SegmentCount);
        }

        [Test]
        public void Undo_RestoresThePreviousBelt_ThenTheStart_ThenNothing()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(5, 1));
            var twoPoints = t.Path();
            t.TryAdd(V(5, 6));

            Assert.IsTrue(t.Undo());
            CollectionAssert.AreEqual(twoPoints, t.Path());
            Assert.IsTrue(t.Undo());
            Assert.AreEqual(1, t.Nodes.Count);
            Assert.IsTrue(t.Undo());
            Assert.IsFalse(t.HasStart);
            Assert.IsFalse(t.Undo());
        }

        [Test]
        public void Undo_AfterAMerge_ReopensTheTrace()
        {
            _belts.Add(V(8, 1));
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(8, 1));
            t.Undo();
            Assert.IsFalse(t.IsEnded);
            Assert.AreEqual(ConveyorTrace.AddResult.Added, t.TryAdd(V(1, 6)));
        }

        [Test]
        public void Rotate_BendsOnlyTheLastSegment()
        {
            var t = NewTrace();
            t.TryAdd(V(1, 1));
            t.TryAdd(V(4, 4));   // segment 0: an L
            t.TryAdd(V(8, 8));   // segment 1: an L
            var before = t.Path();
            var firstSegment = before.GetRange(0, 7);

            Assert.IsTrue(t.FlipLastBend());
            var after = t.Path();
            CollectionAssert.AreEqual(firstSegment, after.GetRange(0, 7), "the earlier segment is untouched");
            CollectionAssert.AreNotEqual(before, after, "the last L bends the other way");
            AssertConnected(after);
            Assert.AreEqual(V(8, 8), after[after.Count - 1]);
        }

        [Test]
        public void Rotate_WithNoSegment_DoesNothing()
        {
            var t = NewTrace();
            Assert.IsFalse(t.FlipLastBend());
            t.TryAdd(V(1, 1));
            Assert.IsFalse(t.FlipLastBend());
        }

        [Test]
        public void ConveyorBarLabel_FollowsTheTrace()
        {
            Assert.AreEqual("Tap a belt to remove",          HudText.ConveyorBarLabel(true,  true,  true,  false));
            Assert.AreEqual("Tap a start",                   HudText.ConveyorBarLabel(false, false, false, false));
            Assert.AreEqual("Tap the next point",            HudText.ConveyorBarLabel(false, true,  false, false));
            Assert.AreEqual("Tap the next point, or place",  HudText.ConveyorBarLabel(false, true,  true,  false));
            Assert.AreEqual("Joins a belt: place it",        HudText.ConveyorBarLabel(false, true,  true,  true));
        }
    }
}
