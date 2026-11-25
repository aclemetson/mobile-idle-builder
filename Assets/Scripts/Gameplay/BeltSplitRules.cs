using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>One belt split as saved: its cell, output mask and the weight of each direction (N, E, S, W).</summary>
    [Serializable]
    public struct BeltSplitData
    {
        public int  X, Y;
        public int  Mask;
        public int  W0, W1, W2, W3;

        public int Weight(int dir) => dir switch { 0 => W0, 1 => W1, 2 => W2, 3 => W3, _ => 0 };
    }

    /// <summary>
    /// Pure rules for belt splits (<see cref="BeltSplit"/>), so they are tested without ECS: the weighted turn that
    /// picks each item's exit, which sides may be outputs, and the flattened save format.
    /// </summary>
    public static class BeltSplitRules
    {
        public const int MinWeight  = 1;
        public const int MaxWeight  = 5;
        public const int MinOutputs = 2;   // fewer than two outputs is just a belt
        public const int MaxOutputs = 3;   // a tile has four sides and at least one is the way in

        /// <summary>Values per split in <see cref="GridSaveData.splits"/>: x, y, mask, w0..w3.</summary>
        public const int Stride = 7;

        public static bool Has(int mask, int dir) => dir >= 0 && dir < 4 && (mask & (1 << dir)) != 0;

        public static int Count(int mask)
        {
            int n = 0;
            for (int d = 0; d < 4; d++) if (Has(mask, d)) n++;
            return n;
        }

        public static int ClampWeight(int w) => w < MinWeight ? MinWeight : w > MaxWeight ? MaxWeight : w;

        /// <summary>
        /// Smooth weighted round robin (the same one nginx uses): every eligible side gains its weight, the
        /// richest wins and pays back the total. Over any window the picks match the weights as closely as
        /// whole items allow (2:1 gives A, B, A, A, B, A...), and a side that can't take an item right now
        /// neither gains nor pays, so a backed-up branch doesn't hoard turns. Returns the direction, or -1 when
        /// no eligible side.
        /// </summary>
        public static int Pick(int mask, Func<int, int> weight, Func<int, int> credit, Action<int, int> setCredit,
                               Func<int, bool> eligible)
        {
            int best = -1, bestCredit = int.MinValue, total = 0;
            for (int d = 0; d < 4; d++)
            {
                if (!Has(mask, d) || (eligible != null && !eligible(d))) continue;
                int w = ClampWeight(weight(d));
                int c = credit(d) + w;
                setCredit(d, c);
                total += w;
                if (c > bestCredit) { bestCredit = c; best = d; }
            }
            if (best >= 0) setCredit(best, credit(best) - total);
            return best;
        }

        /// <summary><see cref="Pick"/> over a <see cref="BeltSplit"/> (credits updated in place).</summary>
        public static int Pick(ref BeltSplit split, Func<int, bool> eligible)
        {
            // Plain arrays, not delegates on the struct's own methods: those would box a copy, and the credit
            // updates would be lost (every pick would go to the heaviest side).
            var weights = new[] { split.W0, split.W1, split.W2, split.W3 };
            var credits = new[] { split.C0, split.C1, split.C2, split.C3 };
            int dir = Pick(split.Mask, d => weights[d], d => credits[d], (d, c) => credits[d] = c, eligible);
            split.C0 = credits[0]; split.C1 = credits[1]; split.C2 = credits[2]; split.C3 = credits[3];
            return dir;
        }

        /// <summary>
        /// The sides a split on this tile may send items to: every side that isn't a way in. <paramref name="inboundMask"/>
        /// has a bit per side some belt (or a building output) feeds this tile from.
        /// </summary>
        public static int AllowedOutputs(int inboundMask) => 0b1111 & ~inboundMask;

        /// <summary>
        /// A valid output mask for a split: only allowed sides, 2 to 3 of them. Returns false (and 0) when the
        /// sides chosen don't make a split; the caller then removes it.
        /// </summary>
        public static bool Normalize(int requestedMask, int inboundMask, out int mask)
        {
            mask = requestedMask & AllowedOutputs(inboundMask);
            int n = Count(mask);
            if (n < MinOutputs || n > MaxOutputs) { mask = 0; return false; }
            return true;
        }

        /// <summary>Flattens splits for <see cref="GridSaveData.splits"/> (null when there are none).</summary>
        public static int[] Flatten(IReadOnlyList<BeltSplitData> splits)
        {
            if (splits == null || splits.Count == 0) return null;
            var a = new int[splits.Count * Stride];
            for (int i = 0; i < splits.Count; i++)
            {
                var s = splits[i];
                int o = i * Stride;
                a[o] = s.X; a[o + 1] = s.Y; a[o + 2] = s.Mask;
                a[o + 3] = s.W0; a[o + 4] = s.W1; a[o + 5] = s.W2; a[o + 6] = s.W3;
            }
            return a;
        }

        /// <summary>Reads <see cref="GridSaveData.splits"/>; a truncated tail is ignored.</summary>
        public static List<BeltSplitData> Parse(int[] flat)
        {
            var list = new List<BeltSplitData>();
            if (flat == null) return list;
            for (int o = 0; o + Stride <= flat.Length; o += Stride)
                list.Add(new BeltSplitData
                {
                    X = flat[o], Y = flat[o + 1], Mask = flat[o + 2] & 0b1111,
                    W0 = flat[o + 3], W1 = flat[o + 4], W2 = flat[o + 5], W3 = flat[o + 6],
                });
            return list;
        }

        /// <summary>"2 : 1" style label for the weights of the outputs in N, E, S, W order.</summary>
        public static string RatioText(int mask, Func<int, int> weight)
        {
            var parts = new List<string>(4);
            for (int d = 0; d < 4; d++) if (Has(mask, d)) parts.Add(ClampWeight(weight(d)).ToString());
            return string.Join(" : ", parts);
        }
    }
}
