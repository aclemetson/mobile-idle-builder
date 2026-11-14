using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Round-robin for a Belt Splitter. Pure, so the live belt code and the tests agree.
    /// Sides take turns in port order; a side that can't take an item right now (no belt, or its belt is
    /// still full) is skipped, so one blocked branch never stalls the others.
    /// </summary>
    public static class SplitterRotation
    {
        /// <summary>
        /// The side that gets the next item: the first of <paramref name="readyFacings"/> at or after
        /// <paramref name="nextFacing"/> in <paramref name="outputOrder"/> (wrapping). -1 when none is ready.
        /// </summary>
        public static int Pick(IReadOnlyList<int> outputOrder, int nextFacing, ICollection<int> readyFacings)
        {
            if (outputOrder == null || outputOrder.Count == 0 || readyFacings == null || readyFacings.Count == 0)
                return -1;

            int start = IndexOf(outputOrder, nextFacing);
            if (start < 0) start = 0;
            for (int k = 0; k < outputOrder.Count; k++)
            {
                int facing = outputOrder[(start + k) % outputOrder.Count];
                if (readyFacings.Contains(facing)) return facing;
            }
            return -1;
        }

        /// <summary>The side after <paramref name="facing"/> in <paramref name="outputOrder"/> (wrapping).</summary>
        public static int After(IReadOnlyList<int> outputOrder, int facing)
        {
            if (outputOrder == null || outputOrder.Count == 0) return facing;
            int i = IndexOf(outputOrder, facing);
            return outputOrder[(i + 1) % outputOrder.Count];
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }
    }
}
