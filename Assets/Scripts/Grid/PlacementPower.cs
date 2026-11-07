using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure placement-time power check (#302). Mirrors <see cref="PowerGridSystem"/>: only grid-linked power
    /// nodes (generators and linked relays) cover consumers, via <see cref="PowerCoverageMath.FootprintWithinRadius"/>.
    /// Unit spec: <c>PlacementPowerTests</c>.
    /// </summary>
    public static class PlacementPower
    {
        /// <summary>True when the footprint (x, y, w, h) is inside the InfluenceRadius of any grid-linked node.</summary>
        public static bool IsFootprintPowered(IReadOnlyList<PowerConnectionGraph.PowerNode> nodes, int x, int y, int w, int h)
        {
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (!n.IsGridLinked) continue;
                if (PowerCoverageMath.FootprintWithinRadius(
                        n.AnchorX, n.AnchorY, n.AnchorX + n.Width - 1, n.AnchorY + n.Height - 1,
                        x, y, x + w - 1, y + h - 1, n.InfluenceRadius))
                    return true;
            }
            return false;
        }
    }
}
