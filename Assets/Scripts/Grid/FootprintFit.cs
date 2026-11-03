using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure grid math for restoring a saved building whose footprint has grown since it was saved
    /// (the Atom Generator went from 1x1 to 2x1 in #275). Old saves hold its anchor only; if the new
    /// footprint now overlaps a neighbour, try shifting it one tile back along each axis so the old
    /// cell becomes its far end. GridSaveService skips and refunds anything that still doesn't fit.
    /// </summary>
    public static class FootprintFit
    {
        /// <summary>Cells covered by a footprint at <paramref name="anchor"/> (rotation swaps its axes).</summary>
        public static IEnumerable<Vector2Int> Cells(Vector2Int anchor, Vector2Int footprint, int rotation)
        {
            var fp = PortUtils.RotatedFootprint(footprint, rotation);
            for (int dx = 0; dx < fp.x; dx++)
            for (int dy = 0; dy < fp.y; dy++)
                yield return anchor + new Vector2Int(dx, dy);
        }

        /// <summary>
        /// First anchor, among the saved one and one-tile shifts back along x then y, whose every cell
        /// passes <paramref name="isFree"/>. False when none does.
        /// </summary>
        public static bool TryFit(Vector2Int anchor, Vector2Int footprint, int rotation,
                                  Func<Vector2Int, bool> isFree, out Vector2Int fitted)
        {
            var fp = PortUtils.RotatedFootprint(footprint, rotation);
            var candidates = new List<Vector2Int> { anchor };
            if (fp.x > 1) candidates.Add(anchor - new Vector2Int(fp.x - 1, 0));
            if (fp.y > 1) candidates.Add(anchor - new Vector2Int(0, fp.y - 1));

            foreach (var c in candidates)
            {
                bool ok = true;
                foreach (var cell in Cells(c, footprint, rotation))
                    if (!isFree(cell)) { ok = false; break; }
                if (!ok) continue;
                fitted = c;
                return true;
            }
            fitted = anchor;
            return false;
        }
    }
}
