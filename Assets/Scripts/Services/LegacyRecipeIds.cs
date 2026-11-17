using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Recipe ids that existed in the retired Assets/Data/recipes.json but not in game_data.json (#336 phase B).
    /// Saves written before the switch hold these ids in recipe_knowledge.json and in the manual craft queue;
    /// they are mapped to the game_data.json recipe that makes the same output, never dropped.
    ///
    /// Only "helium" needed a mapping: the other legacy ids that had no game_data.json recipe
    /// (alpha_particle, beta_particle) were item entries with no inputs, which the loader always filtered out,
    /// so no save can hold them.
    /// </summary>
    public static class LegacyRecipeIds
    {
        /// <summary>Old id to new id. Every target must be a recipe in game_data.json (enforced by a test).</summary>
        public static readonly Dictionary<string, string> Map = new()
        {
            // Legacy "helium" made helium_4 from 2 protons + 2 neutrons + 2 electrons in the Atomic Assembler.
            { "helium", "helium_4" },
        };

        /// <summary>The current id for <paramref name="id"/>: the mapped id when it is a legacy one, else unchanged.</summary>
        public static string Resolve(string id) =>
            id != null && Map.TryGetValue(id, out var current) ? current : id;

        /// <summary>
        /// Rewrites legacy ids in a loaded recipe-knowledge save. When the new id already has an entry the two
        /// merge (known if either was known) and the legacy entry is removed. Returns true if anything changed.
        /// </summary>
        public static bool Migrate(RecipeKnowledgeSave save)
        {
            if (save?.entries == null) return false;

            bool changed = false;
            for (int i = save.entries.Count - 1; i >= 0; i--)
            {
                var entry = save.entries[i];
                if (entry == null || entry.id == null || !Map.TryGetValue(entry.id, out var target)) continue;

                RecipeKnowledgeEntry existing = null;
                foreach (var e in save.entries)
                    if (e != null && e != entry && e.id == target) { existing = e; break; }

                if (existing != null)
                {
                    existing.previous_research |= entry.previous_research;
                    save.entries.RemoveAt(i);
                }
                else
                {
                    entry.id = target;
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>Rewrites legacy recipe ids in the manual craft queue. Returns true if anything changed.</summary>
        public static bool Migrate(CraftQueueSaveData queue)
        {
            if (queue?.entries == null) return false;

            bool changed = false;
            foreach (var entry in queue.entries)
            {
                if (entry == null || entry.recipeId == null || !Map.TryGetValue(entry.recipeId, out var target)) continue;
                entry.recipeId = target;
                changed = true;
            }
            return changed;
        }
    }
}
