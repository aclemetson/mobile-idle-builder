using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class TutorialRouteGuideTests
    {
        // ── Port fronts (ConveyorSystem convention) ──────────────────────────

        [Test]
        public void OutputFront_IsTheCellTheFacingPointsAt()
            => Assert.AreEqual(new Vector2Int(3, 5), TutorialRouteGuide.OutputFront(new Vector2Int(3, 4), OutputDirection.North));

        [Test]
        public void InputFront_IsTheCellBehindTheFacing()
            // Items travel North into the port, so the belt sits to the South.
            => Assert.AreEqual(new Vector2Int(3, 3), TutorialRouteGuide.InputFront(new Vector2Int(3, 4), OutputDirection.North));

        // ── TryPickRoute ─────────────────────────────────────────────────────

        [Test]
        public void TryPickRoute_PicksTheClosestFreePair()
        {
            var starts = new List<Vector2Int> { new(0, 0), new(5, 5) };
            var ends   = new List<Vector2Int> { new(6, 5), new(20, 20) };
            Assert.IsTrue(TutorialRouteGuide.TryPickRoute(starts, ends, _ => true, out var s, out var e));
            Assert.AreEqual(new Vector2Int(5, 5), s);
            Assert.AreEqual(new Vector2Int(6, 5), e);
        }

        [Test]
        public void TryPickRoute_SkipsStartsThatAlreadyHaveABelt()
        {
            // The electron harvester's front already carries a belt; guide to the quark harvester.
            var routed = new Vector2Int(5, 5);
            var starts = new List<Vector2Int> { routed, new(1, 1) };
            var ends   = new List<Vector2Int> { new(6, 5) };
            Assert.IsTrue(TutorialRouteGuide.TryPickRoute(starts, ends, c => c != routed, out var s, out _));
            Assert.AreEqual(new Vector2Int(1, 1), s);
        }

        [Test]
        public void TryPickRoute_NothingFree_ReturnsFalse()
        {
            var starts = new List<Vector2Int> { new(0, 0) };
            var ends   = new List<Vector2Int> { new(1, 0) };
            Assert.IsFalse(TutorialRouteGuide.TryPickRoute(starts, ends, _ => false, out _, out _));
        }

        // ── SuggestAnchor ────────────────────────────────────────────────────

        // Strong Force Combiner shape: 2x1, inputs on both cells from the South, output North of (0,0).
        private static readonly Vector2Int Fp = new(2, 1);
        private static readonly List<Vector2Int> In  = new() { new(0, -1), new(1, -1) };
        private static readonly List<Vector2Int> Out = new() { new(0, 1) };

        [Test]
        public void SuggestAnchor_SitsBetweenSourcesAndSink()
        {
            // Quark harvester outputs at the bottom, the demon's input above them.
            var sources = new List<Vector2Int> { new(4, 0), new(5, 0) };
            var sinks   = new List<Vector2Int> { new(4, 8) };

            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out, sources, sinks, _ => true, _ => true);

            Assert.IsTrue(a.HasValue);
            Assert.AreEqual(4, a.Value.x, "inputs line up with the harvesters");
            // Row 1 would put the inputs on the harvester fronts themselves (a one-tile "belt"), so
            // the closest real route leaves one belt tile between them (#236).
            Assert.AreEqual(2, a.Value.y);
        }

        [Test]
        public void SuggestAnchor_EqualRoutes_PicksTheSpotBalancedBetweenSourceAndSink()
        {
            // One source below, the sink straight above: every row in between costs the same belt.
            // The tie goes to the middle, not the spot hugging the source (#236).
            var sources = new List<Vector2Int> { new(0, 0) };
            var sinks   = new List<Vector2Int> { new(0, 8) };

            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out, sources, sinks, _ => true, _ => true);

            Assert.AreEqual(new Vector2Int(0, 4), a);
        }

        [Test]
        public void SuggestAnchor_EachSourceNeedsItsOwnInput()
        {
            // Two sources side by side. With one belt per input, the closest spot puts the two inputs
            // directly above them, one belt tile up (inputs at (2,1) and (3,1)).
            var sources = new List<Vector2Int> { new(2, 0), new(3, 0) };
            bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < 10 && c.y < 10;
            // The harvester fronts carry the start of each belt, so nothing can be built on them.
            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out, sources,
                new List<Vector2Int>(), c => InBounds(c) && !sources.Contains(c), InBounds);

            Assert.AreEqual(new Vector2Int(2, 2), a);
        }

        // ── CountLinkedSources ───────────────────────────────────────────────

        private static Dictionary<Vector2Int, Vector2Int?> Belt(params Vector2Int[] cells)
        {
            var d = new Dictionary<Vector2Int, Vector2Int?>();
            for (int i = 0; i < cells.Length; i++)
                d[cells[i]] = i + 1 < cells.Length ? cells[i + 1] : (Vector2Int?)null;
            return d;
        }

        [Test]
        public void CountLinkedSources_CountsABeltWhoseTailIsOnTheInputFront()
        {
            var fronts = new List<(int, Vector2Int)> { (1, new(1, 0)) };
            var inputs = new HashSet<Vector2Int> { new(3, 0) };
            Assert.AreEqual(1, TutorialRouteGuide.CountLinkedSources(fronts, inputs, Belt(new(1, 0), new(2, 0), new(3, 0))));
            Assert.AreEqual(0, TutorialRouteGuide.CountLinkedSources(fronts, inputs, Belt(new(1, 0), new(2, 0))));
        }

        [Test]
        public void CountLinkedSources_ALoopedBeltTerminates()
        {
            var belts = new Dictionary<Vector2Int, Vector2Int?>
                { [new(0, 0)] = new Vector2Int(1, 0), [new(1, 0)] = new Vector2Int(0, 0) };
            var fronts = new List<(int, Vector2Int)> { (1, new(0, 0)) };
            Assert.AreEqual(0, TutorialRouteGuide.CountLinkedSources(fronts, new HashSet<Vector2Int> { new(5, 5) }, belts));
        }

        [Test]
        public void SuggestAnchor_AvoidsUnbuildableCells()
        {
            var sources = new List<Vector2Int> { new(4, 0), new(5, 0) };
            var sinks   = new List<Vector2Int> { new(4, 8) };
            var blocked = new HashSet<Vector2Int> { new(4, 1), new(5, 1) };

            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out, sources, sinks,
                c => !blocked.Contains(c), _ => true);

            Assert.IsTrue(a.HasValue);
            Assert.IsFalse(blocked.Contains(a.Value) || blocked.Contains(a.Value + Vector2Int.right));
        }

        [Test]
        public void SuggestAnchor_PortBeltCellsMustBeFreeAndInBounds()
        {
            // Row 0 would put the input belts at y = -1, outside the grid.
            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out,
                new List<Vector2Int>(), new List<Vector2Int>(),
                _ => true, c => c.x >= 0 && c.y >= 0 && c.x < 10 && c.y < 10);
            Assert.IsTrue(a.HasValue);
            Assert.GreaterOrEqual(a.Value.y, 1);
        }

        [Test]
        public void SuggestAnchor_NothingFits_ReturnsNull()
        {
            var a = TutorialRouteGuide.SuggestAnchor(10, 10, Fp, In, Out,
                new List<Vector2Int>(), new List<Vector2Int>(), _ => false, _ => true);
            Assert.IsNull(a);
        }

        // ── Target id parsing ────────────────────────────────────────────────

        [Test]
        public void TryParseRoute_ReadsFromAndTo()
        {
            Assert.IsTrue(TutorialRouteGuide.TryParseRoute("route:harvester>maxwells_demon", out var f, out var t));
            Assert.AreEqual("harvester", f);
            Assert.AreEqual("maxwells_demon", t);
            Assert.IsFalse(TutorialRouteGuide.TryParseRoute("quark_field", out _, out _));
            Assert.IsFalse(TutorialRouteGuide.TryParseRoute("route:harvester", out _, out _));
        }

        [Test]
        public void TryParseSuggest_ReadsBuildingFromAndTo()
        {
            Assert.IsTrue(TutorialRouteGuide.TryParseSuggest(
                "suggest:strong_force_combiner:harvester>maxwells_demon", out var b, out var f, out var t));
            Assert.AreEqual("strong_force_combiner", b);
            Assert.AreEqual("harvester", f);
            Assert.AreEqual("maxwells_demon", t);
            Assert.IsFalse(TutorialRouteGuide.TryParseSuggest("suggest:combiner", out _, out _, out _));
        }
    }
}
