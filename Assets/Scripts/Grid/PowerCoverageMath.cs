using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The single source of truth for the proximity power grid's coverage test: a building is powered if
    /// <b>any part of its footprint square overlaps the source's circular power area</b> (touching
    /// counts). This is a geometric circle-vs-rectangle overlap — not a cell-centre gap — so a square
    /// that only partially reaches into the radius still connects.
    ///
    /// The power area is a circle centred on the source footprint, with radius
    /// <c>Rc = InfluenceRadius + halfOfTheSourcesLargerDimension</c> (in tiles), so along an axis it
    /// reaches exactly <c>InfluenceRadius</c> tiles past the source's edge — matching the ring
    /// <see cref="PlacementRadiusIndicator"/> draws. Cells are unit squares centred on integer
    /// coordinates (each spans +/-0.5 around its centre), matching <see cref="GridRenderer"/>.
    ///
    /// This mirrors <see cref="PowerGridSystem"/>'s Burst-compiled <c>IsWithinAnyGenerator</c>; the sim
    /// keeps its own inlined copy (Burst can't call into this managed assembly), but every MonoBehaviour
    /// call site (GridRenderer coverage preview, BuildingVisualizer radius highlight) routes through here
    /// so the highlighted/powered area always equals what the player sees. Unit spec:
    /// <c>PowerCoverageMathTests</c>.
    /// </summary>
    public static class PowerCoverageMath
    {
        /// <summary>
        /// True if the target footprint rect (cells <paramref name="tgtMinX"/>..<paramref name="tgtMaxX"/>,
        /// <paramref name="tgtMinY"/>..<paramref name="tgtMaxY"/>) overlaps the circular power area of the
        /// source footprint rect (cells srcMin..srcMax) with influence <paramref name="radius"/> tiles.
        /// Touching counts. A non-positive radius never connects.
        /// </summary>
        public static bool FootprintWithinRadius(
            int srcMinX, int srcMinY, int srcMaxX, int srcMaxY,
            int tgtMinX, int tgtMinY, int tgtMaxX, int tgtMaxY, float radius)
        {
            if (radius <= 0f) return false;

            // Circle centred on the source footprint; radius reaches `radius` tiles past its edge.
            float srcW = srcMaxX - srcMinX + 1;
            float srcH = srcMaxY - srcMinY + 1;
            float ccx  = (srcMinX + srcMaxX) * 0.5f;
            float ccy  = (srcMinY + srcMaxY) * 0.5f;
            float rc   = radius + Mathf.Max(srcW, srcH) * 0.5f;

            // Closest point of the target SQUARE (each cell spans +/-0.5 around its centre) to the circle
            // centre; overlap (distance <= Rc) means any part of the square is inside the power area.
            float dx = Mathf.Max(0f, Mathf.Max((tgtMinX - 0.5f) - ccx, ccx - (tgtMaxX + 0.5f)));
            float dy = Mathf.Max(0f, Mathf.Max((tgtMinY - 0.5f) - ccy, ccy - (tgtMaxY + 0.5f)));
            return dx * dx + dy * dy <= rc * rc;
        }
    }
}
