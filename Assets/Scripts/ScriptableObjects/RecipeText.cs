using System.Collections.Generic;
using System.Text;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Text shown under a recipe's name in the building picker (#336). Pure functions so EditMode tests
    /// need no assets. Recipes are named after their product; this line says where and how it is made.
    /// </summary>
    public static class RecipeText
    {
        /// <summary>
        /// "Building · 1 Proton, 1 Neutron", "Building · label (4 Hydrogen)", "Building" when there are no inputs,
        /// each optionally followed by " → + Krypton, 3 Neutron" for co-products that land in the building.
        /// </summary>
        public static string MethodLine(string buildingName, string methodLabel,
            IList<(string name, int quantity)> inputs, IList<(string name, int quantity)> coProducts)
        {
            var sb = new StringBuilder();
            sb.Append(buildingName ?? "");

            string inputText = Join(inputs);
            bool hasLabel = !string.IsNullOrEmpty(methodLabel);

            if (hasLabel)
            {
                sb.Append(" · ").Append(methodLabel);
                if (inputText.Length > 0) sb.Append(" (").Append(inputText).Append(')');
            }
            else if (inputText.Length > 0)
            {
                sb.Append(" · ").Append(inputText);
            }

            string co = Join(coProducts);
            if (co.Length > 0) sb.Append(" → + ").Append(co);
            return sb.ToString();
        }

        /// <summary>Method line for a recipe asset: first valid building, its inputs, and non-escaping byproducts.</summary>
        public static string MethodLine(RecipeSO recipe)
        {
            if (recipe == null) return "";

            string building = "";
            if (recipe.validBuildings != null && recipe.validBuildings.Length > 0 && recipe.validBuildings[0] != null)
                building = recipe.validBuildings[0].displayName ?? recipe.validBuildings[0].name;

            var inputs = new List<(string, int)>();
            if (recipe.inputs != null)
                foreach (var i in recipe.inputs)
                    if (i.item != null && i.quantity > 0) inputs.Add((i.item.displayName ?? i.item.name, i.quantity));

            // Byproducts that escape as entropy never reach the building, so they stay off the line.
            var co = new List<(string, int)>();
            if (recipe.byproducts != null)
                foreach (var b in recipe.byproducts)
                    if (b.item != null && b.quantity > 0 && !b.item.escapesAsEntropy)
                        co.Add((b.item.displayName ?? b.item.name, b.quantity));

            return MethodLine(building, recipe.methodLabel, inputs, co);
        }

        static string Join(IList<(string name, int quantity)> items)
        {
            if (items == null || items.Count == 0) return "";
            var parts = new List<string>(items.Count);
            foreach (var (name, quantity) in items)
                parts.Add(quantity == 1 ? $"1 {name}" : $"{quantity} {name}");
            return string.Join(", ", parts);
        }
    }
}
