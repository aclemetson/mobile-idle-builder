namespace MobileIdleBuilder
{
    /// <summary>
    /// Which way a belt's tail (last tile) is DRAWN exiting (#223). ConveyorSystem lets a tail feed any
    /// adjacent input port that faces it, not only the one straight ahead, so a belt drawn up beside a
    /// building still delivers, and a belt cut by deleting its downstream neighbour keeps feeding
    /// whatever input it now touches. Drawing the tile straight past that input hid where items went.
    ///
    /// The choice uses the same <see cref="TailInputPriority"/> ranking ConveyorSystem.TryPickInput uses
    /// (#275): the input the tile's ExitDir points at first, Maxwell's Demon last, ties in direction
    /// order (North, East, South, West). The simulation can still skip a preferred input for one item it
    /// won't take right now (full, or unused by its recipe); the picture shows the preferred input.
    /// </summary>
    public static class TailDelivery
    {
        /// <summary>
        /// The direction to draw the tail exiting: the best-ranked input it can feed, or its own
        /// <paramref name="exitDir"/> when it feeds nothing. Never a U-turn back out of the side the
        /// items enter from (<paramref name="entryDir"/> is the travel direction on entry).
        /// <paramref name="feedsInput"/>[d] is true when an input port faces this tile from direction d;
        /// <paramref name="isSink"/>[d] when that input belongs to an entropy sink (Maxwell's Demon).
        /// </summary>
        public static int VisualExit(int entryDir, int exitDir, bool[] feedsInput, bool[] isSink)
        {
            int best = -1, bestRank = int.MaxValue;
            for (int d = 0; d < 4; d++)
            {
                if (!feedsInput[d]) continue;
                if (entryDir >= 0 && entryDir <= 3 && d == (entryDir + 2) % 4) continue;   // no U-turn
                int rank = TailInputPriority.Rank(d == exitDir, isSink != null && isSink[d]);
                if (rank < bestRank) { bestRank = rank; best = d; }
            }
            return best >= 0 ? best : exitDir;
        }
    }
}
