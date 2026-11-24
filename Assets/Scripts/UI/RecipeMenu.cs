using System;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Recipes menu layout (#488): item-type tabs over a horizontally scrolling row of cards, one per item. An
    /// item with several ways to make it gets a drop-up on its card to pick the method. Pure helpers live here;
    /// HUDController.BuildRecipeList builds the cards.
    /// </summary>
    public static class RecipeMenu
    {
        public const string Prefix = "recipes";

        public static readonly string[] Tabs = { "particles", "elements", "isotopes", "molecules", "materials", "life" };

        public static string TabLabel(string tab) => tab switch
        {
            "particles" => "Particles",
            "elements"  => "Elements",
            "isotopes"  => "Isotopes",
            "molecules" => "Molecules",
            "materials" => "Materials",
            "life"      => "Life",
            _           => tab,
        };

        /// <summary>The tab for an item of <paramref name="category"/> (null: the item is unknown).</summary>
        public static string TabOf(ItemCategory? category) => category switch
        {
            null                          => "particles",
            ItemCategory.RawResource      => "particles",
            ItemCategory.Nucleon          => "particles",
            ItemCategory.Particle         => "particles",
            ItemCategory.Element          => "elements",
            ItemCategory.Isotope          => "isotopes",
            ItemCategory.Molecule         => "molecules",
            ItemCategory.OrganicCompound  => "molecules",
            ItemCategory.Alloy            => "materials",
            ItemCategory.Component        => "materials",
            ItemCategory.Megastructure    => "materials",
            _                             => "life",   // Biomolecule, CellPart, Organism
        };

        /// <summary>
        /// How a method reads in the drop-up and on the card: "Made in a Fusion Reactor · Fast", the method label,
        /// or "Craft from the menu".
        /// </summary>
        public static string MethodTitle(RecipeJson method, Func<string, string> buildingName)
        {
            if (method == null) return "";
            string where = RecipeGrouping.MadeInText(method, buildingName);
            if (string.IsNullOrEmpty(where))
                return string.IsNullOrEmpty(method.method_label) ? "Craft from the menu" : method.method_label;
            return string.IsNullOrEmpty(method.method_label) ? where : where + " · " + method.method_label;
        }

        /// <summary>
        /// A method's short name in the drop-up: its label ("p-p chain"), else where it is made ("Fusion Reactor"),
        /// else "From the menu".
        /// </summary>
        public static string ShortMethodTitle(RecipeJson method, Func<string, string> buildingName)
        {
            if (method == null) return "";
            if (!string.IsNullOrEmpty(method.method_label)) return method.method_label;
            if (method.requiresBuilding && method.crafted_in != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var id in method.crafted_in)
                {
                    if (string.IsNullOrEmpty(id) || id == RecipeDatabase.InventoryMenuSource) continue;
                    string n = buildingName?.Invoke(id);
                    names.Add(string.IsNullOrEmpty(n) ? id : n);
                }
                if (names.Count > 0) return string.Join(" / ", names);
            }
            return "From the menu";
        }

        /// <summary>The drop-up toggle's text: "3 ways to make" while closed, "Close" while open (plain text: the
        /// UI font has no arrow glyphs).</summary>
        public static string WaysText(int count, bool open) => open ? "Close" : $"{count} ways to make";
    }
}
