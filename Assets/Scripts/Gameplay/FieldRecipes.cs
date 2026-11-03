using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Which of a field building's recipes it can run on a given field. Shared by placement (the
    /// "what should it collect?" chooser) and the building inspector so both offer the same list.
    /// </summary>
    public static class FieldRecipes
    {
        /// <summary>
        /// Recipes that collect one of the field's drops (a Harvester on a quark field: Up or Down Quark).
        /// When none do, every input-free recipe the building supports: a Nucleon Harvester's Proton and
        /// Neutron are not raw drops of the quark field it stands on (#313).
        /// </summary>
        public static List<RecipeSO> For(BuildingSO building, FieldSO field)
        {
            var result = new List<RecipeSO>();
            if (building?.supportedRecipes == null || field == null) return result;

            var drops = new HashSet<int>();
            foreach (var item in field.outputItems)
                if (item != null) drops.Add(item.itemId);

            foreach (var r in building.supportedRecipes)
                if (r?.outputItem != null && drops.Contains(r.outputItem.itemId))
                    result.Add(r);
            if (result.Count > 0) return result;

            foreach (var r in building.supportedRecipes)
                if (r?.outputItem != null && (r.inputs == null || r.inputs.Length == 0))
                    result.Add(r);
            return result;
        }
    }
}
