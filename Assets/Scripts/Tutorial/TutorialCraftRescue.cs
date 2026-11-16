using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Gets a save unstuck when a tutorial craft step can no longer be finished: field collection is paused
    /// on the step, and the player has run out of the raw inputs (for example they sold their quarks before
    /// <see cref="TutorialSellPolicy"/> existed). Reports which fields to reopen so they can collect the rest.
    /// </summary>
    public static class TutorialCraftRescue
    {
        const int MaxDepth = 3;

        /// <summary>
        /// The field type to reopen collection for: one type when only one kind of raw input is short,
        /// <see cref="FieldTypes.None"/> (every field) when several are, or null when nothing needs reopening.
        /// Only steps that pause collection and wait on an <see cref="ConditionType.InventoryMin"/> qualify.
        /// </summary>
        /// <param name="itemKey">Item int id to string id.</param>
        /// <param name="available">What the player has of an item: held plus queued crafts.</param>
        /// <param name="recipeFor">The recipe that makes an item (recipe id = item id), or null.</param>
        /// <param name="fieldTypeOf">The item's field type; raw field items have one, crafted items "None".</param>
        public static string FieldsNeeded(TutorialStepDef step,
                                          Func<int, string> itemKey,
                                          Func<string, int> available,
                                          Func<string, RecipeJson> recipeFor,
                                          Func<string, string> fieldTypeOf)
        {
            if (step?.onEnter == null || !step.onEnter.blockCollection) return null;
            var cond = step.advanceCondition;
            if (cond == null || cond.type != ConditionType.InventoryMin || cond.items == null) return null;

            var pool = new Dictionary<string, int>();
            int Take(string id, int want)
            {
                if (!pool.TryGetValue(id, out int have)) have = Math.Max(0, available(id));
                int used = Math.Min(have, want);
                pool[id] = have - used;
                return want - used;
            }

            var need = new Dictionary<string, int>();
            foreach (var req in cond.items)
            {
                if (req == null || req.quantity <= 0) continue;
                string id = itemKey(req.itemId);
                if (string.IsNullOrEmpty(id)) continue;
                int shortBy = Take(id, req.quantity);
                if (shortBy > 0) Add(need, id, shortBy);
            }

            var fields = new HashSet<string>();
            for (int depth = 0; depth <= MaxDepth && need.Count > 0; depth++)
            {
                var next = new Dictionary<string, int>();
                foreach (var kv in need)
                {
                    string ft = fieldTypeOf(kv.Key);
                    if (!FieldTypes.IsUnrestricted(ft)) { fields.Add(ft); continue; }

                    var recipe = recipeFor(kv.Key);
                    if (recipe?.inputs == null) continue;
                    int perCraft = Math.Max(1, recipe.output?.quantity ?? 1);
                    int crafts   = (kv.Value + perCraft - 1) / perCraft;
                    foreach (var input in recipe.inputs)
                    {
                        if (input == null || input.quantity <= 0 || string.IsNullOrEmpty(input.id)) continue;
                        int shortBy = Take(input.id, input.quantity * crafts);
                        if (shortBy > 0) Add(next, input.id, shortBy);
                    }
                }
                need = next;
            }

            if (fields.Count == 0) return null;
            if (fields.Count > 1) return FieldTypes.None;
            foreach (var f in fields) return f;
            return null;
        }

        static void Add(Dictionary<string, int> d, string id, int n) =>
            d[id] = (d.TryGetValue(id, out int v) ? v : 0) + n;

        /// <summary>Live check for the current tutorial step; null when no rescue is needed.</summary>
        public static string FieldsNeededNow(TutorialStepDef step)
        {
            if (step?.onEnter == null || !step.onEnter.blockCollection) return null;
            var craft = ManualCraftService.Instance;
            if (craft == null) return null;

            var held   = craft.GetInventoryCounts();
            var queued = new Dictionary<string, int>();
            var queue  = craft.Queue;
            if (queue?.entries != null)
                foreach (var e in queue.entries)
                {
                    var r = ManualCraftService.FindRecipe(e.recipeId);
                    if (r?.output?.id != null) Add(queued, r.output.id, e.count * Math.Max(1, r.output.quantity));
                }

            return FieldsNeeded(step,
                id => ItemDatabase.GetStatic(id)?.id,
                id =>
                {
                    int itemId = ItemDatabase.GetStatic(id)?.itemId ?? -1;
                    int h = itemId >= 0 && held.TryGetValue(itemId, out int q) ? q : 0;
                    return h + (queued.TryGetValue(id, out int k) ? k : 0);
                },
                ManualCraftService.FindRecipe,
                id => ItemDatabase.GetStatic(id)?.fieldType);
        }
    }
}
