using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure timing rules for the recipe-menu craft queue (#310). Works on the saved
    /// <see cref="CraftQueueSaveData"/> so the queue survives save/load and prestige clears it with
    /// the run. Inputs and outputs are not handled here; <see cref="ManualCraftService"/> takes inputs
    /// when an item is queued, refunds them on cancel, and deposits the output when it finishes.
    ///
    /// Only the front entry runs. When an item finishes, the next one starts at that item's finish
    /// time rather than "now", so a queue left running while the app was closed catches up on load.
    /// </summary>
    public static class CraftQueue
    {
        public static bool IsEmpty(CraftQueueSaveData q) => q?.entries == null || q.entries.Count == 0;

        /// <summary>Total items still queued, the one in progress included.</summary>
        public static int TotalItems(CraftQueueSaveData q)
        {
            if (IsEmpty(q)) return 0;
            int total = 0;
            foreach (var e in q.entries) total += e.count;
            return total;
        }

        /// <summary>
        /// Adds <paramref name="count"/> items. Merges into the last entry when it is the same recipe
        /// at the same speed. Starts the clock when the queue was empty.
        /// </summary>
        public static void Enqueue(CraftQueueSaveData q, string recipeId, int count, float unitSeconds, DateTime now)
        {
            if (q == null || count <= 0 || string.IsNullOrEmpty(recipeId)) return;
            q.entries ??= new();

            bool wasEmpty = q.entries.Count == 0;
            var last = wasEmpty ? null : q.entries[q.entries.Count - 1];
            if (last != null && last.recipeId == recipeId && Math.Abs(last.unitSeconds - unitSeconds) < 0.0001f)
                last.count += count;
            else
                q.entries.Add(new CraftQueueEntry { recipeId = recipeId, count = count, unitSeconds = unitSeconds });

            if (wasEmpty) SetActiveComplete(q, now.AddSeconds(unitSeconds));
        }

        /// <summary>
        /// Finishes every item whose time has passed, calling <paramref name="onItemDone"/> once per item
        /// with its recipe id. Returns how many items finished.
        /// </summary>
        public static int Tick(CraftQueueSaveData q, DateTime now, Action<string> onItemDone)
        {
            if (IsEmpty(q)) { if (q != null) q.activeCompleteUtc = ""; return 0; }

            if (!TryGetActiveComplete(q, out var complete))
            {
                // Missing or corrupt stamp: restart the front item rather than finishing it for free.
                SetActiveComplete(q, now.AddSeconds(q.entries[0].unitSeconds));
                return 0;
            }

            int done = 0;
            while (q.entries.Count > 0 && now >= complete)
            {
                var front = q.entries[0];
                onItemDone?.Invoke(front.recipeId);
                done++;
                front.count--;
                if (front.count <= 0) q.entries.RemoveAt(0);
                if (q.entries.Count > 0) complete = complete.AddSeconds(q.entries[0].unitSeconds);
            }

            if (q.entries.Count > 0) SetActiveComplete(q, complete);
            else                     q.activeCompleteUtc = "";
            return done;
        }

        /// <summary>
        /// Removes the entry at <paramref name="index"/> and returns how many items it held (the caller
        /// refunds their inputs). Cancelling the front entry starts the next one from now.
        /// </summary>
        public static int Cancel(CraftQueueSaveData q, int index, DateTime now)
        {
            if (IsEmpty(q) || index < 0 || index >= q.entries.Count) return 0;

            int removed = q.entries[index].count;
            q.entries.RemoveAt(index);

            if (q.entries.Count == 0)  q.activeCompleteUtc = "";
            else if (index == 0)       SetActiveComplete(q, now.AddSeconds(q.entries[0].unitSeconds));
            return removed;
        }

        /// <summary>Progress of the item in progress, 0 to 1. 0 when the queue is empty.</summary>
        public static float ActiveProgress01(CraftQueueSaveData q, DateTime now)
        {
            if (IsEmpty(q) || !TryGetActiveComplete(q, out var complete)) return 0f;
            float unit = q.entries[0].unitSeconds;
            if (unit <= 0f) return 1f;
            double left = (complete - now).TotalSeconds;
            return (float)Math.Clamp(1.0 - left / unit, 0.0, 1.0);
        }

        /// <summary>Seconds until the whole queue is done.</summary>
        public static double SecondsRemaining(CraftQueueSaveData q, DateTime now)
        {
            if (IsEmpty(q) || !TryGetActiveComplete(q, out var complete)) return 0;
            double total = Math.Max(0, (complete - now).TotalSeconds);
            for (int i = 0; i < q.entries.Count; i++)
            {
                int rest = i == 0 ? q.entries[i].count - 1 : q.entries[i].count;
                total += rest * q.entries[i].unitSeconds;
            }
            return total;
        }

        static bool TryGetActiveComplete(CraftQueueSaveData q, out DateTime complete) =>
            DateTime.TryParse(q.activeCompleteUtc, null, DateTimeStyles.RoundtripKind, out complete);

        static void SetActiveComplete(CraftQueueSaveData q, DateTime complete) =>
            q.activeCompleteUtc = complete.ToString("O");
    }
}
