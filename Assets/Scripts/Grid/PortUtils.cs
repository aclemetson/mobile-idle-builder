using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Helpers for transforming building port definitions by rotation and flip.
    ///
    /// Convention:
    ///   - Flip is applied first (horizontal mirror: E ↔ W, cell x → (W-1-x))
    ///   - Rotation is applied after (90° CW steps)
    ///
    /// Rotation CW cell rule in a footprint (W x H):
    ///   (x, y)  →  (H-1-y, x)  and new footprint becomes (H, W)
    /// </summary>
    public static class PortUtils
    {
        /// <summary>Returns the footprint dimensions after n 90° CW rotations.</summary>
        public static Vector2Int RotatedFootprint(Vector2Int fp, int rotation)
        {
            return (rotation % 2 == 0) ? fp : new Vector2Int(fp.y, fp.x);
        }

        /// <summary>Rotates a direction 90° CW, n times.</summary>
        public static OutputDirection RotateDir(OutputDirection dir, int steps)
        {
            return (OutputDirection)(((int)dir + steps) % 4);
        }

        /// <summary>Flips a direction horizontally (E ↔ W; N and S unchanged).</summary>
        public static OutputDirection FlipDir(OutputDirection dir)
        {
            return dir switch
            {
                OutputDirection.East => OutputDirection.West,
                OutputDirection.West => OutputDirection.East,
                _                    => dir
            };
        }

        /// <summary>
        /// Returns the world-relative cell and facing direction for a port,
        /// after applying flip then rotation in the given footprint.
        /// Add the result cell to the anchor cell to get the world grid position.
        /// </summary>
        public static (Vector2Int cell, OutputDirection facing) TransformPort(
            BuildingPort port, Vector2Int baseFootprint, bool flipped, int rotation)
        {
            int x = port.localCell.x;
            int y = port.localCell.y;
            int w = baseFootprint.x;
            int h = baseFootprint.y;
            OutputDirection dir = port.localFacing;

            // 1. Flip (horizontal mirror)
            if (flipped)
            {
                x   = (w - 1) - x;
                dir = FlipDir(dir);
            }

            // 2. Rotation (90° CW, 'rotation' times)
            for (int i = 0; i < rotation; i++)
            {
                int nx = (h - 1) - y;
                int ny = x;
                x = nx;
                y = ny;
                int tmp = w; w = h; h = tmp;
                dir = RotateDir(dir, 1);
            }

            return (new Vector2Int(x, y), dir);
        }

        /// <summary>Returns the grid cell adjacent to worldCell in the given direction.</summary>
        public static Vector2Int AdjacentCell(Vector2Int worldCell, OutputDirection dir)
        {
            return dir switch
            {
                OutputDirection.North => worldCell + new Vector2Int(0,  1),
                OutputDirection.East  => worldCell + new Vector2Int(1,  0),
                OutputDirection.South => worldCell + new Vector2Int(0, -1),
                OutputDirection.West  => worldCell + new Vector2Int(-1, 0),
                _                     => worldCell
            };
        }
    }
}
