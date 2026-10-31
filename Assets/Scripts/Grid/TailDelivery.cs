namespace MobileIdleBuilder
{
    /// <summary>
    /// Which adjacent building input a belt's tail (last tile) feeds (#223). Shared by
    /// ConveyorSystem (where items actually go) and ConveyorVisualizer (where the tile is drawn
    /// pointing), so the picture always matches the simulation.
    ///
    /// A tail feeds any adjacent input port that faces it, not only the one straight ahead, so a
    /// belt drawn up beside a building still delivers, and a belt cut by deleting its downstream
    /// neighbour keeps feeding whatever input it now touches. The input the tile's own ExitDir points
    /// at is tried first; the rest follow in direction order (North, East, South, West).
    /// </summary>
    public static class TailDelivery
    {
        /// <summary>
        /// The <paramref name="i"/>-th direction (0..3) to try for a tail whose drawn exit is
        /// <paramref name="exitDir"/>: the exit itself first, then the other three in ascending order.
        /// An invalid <paramref name="exitDir"/> falls back to plain 0..3. Index-based (no delegate)
        /// so the per-frame simulation path allocates nothing.
        /// </summary>
        public static int DirAt(int exitDir, int i)
        {
            if (exitDir < 0 || exitDir > 3) return i;
            if (i == 0) return exitDir;
            int k = i - 1;
            return k < exitDir ? k : k + 1;
        }

        /// <summary>
        /// The direction the tail tile should be DRAWN exiting: the input it feeds, or its own
        /// <paramref name="exitDir"/> when it feeds nothing. Never a U-turn back out of the side the
        /// items enter from (<paramref name="entryDir"/> is the travel direction on entry).
        /// <paramref name="feedsInput"/>[d] is true when an input port faces this tile from direction d.
        /// </summary>
        public static int VisualExit(int entryDir, int exitDir, bool[] feedsInput)
        {
            for (int i = 0; i < 4; i++)
            {
                int d = DirAt(exitDir, i);
                if (!feedsInput[d]) continue;
                if (entryDir >= 0 && entryDir <= 3 && d == (entryDir + 2) % 4) continue;   // no U-turn
                return d;
            }
            return exitDir;
        }
    }
}
