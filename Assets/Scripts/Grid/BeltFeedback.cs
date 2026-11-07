using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure helpers behind the belt-drawing feedback (#301a): why a tap was refused, which existing
    /// belts block the straight run, and when a route counts as a long detour. No scene access, so
    /// all of it is unit-testable.
    /// </summary>
    public static class BeltFeedback
    {
        public const string BadStartMessage     = "Start on a free tile or a building output";
        public const string BadEndMessage       = "Can't end there";
        public const string RetapStartMessage   = "Tap another tile to set the end";
        public const string ContinuingMessage   = "Continuing from the last belt. Tap New to start elsewhere.";

        /// <summary>A route longer than this multiple of the Manhattan distance is a detour.</summary>
        public const int DetourFactor = 2;

        /// <summary>
        /// Why a Create-mode tap is refused, or <c>null</c> when it is accepted. Built from the same
        /// checks as the controller's endpoint test (a cell is a valid endpoint when it is free or
        /// already holds a belt).
        /// </summary>
        /// <param name="hasStart">A start anchor is already chosen.</param>
        /// <param name="isStartCell">The tapped cell is the current start cell.</param>
        /// <param name="isFree">The cell is in bounds and unoccupied.</param>
        /// <param name="isConveyor">The cell already holds a belt.</param>
        public static string RefusalReason(bool hasStart, bool isStartCell, bool isFree, bool isConveyor)
        {
            bool validEndpoint = isFree || isConveyor;

            if (!hasStart)
                return validEndpoint ? null : BadStartMessage;

            if (isStartCell)
                return isConveyor ? RetapStartMessage : null;   // a free start re-tap drops a single belt

            return validEndpoint ? null : BadEndMessage;
        }

        /// <summary>
        /// True when a found route is much longer than the straight Manhattan distance, i.e. it loops
        /// around something. <paramref name="routeCellCount"/> counts cells including both ends.
        /// </summary>
        public static bool IsDetour(int routeCellCount, Vector2Int start, Vector2Int end)
        {
            int manhattan = Mathf.Abs(end.x - start.x) + Mathf.Abs(end.y - start.y);
            int steps     = routeCellCount - 1;
            return steps > DetourFactor * manhattan;
        }

        /// <summary>
        /// The existing belt cells that sit on the straight L between start and end (the cells
        /// <see cref="ConveyorPathfinder.BuildOneTurnPath"/> would have used), excluding the endpoints.
        /// Tries the preferred elbow first, then the other, and returns the first that has any belt on it.
        /// </summary>
        public static List<Vector2Int> BlockingBeltCells(
            Vector2Int start, Vector2Int end, bool horizontalFirst, Func<int, int, bool> isBeltCell)
        {
            var result = new List<Vector2Int>();
            if (isBeltCell == null) return result;

            for (int pass = 0; pass < 2; pass++)
            {
                bool hFirst = pass == 0 ? horizontalFirst : !horizontalFirst;
                var path = ConveyorPathfinder.BuildOneTurnPath(start, end, hFirst, (x, y) => true);
                if (path == null) continue;

                foreach (var c in path)
                {
                    if (c == start || c == end) continue;
                    if (isBeltCell(c.x, c.y) && !result.Contains(c)) result.Add(c);
                }
                if (result.Count > 0) break;
            }
            return result;
        }
    }

    /// <summary>
    /// Allows at most one toast per key within a window, so a player hammering a refused tile does
    /// not stack identical banners. The clock is injected for tests (same pattern as
    /// <see cref="DrawerTapGuard"/>).
    /// </summary>
    public sealed class ToastRateLimiter
    {
        private readonly double                      _intervalSeconds;
        private readonly Func<double>                _clock;
        private readonly Dictionary<string, double>  _lastShown = new();

        public ToastRateLimiter(double intervalSeconds, Func<double> clock = null)
        {
            _intervalSeconds = intervalSeconds;
            _clock           = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>True (and records the time) when the toast with this key may show now.</summary>
        public bool TryAcquire(string key)
        {
            double now = _clock();
            if (_lastShown.TryGetValue(key, out double last) && now - last < _intervalSeconds)
                return false;
            _lastShown[key] = now;
            return true;
        }

        /// <summary>Forgets all history (e.g. when conveyor mode restarts).</summary>
        public void Reset() => _lastShown.Clear();
    }
}
