using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests for ConveyorPathfinder. The pathfinder is pure (no scene/ECS/input), so the
    /// only fixture needed is an in-memory grid predicate built from a width/height and a set of
    /// blocked cells.
    /// </summary>
    [TestFixture]
    public class ConveyorPathfinderTests
    {
        /// <summary>Builds an "is this cell free for a new segment?" predicate over a bounded grid.</summary>
        private static Func<int, int, bool> Grid(int w, int h, params Vector2Int[] blocked)
        {
            var set = new HashSet<Vector2Int>(blocked);
            return (x, y) => x >= 0 && x < w && y >= 0 && y < h && !set.Contains(new Vector2Int(x, y));
        }

        private static int CountTurns(List<Vector2Int> path)
        {
            int turns = 0;
            for (int i = 2; i < path.Count; i++)
            {
                Vector2Int d1 = path[i - 1] - path[i - 2];
                Vector2Int d2 = path[i]     - path[i - 1];
                if (d1 != d2) turns++;
            }
            return turns;
        }

        private static void AssertContiguous(List<Vector2Int> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                int manhattan = Mathf.Abs(path[i].x - path[i - 1].x) + Mathf.Abs(path[i].y - path[i - 1].y);
                Assert.AreEqual(1, manhattan, $"Cells {path[i - 1]} and {path[i]} are not 4-adjacent");
            }
        }

        // ── BuildOneTurnPath ────────────────────────────────────────────────

        [Test]
        public void BuildOneTurnPath_StraightHorizontal_ReturnsEveryCell()
        {
            var path = ConveyorPathfinder.BuildOneTurnPath(
                new Vector2Int(0, 0), new Vector2Int(3, 0), true, Grid(10, 10));

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) },
                path);
        }

        [Test]
        public void BuildOneTurnPath_Diagonal_ElbowFlagChangesTheCorner()
        {
            var start = new Vector2Int(0, 0);
            var end   = new Vector2Int(2, 3);

            var horizontalFirst = ConveyorPathfinder.BuildOneTurnPath(start, end, true,  Grid(10, 10));
            var verticalFirst   = ConveyorPathfinder.BuildOneTurnPath(start, end, false, Grid(10, 10));

            Assert.IsNotNull(horizontalFirst);
            Assert.IsNotNull(verticalFirst);

            // Horizontal-first travels along X before turning: second cell steps in X.
            Assert.AreEqual(new Vector2Int(1, 0), horizontalFirst[1], "Horizontal-first should step in X first");
            // Vertical-first travels along Y before turning: second cell steps in Y.
            Assert.AreEqual(new Vector2Int(0, 1), verticalFirst[1], "Vertical-first should step in Y first");

            // Both reach the same endpoints with exactly one turn.
            Assert.AreEqual(start, horizontalFirst[0]);
            Assert.AreEqual(end,   horizontalFirst[horizontalFirst.Count - 1]);
            Assert.AreEqual(1, CountTurns(horizontalFirst));
            Assert.AreEqual(1, CountTurns(verticalFirst));
        }

        [Test]
        public void BuildOneTurnPath_BlockedCorner_ReturnsNull()
        {
            // Both the corner cells for a (0,0)->(2,2) L are (2,0) and (0,2). Block both
            // so neither elbow orientation is free.
            var grid = Grid(10, 10, new Vector2Int(2, 0), new Vector2Int(0, 2));

            var horizontalFirst = ConveyorPathfinder.BuildOneTurnPath(
                new Vector2Int(0, 0), new Vector2Int(2, 2), true, grid);

            Assert.IsNull(horizontalFirst, "A blocked corner must make the one-turn path unavailable");
        }

        [Test]
        public void BuildOneTurnPath_EndpointOnOccupiedCell_IsAllowed()
        {
            // End cell is occupied (e.g. an existing belt) so the predicate reports it as not free,
            // but it is a valid endpoint. Intermediates are clear.
            var grid = Grid(10, 10, new Vector2Int(3, 0));

            var path = ConveyorPathfinder.BuildOneTurnPath(
                new Vector2Int(0, 0), new Vector2Int(3, 0), true, grid);

            Assert.IsNotNull(path, "An occupied endpoint must not block a one-turn path");
            Assert.AreEqual(new Vector2Int(3, 0), path[path.Count - 1]);
        }

        // ── FindRoute ───────────────────────────────────────────────────────

        [Test]
        public void FindRoute_OneTurnBlocked_RoutesAroundObstacle()
        {
            // Vertical wall at x=2 for y=0..3, with a gap at y=4. A direct L from (0,0) to (4,0)
            // is blocked, so the route must detour upward and back down.
            var grid = Grid(6, 6,
                new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(2, 3));

            var start = new Vector2Int(0, 0);
            var end   = new Vector2Int(4, 0);

            // Sanity: the simple one-turn paths really are blocked.
            Assert.IsNull(ConveyorPathfinder.BuildOneTurnPath(start, end, true,  grid));
            Assert.IsNull(ConveyorPathfinder.BuildOneTurnPath(start, end, false, grid));

            var route = ConveyorPathfinder.FindRoute(start, end, grid);

            Assert.IsNotNull(route, "BFS must route around the wall");
            Assert.AreEqual(start, route[0]);
            Assert.AreEqual(end,   route[route.Count - 1]);
            AssertContiguous(route);
            for (int i = 1; i < route.Count - 1; i++)
                Assert.IsTrue(grid(route[i].x, route[i].y), $"Intermediate {route[i]} must be free");
        }

        [Test]
        public void FindRoute_WalledOffDestination_ReturnsNull()
        {
            // (4,4) is a corner; block its only two in-bounds neighbours so it cannot be entered.
            var grid = Grid(5, 5, new Vector2Int(3, 4), new Vector2Int(4, 3));

            var route = ConveyorPathfinder.FindRoute(new Vector2Int(0, 0), new Vector2Int(4, 4), grid);

            Assert.IsNull(route, "An unreachable destination must return null");
        }

        [Test]
        public void FindRoute_OpenGrid_MinimizesTurns()
        {
            // On a clear grid the diagonal target is reachable with a single turn; the route must
            // not waste extra bends.
            var route = ConveyorPathfinder.FindRoute(
                new Vector2Int(0, 0), new Vector2Int(3, 3), Grid(10, 10));

            Assert.IsNotNull(route);
            Assert.AreEqual(1, CountTurns(route), "An open L-route should use exactly one turn");
            // Shortest Manhattan distance is 6 steps -> 7 cells.
            Assert.AreEqual(7, route.Count, "Route should be the shortest length (no detours)");
        }

        [Test]
        public void FindRoute_EndpointOnOccupiedCell_StillReached()
        {
            // The end cell is reported as not-free (an existing belt) but must still be reachable.
            var grid = Grid(6, 6, new Vector2Int(4, 0));

            var route = ConveyorPathfinder.FindRoute(new Vector2Int(0, 0), new Vector2Int(4, 0), grid);

            Assert.IsNotNull(route, "An occupied endpoint must still be reachable as the goal");
            Assert.AreEqual(new Vector2Int(4, 0), route[route.Count - 1]);
        }

        // ── Router cost (#301b) ─────────────────────────────────────────────

        [Test]
        public void FindRoute_ThreeByThreeObstacle_GoesAroundWithTwoTurnsAndNoLoop()
        {
            // 3x3 block at x=3..5, y=1..3 sits between (0,2) and (6,2). The cheapest way round is
            // up (or down) 2, across 6, back 2: 10 steps, 2 turns.
            var blocked = new List<Vector2Int>();
            for (int x = 3; x <= 5; x++)
                for (int y = 1; y <= 3; y++)
                    blocked.Add(new Vector2Int(x, y));
            var grid = Grid(8, 6, blocked.ToArray());

            var route = ConveyorPathfinder.FindRoute(new Vector2Int(0, 2), new Vector2Int(6, 2), grid);

            Assert.IsNotNull(route);
            AssertContiguous(route);
            Assert.AreEqual(11, route.Count, "No long loop: 10 steps is the minimum");
            Assert.AreEqual(2, CountTurns(route));
        }

        [Test]
        public void FindRoute_PortFrontOnDirectPath_IsAvoidedWhenAnEquallyShortRouteExists()
        {
            // Horizontal-first L from (0,0) to (4,2) passes (2,0), a port front. A vertical-first L
            // has the same steps and turns, so the penalised cell must be avoided.
            var portFront = new Vector2Int(2, 0);
            var start = new Vector2Int(0, 0);
            var end   = new Vector2Int(4, 2);
            var extra = ConveyorPathfinder.MakeExtraCost(
                new HashSet<Vector2Int> { portFront }, null, start, end);

            var route = ConveyorPathfinder.FindRoute(start, end, Grid(8, 6), extra);

            Assert.IsNotNull(route);
            CollectionAssert.DoesNotContain(route, portFront);
            Assert.AreEqual(7, route.Count, "Still a shortest route");
            Assert.AreEqual(1, CountTurns(route));
        }

        [Test]
        public void FindRoute_PortFrontOnStraightLine_IsDetouredAroundByOneRow()
        {
            // Straight run (0,2)->(6,2) passes a free port front at (3,2). A one-row detour (2 extra
            // steps, 4 extra turns = 140) is cheaper than blocking the port (PortFrontCost 150), so it goes around.
            var portFront = new Vector2Int(3, 2);
            var start = new Vector2Int(0, 2);
            var end   = new Vector2Int(6, 2);
            var extra = ConveyorPathfinder.MakeExtraCost(
                new HashSet<Vector2Int> { portFront }, null, start, end);

            var route = ConveyorPathfinder.FindRoute(start, end, Grid(8, 6), extra);

            Assert.IsNotNull(route);
            AssertContiguous(route);
            CollectionAssert.DoesNotContain(route, portFront);
            Assert.AreEqual(9, route.Count, "One-row detour: 7 cells + 2");
        }

        [Test]
        public void FindRoute_WithExtraCost_UnreachableEndStillReturnsNull()
        {
            var grid  = Grid(5, 5, new Vector2Int(3, 4), new Vector2Int(4, 3));
            var start = new Vector2Int(0, 0);
            var end   = new Vector2Int(4, 4);
            var extra = ConveyorPathfinder.MakeExtraCost(new HashSet<Vector2Int>(), (x, y) => false, start, end);

            Assert.IsNull(ConveyorPathfinder.FindRoute(start, end, grid, extra));
        }

        [Test]
        public void FindRoute_OpenGridWithNoExtraCost_StillStraightOrOneTurnL()
        {
            var straight = ConveyorPathfinder.FindRoute(new Vector2Int(0, 0), new Vector2Int(5, 0), Grid(8, 8));
            Assert.AreEqual(6, straight.Count);
            Assert.AreEqual(0, CountTurns(straight));

            var l = ConveyorPathfinder.FindRoute(new Vector2Int(0, 0), new Vector2Int(5, 3), Grid(8, 8));
            Assert.AreEqual(9, l.Count);
            Assert.AreEqual(1, CountTurns(l));
        }

        [Test]
        public void MakeExtraCost_StartAndEndAreFree_PortFrontsAndNeighboursCost()
        {
            var start = new Vector2Int(1, 1);
            var end   = new Vector2Int(5, 1);
            var fronts = new HashSet<Vector2Int> { start, end, new Vector2Int(3, 1) };
            var building = new Vector2Int(3, 2);
            var extra = ConveyorPathfinder.MakeExtraCost(
                fronts, (x, y) => x == building.x && y == building.y, start, end);

            Assert.AreEqual(0, extra(start.x, start.y), "Start never costs extra");
            Assert.AreEqual(0, extra(end.x, end.y), "End never costs extra, even as a port front");
            Assert.AreEqual(ConveyorPathfinder.PortFrontCost, extra(3, 1), "Port front beats building-adjacent");
            Assert.AreEqual(ConveyorPathfinder.BuildingAdjacentCost, extra(4, 2));
            Assert.AreEqual(0, extra(7, 7));
        }

        [Test]
        public void PathCost_MatchesRouterWeights_AndIgnoresEndpointExtras()
        {
            var path = new List<Vector2Int>
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1)
            };
            Func<int, int, int> extra = (x, y) => 100;

            // 3 steps + 1 turn + one intermediate extra per intermediate cell (2); endpoints excluded.
            long expected = 3 * ConveyorPathfinder.StepCost + ConveyorPathfinder.TurnCost + 2 * 100;
            Assert.AreEqual(expected, ConveyorPathfinder.PathCost(path, extra));
            Assert.AreEqual(long.MaxValue, ConveyorPathfinder.PathCost(null));
        }

        [Test]
        public void OneTurnL_IsKept_WhenItCostsNoMoreThanTheRoute()
        {
            // Mirrors RecomputePath: keep the L unless the route is strictly cheaper.
            var start = new Vector2Int(0, 0);
            var end   = new Vector2Int(4, 2);
            var grid  = Grid(8, 6);
            var l     = ConveyorPathfinder.BuildOneTurnPath(start, end, true, grid);

            var cleanExtra = ConveyorPathfinder.MakeExtraCost(null, null, start, end);
            var route = ConveyorPathfinder.FindRoute(start, end, grid, cleanExtra);
            Assert.LessOrEqual(ConveyorPathfinder.PathCost(l, cleanExtra), ConveyorPathfinder.PathCost(route, cleanExtra));

            // With a port front on the L's straight leg the route becomes strictly cheaper.
            var penal = ConveyorPathfinder.MakeExtraCost(
                new HashSet<Vector2Int> { new Vector2Int(2, 0) }, null, start, end);
            var route2 = ConveyorPathfinder.FindRoute(start, end, grid, penal);
            Assert.Less(ConveyorPathfinder.PathCost(route2, penal), ConveyorPathfinder.PathCost(l, penal));
        }

        [Test]
        public void FindRoute_StartEqualsEnd_ReturnsSingleCell()
        {
            var route = ConveyorPathfinder.FindRoute(
                new Vector2Int(2, 2), new Vector2Int(2, 2), Grid(5, 5));

            Assert.IsNotNull(route);
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(new Vector2Int(2, 2), route[0]);
        }
    }
}
