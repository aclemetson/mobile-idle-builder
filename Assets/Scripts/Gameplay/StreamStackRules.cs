using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure rules for stream stacks (Chemistry, 2026-11-27; unit-tested). A bank building drops its products onto
    /// the stream in stacks of 1-<see cref="Max"/> (<see cref="StreamStack"/>), so a crowded stream carries more in
    /// the same space. A stack rides as one item (<see cref="ConveyorItem.Stack"/>), is taken by a building only when
    /// its input has room for all of it, and pays its full count at the mouth.
    /// </summary>
    public static class StreamStackRules
    {
        /// <summary>The largest stack the setting offers.</summary>
        public const int Max = 5;

        /// <summary>The largest stack the research allows: <see cref="ResearchEffects.MaxStreamStack"/>, 1 when none.</summary>
        public static int MaxUnlocked(int maxStreamStack) => Clamp(maxStreamStack);

        /// <summary><paramref name="size"/> kept within 1-<see cref="Max"/>.</summary>
        public static int Clamp(int size) => size < 1 ? 1 : size > Max ? Max : size;

        /// <summary>The stack a building drops: its setting, capped by what research allows.</summary>
        public static int Effective(int setting, int maxUnlocked)
        {
            int s = Clamp(setting), m = Clamp(maxUnlocked);
            return s < m ? s : m;
        }

        /// <summary>
        /// How much bigger a stack draws on the stream than one unit: x1 for one, +8% per extra unit (x1.32 for five).
        /// Its colour (<see cref="StackColor"/>) is the main cue; the size only backs it up.
        /// </summary>
        public static float VisualScale(int quantity) => 1f + 0.08f * (Clamp(quantity) - 1);

        /// <summary>
        /// The colour of a stack of <paramref name="quantity"/>: the glow around it on the stream and its button in the
        /// inspector (one source, so they always match). Rises like loot rarity: none for one, then green, blue,
        /// purple, gold.
        /// </summary>
        public static UnityEngine.Color StackColor(int quantity) => Clamp(quantity) switch
        {
            2 => Hex(0x5BD66B),
            3 => Hex(0x4C9BFF),
            4 => Hex(0xB565FF),
            5 => Hex(0xFFC23D),
            _ => UnityEngine.Color.clear,
        };

        private static UnityEngine.Color Hex(int rgb) =>
            new UnityEngine.Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>True when an input holding <paramref name="held"/> of an item takes a whole stack of <paramref name="quantity"/>.</summary>
        public static bool HasRoomFor(int held, int quantity, int cap) => held + quantity <= cap;

        /// <summary>
        /// Which output to drop and how many. <paramref name="quantities"/> are the building's output slots in order.
        /// The first slot holding a full <paramref name="stack"/> drops one. When the output is full
        /// (<paramref name="total"/> at <paramref name="outputCap"/>) and no slot holds a full stack, the biggest slot
        /// drops what it has rather than stopping the building. Returns the slot index, or -1 to wait.
        /// </summary>
        public static int PickDrop(IList<int> quantities, int stack, int total, int outputCap, out int quantity)
        {
            quantity = 0;
            if (quantities == null || quantities.Count == 0) return -1;
            stack = Clamp(stack);
            for (int i = 0; i < quantities.Count; i++)
                if (quantities[i] >= stack) { quantity = stack; return i; }
            if (total < outputCap) return -1;
            int best = -1;
            for (int i = 0; i < quantities.Count; i++)
                if (quantities[i] > 0 && (best < 0 || quantities[i] > quantities[best])) best = i;
            if (best >= 0) quantity = quantities[best];
            return best;
        }
    }
}
