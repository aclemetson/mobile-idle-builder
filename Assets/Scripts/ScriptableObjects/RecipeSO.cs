using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    [Serializable]
    public struct RecipeIngredient
    {
        public ItemSO item;
        public int quantity;
    }

    [CreateAssetMenu(fileName = "New Recipe", menuName = "MobileIdleBuilder/Recipe")]
    public class RecipeSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;               // matches game_data.json id
        public string displayName;
        [Tooltip("Optional short method name shown on the picker's method line (#336), e.g. 'p-p chain'.")]
        public string methodLabel;

        [Header("ECS Reference")]
        public int recipeId;            // integer ID used in ECS components

        [Header("Classification")]
        public int tier;
        public RecipeCategory category;

        [Header("Inputs")]
        public RecipeIngredient[] inputs;
        public ItemSO[] neutronAdjustment;  // for isotope recipes only

        [Header("Output")]
        public ItemSO outputItem;
        public int outputQuantity = 1;

        [Header("Byproducts")]
        public RecipeIngredient[] byproducts;   // e.g. alpha particle from fusion

        [Header("Timing")]
        public float baseCraftTime = 1f;
        public float manualCraftTime;           // may differ from building craft time

        [Header("Power")]
        public bool powerCostIsDynamic;         // true = calculated from atomic mass
        public float fixedPowerCostEV;

        [Header("Buildings")]
        public BuildingSO[] validBuildings;
        public bool canCraftManually;

        [Header("Unlock")]
        public bool knownFromStart;
        public ResearchSO requiredResearch;
        public ResearchSO unlocksResearch;

        [Header("Simplification")]
        public bool isSimplified;
        [TextArea(1, 3)]
        public string simplificationNote;

        /// <summary>
        /// Every item a craft puts in the building's output: the main output, then each byproduct that doesn't
        /// escape (an alpha particle from a decay, neutrons stripped from a nucleus, #322). Escaping byproducts
        /// are paid as entropy instead (<see cref="EscapeEntropyPerCraft"/>).
        /// </summary>
        public System.Collections.Generic.List<(ItemSO item, int quantity)> ProducedOutputs()
        {
            var list = new System.Collections.Generic.List<(ItemSO, int)>();
            if (outputItem != null) list.Add((outputItem, outputQuantity));
            if (byproducts != null)
                foreach (var b in byproducts)
                    if (b.item != null && b.quantity > 0 && !b.item.escapesAsEntropy) list.Add((b.item, b.quantity));
            return list;
        }

        /// <summary>
        /// Entropy paid on every craft by byproducts that escape the building (see
        /// <see cref="ItemSO.escapesAsEntropy"/>): quantity x sell value, summed. 0 when none escape.
        /// </summary>
        public float EscapeEntropyPerCraft
        {
            get
            {
                if (byproducts == null) return 0f;
                float total = 0f;
                foreach (var b in byproducts)
                    if (b.item != null && b.item.escapesAsEntropy && b.quantity > 0)
                        total += b.quantity * b.item.baseSellValue;
                return total;
            }
        }
    }
}
