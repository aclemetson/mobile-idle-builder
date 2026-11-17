using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>All the ways to make one item: the Recipes tab shows one row per group (#336).</summary>
    public sealed class RecipeGroup
    {
        public string outputId;
        public List<RecipeJson> methods = new();
    }

    /// <summary>Pure helpers behind the Recipes tab, so EditMode tests need no UI or assets.</summary>
    public static class RecipeGrouping
    {
        /// <summary>
        /// Groups recipes by output item id. Groups come out in the order their first recipe appears, and
        /// methods keep input order. Recipes with no output id are skipped.
        /// </summary>
        public static List<RecipeGroup> Group(IEnumerable<RecipeJson> recipes)
        {
            var groups = new List<RecipeGroup>();
            if (recipes == null) return groups;

            var byOutput = new Dictionary<string, RecipeGroup>();
            foreach (var r in recipes)
            {
                string outId = r?.output?.id;
                if (string.IsNullOrEmpty(outId)) continue;
                if (!byOutput.TryGetValue(outId, out var g))
                {
                    g = new RecipeGroup { outputId = outId };
                    byOutput[outId] = g;
                    groups.Add(g);
                }
                g.methods.Add(r);
            }
            return groups;
        }

        /// <summary>
        /// The method a group's main craft buttons use: the remembered one while it is unlocked, else the
        /// first unlocked method craftable from the menu, else the first unlocked one, else the first.
        /// </summary>
        public static RecipeJson PickMethod(RecipeGroup group, string rememberedRecipeId, Func<RecipeJson, bool> isUnlocked)
        {
            if (group == null || group.methods.Count == 0) return null;
            isUnlocked ??= _ => true;

            if (!string.IsNullOrEmpty(rememberedRecipeId))
                foreach (var m in group.methods)
                    if (m.id == rememberedRecipeId && isUnlocked(m)) return m;

            foreach (var m in group.methods)
                if (!m.requiresBuilding && isUnlocked(m)) return m;
            foreach (var m in group.methods)
                if (isUnlocked(m)) return m;
            return group.methods[0];
        }

        /// <summary>
        /// "Made in a Fusion Reactor" for a building-only method (two buildings: "a X or a Y"); empty for a
        /// method craftable from the menu. <paramref name="buildingName"/> maps a building id to its display name.
        /// </summary>
        public static string MadeInText(RecipeJson recipe, Func<string, string> buildingName)
        {
            if (recipe == null || !recipe.requiresBuilding || recipe.crafted_in == null) return "";
            var parts = new List<string>();
            foreach (var id in recipe.crafted_in)
            {
                if (string.IsNullOrEmpty(id) || id == RecipeDatabase.InventoryMenuSource) continue;
                string name = buildingName?.Invoke(id);
                parts.Add(WithArticle(string.IsNullOrEmpty(name) ? id : name));
            }
            return parts.Count == 0 ? "" : "Made in " + string.Join(" or ", parts);
        }

        /// <summary>"a Fusion Reactor", "an Atomic Assembler".</summary>
        public static string WithArticle(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a building";
            return ("AEIOU".IndexOf(char.ToUpperInvariant(name[0])) >= 0 ? "an " : "a ") + name;
        }
    }
}
