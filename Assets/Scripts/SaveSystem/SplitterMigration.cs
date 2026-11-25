using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Belt Splitter buildings were replaced by belt splits (2026-11-24). A saved grid that still holds one gets,
    /// in its place, a one-tile belt that splits to the same output sides with even weights: the belt feeding the
    /// splitter's input now feeds the tile, and the belts that left its outputs become the split's branches.
    ///
    /// The old buildings' ports, in their own frame (before flip, then rotation, as <see cref="PortUtils"/>):
    /// input on the East side; outputs North + South (2-way, id 27) or North + East + South (3-way, id 28).
    /// </summary>
    public static class SplitterMigration
    {
        public const int TwoWayId   = 27;   // splitter_2
        public const int ThreeWayId = 28;   // splitter_3

        static readonly OutputDirection[] TwoWayOuts   = { OutputDirection.North, OutputDirection.South };
        static readonly OutputDirection[] ThreeWayOuts = { OutputDirection.North, OutputDirection.East, OutputDirection.South };

        /// <summary>True for an old splitter building id.</summary>
        public static bool IsSplitterBuilding(int buildingId) => buildingId == TwoWayId || buildingId == ThreeWayId;

        /// <summary>The world-facing output mask of an old splitter at this rotation / flip.</summary>
        public static int OutputMask(int buildingId, int rotation, bool flipped)
        {
            var outs = buildingId == ThreeWayId ? ThreeWayOuts : TwoWayOuts;
            int mask = 0;
            foreach (var o in outs)
            {
                var d = flipped ? PortUtils.FlipDir(o) : o;
                d = PortUtils.RotateDir(d, rotation);
                mask |= 1 << (int)d;
            }
            return mask;
        }

        /// <summary>
        /// Replaces every splitter building in <paramref name="grid"/> with a split belt tile. Returns how many were
        /// converted (0 leaves the grid untouched). Idempotent: converted buildings are gone.
        /// </summary>
        public static int Convert(GridSaveData grid)
        {
            if (grid?.buildings == null) return 0;
            int converted = 0;
            var splits = BeltSplitRules.Parse(grid.splits);
            for (int i = grid.buildings.Count - 1; i >= 0; i--)
            {
                var b = grid.buildings[i];
                if (b == null || !IsSplitterBuilding(b.buildingId) || b.position == null || b.position.Length < 2) continue;

                int x = b.position[0], y = b.position[1];
                int mask = OutputMask(b.buildingId, b.rotation, b.flipped);
                int primary = 0;
                for (int d = 0; d < 4; d++) if (BeltSplitRules.Has(mask, d)) { primary = d; break; }

                grid.buildings.RemoveAt(i);
                grid.conveyors ??= new List<ConveyorSaveData>();
                grid.conveyors.Add(new ConveyorSaveData { cells = new[] { x, y }, singleDir = primary });
                splits.RemoveAll(s => s.X == x && s.Y == y);
                splits.Add(new BeltSplitData
                {
                    X = x, Y = y, Mask = mask,
                    W0 = BeltSplitRules.Has(mask, 0) ? 1 : 0, W1 = BeltSplitRules.Has(mask, 1) ? 1 : 0,
                    W2 = BeltSplitRules.Has(mask, 2) ? 1 : 0, W3 = BeltSplitRules.Has(mask, 3) ? 1 : 0,
                });
                converted++;
            }
            if (converted > 0) grid.splits = BeltSplitRules.Flatten(splits);
            return converted;
        }
    }
}
