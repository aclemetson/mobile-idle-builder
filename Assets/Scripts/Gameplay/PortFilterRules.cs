using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Where a Filter sends an item (#321). Pure, shared by the live systems, the inspector, save/load and the
    /// offline graph so they never disagree.
    ///
    /// <list type="bullet">
    /// <item>A rule naming the item wins: it leaves by that side.</item>
    /// <item>Otherwise it leaves by the "everything else" side, if one is set.</item>
    /// <item>Otherwise it has nowhere to go and waits (the Filter never deletes items).</item>
    /// </list>
    /// An item is named by at most one rule; <see cref="Assign"/> keeps it that way.
    /// </summary>
    public static class PortFilterRules
    {
        /// <summary>ItemID marking the "everything else" side.</summary>
        public const int Everything = -1;

        /// <summary>No side takes the item.</summary>
        public const int NoRoute = -1;

        /// <summary>The facing (OutputDirection) the item leaves by, or <see cref="NoRoute"/>.</summary>
        public static int Route(IReadOnlyList<(int facing, int itemId)> rules, int itemId)
        {
            if (rules == null) return NoRoute;
            int fallback = NoRoute;
            foreach (var (facing, id) in rules)
            {
                if (id == itemId && itemId != Everything) return facing;
                if (id == Everything) fallback = facing;
            }
            return fallback;
        }

        /// <summary>The rules a freshly placed Filter starts with: everything goes straight through.</summary>
        public static List<(int facing, int itemId)> Default(int straightThroughFacing) =>
            new() { (straightThroughFacing, Everything) };

        /// <summary>
        /// Sends <paramref name="itemId"/> (or <see cref="Everything"/>) out of <paramref name="facing"/>, removing
        /// any rule that sent it elsewhere. <paramref name="facing"/> = <see cref="NoRoute"/> just clears it.
        /// </summary>
        public static void Assign(List<(int facing, int itemId)> rules, int itemId, int facing)
        {
            rules.RemoveAll(r => r.itemId == itemId);
            if (facing != NoRoute) rules.Add((facing, itemId));
        }

        /// <summary>Items named on <paramref name="facing"/> (excluding the everything-else marker).</summary>
        public static List<int> ItemsOn(IReadOnlyList<(int facing, int itemId)> rules, int facing)
        {
            var list = new List<int>();
            foreach (var (f, id) in rules)
                if (f == facing && id != Everything) list.Add(id);
            return list;
        }

        /// <summary>Flattens rules to [facing, itemId, facing, itemId…] for <see cref="BuildingSaveData.portFilters"/>.</summary>
        public static int[] Flatten(IReadOnlyList<(int facing, int itemId)> rules)
        {
            if (rules == null || rules.Count == 0) return null;
            var a = new int[rules.Count * 2];
            for (int i = 0; i < rules.Count; i++) { a[2 * i] = rules[i].facing; a[2 * i + 1] = rules[i].itemId; }
            return a;
        }

        /// <summary>Inverse of <see cref="Flatten"/>; null/empty gives an empty list.</summary>
        public static List<(int facing, int itemId)> Parse(int[] flat)
        {
            var list = new List<(int, int)>();
            if (flat == null) return list;
            for (int i = 0; i + 1 < flat.Length; i += 2) list.Add((flat[i], flat[i + 1]));
            return list;
        }
    }
}
