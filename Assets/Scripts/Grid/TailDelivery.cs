using System.Collections.Generic;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Which way a belt's tail (last tile) is DRAWN exiting (#223). ConveyorSystem lets a tail feed any
    /// adjacent input port that faces it, not only the one straight ahead, so a belt drawn up beside a
    /// building still delivers, and a belt cut by deleting its downstream neighbour keeps feeding
    /// whatever input it now touches. Drawing the tile straight past that input hid where items went.
    ///
    /// Once the tail has delivered, it is drawn toward the input it actually feeds
    /// (<see cref="TailDeliveryLog"/>). Before that it uses the same <see cref="TailInputPriority"/>
    /// ranking ConveyorSystem.TryPickInput uses (#275): the input the tile's ExitDir points at first,
    /// Maxwell's Demon last, ties in direction order (North, East, South, West). The ranking alone was
    /// wrong when the top-ranked input's recipe doesn't use the item: a Lepton Field belt ending between
    /// a Fusion Generator (straight ahead) and an Atom Generator was drawn into the Fusion Generator
    /// while every electron went to the Atom Generator.
    /// </summary>
    public static class TailDelivery
    {
        /// <summary>
        /// The direction to draw the tail exiting: <paramref name="deliveredDir"/> (where it has been
        /// delivering, -1 when unknown) if that input is still there, else the best-ranked input it can
        /// feed, else its own <paramref name="exitDir"/>. Never a U-turn back out of the side the items
        /// enter from (<paramref name="entryDir"/> is the travel direction on entry).
        /// <paramref name="feedsInput"/>[d] is true when an input port faces this tile from direction d;
        /// <paramref name="isSink"/>[d] when that input belongs to an entropy sink (Maxwell's Demon).
        /// </summary>
        public static int VisualExit(int entryDir, int exitDir, bool[] feedsInput, bool[] isSink, int deliveredDir = -1)
        {
            bool IsUTurn(int d) => entryDir >= 0 && entryDir <= 3 && d == (entryDir + 2) % 4;

            if (deliveredDir >= 0 && deliveredDir <= 3 && feedsInput[deliveredDir] && !IsUTurn(deliveredDir))
                return deliveredDir;

            int best = -1, bestRank = int.MaxValue;
            for (int d = 0; d < 4; d++)
            {
                if (!feedsInput[d]) continue;
                if (IsUTurn(d)) continue;
                int rank = TailInputPriority.Rank(d == exitDir, isSink != null && isSink[d]);
                if (rank < bestRank) { bestRank = rank; best = d; }
            }
            return best >= 0 ? best : exitDir;
        }
    }

    /// <summary>
    /// Where each belt tail has really been delivering, written by ConveyorSystem on every item it puts
    /// into a building input, read by ConveyorVisualizer to draw the tail bending into that input.
    /// Keyed by cell (runtime only, never saved). A tail's first delivery sets its direction at once;
    /// after that it changes only when <see cref="SettleDeliveries"/> deliveries in a row go the new
    /// way, so a preferred input that is briefly full doesn't make the belt flicker between two inputs.
    /// </summary>
    public static class TailDeliveryLog
    {
        public const int SettleDeliveries = 3;

        struct Entry
        {
            public int Dir;       // direction of the latest delivery
            public int Streak;    // deliveries in a row to Dir
            public int Settled;   // direction the tail is drawn toward
        }

        static readonly Dictionary<int2, Entry> Log = new();

        /// <summary>Bumped whenever any tail's settled direction changes, so the visualizer only redraws then.</summary>
        public static int Version { get; private set; }

        public static void Record(int2 cell, int dir)
        {
            if (dir < 0 || dir > 3) return;
            if (!Log.TryGetValue(cell, out var e))
            {
                Log[cell] = new Entry { Dir = dir, Streak = 1, Settled = dir };
                Version++;
                return;
            }

            if (e.Dir == dir) e.Streak++;
            else { e.Dir = dir; e.Streak = 1; }

            if (e.Settled != dir && e.Streak >= SettleDeliveries)
            {
                e.Settled = dir;
                Version++;
            }
            Log[cell] = e;
        }

        /// <summary>The direction the tail at <paramref name="cell"/> delivers to, or -1 when it hasn't yet.</summary>
        public static int Settled(int2 cell) => Log.TryGetValue(cell, out var e) ? e.Settled : -1;

        /// <summary>Drops one cell (its belt was removed, so a new belt there starts fresh).</summary>
        public static void Forget(int2 cell)
        {
            if (Log.Remove(cell)) Version++;
        }

        public static void Clear()
        {
            Log.Clear();
            Version++;
        }
    }
}
