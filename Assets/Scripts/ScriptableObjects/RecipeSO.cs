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
        public string id;               // matches recipes.json id
        public string displayName;

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
    }
}
