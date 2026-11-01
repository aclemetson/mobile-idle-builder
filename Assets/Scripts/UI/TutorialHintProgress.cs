using System;
using System.Text;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the live "have / need" line shown under the tutorial hint for gather steps (#260). Collecting
    /// gives one item per tap with nothing on screen counting towards the goal, so a step like
    /// gather_for_hydrogen (4 + 2 + 2 items) read as "keep tapping and hope". Pure so it can be tested
    /// without ECS; the caller passes the inventory and item-name lookups.
    /// </summary>
    public static class TutorialHintProgress
    {
        /// <summary>
        /// "Up Quark 3/4, Down Quark 2/2, Electron 0/2" for an InventoryMin condition, or null when the
        /// condition has no item goal. Counts are capped at the goal so overflow never reads as "5/4".
        /// </summary>
        public static string Format(TutorialConditionDef condition, Func<int, int> countOf, Func<int, string> nameOf)
        {
            if (condition == null || condition.type != ConditionType.InventoryMin) return null;
            if (condition.items == null || condition.items.Count == 0) return null;

            var sb = new StringBuilder();
            foreach (var req in condition.items)
            {
                if (req.quantity <= 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                int have = Math.Min(Math.Max(countOf(req.itemId), 0), req.quantity);
                sb.Append(nameOf(req.itemId) ?? $"Item {req.itemId}")
                  .Append(' ').Append(have).Append('/').Append(req.quantity);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}
