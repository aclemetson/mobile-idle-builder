using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Running count of items produced this session, keyed by item id, fed from both production paths:
    /// manual crafts (ManualCraftService) and automated recipe output (ProductionAchievementBridge).
    ///
    /// Exists for the tutorial's ItemCraftedMin condition. An InventoryMin check can't see automated
    /// output: a belt carries it to Maxwell's Demon, which sells it the same frame, so it never reaches
    /// the inventory. Session-scoped on purpose: a step only needs to see the next craft, and a working
    /// line makes another one within seconds of a reload.
    /// </summary>
    public static class CraftLog
    {
        static readonly Dictionary<int, long> s_Counts = new();

        public static void Record(int itemId, int quantity)
        {
            if (itemId < 0 || quantity <= 0) return;
            s_Counts.TryGetValue(itemId, out long have);
            s_Counts[itemId] = have + quantity;
        }

        public static long Count(int itemId) => s_Counts.TryGetValue(itemId, out long n) ? n : 0;

        public static void Clear() => s_Counts.Clear();
    }
}
