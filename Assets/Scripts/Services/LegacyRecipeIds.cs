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
            // Liquid Hydrogen (5 H2 -> LH2) was retired 2026-11-22; a save that knew it knows Water, its sibling.
            { "liquid_hydrogen", "water" },
            // 2026-11-23 Item Timeline (scripts/apply_item_timeline.py): the upward neutron captures became the
            // Spallation descent (same element, made the other way), Iron joins Manganese and Cobalt, Uranium is
            // mined and Np/Pu are bred. Recipes whose product was retired (octane...) are not mapped.
            { "uranium", "collect_uranium" },
            { "plutonium", "breed_plutonium" },
            { "neptunium", "breed_neptunium" },
            { "fuse_iron", "iron_convergence" },
            { "s_capture_cobalt", "spall_cobalt" },
            { "s_capture_nickel", "spall_nickel" },
            { "s_capture_copper", "spall_copper" },
            { "s_capture_zinc", "spall_zinc" },
            { "s_capture_gallium", "spall_gallium" },
            { "s_capture_germanium", "spall_germanium" },
            { "s_capture_arsenic", "spall_arsenic" },
            { "s_capture_selenium", "spall_selenium" },
            { "s_capture_bromine", "spall_bromine" },
            { "s_capture_technetium", "spall_technetium" },
            { "s_capture_ruthenium", "spall_ruthenium" },
            { "s_capture_rhodium", "spall_rhodium" },
            { "s_capture_palladium", "spall_palladium" },
            { "s_capture_silver", "spall_silver" },
            { "s_capture_cadmium", "spall_cadmium" },
            { "s_capture_indium", "spall_indium" },
            { "s_capture_lanthanum", "spall_lanthanum" },
            { "s_capture_praseodymium", "spall_praseodymium" },
            { "s_capture_neodymium", "spall_neodymium" },
            { "s_capture_promethium", "spall_promethium" },
            { "s_capture_samarium", "spall_samarium" },
            { "s_capture_gadolinium", "spall_gadolinium" },
            { "s_capture_terbium", "spall_terbium" },
            { "s_capture_dysprosium", "spall_dysprosium" },
            { "s_capture_holmium", "spall_holmium" },
            { "s_capture_erbium", "spall_erbium" },
            { "s_capture_thulium", "spall_thulium" },
            { "s_capture_ytterbium", "spall_ytterbium" },
            { "s_capture_lutetium", "spall_lutetium" },
            { "s_capture_hafnium", "spall_hafnium" },
            { "s_capture_tantalum", "spall_tantalum" },
            { "s_capture_tungsten", "spall_tungsten" },
            { "s_capture_rhenium", "spall_rhenium" },
            { "s_capture_mercury", "spall_mercury" },
            { "r_capture_europium", "spall_europium" },
            { "r_capture_osmium", "spall_osmium" },
            { "r_capture_iridium", "spall_iridium" },
            { "r_capture_platinum", "spall_platinum" },
            { "r_capture_gold", "spall_gold" },
            { "collect_plutonium", "breed_plutonium" },
            { "methane_combustion", "carbon_dioxide" },
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
